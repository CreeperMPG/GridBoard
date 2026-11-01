# Grid Board

`3x3` 网格黑板书写软件，用于特定场景。

## 功能

- 全屏黑板 + 3×3 网格线，手写墨迹保存在 `%APPDATA%\GridBoard\save`
  - 有改动时每 2 分钟自动保存，关窗口前也会保存（没改动就不写盘）
  - **原子写入**：先写 `save.tmp` 并强制落盘，再原子替换 `save`；被替换下来的上一版留成 `save.bak`
  - 主存档读不出来（断电、磁盘满等）时自动回退到 `save.bak`，右下角会显示「已从备份恢复」
- 笔：12 色 + 3 档粗细；橡皮擦；橡皮菜单里拖动可清屏
- 下降屏幕 / 局部放大（九宫格选区域）
- 导出为图片（PNG / JPEG / BMP）
- **MCP Server**：内置一个只监听 `127.0.0.1` 的 MCP 端点，Agent 可以读板书、改笔色

## 构建

老式 csproj + .NET Framework 4.7.2，**必须用 Visual Studio 的 MSBuild**。
`dotnet build` 会因为老 csproj 里的 `ToolsVersion` 拼不出 `Microsoft.Common.props` 路径，
把所有 NuGet 引用当成缺失（报 `CS0246: 未能找到类型或命名空间名"iNKORE"`）：

    "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe" GridBoard.slnx /restore

## MCP Server

应用启动后自动在本机开一个 Streamable HTTP 端点（不会弹窗、不需要管理员权限）：

    http://127.0.0.1:39321/mcp

配置文件是 `%APPDATA%\GridBoard\mcp.json`，首次启动自动生成；改完要重启应用：

```json
{
  "Enabled": true,
  "Port": 39321,
  "Token": ""
}
```

| 字段 | 说明 |
|---|---|
| `Enabled` | 是否随应用启动 MCP 服务（`false` 时完全不监听） |
| `Port` | 监听端口，固定只绑 `127.0.0.1` |
| `Token` | 可选的 Bearer 令牌；非空时客户端必须带 `Authorization: Bearer <token>` |

启动失败不会影响黑板本身：原因写在 `%APPDATA%\GridBoard\mcp.log`。
运行状态、端口、令牌、调用统计和最近 20 次调用记录，都在**左下角「⋯」→「MCP服务器状态」**
弹出的对话框里看；右下角工具栏不再显示端口。

### 工具

| 工具 | 作用 |
|---|---|
| `board_get_state` | 读当前状态：笔迹颜色（含调色板名）+ 当前工具（`none` / `pen` / `eraser`）。 |
| `board_set_color` | 设置之后笔迹的颜色。支持 `#RRGGBB` / `#AARRGGBB`，也接受调色板名（`white` / `red` / `orange` / `yellow` / `green` / `blue` / `indigo` / `violet` / `gray` / `skyblue` / `lightgreen` / `chocolate`）。已有笔迹不变。 |
| `board_set_tool` | 切换工具：`none`（无）/ `pen`（笔）/ `eraser`（橡皮擦），也接受中文。会同步工具栏的图标高亮，但不会移动鼠标。 |
| `board_screenshot` | 截取当前画面，按 MCP 图片内容返回 PNG。`max_width` 默认 1280（超过等比缩小）；`show_toolbar` 默认 `true`（所见即所得，含底部工具栏）；传 `false` 时才临时收起工具栏与版本号，截完恢复。 |

`board_set_color` / `board_set_tool` 都会把调色板和工具按钮的选中态一起同步，
所以人再看界面时，看到的和 Agent 设的是同一个状态。

### 接入客户端

任何支持 Streamable HTTP 的 MCP 客户端都可以直接连 `http://127.0.0.1:39321/mcp`。
DeepSeek Harness 的写法（profile 的 `cordis.patch.yml`，新增条目必须放在 `insert:` 下面）：

```yaml
- insert:
    - id: mcp-gridboard
      name: '@deepseek-ai/dsh-mcp-client'
      config:
        serverName: gridboard
        transport: streamable-http
        url: http://127.0.0.1:39321/mcp
        failOnStartupError: false
```

Harness 里工具名是 `mcp__gridboard__<工具名>`，例如 `mcp__gridboard__board_screenshot`。
GridBoard 没在运行时连接会失败并自动重连，工具暂时不出现，不影响其它会话。

协议部分用的是官方 `ModelContextProtocol` C# SDK（`netstandard2.0` 目标，net472 可直接引用），
只有 HTTP 那一层是自己用 `HttpListener` 接的，见 `Mcp/GridBoardMcpServer.cs`。
