using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GraphCalc.UI;

public sealed class GimbalView : FrameworkElement
{
    private Q _orientation = Q.Identity;

    public void SetOrientation(Q orientation)
    {
        _orientation = orientation.Normalized();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(230, 230, 230)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        Point origin = new(ActualWidth * 0.5, ActualHeight * 0.5);
        DrawAxis(dc, origin, 0.95, 0, 0, Colors.Red, "X");
        DrawAxis(dc, origin, 0, 0.95, 0, Colors.Green, "Y");
        DrawAxis(dc, origin, 0, 0, 0.95, Colors.Blue, "Z");
    }

    private void DrawAxis(DrawingContext dc, Point origin, double x, double y, double z, Color color, string label)
    {
        Point end = Project(x, y, z);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, 2.4);
        pen.Freeze();
        dc.DrawLine(pen, origin, end);
        dc.DrawEllipse(brush, null, end, 4, 4);
        DrawLabel(dc, label, end.X + 4, end.Y - 12, brush, 12);
    }

    private Point Project(double x, double y, double z)
    {
        V3 rotated = _orientation.Rotate(new V3(x, y, z));
        double scale = Math.Min(ActualWidth, ActualHeight) * 0.34;
        return new Point(ActualWidth * 0.5 + rotated.X * scale, ActualHeight * 0.5 - rotated.Y * scale);
    }

    private void DrawLabel(DrawingContext dc, string text, double x, double y, Brush brush, double size)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(x, y));
    }
}
