using System.Threading.Tasks;

namespace GridBoard.Board
{
    /// <summary>
    /// 画板对外的能力（给 MCP 用）：笔色、工具的读写，以及截图。
    /// 所有方法都可以从任意线程调用，内部会切回 UI 线程。
    /// </summary>
    internal interface IBoardService
    {
        /// <summary>读当前笔色与所选工具。</summary>
        Task<BoardState> GetStateAsync();

        /// <summary>设置后续笔迹的颜色。返回实际生效的颜色。</summary>
        Task<BoardColor> SetInkColorAsync(string color);

        /// <summary>切换工具：none（无）/ pen（笔）/ eraser（橡皮擦）。</summary>
        Task<BoardState> SetToolAsync(string tool);

        /// <summary>截取窗口画面，PNG 字节流。</summary>
        /// <param name="maxWidth">宽度上限（像素），超过就等比缩小；&lt;= 0 表示原尺寸。</param>
        /// <param name="hideToolbar">截图前临时藏起底部浮层和版本号。</param>
        Task<BoardScreenshot> CaptureAsync(int maxWidth, bool hideToolbar);
    }

    /// <summary>画板的对外状态：笔色 + 当前工具。</summary>
    internal sealed class BoardState
    {
        /// <summary>实际生效的笔色，形如 #RRGGBB（带透明度时是 #AARRGGBB）。</summary>
        public string Color { get; set; }

        /// <summary>颜色命中调色板时的名字；自定义颜色为 null。</summary>
        public string PaletteName { get; set; }

        /// <summary>工具标识：none / pen / eraser。</summary>
        public string Tool { get; set; }

        /// <summary>工具的中文名：无 / 笔 / 橡皮擦。</summary>
        public string ToolLabel { get; set; }
    }

    internal sealed class BoardColor
    {
        /// <summary>实际生效的颜色，形如 #RRGGBB（带透明度时是 #AARRGGBB）。</summary>
        public string Hex { get; set; }

        /// <summary>如果在调色板里，这是命中的那一格的名字；否则为 null。</summary>
        public string PaletteName { get; set; }
    }

    internal sealed class BoardScreenshot
    {
        public byte[] Png { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool ToolbarHidden { get; set; }
    }
}
