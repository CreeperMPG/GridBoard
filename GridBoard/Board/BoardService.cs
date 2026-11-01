using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GridBoard.Board
{
    /// <summary>
    /// <see cref="IBoardService"/> 的实现：改动都落回 <see cref="MainWindow"/> 上的真实控件，
    /// 并且一律通过 Dispatcher 在 UI 线程执行（MCP 的调用方在后台线程）。
    /// </summary>
    internal sealed class BoardService : IBoardService
    {
        /// <summary>
        /// 调色板：顺序必须和 MainWindow.xaml 里 ColorSelector 那 12 个色块一致，
        /// 这样按索引就能把「选中的格子」和「实际颜色」对上。
        /// </summary>
        private static readonly (string Name, Color Value)[] Palette =
        {
            ("white", Colors.White),
            ("red", Colors.Red),
            ("orange", Colors.Orange),
            ("yellow", Colors.Yellow),
            ("green", Colors.Green),
            ("blue", Colors.Blue),
            ("indigo", Colors.Indigo),
            ("violet", Colors.Violet),
            ("gray", Colors.Gray),
            ("skyblue", Colors.SkyBlue),
            ("lightgreen", Colors.LightGreen),
            ("chocolate", Colors.Chocolate)
        };

        private static readonly Dictionary<string, Color> Aliases = BuildAliases();
        private static readonly Dictionary<string, SelectedOptionType> ToolAliases = BuildToolAliases();

        private readonly MainWindow _window;

        internal BoardService(MainWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        // ==================== 颜色与工具 ====================

        public Task<BoardState> GetStateAsync()
        {
            return _window.Dispatcher.InvokeAsync(BuildState).Task;
        }

        public Task<BoardColor> SetInkColorAsync(string color)
        {
            Color parsed = ParseColor(color);
            return _window.Dispatcher.InvokeAsync(() => ApplyInkColor(parsed)).Task;
        }

        public Task<BoardState> SetToolAsync(string tool)
        {
            SelectedOptionType option = ParseTool(tool);
            return _window.Dispatcher.InvokeAsync(() =>
            {
                // 和按钮点击走同一条路：改 SelectedOption 会同步图标、高亮和 InkCanvas 的编辑模式。
                // 区别只在不移动鼠标（那是给"手点完按钮要接着写"准备的，Agent 调用不该抢指针）。
                _window.SelectedOption = option;
                return BuildState();
            }).Task;
        }

        private BoardState BuildState()
        {
            Color color = _window.AppCanvas.DefaultDrawingAttributes.Color;
            SelectedOptionType tool = _window.SelectedOption;

            return new BoardState
            {
                Color = FormatColor(color),
                PaletteName = PaletteNameOf(color),
                Tool = ToolKeyOf(tool),
                ToolLabel = ToolLabelOf(tool)
            };
        }

        private BoardColor ApplyInkColor(Color color)
        {
            _window.AppCanvas.DefaultDrawingAttributes.Color = color;

            return new BoardColor
            {
                Hex = FormatColor(color),
                PaletteName = SyncColorSelector(color)
            };
        }

        /// <summary>
        /// 让笔刷浮层跟上实际笔色：
        /// 命中调色板 → 选中那一格，并收起「Agent 选择的颜色」；
        /// 不在调色板里 → 清掉选中（不能留着上一个色块亮着误导人），改在色块上方显示那一行。
        /// 设 SelectedIndex 不会触发 ItemClick（那只在用户点击时发），所以不会打转。
        /// </summary>
        private string SyncColorSelector(Color color)
        {
            var selector = _window.ColorSelector;
            for (int i = 0; i < selector.Items.Count; i++)
            {
                var rect = selector.Items[i] as Rectangle;
                var brush = rect == null ? null : rect.Fill as SolidColorBrush;
                if (brush == null || brush.Color != color)
                    continue;

                selector.SelectedIndex = i;
                _window.HideAgentColor();
                return i < Palette.Length ? Palette[i].Name : null;
            }

            selector.SelectedIndex = -1;
            _window.ShowAgentColor(color);
            return null;
        }

        private static string PaletteNameOf(Color color)
        {
            for (int i = 0; i < Palette.Length; i++)
            {
                if (Palette[i].Value == color)
                    return Palette[i].Name;
            }

            return null;
        }

        private static SelectedOptionType ParseTool(string tool)
        {
            if (!string.IsNullOrWhiteSpace(tool))
            {
                SelectedOptionType option;
                if (ToolAliases.TryGetValue(tool.Trim(), out option))
                    return option;
            }

            throw new ArgumentException(
                "无法识别的工具“" + tool + "”。请用 none（无）/ pen（笔）/ eraser（橡皮擦）。");
        }

        private static string ToolKeyOf(SelectedOptionType tool)
        {
            switch (tool)
            {
                case SelectedOptionType.Pen: return "pen";
                case SelectedOptionType.Eraser: return "eraser";
                default: return "none";
            }
        }

        private static string ToolLabelOf(SelectedOptionType tool)
        {
            switch (tool)
            {
                case SelectedOptionType.Pen: return "笔";
                case SelectedOptionType.Eraser: return "橡皮擦";
                default: return "无";
            }
        }

        private static Dictionary<string, SelectedOptionType> BuildToolAliases()
        {
            return new Dictionary<string, SelectedOptionType>(StringComparer.OrdinalIgnoreCase)
            {
                ["none"] = SelectedOptionType.None,
                ["cursor"] = SelectedOptionType.None,
                ["无"] = SelectedOptionType.None,
                ["pen"] = SelectedOptionType.Pen,
                ["笔"] = SelectedOptionType.Pen,
                ["eraser"] = SelectedOptionType.Eraser,
                ["橡皮"] = SelectedOptionType.Eraser,
                ["橡皮擦"] = SelectedOptionType.Eraser
            };
        }

        // ==================== 截图 ====================

        public async Task<BoardScreenshot> CaptureAsync(int maxWidth, bool hideToolbar)
        {
            Dispatcher ui = _window.Dispatcher;
            bool toolbarHidden = false;

            if (hideToolbar)
            {
                await ui.InvokeAsync(() => _window.SetFloatingUiVisible(false));
                toolbarHidden = true;
            }

            try
            {
                // 等一次布局/渲染走完，免得截到浮层收起前的残影
                await ui.InvokeAsync(() => { }, DispatcherPriority.Background);

                RenderTargetBitmap bitmap = await ui.InvokeAsync(_window.CaptureWindowBitmap);

                BitmapSource output = maxWidth > 0 && bitmap.PixelWidth > maxWidth
                    ? ScaleDown(bitmap, maxWidth)
                    : bitmap;

                return new BoardScreenshot
                {
                    Png = EncodePng(output),
                    Width = output.PixelWidth,
                    Height = output.PixelHeight,
                    ToolbarHidden = toolbarHidden
                };
            }
            finally
            {
                if (toolbarHidden)
                    await ui.InvokeAsync(() => _window.SetFloatingUiVisible(true));
            }
        }

        private static BitmapSource ScaleDown(BitmapSource source, int maxWidth)
        {
            double scale = (double)maxWidth / source.PixelWidth;
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }

        private static byte[] EncodePng(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        // ==================== 颜色解析 ====================

        private static Dictionary<string, Color> BuildAliases()
        {
            var map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, value) in Palette)
                map[name] = value;

            map["grey"] = Colors.Gray;

            map["白"] = Colors.White;
            map["红"] = Colors.Red;
            map["橙"] = Colors.Orange;
            map["黄"] = Colors.Yellow;
            map["绿"] = Colors.Green;
            map["蓝"] = Colors.Blue;
            map["靛"] = Colors.Indigo;
            map["紫"] = Colors.Violet;
            map["灰"] = Colors.Gray;
            map["天蓝"] = Colors.SkyBlue;
            map["浅绿"] = Colors.LightGreen;
            map["棕"] = Colors.Chocolate;
            map["巧克力"] = Colors.Chocolate;

            return map;
        }

        private static Color ParseColor(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("color 不能为空。");

            string text = raw.Trim();

            Color named;
            if (Aliases.TryGetValue(text, out named))
                return named;

            string hex = text[0] == '#' ? text.Substring(1) : text;
            if (hex.Length == 3)
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });

            if (hex.Length == 6 || hex.Length == 8)
            {
                uint value;
                if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                {
                    byte a = hex.Length == 8 ? (byte)(value >> 24) : (byte)255;
                    byte r = (byte)(value >> 16);
                    byte g = (byte)(value >> 8);
                    byte b = (byte)value;
                    return Color.FromArgb(a, r, g, b);
                }
            }

            throw new ArgumentException(
                "无法识别的颜色“" + raw + "”。请用 #RRGGBB / #AARRGGBB，或调色板名：" +
                string.Join(" / ", Palette.Select(p => p.Name)) + "。");
        }

        private static string FormatColor(Color color)
        {
            return color.A == 255
                ? string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B)
                : string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", color.A, color.R, color.G, color.B);
        }
    }
}
