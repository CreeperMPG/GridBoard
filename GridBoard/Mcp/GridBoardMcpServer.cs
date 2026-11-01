using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GridBoard.Board;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GridBoard.Mcp
{
    /// <summary>
    /// 内置的 MCP 服务：把「改笔色 / 截图」两件事暴露成 MCP 工具，
    /// 传输用官方 SDK 的 Streamable HTTP，HTTP 那一层由本类的 HttpListener 适配。
    /// 只监听 127.0.0.1，随主窗口启动，退出时释放。
    /// </summary>
    internal sealed class GridBoardMcpServer : IDisposable
    {
        private const string EndpointPath = "/mcp";
        private const string AcceptHeader = "Accept";
        private const string AuthorizationHeader = "Authorization";
        private const string ProtocolVersionHeader = "MCP-Protocol-Version";
        private const string SessionIdHeader = "Mcp-Session-Id";
        private const string SessionId = "gridboard";
        private const string LogFileName = "mcp.log";

        private readonly IBoardService _board;
        private readonly McpEndpointSettings _settings;
        private readonly List<Tool> _tools;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        // 调用历史：只留最近 MaxCallHistory 条，给「MCP 服务器状态」对话框看
        private const int MaxCallHistory = 20;
        private readonly object _historyLock = new object();
        private readonly Queue<McpCallRecord> _history = new Queue<McpCallRecord>();
        private int _totalCalls;
        private int _failedCalls;
        private string _lastError;
        private DateTime _startedAt;

        private readonly HttpListener _listener = new HttpListener();
        private StreamableHttpServerTransport _transport;
        private McpServer _server;

        internal GridBoardMcpServer(IBoardService board, McpEndpointSettings settings)
        {
            _board = board ?? throw new ArgumentNullException(nameof(board));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tools = BuildTools();
        }

        internal string EndpointUrl
        {
            get { return "http://127.0.0.1:" + _settings.Port + EndpointPath; }
        }

        /// <summary>抓一份状态快照给「MCP 服务器状态」对话框。</summary>
        internal McpStatusSnapshot GetStatus()
        {
            lock (_historyLock)
            {
                return new McpStatusSnapshot
                {
                    Running = _listener.IsListening,
                    StateLabel = "运行中",
                    Endpoint = EndpointUrl,
                    TokenRequired = !string.IsNullOrEmpty(_settings.Token),
                    StartedAt = _startedAt,
                    TotalCalls = _totalCalls,
                    FailedCalls = _failedCalls,
                    LastError = _lastError,
                    Calls = new List<McpCallRecord>(_history),
                    ConfigPath = McpEndpointSettings.FilePath,
                    LogPath = LogFilePath
                };
            }
        }

        internal void Start()
        {
            _startedAt = DateTime.Now;
            _listener.Prefixes.Add("http://127.0.0.1:" + _settings.Port + "/");
            _listener.Start();

            _transport = new StreamableHttpServerTransport(NullLoggerFactory.Instance)
            {
                SessionId = SessionId
            };

            var options = new McpServerOptions
            {
                ServerInfo = new Implementation
                {
                    Name = "GridBoard",
                    Version = MainWindow.Version
                },
                ServerInstructions =
                    "GridBoard 是一块全屏黑板：黑色底、白色九宫格线，用户在上面手写板书。" +
                    "board_get_state 读当前笔色与工具；board_set_color 改之后笔迹的颜色；" +
                    "board_set_tool 在 none / pen / eraser 之间切换；" +
                    "board_screenshot 返回当前画面的 PNG（默认包含底部工具栏）。",
                Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability()
                },
                Handlers = new McpServerHandlers
                {
                    ListToolsHandler = ListToolsAsync,
                    CallToolHandler = CallToolAsync
                }
            };

            _server = McpServer.Create(_transport, options, NullLoggerFactory.Instance, null);
            _ = Task.Run(() => _server.RunAsync(_cts.Token), _cts.Token);
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);

            Log("MCP 服务已启动：" + EndpointUrl + (string.IsNullOrEmpty(_settings.Token) ? "（无令牌）" : "（需要令牌）"));
        }

        // ==================== HTTP 层 ====================

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested || !_listener.IsListening)
                        return;

                    Log("接受连接失败：" + ex.Message);
                    continue;
                }

                _ = Task.Run(() => HandleRequestAsync(context));
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                HttpListenerRequest request = context.Request;

                if (!string.Equals(request.Url.AbsolutePath, EndpointPath, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteTextAsync(context, 404, "这里只有 " + EndpointPath + "。");
                    return;
                }

                if (!IsAuthorized(request))
                {
                    await WriteTextAsync(context, 401, "缺少或错误的 Bearer 令牌。");
                    return;
                }

                switch (request.HttpMethod)
                {
                    case "POST":
                        await HandlePostAsync(context);
                        return;
                    case "GET":
                        await WriteTextAsync(context, 405, "本服务只处理 POST（不支持独立 SSE 流）。");
                        return;
                    case "DELETE":
                        await WriteTextAsync(context, 405, "本服务不维护可删除的会话。");
                        return;
                    default:
                        await WriteTextAsync(context, 405, "Method Not Allowed");
                        return;
                }
            }
            catch (Exception ex)
            {
                Log("处理请求出错：" + ex);
                try
                {
                    await WriteTextAsync(context, 500, "Internal Server Error：" + ex.Message);
                }
                catch
                {
                    // 响应可能已经写出去了，忽略
                }
            }
        }

        private async Task HandlePostAsync(HttpListenerContext context)
        {
            if (!AcceptsBothMediaTypes(context.Request))
            {
                await WriteJsonRpcErrorAsync(context, 406, -32600,
                    "客户端必须同时接受 application/json 与 text/event-stream。");
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            JsonRpcMessage message;
            try
            {
                message = JsonSerializer.Deserialize<JsonRpcMessage>(body, McpJsonUtilities.DefaultOptions);
            }
            catch (JsonException ex)
            {
                await WriteJsonRpcErrorAsync(context, 400, -32700, "请求体不是合法的 JSON-RPC 消息：" + ex.Message);
                return;
            }

            if (message == null)
            {
                await WriteJsonRpcErrorAsync(context, 400, -32600, "请求体不是合法的 JSON-RPC 消息。");
                return;
            }

            // 新版协议按请求自带版本信息，SDK 要靠这里决定走哪套语义
            if (message.Context == null)
                message.Context = new JsonRpcMessageContext();
            string protocolVersion = context.Request.Headers[ProtocolVersionHeader];
            if (!string.IsNullOrEmpty(protocolVersion))
                message.Context.ProtocolVersion = protocolVersion;

            using (var responseBody = new MemoryStream())
            {
                bool wroteResponse = await _transport.HandlePostRequestAsync(message, responseBody, _cts.Token);

                if (!wroteResponse)
                {
                    SetSessionHeaders(context);
                    context.Response.StatusCode = 202;
                    context.Response.ContentLength64 = 0;
                    context.Response.Close();
                    return;
                }

                byte[] payload = responseBody.ToArray();
                SetSessionHeaders(context);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
                context.Response.Close();
            }
        }

        private void SetSessionHeaders(HttpListenerContext context)
        {
            context.Response.Headers[SessionIdHeader] = SessionId;
        }

        private bool IsAuthorized(HttpListenerRequest request)
        {
            if (string.IsNullOrEmpty(_settings.Token))
                return true;

            string header = request.Headers[AuthorizationHeader];
            if (string.IsNullOrEmpty(header))
                return false;

            const string prefix = "Bearer ";
            return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(header.Substring(prefix.Length).Trim(), _settings.Token, StringComparison.Ordinal);
        }

        private static bool AcceptsBothMediaTypes(HttpListenerRequest request)
        {
            string accept = request.Headers[AcceptHeader];
            if (string.IsNullOrEmpty(accept))
                return false;

            return accept.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0
                && accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task WriteTextAsync(HttpListenerContext context, int status, string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text ?? string.Empty);
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
            context.Response.Close();
        }

        private static async Task WriteJsonRpcErrorAsync(HttpListenerContext context, int status, int code, string message)
        {
            string json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = (object)null,
                error = new { code = code, message = message }
            });

            byte[] payload = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
            context.Response.Close();
        }

        // ==================== 工具 ====================

        private static List<Tool> BuildTools()
        {
            var stateTool = new Tool
            {
                Name = "board_get_state",
                Title = "读取黑板状态",
                Description = "读取 GridBoard 当前的笔迹颜色和所选工具（none=无 / pen=笔 / eraser=橡皮擦）。",
                InputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new { },
                    additionalProperties = false
                })
            };

            var setColorTool = new Tool
            {
                Name = "board_set_color",
                Title = "设置笔迹颜色",
                Description =
                    "设置 GridBoard 黑板上之后笔迹的颜色（已经画上的笔迹不受影响）。" +
                    "支持 #RRGGBB / #AARRGGBB，也接受调色板名：" +
                    "white / red / orange / yellow / green / blue / indigo / violet / gray / skyblue / lightgreen / chocolate。",
                InputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        color = new
                        {
                            type = "string",
                            description = "颜色，例如 #FF0000 或 red。"
                        }
                    },
                    required = new[] { "color" },
                    additionalProperties = false
                })
            };

            var setToolTool = new Tool
            {
                Name = "board_set_tool",
                Title = "切换工具",
                Description =
                    "切换 GridBoard 当前工具：none（无，只显示光标）/ pen（笔）/ eraser（橡皮擦）。" +
                    "也接受「无」「笔」「橡皮擦」。",
                InputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        tool = new
                        {
                            type = "string",
                            @enum = new[] { "none", "pen", "eraser" },
                            description = "工具标识，也接受中文：无 / 笔 / 橡皮擦。"
                        }
                    },
                    required = new[] { "tool" },
                    additionalProperties = false
                })
            };

            var screenshotTool = new Tool
            {
                Name = "board_screenshot",
                Title = "截取黑板画面",
                Description = "截取 GridBoard 当前画面并作为 PNG 图片返回，用来查看黑板上已有的板书内容。",
                InputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        max_width = new
                        {
                            type = "integer",
                            minimum = 320,
                            maximum = 3840,
                            @default = 1280,
                            description = "输出图片的宽度上限，默认 1280 像素。"
                        },
                        show_toolbar = new
                        {
                            type = "boolean",
                            @default = true,
                            description =
                                "截图里是否显示底部工具栏，默认 true（所见即所得，不会闪烁）。" +
                                "传 false 时会在截图前临时收起工具栏与版本号，结束后恢复。"
                        }
                    },
                    additionalProperties = false
                })
            };

            return new List<Tool> { stateTool, setColorTool, setToolTool, screenshotTool };
        }

        private ValueTask<ListToolsResult> ListToolsAsync(
            RequestContext<ListToolsRequestParams> request,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ListToolsResult>(new ListToolsResult { Tools = _tools });
        }

        private async ValueTask<CallToolResult> CallToolAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken)
        {
            CallToolRequestParams parameters = request.Params;
            if (parameters == null)
                return Error("缺少调用参数。");

            IDictionary<string, JsonElement> args = parameters.Arguments;

            Stopwatch stopwatch = Stopwatch.StartNew();
            CallToolResult result;
            try
            {
                switch (parameters.Name)
                {
                    case "board_get_state":
                        result = await GetStateAsync();
                        break;
                    case "board_set_color":
                        result = await SetColorAsync(args);
                        break;
                    case "board_set_tool":
                        result = await SetToolAsync(args);
                        break;
                    case "board_screenshot":
                        result = await ScreenshotAsync(args);
                        break;
                    default:
                        result = Error("未知工具：" + parameters.Name);
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                result = Error(ex.Message);
            }
            catch (Exception ex)
            {
                Log("工具 " + parameters.Name + " 执行失败：" + ex);
                result = Error("执行失败：" + ex.Message);
            }
            stopwatch.Stop();

            RecordCall(parameters.Name, result, stopwatch.ElapsedMilliseconds);
            return result;
        }

        /// <summary>记一笔调用历史，并更新计数 / 最近错误。</summary>
        private void RecordCall(string tool, CallToolResult result, long elapsedMilliseconds)
        {
            string detail = FirstText(result);
            if (detail != null && detail.Length > 120)
                detail = detail.Substring(0, 117) + "…";

            var record = new McpCallRecord
            {
                At = DateTime.Now,
                Tool = tool ?? "(未命名)",
                Ok = result.IsError != true,
                ElapsedMilliseconds = elapsedMilliseconds,
                Detail = detail
            };

            lock (_historyLock)
            {
                _totalCalls++;

                if (!record.Ok)
                {
                    _failedCalls++;
                    _lastError = record.At.ToString("HH:mm:ss") + " " + record.Tool + "：" + record.Detail;
                }

                _history.Enqueue(record);
                while (_history.Count > MaxCallHistory)
                    _history.Dequeue();
            }
        }

        private static string FirstText(CallToolResult result)
        {
            if (result.Content == null)
                return null;

            foreach (ContentBlock block in result.Content)
            {
                var text = block as TextContentBlock;
                if (text != null && !string.IsNullOrEmpty(text.Text))
                    return text.Text;
            }

            return null;
        }

        private async Task<CallToolResult> GetStateAsync()
        {
            BoardState state = await _board.GetStateAsync();
            return Text(DescribeState(state));
        }

        private async Task<CallToolResult> SetColorAsync(IDictionary<string, JsonElement> args)
        {
            string color = ReadString(args, "color");
            if (string.IsNullOrEmpty(color))
                return Error("缺少参数 color。");

            BoardColor applied = await _board.SetInkColorAsync(color);

            string text = "笔迹颜色已设为 " + applied.Hex;
            if (!string.IsNullOrEmpty(applied.PaletteName))
                text += "（调色板：" + applied.PaletteName + "）";
            text += "。之后画的笔画都用这个颜色，已有笔迹不变。";

            return Text(text);
        }

        private async Task<CallToolResult> SetToolAsync(IDictionary<string, JsonElement> args)
        {
            string tool = ReadString(args, "tool");
            if (string.IsNullOrEmpty(tool))
                return Error("缺少参数 tool。");

            BoardState state = await _board.SetToolAsync(tool);
            return Text("工具已切换为「" + state.ToolLabel + "」（" + state.Tool + "）。" + DescribeState(state));
        }

        private static string DescribeState(BoardState state)
        {
            string text = "当前笔色 " + state.Color;
            if (!string.IsNullOrEmpty(state.PaletteName))
                text += "（调色板：" + state.PaletteName + "）";
            text += "，当前工具「" + state.ToolLabel + "」（" + state.Tool + "）。";
            return text;
        }

        private async Task<CallToolResult> ScreenshotAsync(IDictionary<string, JsonElement> args)
        {
            int maxWidth = ReadInt(args, "max_width", 1280);
            bool showToolbar = ReadBool(args, "show_toolbar", true);

            if (maxWidth > 0 && maxWidth < 320) maxWidth = 320;
            if (maxWidth > 3840) maxWidth = 3840;

            BoardScreenshot shot = await _board.CaptureAsync(maxWidth, !showToolbar);

            var content = new List<ContentBlock>
            {
                // Data 是 base64 后的字节，必须用 FromBytes：它会把原始 PNG 懒编码成 base64
                ImageContentBlock.FromBytes(shot.Png, "image/png"),
                new TextContentBlock
                {
                    Text = "GridBoard 当前画面，" + shot.Width + "×" + shot.Height + " 像素（PNG），" +
                           (shot.ToolbarHidden ? "工具栏已隐藏。" : "工具栏已包含在画面里。")
                }
            };

            return new CallToolResult { Content = content };
        }

        private static string ReadString(IDictionary<string, JsonElement> args, string key)
        {
            JsonElement value;
            if (args == null || !args.TryGetValue(key, out value))
                return null;

            return value.ValueKind == JsonValueKind.String ? value.GetString() :
                   value.ValueKind == JsonValueKind.Null ? null : value.ToString();
        }

        private static int ReadInt(IDictionary<string, JsonElement> args, string key, int fallback)
        {
            JsonElement value;
            if (args == null || !args.TryGetValue(key, out value))
                return fallback;

            int number;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number))
                return number;

            return int.TryParse(value.ToString(), out number) ? number : fallback;
        }

        private static bool ReadBool(IDictionary<string, JsonElement> args, string key, bool fallback)
        {
            JsonElement value;
            if (args == null || !args.TryGetValue(key, out value))
                return fallback;

            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;

            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) ? parsed : fallback;
        }

        private static CallToolResult Text(string text)
        {
            return new CallToolResult
            {
                Content = new List<ContentBlock> { new TextContentBlock { Text = text } }
            };
        }

        private static CallToolResult Error(string message)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = message } }
            };
        }

        // ==================== 日志与释放 ====================

        internal static string LogFilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GridBoard",
                    LogFileName);
            }
        }

        internal static void Log(string message)
        {
            try
            {
                string path = LogFilePath;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // 日志写不进去也不能影响主流程
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { if (_listener.IsListening) _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }

            try
            {
                if (_server is IAsyncDisposable asyncServer)
                    asyncServer.DisposeAsync().AsTask().Wait(2000);
            }
            catch { }

            try
            {
                if (_transport != null)
                    _transport.DisposeAsync().AsTask().Wait(2000);
            }
            catch { }

            try { _cts.Dispose(); } catch { }
        }
    }
}

