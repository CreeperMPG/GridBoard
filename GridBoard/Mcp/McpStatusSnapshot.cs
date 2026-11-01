using System;
using System.Collections.Generic;

namespace GridBoard.Mcp
{
    /// <summary>一次工具调用的记录，给「MCP 服务器状态」对话框展示。</summary>
    internal sealed class McpCallRecord
    {
        public DateTime At { get; set; }

        /// <summary>工具名，例如 board_set_color。</summary>
        public string Tool { get; set; }

        public bool Ok { get; set; }

        public long ElapsedMilliseconds { get; set; }

        /// <summary>一句话结果（成功时是返回文字，失败时是错误信息）。</summary>
        public string Detail { get; set; }
    }

    /// <summary>MCP 服务的只读状态快照：对话框只读它，不反过来碰服务。</summary>
    internal sealed class McpStatusSnapshot
    {
        public bool Running { get; set; }

        /// <summary>运行中 / 未启用 / 启动失败。</summary>
        public string StateLabel { get; set; }

        /// <summary>状态右边的一句话补充（启动时间、失败原因等）。</summary>
        public string StateDetail { get; set; }

        public string Endpoint { get; set; }
        public bool TokenRequired { get; set; }
        public DateTime StartedAt { get; set; }

        public int TotalCalls { get; set; }
        public int FailedCalls { get; set; }
        public string LastError { get; set; }

        /// <summary>最近的调用，旧 → 新（对话框自己倒过来显示）。</summary>
        public IList<McpCallRecord> Calls { get; set; } = new List<McpCallRecord>();

        public string ConfigPath { get; set; }
        public string LogPath { get; set; }
    }
}
