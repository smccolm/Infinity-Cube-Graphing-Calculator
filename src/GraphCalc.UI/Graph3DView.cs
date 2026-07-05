using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GraphCalc.Shared;

namespace GraphCalc.UI;

public sealed class Graph3DView : FrameworkElement
{
    private const double DefaultZoom = 0.86;
    private const double ExtremeZJumpLimitNormalized = 1.55;
    private const double SegmentLengthLimitNormalized = 1.75;

    private static readonly Q DefaultOrientation =
        (Q.FromAxisAngle(1, 0, 0, -0.54) * Q.FromAxisAngle(0, 0, 1, -0.58)).Normalized();

    private readonly List<SurfaceRenderData> _surfaces = new();
    private Point _lastMouse;
    private V3 _lastTrackballVector;
    private bool _isDragging;
    private Q _orientation = DefaultOrientation;
    private double _zoom = DefaultZoom;
    private GraphRenderStyle _renderStyle = GraphRenderStyle.Mesh;
    private SurfaceQuality _surfaceQuality = SurfaceQuality.Smooth;
    private bool _showSurfaceTriangleEdges;

    public event Action<Q>? RotationChanged;

    public Graph3DView()
    {
        Focusable = true;
        ClipToBounds = true;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
    }

    public Q Orientation => _orientation;
    public GraphRenderStyle RenderStyle => _renderStyle;

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

    public void ResetView()
    {
        _orientation = DefaultOrientation;
        _zoom = DefaultZoom;
        RotationChanged?.Invoke(_orientation);
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
        Rect boundsRect = new(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.White, null, boundsRect);

        if (ActualWidth < 30 || ActualHeight < 30) return;

        DrawCube(dc);
        DrawFunctionData(dc);
        DrawPeripheryMarkers(dc);
        DrawCoordinateLabels(dc);
        DrawAxes(dc);
    }

    private void DrawFunctionData(DrawingContext dc)
    {
        if (_renderStyle == GraphRenderStyle.Surface)
        {
            DrawLitSurfaces(dc);
        }
        else
        {
            DrawWireMeshes(dc);
        }
    }

    private void DrawWireMeshes(DrawingContext dc)
    {
        foreach (SurfaceRenderData surface in _surfaces.Where(s => s.IsVisible && s.Points.Count > 0))
        {
            var brush = new SolidColorBrush(surface.Color);
            brush.Freeze();
            var pen = new Pen(brush, 1.0);
            pen.Freeze();
            int n = surface.GridSize;
            if (n <= 1 || surface.Points.Count < n * n) continue;

            for (int ix = 0; ix < n; ix++)
            {
                if (ShouldDrawSampleLine(n, ix)) DrawSeries(dc, surface, n, ix, true, pen);
            }
            for (int iy = 0; iy < n; iy++)
            {
                if (ShouldDrawSampleLine(n, iy)) DrawSeries(dc, surface, n, iy, false, pen);
            }
        }
    }

