using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
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
            SaveInk();
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
            RecoveryButton.IsEnabled = AppCanvas.Strokes.Count == 0;
        }
        private void AppCanvas_StrokeErased(object sender, RoutedEventArgs e)
        {
            SaveStrokeHistory();
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
            SaveInk();
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
        public bool SaveInk()
        {
            try
            {
                if (AppCanvas.Strokes.Count != 0)
                {
                    string filePath = GetStoragePath();
                    using (FileStream fs = new FileStream(filePath, FileMode.Create))
                    {
                        // 将 InkCanvas 的所有笔画序列化到文件
                        AppCanvas.Strokes.Save(fs);
                    }
                    LastSaved.Text = "上次保存\n" + DateTime.Now.ToString("HH:mm:ss");
                }
                return true;
            }
            catch
            {
                LastSaved.Text = "保存失败";
                return false;
            }
        }
        public void LoadInk()
        {
            try
            {
                string filePath = GetStoragePath();
                if (!File.Exists(filePath))
                {
                    return;
                }

                using (FileStream fs = new FileStream(filePath, FileMode.Open))
                {
                    // 从文件流创建 StrokeCollection 并赋值给 InkCanvas
                    AppCanvas.Strokes = new StrokeCollection(fs);
                }
                LastSaved.Text = $"已加载 {AppCanvas.Strokes.Count} 条笔迹";
            }
            catch
            {
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveInk();
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
        private void ApplyViewState()
        {
            // 按钮文字/图标先更新 —— 它与布局无关，
            // 不能因为尺寸还没就绪（窗口尚未布局完）就跟动画一起被跳过。
            RefreshViewButtons();

            double w = ActualWidth;
            double h = MW.ActualHeight;
            if (w <= 0 || h <= 0)
                return;   // 尺寸还没就绪，这次不动画（等下一次调用）

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

            GrayPanel.BeginAnimation(FrameworkElement.HeightProperty, Anim(grayHeight));
            ZoomScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(scale));
            ZoomScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(scale));
            SlideTransform.BeginAnimation(TranslateTransform.XProperty, Anim(tx));
            SlideTransform.BeginAnimation(TranslateTransform.YProperty, Anim(ty));
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

        /// <summary>下降屏幕的开关（互斥：开它就会关掉局部放大）。</summary>
        private void ApplyDownScreen(bool down)
        {
            _isDown = down;
            if (down)
                _isMagnified = false;   // 互斥：顶掉放大

            ApplyViewState();
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
        private void ApplyMagnify(int region)
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

            ApplyViewState();
        }
    }
}
