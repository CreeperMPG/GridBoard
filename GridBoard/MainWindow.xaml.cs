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
        private int InkWeightBase = 3;
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
                [SelectedOptionType.None] = (NoneButton, NoneButtonIcon, FluentSystemIcons.ProjectionScreenText_24_Regular, FluentSystemIcons.ProjectionScreenText_24_Filled, InkCanvasEditingMode.None),
                [SelectedOptionType.Pen] = (PenButton, PenButtonIcon, FluentSystemIcons.Pen_24_Regular, FluentSystemIcons.Pen_24_Filled, InkCanvasEditingMode.Ink),
                [SelectedOptionType.Eraser] = (EraserButton, EraserButtonIcon, FluentSystemIcons.Eraser_24_Regular, FluentSystemIcons.Eraser_24_Filled, InkCanvasEditingMode.EraseByPoint)
            };
            SelectedOption = SelectedOptionType.None;
            AppCanvas.DefaultDrawingAttributes.IgnorePressure = true;
            AppCanvas.DefaultDrawingAttributes.Width = AppCanvas.DefaultDrawingAttributes.Height = 3;
            LoadInk();
            InitializeTimer();
            Touch.FrameReported += Touch_FrameReported;
        }

        private bool _isTouching = false;
        private Point _lastTouchPoint;
        private Point? _lastPoint = null;
        private void Touch_FrameReported(object sender, TouchFrameEventArgs e)
        {
            // 获取相对于 AppCanvas 的触摸点集合
            var touchPoints = e.GetTouchPoints(AppCanvas);

            if (touchPoints.Count > 0)
            {
                // InkCanvas 只支持单点，取第一个即可
                var tp = touchPoints[0];

                if (tp.Action == TouchAction.Up)
                {
                    _isTouching = false;
                }
                else
                {
                    _isTouching = true;
                    _lastTouchPoint = tp.Position; // 已经相对于 AppCanvas
                }
            }
            else
            {
                _isTouching = false;
            }
        }
        public Point GetPointerPosition()
        {
            if (_isTouching)
            {
                return _lastTouchPoint;
            }
            else
            {
                return Mouse.GetPosition(AppCanvas);
            }
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
                        AppCanvas.DefaultDrawingAttributes.Height = AppCanvas.DefaultDrawingAttributes.Width = 3;
                        break;
                    case "2":
                        AppCanvas.DefaultDrawingAttributes.Height = AppCanvas.DefaultDrawingAttributes.Width = 6;
                        break;
                    case "3":
                        AppCanvas.DefaultDrawingAttributes.Height = AppCanvas.DefaultDrawingAttributes.Width = 9;
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
        //public void LoadInk()
        //{
        //    try
        //    {
        //        string filePath = GetStoragePath();
        //        if (!File.Exists(filePath))
        //        {
        //            return;
        //        }

        //        using (FileStream fs = new FileStream(filePath, FileMode.Open))
        //        {
        //            // 从文件流创建 StrokeCollection 并赋值给 InkCanvas
        //            AppCanvas.Strokes = new StrokeCollection(fs);
        //        }
        //        LastSaved.Text = "已加载笔迹";
        //    }
        //    catch
        //    {
        //    }
        //}
        public void LoadInk()
        {
            try
            {
                string filePath = GetStoragePath();
                if (!File.Exists(filePath))
                {
                    LastSaved.Text = "文件不存在";
                    return;
                }

                // 看看文件大小，极端情况为 0 说明保存就是空的
                var fi = new FileInfo(filePath);
                System.Diagnostics.Debug.WriteLine($"[LoadInk] 文件大小 = {fi.Length} 字节");

                StrokeCollection loaded;
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    loaded = new StrokeCollection(fs);
                }

                System.Diagnostics.Debug.WriteLine($"[LoadInk] 反序列化得到 {loaded.Count} 条笔画");
                foreach (var s in loaded)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LoadInk]  Stroke: 点数={s.StylusPoints.Count}, " +
                        $"Width={s.DrawingAttributes.Width}, " +
                        $"Color={s.DrawingAttributes.Color}");
                }

                var converted = new StrokeCollection();
                foreach (Stroke s in loaded)
                {
                    if (s is VariableWidthStroke)
                    {
                        converted.Add(s);
                    }
                    else
                    {
                        converted.Add(new VariableWidthStroke(s.StylusPoints, s.DrawingAttributes));
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[LoadInk] 转换后 {converted.Count} 条笔画");

                AppCanvas.Strokes = converted;

                LastSaved.Text = $"已加载 {converted.Count} 条笔迹";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadInk] 异常: {ex}");
                LastSaved.Text = "加载失败：" + ex.Message;
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveInk();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
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
    }
}