    private void DrawLitSurfaces(DrawingContext dc)
    {
        var visibleSurfaces = _surfaces.Where(s => s.IsVisible && s.Points.Count > 0).ToList();
        var triangles = new List<ScreenTriangle>();
        foreach (SurfaceRenderData surface in visibleSurfaces)
        {
            int n = surface.GridSize;
            if (n <= 1 || surface.Points.Count < n * n) continue;
            BuildSurfaceTriangles(surface, n, triangles);
        }

        if (triangles.Count == 0) return;

        DrawRasterizedTriangles(dc, triangles, visibleSurfaces.Count);

        if (_showSurfaceTriangleEdges)
        {
            var debugEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(88, 70, 70, 70)), 0.45);
            debugEdgePen.Freeze();
            foreach (ScreenTriangle triangle in triangles)
            {
                dc.DrawLine(debugEdgePen, triangle.A.Screen, triangle.B.Screen);
                dc.DrawLine(debugEdgePen, triangle.B.Screen, triangle.C.Screen);
                dc.DrawLine(debugEdgePen, triangle.C.Screen, triangle.A.Screen);
            }
        }
    }

    private void DrawRasterizedTriangles(DrawingContext dc, List<ScreenTriangle> triangles, int visibleSurfaceCount)
    {
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        if (width < 2 || height < 2) return;

        int supersample = _surfaceQuality == SurfaceQuality.Cinematic ? 2 : 1;
        int rw = width * supersample;
        int rh = height * supersample;
        if (rw <= 0 || rh <= 0 || rw > 5000 || rh > 5000)
        {
            supersample = 1;
            rw = width;
            rh = height;
        }

        int[] pixels = new int[rw * rh];
        double[] zBuffer = new double[pixels.Length];
        Array.Fill(zBuffer, double.NegativeInfinity);

        // R12: a single surface should not blend with itself. Near-vertical TAN boundary
        // walls produce many overlapping triangles; even tiny transparency makes those
        // edge regions look torn or dirty. Use solid opacity for one visible function,
        // and only use transparency when multiple functions are visible together.
        double alpha = visibleSurfaceCount <= 1 ? 1.00 : 0.86;
        if (_surfaceQuality == SurfaceQuality.Fast) alpha = visibleSurfaceCount <= 1 ? 1.00 : 0.80;
        if (_surfaceQuality == SurfaceQuality.Cinematic) alpha = visibleSurfaceCount <= 1 ? 1.00 : 0.90;

        foreach (ScreenTriangle triangle in triangles)
        {
            RasterizeTriangle(pixels, zBuffer, rw, rh, supersample, triangle, alpha);
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

        var options = RenderOptions.GetBitmapScalingMode(this);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        dc.DrawImage(bitmap, new Rect(0, 0, width, height));
        RenderOptions.SetBitmapScalingMode(this, options);
    }

    private static void RasterizeTriangle(int[] pixels, double[] zBuffer, int width, int height, int scale, ScreenTriangle tri, double alphaScale)
    {
        double x1 = tri.A.Screen.X * scale;
        double y1 = tri.A.Screen.Y * scale;
        double x2 = tri.B.Screen.X * scale;
        double y2 = tri.B.Screen.Y * scale;
        double x3 = tri.C.Screen.X * scale;
        double y3 = tri.C.Screen.Y * scale;

        double minXd = Math.Min(x1, Math.Min(x2, x3));
        double maxXd = Math.Max(x1, Math.Max(x2, x3));
        double minYd = Math.Min(y1, Math.Min(y2, y3));
        double maxYd = Math.Max(y1, Math.Max(y2, y3));

        int minX = Math.Max(0, (int)Math.Floor(minXd));
        int maxX = Math.Min(width - 1, (int)Math.Ceiling(maxXd));
        int minY = Math.Max(0, (int)Math.Floor(minYd));
        int maxY = Math.Min(height - 1, (int)Math.Ceiling(maxYd));
        if (maxX < minX || maxY < minY) return;

        double denom = ((y2 - y3) * (x1 - x3)) + ((x3 - x2) * (y1 - y3));
        if (Math.Abs(denom) < 0.0000001) return;

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

                double depth = w1 * tri.A.Depth + w2 * tri.B.Depth + w3 * tri.C.Depth;
                int index = row + x;
                // R12: use a tiny tie margin so nearly coplanar edge-wall triangles do not
                // continually overwrite one another and create speckled transparency artifacts.
                if (depth <= zBuffer[index] + 0.0000005) continue;

                double shade = w1 * tri.A.Shade + w2 * tri.B.Shade + w3 * tri.C.Shade;
                if (tri.BackFace) shade *= tri.IsBoundary ? 0.82 : 0.90;
                if (tri.IsCorner) shade *= 0.94;
                zBuffer[index] = depth;
                pixels[index] = ToPbgra(tri.Color, shade, alphaScale);
            }
        }
    }

    private void BuildSurfaceTriangles(SurfaceRenderData surface, int n, List<ScreenTriangle> triangles)
    {
        if (n <= 1) return;
        ScreenVertex[] vertices = BuildScreenVertices(surface, n);

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

                TryAddTriangle(surface, n, ia, ib, ic, ix, iy, ix + 1, iy, ix, iy + 1, vertices, triangles);
                TryAddTriangle(surface, n, ib, id, ic, ix + 1, iy, ix + 1, iy + 1, ix, iy + 1, vertices, triangles);
            }
        }
    }

    private ScreenVertex[] BuildScreenVertices(SurfaceRenderData surface, int n)
    {
        var vertices = new ScreenVertex[Math.Min(surface.Points.Count, n * n)];
        var projected = new P3[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            Point3Dto point = surface.Points[i];
            projected[i] = point.Valid
                ? ProjectDataPointWithDepth(point.X, point.Y, point.Z)
                : new P3(new Point(double.NaN, double.NaN), double.NaN, new V3(0, 0, 0));
        }

        for (int ix = 0; ix < n; ix++)
        {
            for (int iy = 0; iy < n; iy++)
            {
                int index = ix * n + iy;
                if (index >= vertices.Length || index >= surface.Points.Count || !surface.Points[index].Valid)
                {
                    vertices[index] = ScreenVertex.Invalid;
                    continue;
                }

                int left = Math.Max(0, ix - 1) * n + iy;
                int right = Math.Min(n - 1, ix + 1) * n + iy;
                int down = ix * n + Math.Max(0, iy - 1);
                int up = ix * n + Math.Min(n - 1, iy + 1);
                V3 tangentX = SafeProjected(projected, right).View - SafeProjected(projected, left).View;
                V3 tangentY = SafeProjected(projected, up).View - SafeProjected(projected, down).View;
                V3 normal = V3.Cross(tangentX, tangentY).Normalized();
                double shade = ComputeCinematicShade(normal);
                P3 p = projected[index];
                vertices[index] = new ScreenVertex(p.Screen, p.Depth, shade, true, p.View);
            }
        }

        return vertices;
    }

    private static P3 SafeProjected(P3[] projected, int index)
    {
        if (index < 0 || index >= projected.Length) return new P3(new Point(0, 0), 0, new V3(0, 0, 1));
        return projected[index];
    }

    private void TryAddTriangle(
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
        ScreenVertex[] vertices,
        List<ScreenTriangle> triangles)
    {
        if (i1 < 0 || i2 < 0 || i3 < 0 || i1 >= surface.Points.Count || i2 >= surface.Points.Count || i3 >= surface.Points.Count) return;
        Point3Dto a = surface.Points[i1];
        Point3Dto b = surface.Points[i2];
        Point3Dto c = surface.Points[i3];
        if (!a.Valid || !b.Valid || !c.Valid) return;
        if (!ShouldDrawSegment(surface, a, b, n, x1, y1, x2, y2)) return;
        if (!ShouldDrawSegment(surface, b, c, n, x2, y2, x3, y3)) return;
        if (!ShouldDrawSegment(surface, c, a, n, x3, y3, x1, y1)) return;
        if (i1 >= vertices.Length || i2 >= vertices.Length || i3 >= vertices.Length) return;

        ScreenVertex va = vertices[i1];
        ScreenVertex vb = vertices[i2];
        ScreenVertex vc = vertices[i3];
        if (!va.Valid || !vb.Valid || !vc.Valid) return;

        double area = Math.Abs((vb.Screen.X - va.Screen.X) * (vc.Screen.Y - va.Screen.Y) - (vc.Screen.X - va.Screen.X) * (vb.Screen.Y - va.Screen.Y));
        if (area < 0.04) return;

        bool isBoundary = IsBoundaryCell(n, x1, y1, x2, y2, x3, y3);
        bool isCorner = IsCornerCell(n, x1, y1, x2, y2, x3, y3);
        double longestEdge = Math.Max(ScreenDistance(va.Screen, vb.Screen), Math.Max(ScreenDistance(vb.Screen, vc.Screen), ScreenDistance(vc.Screen, va.Screen)));
        double aspect = (longestEdge * longestEdge) / Math.Max(area, 0.0001);
        double minDepth = Math.Min(va.Depth, Math.Min(vb.Depth, vc.Depth));
        double maxDepth = Math.Max(va.Depth, Math.Max(vb.Depth, vc.Depth));
        double depthSpread = maxDepth - minDepth;

        // R12: keep real boundary walls, but reject the pathological sliver triangles
        // that cause noisy transparent tearing at the two extreme TAN edges.
        if (isCorner && area < 0.45 && aspect > 180.0) return;
        if (isBoundary && area < 0.30 && aspect > 260.0) return;
        if (area < 0.12 && aspect > 420.0 && depthSpread > 0.25) return;

        V3 normal = V3.Cross(vb.View - va.View, vc.View - va.View).Normalized();
        bool backFace = normal.Z < -0.035;
        if (isBoundary && backFace && area < 0.22 && aspect > 160.0) return;

        triangles.Add(new ScreenTriangle(va, vb, vc, surface.Color, isBoundary, isCorner, backFace));
    }

    private static bool IsBoundaryCell(int n, int x1, int y1, int x2, int y2, int x3, int y3)
    {
        int edgeBand = Math.Max(3, n / 80);
        return IsBoundaryIndex(n, x1, edgeBand) || IsBoundaryIndex(n, x2, edgeBand) || IsBoundaryIndex(n, x3, edgeBand) ||
               IsBoundaryIndex(n, y1, edgeBand) || IsBoundaryIndex(n, y2, edgeBand) || IsBoundaryIndex(n, y3, edgeBand);
    }

    private static bool IsCornerCell(int n, int x1, int y1, int x2, int y2, int x3, int y3)
    {
        int edgeBand = Math.Max(3, n / 80);
        bool nearX = IsBoundaryIndex(n, x1, edgeBand) || IsBoundaryIndex(n, x2, edgeBand) || IsBoundaryIndex(n, x3, edgeBand);
        bool nearY = IsBoundaryIndex(n, y1, edgeBand) || IsBoundaryIndex(n, y2, edgeBand) || IsBoundaryIndex(n, y3, edgeBand);
        return nearX && nearY;
    }

    private static bool IsBoundaryIndex(int n, int index, int edgeBand)
    {
        return index <= edgeBand || index >= n - 1 - edgeBand;
    }

    private static double ScreenDistance(Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private double ComputeCinematicShade(V3 normal)
    {
        V3 n = normal.Normalized();
        V3 key = new V3(-0.42, 0.34, 1.0).Normalized();
        V3 fill = new V3(0.60, -0.32, 0.44).Normalized();
        V3 view = new V3(0, 0, 1);

        double keyLight = Math.Abs(V3.Dot(n, key));
        double fillLight = Math.Abs(V3.Dot(n, fill));
        double rim = Math.Pow(1.0 - Math.Min(1.0, Math.Abs(V3.Dot(n, view))), 1.7);
        double specular = Math.Pow(Math.Max(0.0, Math.Abs(V3.Dot(n, key))), _surfaceQuality == SurfaceQuality.Cinematic ? 42.0 : 28.0);

        double ambient = _surfaceQuality == SurfaceQuality.Fast ? 0.48 : _surfaceQuality == SurfaceQuality.Cinematic ? 0.34 : 0.40;
        double shade = ambient + (0.50 * keyLight) + (0.16 * fillLight) + (0.16 * rim) + (0.10 * specular);
        if (_surfaceQuality == SurfaceQuality.Cinematic)
        {
            shade = Math.Pow(shade, 0.92);
        }
        return Math.Clamp(shade, 0.30, 1.20);
    }

    private static int ToPbgra(Color color, double shade, double alphaScale)
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
        double r = color.R;
        double g = color.G;
        double b = color.B;

        // Prevent fully saturated primary colors from looking like flat paint.
        double soften = 0.08;
        r = r * (1.0 - soften) + 255.0 * soften;
        g = g * (1.0 - soften) + 255.0 * soften;
        b = b * (1.0 - soften) + 255.0 * soften;

        if (shade < 1.0)
        {
            double s = Math.Max(0.0, shade);
            r *= s;
            g *= s;
            b *= s;
        }
        else
        {
            double t = Math.Min(1.0, (shade - 1.0) * 0.75);
            r = r + (255.0 - r) * t;
            g = g + (255.0 - g) * t;
            b = b + (255.0 - b) * t;
        }

        return Color.FromRgb(
            (byte)Math.Clamp((int)Math.Round(r), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b), 0, 255));
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

    private static bool ShouldDrawSampleLine(int n, int index)
    {
        if (n <= 193) return true;
        double u = n <= 1 ? 0.0 : -1.0 + (2.0 * index / (n - 1.0));
        double abs = Math.Abs(u);

        // R8 baseline: keep edge-collar detail dense and thin the center first.
        if (abs >= 0.64) return true;
        if (Math.Abs(index - (n / 2)) <= 1) return true;

        int centerStep = n >= 513 ? 4 : n >= 385 ? 3 : 2;
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

            Point projected = ProjectDataPoint(p.X, p.Y, p.Z);
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

        double zJumpLimit = splitMode ? 0.95 : ExtremeZJumpLimitNormalized;
        double lengthLimit = clippedMode ? 2.10 : SegmentLengthLimitNormalized;

        if (zJump > zJumpLimit && zJump > xyLength * (splitMode ? 8.0 : 20.0)) return false;
        if (length > lengthLimit && zJump > xyLength * (splitMode ? 6.0 : 12.0)) return false;
        return true;
    }

    private void DrawCube(DrawingContext dc)
    {
        var lightPen = new Pen(new SolidColorBrush(Color.FromRgb(210, 210, 210)), 1.0);
        lightPen.Freeze();

        var corners = CubeCorners();
        int[,] edges =
        {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        };

        for (int i = 0; i < edges.GetLength(0); i++)
        {
            var a = corners[edges[i, 0]];
            var b = corners[edges[i, 1]];
            dc.DrawLine(lightPen, ProjectNormalized(a.X, a.Y, a.Z), ProjectNormalized(b.X, b.Y, b.Z));
        }
    }

    private static (double X, double Y, double Z)[] CubeCorners()
    {
        return new[]
        {
            (-1.0, -1.0, -1.0), ( 1.0, -1.0, -1.0), ( 1.0,  1.0, -1.0), (-1.0,  1.0, -1.0),
            (-1.0, -1.0,  1.0), ( 1.0, -1.0,  1.0), ( 1.0,  1.0,  1.0), (-1.0,  1.0,  1.0)
        };
    }

    private void DrawPeripheryMarkers(DrawingContext dc)
    {
        var markerBrush = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
        markerBrush.Freeze();
        var edgeMidpoints = new[]
        {
            ( 0.0, -1.0, -1.0), ( 0.0,  1.0, -1.0), ( 0.0, -1.0,  1.0), ( 0.0,  1.0,  1.0),
            (-1.0,  0.0, -1.0), ( 1.0,  0.0, -1.0), (-1.0,  0.0,  1.0), ( 1.0,  0.0,  1.0),
            (-1.0, -1.0,  0.0), ( 1.0, -1.0,  0.0), (-1.0,  1.0,  0.0), ( 1.0,  1.0,  0.0)
        };

        foreach (var point in edgeMidpoints)
        {
            Point p = ProjectNormalized(point.Item1, point.Item2, point.Item3);
            dc.DrawEllipse(markerBrush, null, p, 2.2, 2.2);
        }
    }

    private void DrawCoordinateLabels(DrawingContext dc)
    {
        var labelBrush = new SolidColorBrush(Color.FromArgb(150, 40, 40, 40));
        labelBrush.Freeze();
        var cornerBrush = new SolidColorBrush(Color.FromArgb(135, 40, 40, 40));
        cornerBrush.Freeze();

        DrawCornerLabels(dc, cornerBrush);

        double[] interiorValues = { -Math.PI / 4.0, 0, Math.PI / 4.0 };
        foreach (double theta in interiorValues)
        {
            double n = TanSpace.ToNormalized(theta);
            string label = TanSpace.PrettyPi(theta);

            Point xPoint = ProjectNormalized(n, -1.12, -1.08);
            DrawLabel(dc, label, xPoint.X - 12, xPoint.Y + 2, labelBrush, 10);

            Point yPoint = ProjectNormalized(-1.13, n, -1.08);
            DrawLabel(dc, label, yPoint.X - 18, yPoint.Y + 2, labelBrush, 10);

            Point zPoint = ProjectNormalized(-1.13, -1.08, n);
            DrawLabel(dc, label, zPoint.X - 18, zPoint.Y + 2, labelBrush, 10);
        }
    }

    private void DrawCornerLabels(DrawingContext dc, Brush brush)
    {
        foreach (var c in CubeCorners())
        {
            string label = CornerLabel(c.X, c.Y, c.Z);
            Point p = ProjectNormalized(c.X, c.Y, c.Z);
            double dx = c.X >= 0 ? 4 : -42;
            double dy = c.Z >= 0 ? -18 : 4;
            DrawLabel(dc, label, p.X + dx, p.Y + dy, brush, 9);
        }
    }

    private static string CornerLabel(double x, double y, double z)
    {
        return $"({EndpointLabel(x)},{EndpointLabel(y)},{EndpointLabel(z)})";
    }

    private static string EndpointLabel(double normalized)
    {
        return normalized < 0 ? "-π/2" : "π/2";
    }

    private void DrawAxes(DrawingContext dc)
    {
        DrawAxis(dc, 1.25, 0, 0, Colors.Red, "X");
        DrawAxis(dc, 0, 1.25, 0, Colors.Green, "Y");
        DrawAxis(dc, 0, 0, 1.25, Colors.Blue, "Z");
    }

    private void DrawAxis(DrawingContext dc, double x, double y, double z, Color color, string label)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, 2.4);
        pen.Freeze();
        Point origin = ProjectNormalized(0, 0, 0);
        Point end = ProjectNormalized(x, y, z);
        dc.DrawLine(pen, origin, end);
        DrawLabel(dc, label, end.X + 4, end.Y - 14, brush, 14);
    }

    private Point ProjectDataPoint(double thetaX, double thetaY, double thetaZ)
    {
        return ProjectNormalized(
            TanSpace.ToNormalized(thetaX),
            TanSpace.ToNormalized(thetaY),
            TanSpace.ToNormalized(thetaZ));
    }

    private P3 ProjectDataPointWithDepth(double thetaX, double thetaY, double thetaZ)
    {
        return ProjectNormalizedWithDepth(
            TanSpace.ToNormalized(thetaX),
            TanSpace.ToNormalized(thetaY),
            TanSpace.ToNormalized(thetaZ));
    }

    private Point ProjectNormalized(double x, double y, double z)
    {
        return ProjectNormalizedWithDepth(x, y, z).Screen;
    }

    private P3 ProjectNormalizedWithDepth(double x, double y, double z)
    {
        V3 rotated = _orientation.Rotate(new V3(x, y, z));
        double scale = Math.Min(ActualWidth, ActualHeight) * 0.34 * _zoom;
        return new P3(
            new Point(ActualWidth * 0.5 + rotated.X * scale, ActualHeight * 0.5 - rotated.Y * scale),
            rotated.Z,
            rotated);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _lastMouse = e.GetPosition(this);
        _lastTrackballVector = MapToTrackball(_lastMouse);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        Point current = e.GetPosition(this);
        V3 currentVector = MapToTrackball(current);
        Q delta = Q.FromVectors(_lastTrackballVector, currentVector);
        _orientation = (delta * _orientation).Normalized();
        _lastTrackballVector = currentVector;
        _lastMouse = current;
        RotationChanged?.Invoke(_orientation);
        InvalidateVisual();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _zoom *= e.Delta > 0 ? 1.08 : 0.92;
        _zoom = Math.Clamp(_zoom, 0.25, 4.0);
        InvalidateVisual();
    }

    private V3 MapToTrackball(Point point)
    {
        double size = Math.Max(1.0, Math.Min(ActualWidth, ActualHeight));
        double x = (2.0 * point.X - ActualWidth) / size;
        double y = (ActualHeight - 2.0 * point.Y) / size;
        double lengthSquared = x * x + y * y;
        if (lengthSquared <= 1.0)
        {
            return new V3(x, y, Math.Sqrt(1.0 - lengthSquared)).Normalized();
        }
        double length = Math.Sqrt(lengthSquared);
        return new V3(x / length, y / length, 0).Normalized();
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

    private sealed record P3(Point Screen, double Depth, V3 View);
    private sealed record ScreenVertex(Point Screen, double Depth, double Shade, bool Valid, V3 View)
    {
        public static ScreenVertex Invalid { get; } = new(new Point(double.NaN, double.NaN), double.NaN, 0, false, new V3(0, 0, 0));
    }
    private sealed record ScreenTriangle(ScreenVertex A, ScreenVertex B, ScreenVertex C, Color Color, bool IsBoundary, bool IsCorner, bool BackFace);
}

