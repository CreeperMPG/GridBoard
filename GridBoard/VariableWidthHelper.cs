using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace GridBoard
{
    internal static class VariableWidthHelper
    {
        /// <summary>最细时的宽度相对于基准宽度的比例</summary>
        public const double MinWidthRatio = 0.25;

        /// <summary>
        /// 距离阈值：当相邻两点距离达到该值时，笔迹取最细
        /// 数值越大 -> 需要越快才变细
        /// </summary>
        public const double SpeedFactor = 32.0;

        /// <summary>平滑窗口半径（点左右各取几个点做平均）</summary>
        public const int SmoothWindow = 3;

        /// <summary>
        /// 在给定的 DrawingContext 上绘制一段"变粗细"的笔迹
        /// </summary>
        public static void DrawVariableWidthStroke(
            DrawingContext dc,
            StylusPointCollection points,
            DrawingAttributes attrs)
        {
            if (points == null || points.Count < 2) return;

            double baseWidth = attrs.Width;
            double minWidth = baseWidth * MinWidthRatio;

            // ---------- 第一步：计算每个点的瞬时宽度 ----------
            var rawWidths = new double[points.Count];
            rawWidths[0] = baseWidth;

            for (int i = 1; i < points.Count; i++)
            {
                var p1 = points[i - 1];
                var p2 = points[i];

                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                // 距离越大 -> factor 越小 -> 越细
                double factor = 1.0 - Math.Min(dist / SpeedFactor, 1.0);
                rawWidths[i] = minWidth + (baseWidth - minWidth) * factor;
            }

            // ---------- 第二步：滑动平均，去除抖动 ----------
            var smoothed = new double[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                double sum = 0;
                int count = 0;
                int from = Math.Max(0, i - SmoothWindow);
                int to = Math.Min(points.Count - 1, i + SmoothWindow);
                for (int j = from; j <= to; j++)
                {
                    sum += rawWidths[j];
                    count++;
                }
                smoothed[i] = sum / count;
            }

            // ---------- 第三步：逐段绘制 ----------
            var brush = new SolidColorBrush(attrs.Color);
            brush.Freeze(); // 性能优化

            for (int i = 0; i < points.Count - 1; i++)
            {
                // 相邻两点宽度取平均，让线段过渡自然
                double w = (smoothed[i] + smoothed[i + 1]) * 0.5;
                if (w < 0.1) w = 0.1;

                var pen = new Pen(brush, w)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();

                dc.DrawLine(pen, points[i].ToPoint(), points[i + 1].ToPoint());
            }
        }
    }
}
