using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GraphCalc.Shared;

namespace GraphCalc.UI;

public sealed class ProjectionView : FrameworkElement
{
    private readonly List<SurfaceRenderData> _surfaces = new();
    private GraphRenderStyle _renderStyle = GraphRenderStyle.Mesh;
    private SurfaceQuality _surfaceQuality = SurfaceQuality.Smooth;
    private bool _showSurfaceTriangleEdges;

    public static readonly DependencyProperty PlaneProperty = DependencyProperty.Register(
        nameof(Plane),
        typeof(string),
        typeof(ProjectionView),
        new FrameworkPropertyMetadata("XY", FrameworkPropertyMetadataOptions.AffectsRender));

    public string Plane
    {
        get => (string)GetValue(PlaneProperty);
        set => SetValue(PlaneProperty, value);
    }

    public void SetRenderStyle(GraphRenderStyle style)
    {
        _renderStyle = style;
        InvalidateVisual();
    }

    public void SetSurfaceTriangleEdges(bool enabled)
    {
        _showSurfaceTriangleEdges = enabled;
        InvalidateVisual();
    }

    public void SetSurfaceQuality(SurfaceQuality quality)
    {
        _surfaceQuality = quality;
        InvalidateVisual();
    }

    public void SetSurfaces(IEnumerable<SurfaceRenderData> surfaces)
    {
        _surfaces.Clear();
        _surfaces.AddRange(surfaces);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ActualWidth < 20 || ActualHeight < 20) return;

