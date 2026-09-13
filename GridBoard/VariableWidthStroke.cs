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
    public class VariableWidthStroke : Stroke
    {
        private readonly DrawingAttributes _attrs;

        public VariableWidthStroke(StylusPointCollection pts, DrawingAttributes attrs)
            : base(pts)
        {
            _attrs = attrs;
            this.DrawingAttributes = attrs;
        }

        protected override void DrawCore(DrawingContext dc, DrawingAttributes drawingAttributes)
        {
            var attrs = _attrs ?? drawingAttributes;
            VariableWidthHelper.DrawVariableWidthStroke(dc, this.StylusPoints, attrs);
        }
    }
}
