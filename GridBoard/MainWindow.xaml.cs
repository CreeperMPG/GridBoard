using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using GridBoard.Board;
using GridBoard.Mcp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Xml.Linq;

namespace GridBoard
{
    public enum SelectedOptionType
    {
        None, Pen, Eraser
    }
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int X, int Y);
        private Stack<StrokeCollection> _history = new Stack<StrokeCollection>();
        private const int MaxHistory = 50;

        // 画布相对「上次成功保存」有没有改动：定时器和关窗只在脏的时候写盘，
        // 免得每 2 分钟无脑重写一遍存档。
        private bool _dirty;
        private readonly Dictionary<SelectedOptionType, (AppBarButton button, FontIcon icon, FontIconData regular, FontIconData filled, InkCanvasEditingMode editingMode)> _buttonConfigs;
        private static readonly Brush HighlightBrush = Brushes.DeepSkyBlue;
        private SelectedOptionType _selectedOption;
        public SelectedOptionType SelectedOption
        {
            get
            {
                return _selectedOption;
            }
            set
            {
                _selectedOption = value;
                foreach (var config in _buttonConfigs)
                {
                    if (config.Key == _selectedOption)
                    {
                        config.Value.button.Foreground = HighlightBrush;
                        AppCanvas.EditingMode = config.Value.editingMode;
                    }
                    else
                    {
                        config.Value.button.ClearValue(ForegroundProperty);
                    }
                    config.Value.icon.Icon = (config.Key == _selectedOption)
                            ? config.Value.filled
                            : config.Value.regular;
                }
            }
        }
        public void SetMouseCursorToScreenCenter()
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);

            double dpiScaleX = dpi.DpiScaleX;
            double dpiScaleY = dpi.DpiScaleY;

            double physicalScreenWidth = SystemParameters.PrimaryScreenWidth * dpiScaleX;
            double physicalScreenHeight = SystemParameters.PrimaryScreenHeight * dpiScaleY;

            int centerX = (int)(physicalScreenWidth / 2);
            int centerY = (int)(physicalScreenHeight / 2);

            SetCursorPos(centerX, centerY);
        }
        public MainWindow()
        {
            InitializeComponent();
            AppCanvas.DefaultDrawingAttributes.Color = Colors.White;
            AppCanvas.EraserShape = new RectangleStylusShape(50, 50);
            SaveStrokeHistory();
            _buttonConfigs = new Dictionary<SelectedOptionType, (AppBarButton button, FontIcon icon, FontIconData regular, FontIconData filled, InkCanvasEditingMode editingMode)>
            {
                [SelectedOptionType.None] = (NoneButton, NoneButtonIcon, FluentSystemIcons.Cursor_24_Regular, FluentSystemIcons.Cursor_24_Filled, InkCanvasEditingMode.None),
                [SelectedOptionType.Pen] = (PenButton, PenButtonIcon, FluentSystemIcons.Pen_24_Regular, FluentSystemIcons.Pen_24_Filled, InkCanvasEditingMode.Ink),
                [SelectedOptionType.Eraser] = (EraserButton, EraserButtonIcon, FluentSystemIcons.Eraser_24_Regular, FluentSystemIcons.Eraser_24_Filled, InkCanvasEditingMode.EraseByPoint)
            };
            SelectedOption = SelectedOptionType.None;
            AppCanvas.DefaultDrawingAttributes.IgnorePressure = true;
            AppCanvas.DefaultDrawingAttributes.Width = AppCanvas.DefaultDrawingAttributes.Height = 2;
            LoadInk();
            InitializeTimer();
            SetCopyrightTextState(false);
        }

        private DispatcherTimer _saveTimer;
        private void InitializeTimer()
        {
            _saveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2)
            };
            _saveTimer.Tick += Timer_Tick;
            _saveTimer.Start();
        }
        private void Timer_Tick(object sender, EventArgs e)
        {
            SaveInkIfDirty();
        }

        private void PenButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedOption != SelectedOptionType.Pen)
            {
                SelectedOption = SelectedOptionType.Pen;
                SetMouseCursorToScreenCenter();
            }
        }
        private void EraserButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedOption != SelectedOptionType.Eraser)
            {
                SelectedOption = SelectedOptionType.Eraser;
                SetMouseCursorToScreenCenter();
            }
        }

        private void NoneButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedOption != SelectedOptionType.None)
            {
                SelectedOption = SelectedOptionType.None;
                SetMouseCursorToScreenCenter();
            }
        }
        private void SaveStrokeHistory()
        {
            //var clone = AppCanvas.Strokes.Clone();
            //_history.Push(clone);

            //if (_history.Count > MaxHistory)
            //    _history = new Stack<StrokeCollection>(_history.Take(MaxHistory));
        }

        private void AppCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            SaveStrokeHistory();
            _dirty = true;
            RecoveryButton.IsEnabled = AppCanvas.Strokes.Count == 0;
        }
        private void AppCanvas_StrokeErased(object sender, RoutedEventArgs e)
        {
            SaveStrokeHistory();
            _dirty = true;
            RecoveryButton.IsEnabled = AppCanvas.Strokes.Count == 0;
        }
        private void Undo()
        {
            if (_history.Count > 1)
            {
                _history.Pop();
                var previous = _history.Peek();
                AppCanvas.Strokes = previous.Clone();
            }
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            Undo();
        }

        private void CommandBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            foreach (var config in _buttonConfigs)
            {
                config.Value.button.FlyoutOpeningMode = (config.Key == _selectedOption)
                        ? FlyoutOpeningMode.Click
                        : FlyoutOpeningMode.None;
            }
        }

        private void CommandBar_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            foreach (var config in _buttonConfigs)
            {
                config.Value.button.FlyoutOpeningMode = (config.Key == _selectedOption)
                        ? FlyoutOpeningMode.Click
                        : FlyoutOpeningMode.None;
            }
        }
        private bool isDragging = false;
        private Point startPoint;
        private const int clearThreshold = 160;
        private void ClearSliderDown(Point position)
        {
            isDragging = true;
            startPoint = position;
            ClearSliderContainer.CaptureMouse();
        }
        private void ClearSliderUp(Point position)
        {
            if (!isDragging) return;

            double offsetX = position.X - startPoint.X;
            isDragging = false;
            ClearSliderContainer.ReleaseMouseCapture();

            if (offsetX > clearThreshold)
            {
                SaveInk();
                AppCanvas.Strokes.Clear();
                RecoveryButton.IsEnabled = true;
                SaveStrokeHistory();
                EraserFlyout.Hide();
            }
            ContentText.Margin = new Thickness(8, 8, 8, 8);
            ContentText.Text = ">>> 拖动以清屏";
            SelectedOption = SelectedOptionType.None;
        }
        private void ClearSliderMouseMove(Point endPoint)
        {
            if (!isDragging) return;

            double totalOffsetX = endPoint.X - startPoint.X;

            if (totalOffsetX > clearThreshold)
            {
                ContentText.Text = ">>> 松开以清屏";
            }
            else
            {
                ContentText.Text = ">>> 拖动以清屏";
            }
            ContentText.Margin = new Thickness(8 + Math.Max(totalOffsetX, 0),
                                                   8, 8, 8);
        }

        private void ClearSliderContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ClearSliderDown(e.GetPosition(null));
            e.Handled = true;
        }

        private void ClearSliderContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ClearSliderUp(e.GetPosition(null));
            e.Handled = true;
        }

        private void ClearSliderContainer_MouseMove(object sender, MouseEventArgs e)
        {
            ClearSliderMouseMove(e.GetPosition(null));
            e.Handled = true;
        }

        private void ClearSliderContainer_TouchDown(object sender, TouchEventArgs e)
        {
            ClearSliderDown(e.GetTouchPoint(null).Position);
            e.Handled = true;
        }
        private void ClearSliderContainer_TouchUp(object sender, TouchEventArgs e)
        {
            ClearSliderUp(e.GetTouchPoint(null).Position);
            e.Handled = true;
        }

        private void ClearSliderContainer_TouchMove(object sender, TouchEventArgs e)
        {
            ClearSliderMouseMove(e.GetTouchPoint(null).Position);
            e.Handled = true;
        }

        private void ColorSelector_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is System.Windows.Shapes.Rectangle rect)
            {
                var brush = rect.Fill as SolidColorBrush;
                if (brush != null)
                {
                    AppCanvas.DefaultDrawingAttributes.Color = brush.Color;
                    // 用户自己挑了调色板颜色 → 「Agent 选择的颜色」那行立刻收起
                    HideAgentColor();
                }
            }
        }

        private void PenSizeSelector_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FrameworkElement elem)
            {
                switch (elem.Tag)
                {
                    case "1":
                        AppCanvas.DefaultDrawingAttributes.Height = AppCanvas.DefaultDrawingAttributes.Width = 2;
                        break;
                    case "2":
                        AppCanvas.DefaultDrawingAttributes.Height = AppCanvas.DefaultDrawingAttributes.Width = 4;
                        break;
                    case "3":
                        AppCanvas.DefaultDrawingAttributes.Height = AppCanvas.DefaultDrawingAttributes.Width = 8;
                        break;
                        //case "1":
                        //    InkWeightBase = 3;
                        //    break;
                        //case "2":
                        //    InkWeightBase = 6;
                        //    break;
                        //case "3":
                        //    InkWeightBase = 9;
                        //    break;
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
            SaveInkIfDirty();
            if (_isDown)
            {
                ToggleTransform();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveInk();
        }
        private string GetStoragePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "GridBoard");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            return Path.Combine(folder, "save");
        }
        /// <summary>
        /// 保存笔迹。写临时文件 → 强制落盘 → 原子替换，断电时只会留下完整的旧档或完整的新档，
        /// 不会出现被截断的半截存档。被替换下来的旧档自动留成 save.bak。
        /// </summary>
        public bool SaveInk()
        {
            // 空板不覆盖存档：这样「恢复清屏」还能把清屏前的内容找回来
            if (AppCanvas.Strokes.Count == 0)
                return true;

            string filePath = GetStoragePath();
            string tempPath = filePath + ".tmp";
            string backupPath = filePath + ".bak";

            try
            {
                // 先在内存里序列化好：这样不管 StrokeCollection.Save 是否动过流，
                // 写文件那一步都是我们自己的完整字节
                byte[] payload;
                using (var buffer = new MemoryStream())
                {
                    // 将 InkCanvas 的所有笔画序列化到文件
                    AppCanvas.Strokes.Save(buffer);
                    payload = buffer.ToArray();
                }

                using (FileStream fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(payload, 0, payload.Length);

                    // using 结束只是把数据交给系统缓存；断电时缓存里的东西会没。
                    // Flush(true) 才是真正写进磁盘。
                    fs.Flush(true);
                }

                if (File.Exists(filePath))
                {
                    // 原子替换：同一卷上要么旧档完整、要么新档完整
                    File.Replace(tempPath, filePath, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }

                _dirty = false;
                LastSaved.Text = "上次保存\n" + DateTime.Now.ToString("HH:mm:ss");
                return true;
            }
            catch
            {
                TryDeleteFile(tempPath);
                LastSaved.Text = "保存失败";
                return false;
            }
        }

        /// <summary>只在画布有改动时保存；定时器和关窗走这个。</summary>
        private void SaveInkIfDirty()
        {
            if (_dirty)
                SaveInk();
        }

        public void LoadInk()
        {
            string filePath = GetStoragePath();

            if (TryLoadInk(filePath, null))
                return;

            // 主存档坏了（断电、磁盘满…）就用上一次保存时的备份
            if (TryLoadInk(filePath + ".bak", "已从备份恢复 {0} 条笔迹"))
                return;

            if (File.Exists(filePath) || File.Exists(filePath + ".bak"))
                LastSaved.Text = "存档读取失败";
        }

        /// <summary>
        /// 读一个存档文件。文件不存在或内容损坏时返回 false，不抛异常。
        /// </summary>
        private bool TryLoadInk(string path, string successFormat)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // 从文件流创建 StrokeCollection 并赋值给 InkCanvas
                    AppCanvas.Strokes = new StrokeCollection(fs);
                }

                _dirty = false;
                LastSaved.Text = successFormat == null
                    ? string.Format("已加载 {0} 条笔迹", AppCanvas.Strokes.Count)
                    : string.Format(successFormat, AppCanvas.Strokes.Count);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 删不掉就留着，下次保存会被覆盖
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 关窗口前把没保存的改动写掉（以前这里是注释掉的，会丢最多 2 分钟）
            SaveInkIfDirty();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 双击灰色区域恢复屏幕
            DoubleTapBehavior.AddDoubleTapHandler(GrayPanel, Border_DoubleTap);

            Topmost = true;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Topmost = false;

            TryStartMcpServer();
        }

        private void RecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            LoadInk();
        }
        // ==================== 视图状态（下降屏幕 / 局部放大，互斥） ====================
        //
        // 「下降屏幕」和「局部放大」共用同一组变换（GrayPanel.Height + Scale + Translate），
        // 两者互斥：同一时刻只有一个激活，切换时一趟动画从当前位置直接走到新目标
        // （不先复位再播放，WPF 的 BeginAnimation 天然从当前渲染值补间）。
        //
        // 状态标志只由下面两个 Apply 方法维护，按钮处理器不直接碰它们 ——
        // 否则标志位和实际动画会脱节。

        private const double AnimMs = 555;

        private bool _isDown;
        private bool _isMagnified;
        private int _magnifyRegion = 4;   // 默认正中格

        private static PowerEase ViewEase()
        {
            return new PowerEase { EasingMode = EasingMode.EaseOut, Power = 6 };
        }

        private static DoubleAnimation Anim(double to)
        {
            return new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(AnimMs),
                EasingFunction = ViewEase()
            };
        }

        /// <summary>
        /// 唯一写变换的地方：按当前状态算出最终形态，一趟动画写进去。
        /// </summary>
        /// <returns>
        /// 这趟动画里代表性的那条（GrayPanel 高度，五条时长/缓动完全相同，
        /// 等它一条就是等全部），供调用方等待动画结束；尺寸未就绪时返回 null。
        /// </returns>
        private DoubleAnimation ApplyViewState()
        {
            // 按钮文字/图标先更新 —— 它与布局无关，
            // 不能因为尺寸还没就绪（窗口尚未布局完）就跟动画一起被跳过。
            RefreshViewButtons();

            double w = ActualWidth;
            double h = MW.ActualHeight;
            if (w <= 0 || h <= 0)
                return null;   // 尺寸还没就绪，这次不动画（等下一次调用）

            // ---- 默认：什么都没开 ----
            double grayHeight = 0;
            double scale = 1;
            double tx = 0;
            double ty = 0;

            if (_isDown)
            {
                // 下降屏幕：内容下移半屏，灰色区域占上半屏（双击它可恢复）
                grayHeight = h * 0.5;
                ty = h * 0.5;
            }
            else if (_isMagnified && _magnifyRegion >= 0 && _magnifyRegion < 9)
            {
                // 局部放大：等比放大到铺满，并把选中格的中心对准屏幕正中。
                // 变换链是 TransformGroup[Scale → Translate]，实测矩阵 (s,0,0,s,tx,ty)，
                // 平移量是最后叠加的屏幕像素，不会被缩放放大 → screen = p × s + t
                double cx = CellCenters[_magnifyRegion % 3];
                double cy = CellCenters[_magnifyRegion / 3];

                // 等比放大，宽高比不变 —— 九等分的每格尺寸相同，倍数与选的是哪一格无关。
                scale = ZoomScaleFactor;

                tx = w * (0.5 - cx * scale);
                ty = h * (0.5 - cy * scale) + 120 * (2 - _magnifyRegion / 3);

                // 放大时灰条收回 —— 它本来就是用来取消下降屏幕的
                grayHeight = 0;
            }

            var grayAnim = Anim(grayHeight);
            GrayPanel.BeginAnimation(FrameworkElement.HeightProperty, grayAnim);
            ZoomScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(scale));
            ZoomScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(scale));
            SlideTransform.BeginAnimation(TranslateTransform.XProperty, Anim(tx));
            SlideTransform.BeginAnimation(TranslateTransform.YProperty, Anim(ty));
            return grayAnim;
        }

        /// <summary>按钮的文字与图标跟着状态走，集中在同一处更新。</summary>
        private void RefreshViewButtons()
        {
            HalfScreenButton.Label = _isDown ? "恢复屏幕" : "下降屏幕";
            HalfScreenButtonIcon.Icon = _isDown
                ? FluentSystemIcons.ArrowUp_24_Regular
                : FluentSystemIcons.ArrowDown_24_Regular;

            MagnifyButton.Label = _isMagnified ? "恢复屏幕" : "局部放大";
            MagnifyButtonIcon.Icon = _isMagnified
                ? FluentSystemIcons.ZoomOut_24_Regular
                : FluentSystemIcons.ZoomIn_24_Regular;
        }

        /// <summary>
        /// 下降屏幕的开关（互斥：开它就会关掉局部放大）。
        /// </summary>
        /// <returns>这趟动画的代表条，调用方需要等动画结束时用；否则忽略即可。</returns>
        private DoubleAnimation ApplyDownScreen(bool down)
        {
            _isDown = down;
            if (down)
                _isMagnified = false;   // 互斥：顶掉放大

            return ApplyViewState();
        }

        private void ToggleTransform()
        {
            ApplyDownScreen(!_isDown);
        }

        private void HalfScreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTransform();
        }

        private void Border_DoubleTap(object sender, DoubleTapRoutedEventArgs e)
        {
            // 双击灰条 = 取消下降屏幕（灰条在放大状态下是收回的，所以这里只会关掉下降）
            if (_isDown)
                ApplyDownScreen(false);
        }

        // ==================== 局部放大 ====================

        // 3×3 九等分的格子中心
        private static readonly double[] CellCenters = { 1.0 / 4.2, 0.5, 3.2 / 4.2 };

        // 局部放大的等比放大率。
        // 0.9 × 铺满所需的 3 倍 = 2.700：画面约占屏幕 90%（每边留 10% 余量），
        // 免得把网格线正好切在屏幕边缘上。
        private const double ZoomScaleFactor = 1.5;

        private void MagnifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMagnified)
            {
                MagnifyButton.FlyoutOpeningMode = FlyoutOpeningMode.None;
                ApplyMagnify(-1);
            }
            else
            {
                MagnifyButton.FlyoutOpeningMode = FlyoutOpeningMode.Click;
            }
        }

        private void MagnifyRegionSelector_ItemClick(object sender, ItemClickEventArgs e)
        {
            var border = e.ClickedItem as Border;
            if (border == null)
                return;

            int index;
            if (!int.TryParse(Convert.ToString(border.Tag), out index))
                return;

            _magnifyRegion = index;

            // 先收 Flyout，再启动动画（顺序不能反）
            MagnifyFlyout.Hide();

            ApplyMagnify(index);
        }

        /// <summary>
        /// 局部放大的应用层：region >= 0 表示放大该格并激活；region &lt; 0 表示关闭。
        /// 互斥：开它就会关掉下降屏幕。动画统一由 <see cref="ApplyViewState"/> 下发。
        /// </summary>
        /// <returns>这趟动画的代表条，调用方需要等动画结束时用；否则忽略即可。</returns>
        private DoubleAnimation ApplyMagnify(int region)
        {
            if (region >= 0 && region < 9)
            {
                _magnifyRegion = region;
                _isMagnified = true;
                _isDown = false;          // 互斥：顶掉下降屏幕（灰条一并收回）
            }
            else
            {
                _isMagnified = false;
            }

            return ApplyViewState();
        }
        public static readonly string Version = "1.4.0";
        private void SetCopyrightTextState(bool exporting)
        {
            if (exporting)
            {
                string dateStr = DateTime.Today.ToString("yyyyMMdd");
                VersionText.Text = $"GridBoard {Version} | {dateStr}";
                VersionText.FontSize = 16;
                VersionText.Margin = new Thickness(8);
            }
            else
            {
                VersionText.Text = $"Version {Version} | (c) Copyright CreeperMPG | GitHub:CreeperMPG/GridBoard";
                VersionText.FontSize = 5.5;
                VersionText.Margin = new Thickness(2.5);
            }
        }

        // ==================== MCP 服务 ====================
        //
        // 两个工具：board_set_color（改笔色）、board_screenshot（截图）。
        // 端点配置在 %APPDATA%\GridBoard\mcp.json；启不来只记日志 + 状态提示，不影响黑板本身。

        private GridBoardMcpServer _mcpServer;
        private McpEndpointSettings _mcpSettings;
        private string _mcpStartError;

        private void TryStartMcpServer()
        {
            _mcpSettings = McpEndpointSettings.Load();
            if (!_mcpSettings.Enabled)
                return;

            try
            {
                _mcpServer = new GridBoardMcpServer(new BoardService(this), _mcpSettings);
                _mcpServer.Start();
            }
            catch (Exception ex)
            {
                GridBoardMcpServer.Log("MCP 服务启动失败：" + ex);
                _mcpServer = null;
                _mcpStartError = ex.Message;
            }
        }

        /// <summary>
        /// 凑一份状态快照给对话框：服务在跑就用它自己的统计，
        /// 没在跑（未启用 / 启动失败）也要能说清楚为什么。
        /// </summary>
        private McpStatusSnapshot BuildMcpStatus()
        {
            if (_mcpServer != null)
                return _mcpServer.GetStatus();

            int port = _mcpSettings != null ? _mcpSettings.Port : McpEndpointSettings.DefaultPort;

            string label = "未启用";
            string detail = "mcp.json 里 Enabled = false";
            if (!string.IsNullOrEmpty(_mcpStartError))
            {
                label = "启动失败";
                detail = _mcpStartError;
            }

            return new McpStatusSnapshot
            {
                Running = false,
                StateLabel = label,
                StateDetail = detail,
                Endpoint = "http://127.0.0.1:" + port + "/mcp",
                TokenRequired = _mcpSettings != null && !string.IsNullOrEmpty(_mcpSettings.Token),
                TotalCalls = 0,
                FailedCalls = 0,
                Calls = new List<McpCallRecord>(),
                ConfigPath = McpEndpointSettings.FilePath,
                LogPath = GridBoardMcpServer.LogFilePath
            };
        }

        private async void McpStatusButton_Click(object sender, RoutedEventArgs e)
        {
            await McpStatusDialog.ShowAsync(this, BuildMcpStatus());
        }

        /// <summary>
        /// 截图用：临时收起底部浮层与版本号（MCP 的 show_toolbar=false）。
        /// 与「导出为图片」不同，这里不动视图状态，也不改版权文字。
        /// </summary>
        internal void SetFloatingUiVisible(bool visible)
        {
            Visibility state = visible ? Visibility.Visible : Visibility.Collapsed;
            BottomToolbar.Visibility = state;
            BottomLeftBar.Visibility = state;
            BottomRightBar.Visibility = state;
            VersionText.Visibility = state;
        }

        // ==================== 笔刷浮层里的「Agent 选择的颜色」 ====================
        //
        // 笔色不在 12 色调色板里时（只有 Agent 会设这种颜色），浮层里在色块选择器上方多一行：
        // 左边文字「Agent 选择的颜色」、右边一个 30×30 的色块。
        // 反过来，只要笔色回到调色板里（Agent 设了调色板色，或用户自己点了色块），这行就收起。

        internal void ShowAgentColor(Color color)
        {
            AgentColorChip.Background = new SolidColorBrush(color);
            AgentColorRow.Visibility = Visibility.Visible;
        }

        internal void HideAgentColor()
        {
            AgentColorRow.Visibility = Visibility.Collapsed;
        }

        // ==================== 导出为图片 ====================
        //
        // 流程：恢复正常视图（等 555ms 动画落地）→ 藏起底部浮层与版本号小字
        //       → RenderTargetBitmap 截整个窗口 → SaveFileDialog 选路径写盘
        //       → finally 把浮层放回来（取消/异常也保证放回）。
        // 截完图视图就停在正常状态，不回退原来的「下降屏幕/局部放大」。

        private bool _isExporting;   // 防重入：动画/对话框期间再点一次会乱

        private async void SaveScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isExporting)
                return;
            _isExporting = true;
            try
            {
                // 1. 恢复视图 —— 两者互斥，同一时刻只会命中一个
                bool wasInSpecialView = _isDown || _isMagnified;
                DoubleAnimation restoreAnim = null;
                if (_isDown)
                    restoreAnim = ApplyDownScreen(false);
                else if (_isMagnified)
                    restoreAnim = ApplyMagnify(-1);

                // 2. 等这趟动画走完。挂 Completed 精确等；
                //    兜底超时是防「动画被别处新开的一趟顶掉、Completed 永远不来」把导出卡死。
                if (wasInSpecialView && restoreAnim != null)
                {
                    var finished = new TaskCompletionSource<object>();
                    restoreAnim.Completed += (s, a) => finished.TrySetResult(null);
                    await Task.WhenAny(finished.Task, Task.Delay((int)AnimMs + 100));
                }
                
                // 3. 藏起浮层，补一帧渲染，免得截到收起前的残影
                BottomToolbar.Visibility = Visibility.Collapsed;
                BottomLeftBar.Visibility = Visibility.Collapsed;
                BottomRightBar.Visibility = Visibility.Collapsed;
                SetCopyrightTextState(true);
                MW.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

                // 4. 截窗
                RenderTargetBitmap bmp = CaptureWindowBitmap();

                // 5. 选路径 + 写盘（取消则什么都不做，直接走 finally）
                SaveScreenshot(bmp);
            }
            catch (Exception ex)
            {
                iNKORE.UI.WPF.Modern.Controls.MessageBox.Show(this, "导出图片失败：" + ex.Message, "GridBoard",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BottomToolbar.Visibility = Visibility.Visible;
                BottomLeftBar.Visibility = Visibility.Visible;
                BottomRightBar.Visibility = Visibility.Visible;
                SetCopyrightTextState(false);
                _isExporting = false;
            }
        }

        /// <summary>
        /// 按屏幕物理分辨率截取整个窗口。
        /// 像素尺寸与 DPI 必须配套：像素 = DIP × 缩放、dpi = 96 × 缩放 ——
        /// 这样画布的 DIP 范围正好铺满位图，150% 缩放下也不会截出糊图。
        /// 预览截图（MCP 的 board_screenshot）也走这里，所以标 internal。
        /// </summary>
        internal RenderTargetBitmap CaptureWindowBitmap()
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int pxW = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
            int pxH = (int)Math.Round(ActualHeight * dpi.DpiScaleY);

            var bmp = new RenderTargetBitmap(pxW, pxH,
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bmp.Render(this);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>弹保存框并按所选格式写盘；用户取消时什么都不做。</summary>
        private void SaveScreenshot(BitmapSource bmp)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出为图片",
                Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg|BMP 图片 (*.bmp)|*.bmp",
                AddExtension = false,   // 扩展名自己补：DefaultExt 是固定的，
                                        // 用户选 JPG 不写扩展名时会被补成 .png（内容却是 JPEG）
                FileName = "GridBoard_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            };

            if (dlg.ShowDialog(this) != true)
                return;

            BitmapEncoder encoder;
            string defaultExt;
            switch (dlg.FilterIndex)
            {
                case 2:
                    encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    defaultExt = ".jpg";
                    break;
                case 3:
                    encoder = new BmpBitmapEncoder();
                    defaultExt = ".bmp";
                    break;
                default:
                    encoder = new PngBitmapEncoder();
                    defaultExt = ".png";
                    break;
            }

            string path = dlg.FileName;
            if (string.IsNullOrEmpty(Path.GetExtension(path)))
                path += defaultExt;

            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (_mcpServer != null)
            {
                _mcpServer.Dispose();
                _mcpServer = null;
            }
            App._lockStream?.Dispose();
            Environment.Exit(0);
        }
    }
}
