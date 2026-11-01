using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern.Controls;

namespace GridBoard.Mcp
{
    /// <summary>
    /// 「MCP 服务器状态」对话框：运行状态、端点、令牌、调用统计和最近调用。
    /// 内容全部按快照现搭，不碰服务本身，也不会因为服务已停就崩。
    /// </summary>
    internal static class McpStatusDialog
    {
        private static readonly Brush SecondaryBrush = new SolidColorBrush(Color.FromRgb(0x5F, 0x5F, 0x5F));
        private static readonly Brush DividerBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10));
        private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
        private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));

        internal static async Task ShowAsync(Window owner, McpStatusSnapshot status)
        {
            var dialog = new ContentDialog
            {
                Owner = owner,
                Title = "MCP 服务器状态",
                Content = BuildContent(status),
                PrimaryButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary
            };

            string logPath = status.LogPath;
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                dialog.SecondaryButtonText = "打开日志";
                dialog.SecondaryButtonClick += (sender, args) => OpenLog(logPath);
            }

            await dialog.ShowAsync(owner);
        }

        private static FrameworkElement BuildContent(McpStatusSnapshot status)
        {
            var panel = new StackPanel
            {
                MinWidth = 440,
                MaxWidth = 620
            };

            panel.Children.Add(BuildStateRow(status));
            panel.Children.Add(BuildInfoGrid(status));
            panel.Children.Add(BuildHistory(status));

            return new ScrollViewer
            {
                Content = panel,
                MaxHeight = 460,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        // ==================== 状态行 ====================

        private static FrameworkElement BuildStateRow(McpStatusSnapshot status)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 2)
            };

            row.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = StateBrush(status),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            row.Children.Add(new TextBlock
            {
                Text = status.StateLabel,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (!string.IsNullOrEmpty(status.StateDetail))
            {
                row.Children.Add(new TextBlock
                {
                    Text = status.StateDetail,
                    Foreground = SecondaryBrush,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400
                });
            }

            return row;
        }

        private static Brush StateBrush(McpStatusSnapshot status)
        {
            if (status.Running)
                return RunningBrush;

            return status.StateLabel == "启动失败" ? FailedBrush : IdleBrush;
        }

        // ==================== 键值行 ====================

        private static FrameworkElement BuildInfoGrid(McpStatusSnapshot status)
        {
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AddRow(grid, "端点", status.Endpoint);

            AddRow(grid, "令牌", status.TokenRequired
                ? "已启用（客户端需带 Bearer 令牌）"
                : "未启用（本机免验证）");

            if (status.Running)
            {
                AddRow(grid, "启动", status.StartedAt.ToString("HH:mm:ss") +
                    "（已运行 " + DescribeUptime(DateTime.Now - status.StartedAt) + "）");
            }

            AddRow(grid, "调用", status.TotalCalls + " 次，失败 " + status.FailedCalls + " 次");

            if (!string.IsNullOrEmpty(status.LastError))
                AddRow(grid, "最近错误", status.LastError);

            AddRow(grid, "配置", status.ConfigPath);

            return grid;
        }

        private static void AddRow(Grid grid, string label, string value)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = SecondaryBrush,
                Margin = new Thickness(0, 4, 12, 4)
            };

            var valueBlock = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4)
            };

            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);

            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
        }

        // ==================== 调用历史 ====================

        private static FrameworkElement BuildHistory(McpStatusSnapshot status)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            panel.Children.Add(new Border
            {
                Height = 1,
                Background = DividerBrush,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "最近调用",
                FontWeight = FontWeights.SemiBold
            });

            if (status.Calls == null || status.Calls.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "还没有工具调用记录。",
                    Foreground = SecondaryBrush,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                return panel;
            }

            var list = new StackPanel();
            for (int i = status.Calls.Count - 1; i >= 0; i--)   // 新的排在上面
                list.Children.Add(BuildCallRow(status.Calls[i]));

            panel.Children.Add(new ScrollViewer
            {
                Content = list,
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 6, 0, 0)
            });

            return panel;
        }

        private static FrameworkElement BuildCallRow(McpCallRecord call)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var head = new StackPanel { Orientation = Orientation.Horizontal };

            head.Children.Add(new TextBlock
            {
                Text = call.At.ToString("HH:mm:ss"),
                Foreground = SecondaryBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0)
            });

            head.Children.Add(new TextBlock
            {
                Text = call.Tool,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0)
            });

            head.Children.Add(new TextBlock
            {
                Text = call.Ok ? "成功" : "失败",
                Foreground = call.Ok ? RunningBrush : FailedBrush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0)
            });

            head.Children.Add(new TextBlock
            {
                Text = call.ElapsedMilliseconds + " ms",
                Foreground = SecondaryBrush,
                FontSize = 12
            });

            row.Children.Add(head);

            if (!string.IsNullOrEmpty(call.Detail))
            {
                row.Children.Add(new TextBlock
                {
                    Text = call.Detail,
                    Foreground = SecondaryBrush,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            return row;
        }

        // ==================== 小工具 ====================

        private static string DescribeUptime(TimeSpan span)
        {
            if (span.TotalSeconds < 0)
                return "0 秒";

            if (span.TotalHours >= 1)
                return (int)span.TotalHours + " 小时 " + span.Minutes + " 分";

            if (span.TotalMinutes >= 1)
                return (int)span.TotalMinutes + " 分 " + span.Seconds + " 秒";

            return (int)span.TotalSeconds + " 秒";
        }

        private static void OpenLog(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch
            {
                // 打不开资源管理器就算了，别把对话框搞崩
            }
        }
    }
}
