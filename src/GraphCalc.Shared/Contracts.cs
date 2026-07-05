namespace GraphCalc.Shared;

public static class TanSpace
{
    public const string SamplerName = "BoundaryWeightedTanSampler";
    public const double HalfPi = Math.PI / 2.0;
    public const double AxisMin = -HalfPi;
    public const double AxisMax = HalfPi;
    public const double DefaultEdgeInset = 0.0001;

    public static double ToNormalized(double theta)
    {
        return Math.Clamp(theta / HalfPi, -1.0, 1.0);
    }

    public static string PrettyPi(double theta)
    {
        const double tolerance = 0.000001;
        if (Math.Abs(theta + HalfPi) < tolerance) return "-π/2";
        if (Math.Abs(theta + Math.PI / 4.0) < tolerance) return "-π/4";
        if (Math.Abs(theta) < tolerance) return "0";
        if (Math.Abs(theta - Math.PI / 4.0) < tolerance) return "π/4";
        if (Math.Abs(theta - HalfPi) < tolerance) return "π/2";
        return theta.ToString("0.###");
    }
}

public sealed class HealthResponse
{
    public string Service { get; set; } = "GraphCalc.Api";
    public bool TestMode { get; set; }
    public string StartedUtc { get; set; } = "";
    public string DbPath { get; set; } = "";
    public string LogPath { get; set; } = "";
}

public sealed class GraphFunctionDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "F1";
    public string Expression { get; set; } = "sin(x)+cos(y)";
    public string ColorHex { get; set; } = "#E91B23";
    public bool IsVisible { get; set; } = true;
    public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
}

public sealed class UpsertFunctionRequest
{
    public string? Id { get; set; }
    public string Label { get; set; } = "F1";
    public string Expression { get; set; } = "sin(x)+cos(y)";
    public string ColorHex { get; set; } = "#E91B23";
    public bool IsVisible { get; set; } = true;
}

public sealed class CalculationRequest
{
    public string FunctionId { get; set; } = "";
    public string Label { get; set; } = "F1";
    public string Expression { get; set; } = "sin(x)+cos(y)";
    public string ColorHex { get; set; } = "#E91B23";
    public bool IsVisible { get; set; } = true;

    public string SamplerName { get; set; } = TanSpace.SamplerName;
    public string LodName { get; set; } = "Standard";
    public int AxisSampleCount { get; set; } = 81;
    public double EdgeInset { get; set; } = TanSpace.DefaultEdgeInset;

    // Kept for compatibility with the starter database/cache shape.
    // For this revision it always mirrors AxisSampleCount.
    public int GridSize { get; set; } = 81;

    // Display-space TAN bounds. These are fixed and are not used for autoscaling.
    public double XMin { get; set; } = TanSpace.AxisMin;
    public double XMax { get; set; } = TanSpace.AxisMax;
    public double YMin { get; set; } = TanSpace.AxisMin;
    public double YMax { get; set; } = TanSpace.AxisMax;
}

public sealed class Point3Dto
{
    // These values are TAN display angles, not raw real-space values.
    // X, Y, and Z are all in approximately [-π/2, π/2].
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public bool Valid { get; set; } = true;
}

public sealed class CalculationResultDto
{
    public string ResultId { get; set; } = Guid.NewGuid().ToString("N");
    public string FunctionId { get; set; } = "";
    public string Label { get; set; } = "F1";
    public string Expression { get; set; } = "";
    public string ExpressionHash { get; set; } = "";
    public string ColorHex { get; set; } = "#E91B23";
    public bool IsVisible { get; set; } = true;
    public string SamplerName { get; set; } = TanSpace.SamplerName;
    public string LodName { get; set; } = "Standard";
    public int AxisSampleCount { get; set; } = 81;
    public double EdgeInset { get; set; } = TanSpace.DefaultEdgeInset;
    public int GridSize { get; set; }
    public double XMin { get; set; }
    public double XMax { get; set; }
    public double YMin { get; set; }
    public double YMax { get; set; }
    public string Status { get; set; } = "Ready";
    public bool FromCache { get; set; }
    public string? ErrorMessage { get; set; }

    // R8: behavior metadata is calculated per function/result for diagnostics.
    // Normal rendering uses actual outer samples and an edge-weighted TAN sampler.
    public string BoundaryBehavior { get; set; } = "Unclassified";
    public string RenderMode { get; set; } = "Wireframe";
    public int BoundarySampleCount { get; set; }
    public int InvalidSampleCount { get; set; }
    public int LargeJumpCount { get; set; }
    public double BoundaryZMin { get; set; }
    public double BoundaryZMax { get; set; }
    public double BoundaryZAbsMax { get; set; }

    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public List<Point3Dto> Points { get; set; } = new();
}

public sealed class TestModeRequest
{
    public bool Enabled { get; set; }
}

public sealed class TestLogRequest
{
    public string EventName { get; set; } = "UI.Note";
    public string Message { get; set; } = "";
}
