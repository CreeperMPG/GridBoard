using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Windows.Media;

namespace GridBoard
{
    public class VariableWidthDynamicRenderer : DynamicRenderer
    {
        private readonly DrawingAttributes _attrs;

        public VariableWidthDynamicRenderer(DrawingAttributes attrs)
        {
            _attrs = attrs;
        }

        protected override void OnDraw(DrawingContext drawingContext,
                                       StylusPointCollection stylusPoints,
                                       Geometry geometry,
                                       Brush fillBrush)
        {
            VariableWidthHelper.DrawVariableWidthStroke(
                drawingContext, stylusPoints, _attrs);
        }
    }
}
