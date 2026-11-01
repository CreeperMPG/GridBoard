using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GridBoard.Mcp
{
    /// <summary>
    /// MCP 端点的本机配置，落在 %APPDATA%\GridBoard\mcp.json。
    /// 文件不存在时按默认值生成一份，方便主人直接编辑端口/开关/令牌。
    /// </summary>
    internal sealed class McpEndpointSettings
    {
        public const int DefaultPort = 39321;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>是否随应用启动 MCP 服务。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>只监听 127.0.0.1 的端口。</summary>
        public int Port { get; set; } = DefaultPort;

        /// <summary>可选的 Bearer 令牌；留空表示本机不校验。</summary>
        public string Token { get; set; } = string.Empty;

        public static string FilePath
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GridBoard");
                return Path.Combine(folder, "mcp.json");
            }
        }

        public static McpEndpointSettings Load()
        {
            string path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<McpEndpointSettings>(File.ReadAllText(path), JsonOptions);
                    if (loaded != null)
                        return loaded.Sanitized();
                }
            }
            catch (Exception ex)
            {
                GridBoardMcpServer.Log("读取 " + path + " 失败，改用默认配置：" + ex.Message);
            }

            var settings = new McpEndpointSettings();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
            }
            catch (Exception ex)
            {
                GridBoardMcpServer.Log("写入 " + path + " 失败：" + ex.Message);
            }

            return settings;
        }

        private McpEndpointSettings Sanitized()
        {
            if (Port < 1 || Port > 65535)
                Port = DefaultPort;
            if (Token == null)
                Token = string.Empty;
            return this;
        }
    }
}
