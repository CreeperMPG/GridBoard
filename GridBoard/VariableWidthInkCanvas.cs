using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace GridBoard
{
    public class VariableWidthInkCanvas : InkCanvas
    {
        public VariableWidthInkCanvas()
        {
            // 用自定义渲染器替换默认的
            this.DynamicRenderer = new VariableWidthDynamicRenderer(this.DefaultDrawingAttributes);
        }

        protected override void OnStrokeCollected(InkCanvasStrokeCollectedEventArgs e)
        {
            // 移除默认 Stroke
            this.Strokes.Remove(e.Stroke);

            // 用自定义 Stroke 替换
            var custom = new VariableWidthStroke(e.Stroke.StylusPoints, this.DefaultDrawingAttributes);
            this.Strokes.Add(custom);

            base.OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(custom));
        }
    }
}