        foreach (SurfaceRenderData surface in _surfaces.Where(s => s.IsVisible && s.Points.Count > 0))
        {
            if (_renderStyle == GraphRenderStyle.Surface) DrawSurfaceFilled(dc, surface);
            else DrawSurfaceMesh(dc, surface);
        }
        DrawFrameMarkers(dc);
        DrawAxes(dc);
    }

    private void DrawAxes(DrawingContext dc)
    {
        var pen = new Pen(Brushes.Black, 1.0);
        pen.Freeze();
        Point verticalTop = Project(0, TanSpace.AxisMax);
        Point verticalBottom = Project(0, TanSpace.AxisMin);
        dc.DrawLine(pen, verticalBottom, verticalTop);

        Point horizontalLeft = Project(TanSpace.AxisMin, 0);
        Point horizontalRight = Project(TanSpace.AxisMax, 0);
        dc.DrawLine(pen, horizontalLeft, horizontalRight);

        string aLabel = Plane.Length >= 1 ? Plane[0].ToString() : "A";
        string bLabel = Plane.Length >= 2 ? Plane[1].ToString() : "B";
        DrawLabel(dc, aLabel, ActualWidth - 24, ActualHeight * 0.5 + 4, Brushes.Black, 14);
        DrawLabel(dc, bLabel, ActualWidth * 0.5 - 5, 8, Brushes.Black, 14);
    }

    private void DrawFrameMarkers(DrawingContext dc)
    {
        var dotBrush = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
        dotBrush.Freeze();
        var labelBrush = new SolidColorBrush(Color.FromArgb(140, 40, 40, 40));
        labelBrush.Freeze();
        var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(85, 0, 0, 0)), 1.0);
        tickPen.Freeze();

        double[] values = { -TanSpace.HalfPi, -Math.PI / 4.0, 0, Math.PI / 4.0, TanSpace.HalfPi };
        foreach (double theta in values)
        {
            string label = TanSpace.PrettyPi(theta);
            Point bottom = Project(theta, TanSpace.AxisMin);
            Point left = Project(TanSpace.AxisMin, theta);

            dc.DrawLine(tickPen, new Point(bottom.X, bottom.Y - 4), new Point(bottom.X, bottom.Y + 4));
            dc.DrawLine(tickPen, new Point(left.X - 4, left.Y), new Point(left.X + 4, left.Y));

            if (Math.Abs(theta) < 0.000001)
            {
                dc.DrawEllipse(dotBrush, null, Project(0, TanSpace.AxisMin), 2.2, 2.2);
                dc.DrawEllipse(dotBrush, null, Project(0, TanSpace.AxisMax), 2.2, 2.2);
                dc.DrawEllipse(dotBrush, null, Project(TanSpace.AxisMin, 0), 2.2, 2.2);
                dc.DrawEllipse(dotBrush, null, Project(TanSpace.AxisMax, 0), 2.2, 2.2);
            }

            DrawLabel(dc, label, bottom.X - 12, Math.Min(ActualHeight - 14, bottom.Y + 4), labelBrush, 9);
            DrawLabel(dc, label, Math.Max(2, left.X + 2), left.Y - 6, labelBrush, 9);
        }
    }

    private void DrawSurfaceMesh(DrawingContext dc, SurfaceRenderData surface)
    {
        var brush = new SolidColorBrush(surface.Color);
        brush.Freeze();
        var pen = new Pen(brush, 1.0);
        pen.Freeze();

        int n = surface.GridSize;
        if (n <= 1 || surface.Points.Count < n * n) return;

        for (int ix = 0; ix < n; ix++)
        {
            if (ShouldDrawSampleLine(n, ix)) DrawSeries(dc, surface, n, ix, true, pen);
        }
        for (int iy = 0; iy < n; iy++)
        {
            if (ShouldDrawSampleLine(n, iy)) DrawSeries(dc, surface, n, iy, false, pen);
        }
    }

    private void DrawSurfaceFilled(DrawingContext dc, SurfaceRenderData surface)
    {
        int n = surface.GridSize;
        if (n <= 1 || surface.Points.Count < n * n) return;

        var triangles = new List<ProjectionTriangle>();
        for (int ix = 0; ix < n - 1; ix++)
        {
            if (!ShouldDrawSurfaceCellBand(n, ix)) continue;
            for (int iy = 0; iy < n - 1; iy++)
            {
                if (!ShouldDrawSurfaceCellBand(n, iy)) continue;
                int ia = ix * n + iy;
                int ib = (ix + 1) * n + iy;
                int ic = ix * n + iy + 1;
                int id = (ix + 1) * n + iy + 1;
                AddProjectionTriangleIfSafe(surface, n, ia, ib, ic, ix, iy, ix + 1, iy, ix, iy + 1, triangles);
                AddProjectionTriangleIfSafe(surface, n, ib, id, ic, ix + 1, iy, ix + 1, iy + 1, ix, iy + 1, triangles);
            }
        }

        DrawProjectionRaster(dc, triangles, surface.Color);

        if (_showSurfaceTriangleEdges)
        {
            var debugEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(78, 80, 80, 80)), 0.45);
            debugEdgePen.Freeze();
            foreach (ProjectionTriangle triangle in triangles)
            {
                dc.DrawLine(debugEdgePen, triangle.A, triangle.B);
                dc.DrawLine(debugEdgePen, triangle.B, triangle.C);
                dc.DrawLine(debugEdgePen, triangle.C, triangle.A);
            }
        }
    }

    private void AddProjectionTriangleIfSafe(
        SurfaceRenderData surface,
        int n,
        int i1,
        int i2,
        int i3,
        int x1,
        int y1,
        int x2,
        int y2,
        int x3,
        int y3,
        List<ProjectionTriangle> triangles)
    {
        if (i1 < 0 || i2 < 0 || i3 < 0 || i1 >= surface.Points.Count || i2 >= surface.Points.Count || i3 >= surface.Points.Count) return;
        Point3Dto a = surface.Points[i1];
        Point3Dto b = surface.Points[i2];
        Point3Dto c = surface.Points[i3];
        if (!a.Valid || !b.Valid || !c.Valid) return;
        if (!ShouldDrawSegment(surface, a, b, n, x1, y1, x2, y2)) return;
        if (!ShouldDrawSegment(surface, b, c, n, x2, y2, x3, y3)) return;
        if (!ShouldDrawSegment(surface, c, a, n, x3, y3, x1, y1)) return;

        (double a1, double a2) = GetPlaneValues(a);
        (double b1, double b2) = GetPlaneValues(b);
        (double c1, double c2) = GetPlaneValues(c);
        Point pa = Project(a1, a2);
        Point pb = Project(b1, b2);
        Point pc = Project(c1, c2);
        double area = Math.Abs((pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y));
        if (area < 0.03) return;
        double shade = ProjectionShade(a, b, c);
        triangles.Add(new ProjectionTriangle(pa, pb, pc, shade));
    }

    private void DrawProjectionRaster(DrawingContext dc, List<ProjectionTriangle> triangles, Color color)
    {
        if (triangles.Count == 0) return;
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        int supersample = _surfaceQuality == SurfaceQuality.Cinematic ? 2 : 1;
        int rw = width * supersample;
        int rh = height * supersample;
        if (rw <= 0 || rh <= 0 || rw > 2400 || rh > 2400)
        {
            supersample = 1;
            rw = width;
            rh = height;
        }

        int[] pixels = new int[rw * rh];
        // R12: projection panels should read as calm filled footprints, not as stacked
        // semi-transparent sheets. Keep them moderate so repeated edge samples do not
        // accumulate into dirty boundary bands.
        double alpha = _surfaceQuality == SurfaceQuality.Fast ? 0.48 : _surfaceQuality == SurfaceQuality.Cinematic ? 0.58 : 0.52;
        foreach (ProjectionTriangle triangle in triangles)
        {
            RasterizeProjectionTriangle(pixels, rw, rh, supersample, triangle, color, alpha);
        }

        var bitmap = BitmapSource.Create(
            rw,
            rh,
            96 * supersample,
            96 * supersample,
            PixelFormats.Pbgra32,
            null,
            pixels,
            rw * 4);
        bitmap.Freeze();
        dc.DrawImage(bitmap, new Rect(0, 0, width, height));
    }

    private static void RasterizeProjectionTriangle(int[] pixels, int width, int height, int scale, ProjectionTriangle tri, Color color, double alphaScale)
    {
        double x1 = tri.A.X * scale;
        double y1 = tri.A.Y * scale;
        double x2 = tri.B.X * scale;
        double y2 = tri.B.Y * scale;
        double x3 = tri.C.X * scale;
        double y3 = tri.C.Y * scale;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x1, Math.Min(x2, x3))));
        int maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(x1, Math.Max(x2, x3))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(y1, Math.Min(y2, y3))));
        int maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(y1, Math.Max(y2, y3))));
        if (maxX < minX || maxY < minY) return;

        double denom = ((y2 - y3) * (x1 - x3)) + ((x3 - x2) * (y1 - y3));
        if (Math.Abs(denom) < 0.0000001) return;
        int src = ToPremultiplied(color, tri.Shade, alphaScale);
        int srcA = (src >> 24) & 0xff;
        int srcR = (src >> 16) & 0xff;
        int srcG = (src >> 8) & 0xff;
        int srcB = src & 0xff;

        for (int y = minY; y <= maxY; y++)
        {
            double py = y + 0.5;
            int row = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double w1 = (((y2 - y3) * (px - x3)) + ((x3 - x2) * (py - y3))) / denom;
                double w2 = (((y3 - y1) * (px - x3)) + ((x1 - x3) * (py - y3))) / denom;
                double w3 = 1.0 - w1 - w2;
                if (w1 < -0.0006 || w2 < -0.0006 || w3 < -0.0006) continue;

                int index = row + x;
                int dst = pixels[index];
                int invA = 255 - srcA;
                int dstA = (dst >> 24) & 0xff;
                int dstR = (dst >> 16) & 0xff;
                int dstG = (dst >> 8) & 0xff;
                int dstB = dst & 0xff;
                int outA = srcA + (dstA * invA / 255);
                int outR = srcR + (dstR * invA / 255);
                int outG = srcG + (dstG * invA / 255);
                int outB = srcB + (dstB * invA / 255);
                pixels[index] = outB | (outG << 8) | (outR << 16) | (outA << 24);
            }
        }
    }

    private double ProjectionShade(Point3Dto a, Point3Dto b, Point3Dto c)
    {
        (double a1, double a2) = GetPlaneValues(a);
        (double b1, double b2) = GetPlaneValues(b);
        (double c1, double c2) = GetPlaneValues(c);
        double dx = Math.Abs((b1 - a1) + (c1 - a1));
        double dy = Math.Abs((b2 - a2) + (c2 - a2));
        double activity = Math.Min(1.0, (dx + dy) / Math.PI);
        double shade = 0.76 + 0.20 * activity;
        return _surfaceQuality == SurfaceQuality.Cinematic ? Math.Min(1.06, shade + 0.06) : shade;
    }

    private static int ToPremultiplied(Color color, double shade, double alphaScale)
    {
        Color toned = ApplyMaterialTone(color, shade);
        byte a = (byte)Math.Clamp((int)Math.Round(255.0 * alphaScale), 0, 255);
        byte r = (byte)(toned.R * a / 255);
        byte g = (byte)(toned.G * a / 255);
        byte b = (byte)(toned.B * a / 255);
        return b | (g << 8) | (r << 16) | (a << 24);
    }

    private static Color ApplyMaterialTone(Color color, double shade)
    {
        double r = color.R * 0.90 + 255.0 * 0.10;
        double g = color.G * 0.90 + 255.0 * 0.10;
        double b = color.B * 0.90 + 255.0 * 0.10;
        if (shade < 1.0)
        {
            r *= shade;
            g *= shade;
            b *= shade;
        }
        else
        {
            double t = Math.Min(1.0, (shade - 1.0) * 0.75);
            r += (255.0 - r) * t;
            g += (255.0 - g) * t;
            b += (255.0 - b) * t;
        }
        return Color.FromRgb(
            (byte)Math.Clamp((int)Math.Round(r), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b), 0, 255));
    }

    private void DrawTriangleIfSafe(
        DrawingContext dc,
        SurfaceRenderData surface,
        int n,
        int i1,
        int i2,
        int i3,
        int x1,
        int y1,
        int x2,
        int y2,
        int x3,
        int y3,
        Brush brush,
        Pen? debugEdgePen)
    {
        if (i1 < 0 || i2 < 0 || i3 < 0 || i1 >= surface.Points.Count || i2 >= surface.Points.Count || i3 >= surface.Points.Count) return;
        Point3Dto a = surface.Points[i1];
        Point3Dto b = surface.Points[i2];
        Point3Dto c = surface.Points[i3];
        if (!a.Valid || !b.Valid || !c.Valid) return;
        if (!ShouldDrawSegment(surface, a, b, n, x1, y1, x2, y2)) return;
        if (!ShouldDrawSegment(surface, b, c, n, x2, y2, x3, y3)) return;
        if (!ShouldDrawSegment(surface, c, a, n, x3, y3, x1, y1)) return;

        (double a1, double a2) = GetPlaneValues(a);
        (double b1, double b2) = GetPlaneValues(b);
        (double c1, double c2) = GetPlaneValues(c);
        Point pa = Project(a1, a2);
        Point pb = Project(b1, b2);
        Point pc = Project(c1, c2);
        double area = Math.Abs((pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y));
        if (area < 0.03) return;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(pa, true, true);
            ctx.LineTo(pb, true, false);
            ctx.LineTo(pc, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, debugEdgePen, geometry);
    }

    private static Color BlendTowardWhite(Color color, double whiteMix)
    {
        byte Mix(byte value)
        {
            return (byte)Math.Clamp((int)Math.Round(value * (1.0 - whiteMix) + 255.0 * whiteMix), 0, 255);
        }
        return Color.FromArgb(255, Mix(color.R), Mix(color.G), Mix(color.B));
    }

    private static bool ShouldDrawSampleLine(int n, int index)
    {
        if (n <= 193) return true;
        double u = n <= 1 ? 0.0 : -1.0 + (2.0 * index / (n - 1.0));
        double abs = Math.Abs(u);

        if (abs >= 0.64) return true;
        if (Math.Abs(index - (n / 2)) <= 1) return true;

        int centerStep = n >= 513 ? 4 : n >= 385 ? 3 : 2;
        return index % centerStep == 0;
    }

    private static bool ShouldDrawSurfaceCellBand(int n, int index)
    {
        if (n <= 257) return true;
        double u = n <= 1 ? 0.0 : -1.0 + (2.0 * index / (n - 1.0));
        double abs = Math.Abs(u);
        if (abs >= 0.64) return true;
        if (Math.Abs(index - (n / 2)) <= 1) return true;
        int centerStep = n >= 513 ? 3 : 2;
        return index % centerStep == 0;
    }

    private void DrawSeries(DrawingContext dc, SurfaceRenderData surface, int n, int fixedIndex, bool fixedX, Pen pen)
    {
        Point? lastProjected = null;
        Point3Dto? lastData = null;
        int lastIx = -1;
        int lastIy = -1;

        for (int i = 0; i < n; i++)
        {
            int ix = fixedX ? fixedIndex : i;
            int iy = fixedX ? i : fixedIndex;
            int index = ix * n + iy;
            if (index < 0 || index >= surface.Points.Count) break;
            Point3Dto p = surface.Points[index];
            if (!p.Valid)
            {
                lastProjected = null;
                lastData = null;
                lastIx = -1;
                lastIy = -1;
                continue;
            }

            (double a, double b) = GetPlaneValues(p);
            Point projected = Project(a, b);
            if (lastProjected.HasValue && lastData != null && ShouldDrawSegment(surface, lastData, p, n, lastIx, lastIy, ix, iy))
            {
                dc.DrawLine(pen, lastProjected.Value, projected);
            }
            lastProjected = projected;
            lastData = p;
            lastIx = ix;
            lastIy = iy;
        }
    }

    private static bool ShouldDrawSegment(SurfaceRenderData surface, Point3Dto? a, Point3Dto b, int n, int ax, int ay, int bx, int by)
    {
        if (a == null || !a.Valid || !b.Valid) return false;

        int gridDistance = Math.Abs(bx - ax) + Math.Abs(by - ay);
        if (gridDistance <= 0 || gridDistance > 3) return false;

        double dx = TanSpace.ToNormalized(b.X) - TanSpace.ToNormalized(a.X);
        double dy = TanSpace.ToNormalized(b.Y) - TanSpace.ToNormalized(a.Y);
        double dz = TanSpace.ToNormalized(b.Z) - TanSpace.ToNormalized(a.Z);
        double xyLength = Math.Sqrt(dx * dx + dy * dy);
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double zJump = Math.Abs(dz);

        bool splitMode = string.Equals(surface.RenderMode, "SplitWireframe", StringComparison.OrdinalIgnoreCase);
        bool clippedMode = string.Equals(surface.RenderMode, "ClippedWireframe", StringComparison.OrdinalIgnoreCase);
        double zJumpLimit = splitMode ? 0.95 : 1.55;
        double lengthLimit = clippedMode ? 2.10 : 1.75;

        if (zJump > zJumpLimit && zJump > xyLength * (splitMode ? 8.0 : 20.0)) return false;
        if (length > lengthLimit && zJump > xyLength * (splitMode ? 6.0 : 12.0)) return false;
        return true;
    }

    private (double A, double B) GetPlaneValues(Point3Dto p)
    {
        return Plane.ToUpperInvariant() switch
        {
            "XZ" => (p.X, p.Z),
            "YZ" => (p.Y, p.Z),
            _ => (p.X, p.Y)
        };
    }

    private Point Project(double a, double b)
    {
        double margin = 20;
        double width = Math.Max(1, ActualWidth - margin * 2);
        double height = Math.Max(1, ActualHeight - margin * 2);
        double x = margin + (a - TanSpace.AxisMin) / (TanSpace.AxisMax - TanSpace.AxisMin) * width;
        double y = ActualHeight - margin - (b - TanSpace.AxisMin) / (TanSpace.AxisMax - TanSpace.AxisMin) * height;
        return new Point(x, y);
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
    private sealed record ProjectionTriangle(Point A, Point B, Point C, double Shade);

}