public readonly struct V3
{
    public V3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public V3 Normalized()
    {
        double length = Length;
        if (length < 0.000000000001) return new V3(0, 0, 1);
        return new V3(X / length, Y / length, Z / length);
    }

    public static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static V3 Cross(V3 a, V3 b)
    {
        return new V3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
    }

    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}

public readonly struct Q
{
    public Q(double w, double x, double y, double z)
    {
        W = w;
        X = x;
        Y = y;
        Z = z;
    }

    public double W { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public static Q Identity => new(1, 0, 0, 0);

    public static Q FromAxisAngle(double x, double y, double z, double angle)
    {
        V3 axis = new V3(x, y, z).Normalized();
        double half = angle * 0.5;
        double s = Math.Sin(half);
        return new Q(Math.Cos(half), axis.X * s, axis.Y * s, axis.Z * s).Normalized();
    }

    public static Q FromVectors(V3 from, V3 to)
    {
        V3 a = from.Normalized();
        V3 b = to.Normalized();
        double dot = Math.Clamp(V3.Dot(a, b), -1.0, 1.0);
        if (dot > 0.999999) return Identity;
        if (dot < -0.999999)
        {
            V3 axis = Math.Abs(a.X) < 0.8 ? V3.Cross(a, new V3(1, 0, 0)) : V3.Cross(a, new V3(0, 1, 0));
            return FromAxisAngle(axis.X, axis.Y, axis.Z, Math.PI);
        }

        V3 cross = V3.Cross(a, b);
        return new Q(1.0 + dot, cross.X, cross.Y, cross.Z).Normalized();
    }

    public Q Normalized()
    {
        double length = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
        if (length < 0.000000000001) return Identity;
        return new Q(W / length, X / length, Y / length, Z / length);
    }

    public V3 Rotate(V3 v)
    {
        Q p = new(0, v.X, v.Y, v.Z);
        Q result = this * p * Conjugate();
        return new V3(result.X, result.Y, result.Z);
    }

    public Q Conjugate() => new(W, -X, -Y, -Z);

    public static Q operator *(Q a, Q b)
    {
        return new Q(
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);
    }
}
