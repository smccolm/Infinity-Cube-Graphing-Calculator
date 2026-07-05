using System.Windows.Media;
using GraphCalc.Shared;

namespace GraphCalc.UI;

public enum GraphRenderStyle
{
    Mesh,
    Surface
}

public enum SurfaceQuality
{
    Fast,
    Smooth,
    Cinematic
}

public sealed class SurfaceRenderData
{
    public string FunctionId { get; set; } = "";
    public string Label { get; set; } = "F";
    public string Expression { get; set; } = "";
    public Color Color { get; set; } = Colors.Red;
    public bool IsVisible { get; set; } = true;
    public int GridSize { get; set; }
    public string BoundaryBehavior { get; set; } = "Unclassified";
    public string RenderMode { get; set; } = "Wireframe";
    public int InvalidSampleCount { get; set; }
    public int LargeJumpCount { get; set; }
    public double BoundaryZMin { get; set; }
    public double BoundaryZMax { get; set; }
    public List<Point3Dto> Points { get; set; } = new();
}
