using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace GridBoard
{
    public enum DoubleTapInput
    {
        Mouse,
        Stylus,
        Touch
    }

    public class DoubleTapRoutedEventArgs : RoutedEventArgs
    {
        public DoubleTapRoutedEventArgs()
        {
        }

        public DoubleTapRoutedEventArgs(RoutedEvent routedEvent, object source)
            : base(routedEvent, source)
        {
        }

        /// <summary>来源：鼠标 / 笔 / 手指。</summary>
        // 注意：不要叫 Source —— RoutedEventArgs 已经有 Source 了
        public DoubleTapInput InputKind { get; set; }

        /// <summary>抬起点在源元素坐标系里的位置。</summary>
        public Point Position { get; set; }
    }

    public delegate void DoubleTapEventHandler(object sender, DoubleTapRoutedEventArgs e);

    /// <summary>
    /// 给任意 UIElement（含 Border）附加「双击 / 双击触摸」事件。
    /// 鼠标走 ClickCount==2，触摸走时间+位移自判，两条路径互不干扰。
    ///
    /// 用法（两种都行，互相独立，也可以同时用）：
    ///   XAML：  local:DoubleTapBehavior.IsEnabled="True"
    ///   代码：  DoubleTapBehavior.AddDoubleTapHandler(element, OnDoubleTap);
    /// </summary>
    /// <remarks>
    /// ⚠️⚠️ 不要写 local:DoubleTapBehavior.DoubleTap="OnDoubleTap" ⚠️⚠️
    ///
    /// WPF 的 XAML 编译器【不会】把「附加路由事件」接到处理器上，而且【不报错】。
    /// 实测（.NET Framework 4.7.2）：写了那行属性，构建照样成功，
    /// 但生成的 obj\Debug\MainWindow.g.cs 里连一行接线代码都没有 ——
    /// 表现就是「双击毫无反应」，且极难定位。
    ///
    /// 判断方法：构建后去 obj\Debug\MainWindow.g.cs 里搜方法名，搜不到就是没接上。
    ///
    /// 这里用 <see cref="IsEnabledProperty"/> 这个【附加属性】代替它 ——
    /// 附加属性是 XAML 编译器确定支持的。
    /// </remarks>
    public static class DoubleTapBehavior
    {
        [DllImport("user32.dll")]
        private static extern int GetDoubleClickTime();

        // ---------------- 容差常量（鼠标和手指不是一个量级） ----------------

        // SystemParameters.MinimumHorizontalDragDistance 就是 SM_CXDRAG，默认 4px —— 那是给鼠标的。
        // 手指按一下的抖动轻松超过 4px，两次 tap 更不可能落在 4px 以内。
        // 用鼠标的阈值去判触摸，会让每一次 tap 都被当成「拖动」→ 触屏 100% 触发不了。
        private const double TouchTapSlop = 16.0;         // 单次 tap：按下 → 抬起的最大位移
        private const double TouchDoubleTapSlop = 48.0;   // 两次 tap 之间：抬起点的最大距离

        // ------------------------- 事件 -------------------------

        public static readonly RoutedEvent DoubleTapEvent =
            EventManager.RegisterRoutedEvent(
                "DoubleTap",
                RoutingStrategy.Bubble,
                typeof(DoubleTapEventHandler),
                typeof(DoubleTapBehavior));

        // ---------- 挂钩开关（XAML 走这个） ----------

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(DoubleTapBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value)
        {
            element.SetValue(IsEnabledProperty, value);
        }

        public static bool GetIsEnabled(DependencyObject element)
        {
            return (bool)element.GetValue(IsEnabledProperty);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ui = d as UIElement;
            if (ui == null)
                return;

            if ((bool)e.NewValue)
                Hook(ui);
            else
                Unhook(ui);
        }

        // ---------- 订阅（代码走这个，顺带也会挂钩子） ----------

        public static void AddDoubleTapHandler(DependencyObject element, DoubleTapEventHandler handler)
        {
            var ui = element as UIElement;
            if (ui == null)
                return;

            Hook(ui);

            // handledEventsToo: true —— 不接受已处理事件的话，
            // 一旦上游有人标记 Handled，这个事件就永远收不到。
            ui.AddHandler(DoubleTapEvent, handler, true);
        }

        public static void RemoveDoubleTapHandler(DependencyObject element, DoubleTapEventHandler handler)
        {
            var ui = element as UIElement;
            if (ui == null)
                return;

            ui.RemoveHandler(DoubleTapEvent, handler);
            Unhook(ui);
        }

        // ------------------------- 内部状态 -------------------------

        // 状态挂在元素自己的附加属性上，避免静态字典导致元素泄漏。
        private sealed class HookState
        {
            public int Count;                              // 引用计数：IsEnabled 与 AddDoubleTapHandler 共用
            public bool IsDown;                            // 手指当前是否按着
            public Point DownPoint;                        // 按下位置
            public DateTime LastUpUtc = DateTime.MinValue; // 上一次完整点击抬起的时间
            public Point LastUpPoint;                      // 上一次完整点击抬起的位置
        }

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State",
                typeof(HookState),
                typeof(DoubleTapBehavior),
                new PropertyMetadata(null));

        // 用 AddHandler/RemoveHandler 时必须持有同一个委托实例，否则摘不掉。
        private static readonly MouseButtonEventHandler MouseDownHandler = OnPreviewMouseLeftButtonDown;
        private static readonly EventHandler<TouchEventArgs> TouchDownHandler = OnPreviewTouchDown;
        private static readonly EventHandler<TouchEventArgs> TouchUpHandler = OnPreviewTouchUp;

        private static void Hook(UIElement element)
        {
            var state = (HookState)element.GetValue(StateProperty);

            if (state == null)
            {
                state = new HookState();
                element.SetValue(StateProperty, state);

                // 触摸别被「按住不放」和「轻扫(flick)」手势抢走
                Stylus.SetIsPressAndHoldEnabled(element, false);
                Stylus.SetIsFlicksEnabled(element, false);

                element.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, MouseDownHandler, true);
                element.AddHandler(UIElement.PreviewTouchDownEvent, TouchDownHandler, true);
                element.AddHandler(UIElement.PreviewTouchUpEvent, TouchUpHandler, true);

                Log("Hook() 完成：" + element.GetType().Name);
            }

            state.Count++;
        }

        private static void Unhook(UIElement element)
        {
            var state = (HookState)element.GetValue(StateProperty);
            if (state == null)
                return;

            state.Count--;
            if (state.Count > 0)
                return;

            element.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, MouseDownHandler);
            element.RemoveHandler(UIElement.PreviewTouchDownEvent, TouchDownHandler);
            element.RemoveHandler(UIElement.PreviewTouchUpEvent, TouchUpHandler);
            element.ClearValue(StateProperty);
        }

        // ------------------------- 鼠标 / 笔 -------------------------

        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as UIElement;
            if (element == null)
                return;

            // 触摸会被 WPF「提升」成鼠标事件，那批要跳过，否则和 Touch 分支重复触发。
            // ⚠️ 只跳过 TabletDeviceType.Touch —— 真正的笔(TabletDeviceType.Stylus)
            //    也必须能双击，不能被一起滤掉。
            var stylus = e.StylusDevice;
            if (stylus != null && stylus.TabletDevice != null &&
                stylus.TabletDevice.Type == TabletDeviceType.Touch)
                return;

            if (e.ClickCount != 2)
                return;

            var kind = e.StylusDevice != null ? DoubleTapInput.Stylus : DoubleTapInput.Mouse;

            // ⚠️ 顺序要紧：先抛事件，后标记 Handled。
            // 反过来的话，RaiseEvent 出来的事件会落进「当前已 Handled」的上下文，
            // 冒泡回本源元素时被路由引擎滤掉。
            Raise(element, kind, e.GetPosition(element));

            e.Handled = true;   // 双击已成立，别再往下传
        }

        // ------------------------- 触摸 -------------------------

        private static void OnPreviewTouchDown(object sender, TouchEventArgs e)
        {
            var element = sender as UIElement;
            if (element == null)
                return;

            var state = (HookState)element.GetValue(StateProperty);
            if (state == null)
                return;

            state.IsDown = true;
            state.DownPoint = e.GetTouchPoint(element).Position;

            Log("TouchDown  id=" + e.TouchDevice.Id + "  at " + Fmt(state.DownPoint));

            // 刻意【不】设置 e.Handled = true：
            // 一旦吃掉，ScrollViewer 的拖动滚动、内部按钮的点击都会失效。
            // 提升出来的鼠标事件由上面那个 TabletDeviceType 判断挡住就够了。
        }

        private static void OnPreviewTouchUp(object sender, TouchEventArgs e)
        {
            var element = sender as UIElement;
            if (element == null)
                return;

            var state = (HookState)element.GetValue(StateProperty);
            if (state == null || !state.IsDown)
            {
                Log("TouchUp    被忽略（state=" + (state == null ? "null" : "IsDown=false") + "）");
                return;
            }

            state.IsDown = false;

            var upPoint = e.GetTouchPoint(element).Position;
            var upUtc = DateTime.UtcNow;

            // 抬起时离按下太远 → 这是一次拖动/滑动，不算 tap，并清掉双击记忆
            if (!WithinSlop(upPoint, state.DownPoint, TouchTapSlop))
            {
                Log("TouchUp    判为拖动（位移 " + Fmt(upPoint) + " vs " + Fmt(state.DownPoint) + "），双击记忆已清空");
                state.LastUpUtc = DateTime.MinValue;
                return;
            }

            var elapsedMs = (upUtc - state.LastUpUtc).TotalMilliseconds;
            var distance = Distance(upPoint, state.LastUpPoint);
            var isDoubleTap =
                elapsedMs <= GetDoubleClickTime()
                && WithinSlop(upPoint, state.LastUpPoint, TouchDoubleTapSlop);

            Log("TouchUp    at " + Fmt(upPoint)
                + "  距上次 " + elapsedMs.ToString("F0") + "ms（上限 " + GetDoubleClickTime() + "）"
                + "  距离 " + distance.ToString("F1") + "px（上限 " + TouchDoubleTapSlop + "）"
                + "  → " + (isDoubleTap ? "双击成立" : "还是一次单击"));

            if (isDoubleTap)
            {
                state.LastUpUtc = DateTime.MinValue;   // 清掉，三连击不会再来一次
                Raise(element, DoubleTapInput.Touch, upPoint);
            }
            else
            {
                state.LastUpUtc = upUtc;
                state.LastUpPoint = upPoint;
            }
        }

        // ------------------------- 工具方法 -------------------------

        private static void Raise(UIElement element, DoubleTapInput kind, Point position)
        {
            var args = new DoubleTapRoutedEventArgs(DoubleTapEvent, element)
            {
                InputKind = kind,
                Position = position,
                Handled = false          // 显式归零，别继承任何残留状态
            };

            element.RaiseEvent(args);
        }

        // 鼠标用系统阈值，触摸用上面那套更宽松的常量。
        private static bool WithinSlop(Point a, Point b, double slop)
        {
            return Math.Abs(a.X - b.X) <= slop
                && Math.Abs(a.Y - b.Y) <= slop;
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string Fmt(Point p)
        {
            return "(" + p.X.ToString("F1") + "," + p.Y.ToString("F1") + ")";
        }

        // ---------------- 临时诊断（查完就删） ----------------

#if DEBUG
        private static void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GridBoard.DoubleTap.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // 诊断日志绝不能影响功能
            }
        }
#else
        private static void Log(string message)
        {
        }
#endif
    }
}
