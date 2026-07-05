using GraphCalc.Api;
using GraphCalc.Shared;

var builder = WebApplication.CreateBuilder(args);

int port = builder.Configuration.GetValue<int?>("GraphCalc:Port") ?? 8765;
bool defaultTestMode = builder.Configuration.GetValue<bool?>("GraphCalc:TestMode") ?? true;
int maxGridSize = builder.Configuration.GetValue<int?>("GraphCalc:GridSizeMax") ?? 513;

string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GraphCalc");
string dbPath = Path.Combine(root, "data", "graphcalc.db");
string logPath = Path.Combine(root, "logs");
Directory.CreateDirectory(root);

TestLog.Initialize("GraphCalc.Api", defaultTestMode, logPath);
TestLog.Info("Api.Startup", "API startup requested.", new Dictionary<string, string>
{
    ["port"] = port.ToString(),
    ["dbPath"] = dbPath,
    ["logPath"] = logPath,
    ["maxGridSize"] = maxGridSize.ToString()
});

var storage = new GraphStorage(dbPath);
storage.Initialize();

builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var app = builder.Build();
string startedUtc = DateTime.UtcNow.ToString("O");

app.MapGet("/health", () =>
{
    TestLog.Info("Api.Health", "Health check served.");
    return Results.Ok(new HealthResponse
    {
        TestMode = TestLog.TestMode,
        StartedUtc = startedUtc,
        DbPath = storage.DbPath,
        LogPath = TestLog.LogDirectory
    });
});

app.MapPost("/test-mode", (TestModeRequest request) =>
{
    TestLog.SetTestMode(request.Enabled);
    return Results.Ok(new { testMode = TestLog.TestMode, logPath = TestLog.LogDirectory });
});

app.MapPost("/test-log", (TestLogRequest request) =>
{
    TestLog.Info(request.EventName, request.Message);
    return Results.Ok(new { logged = TestLog.TestMode });
});

app.MapGet("/functions", () =>
{
    List<GraphFunctionDto> functions = storage.GetFunctions();
    TestLog.Info("Functions.List", "Function list returned.", new Dictionary<string, string>
    {
        ["count"] = functions.Count.ToString()
    });
    return Results.Ok(functions);
});

app.MapPost("/functions", (UpsertFunctionRequest request) =>
{
    GraphFunctionDto dto = storage.UpsertFunction(request);
    TestLog.Info("Functions.Upsert", "Function saved.", new Dictionary<string, string>
    {
        ["functionId"] = dto.Id,
        ["label"] = dto.Label,
        ["expression"] = dto.Expression,
        ["isVisible"] = dto.IsVisible.ToString()
    });
    return Results.Ok(dto);
});

app.MapPost("/calculate", (CalculationRequest request) =>
{
    string correlationId = Guid.NewGuid().ToString("N")[..8];
    request.Expression = request.Expression.Trim();
    request.SamplerName = TanSpace.SamplerName;
    request.AxisSampleCount = NormalizeAxisSampleCount(request.AxisSampleCount, maxGridSize);
    request.GridSize = request.AxisSampleCount;
    request.EdgeInset = NormalizeEdgeInset(request.EdgeInset);
    request.XMin = TanSpace.AxisMin;
    request.XMax = TanSpace.AxisMax;
    request.YMin = TanSpace.AxisMin;
    request.YMax = TanSpace.AxisMax;

    double thetaMax = TanSpace.HalfPi - request.EdgeInset;
    double thetaMin = -thetaMax;
    List<double> axisP = BuildBoundaryWeightedAxis(request.AxisSampleCount, request.EdgeInset, out int edgeCollarSamples, out double tanThetaMax);
    request.AxisSampleCount = axisP.Count;
    request.GridSize = axisP.Count;
    int totalSamples = request.AxisSampleCount * request.AxisSampleCount;

    TestLog.Info("Calculation.Start", "Calculation request received.", new Dictionary<string, string>
    {
        ["correlationId"] = correlationId,
        ["functionId"] = request.FunctionId,
        ["label"] = request.Label,
        ["expression"] = request.Expression,
        ["sampler"] = request.SamplerName,
        ["lodName"] = request.LodName,
        ["axisSampleCount"] = request.AxisSampleCount.ToString(),
        ["totalSamples"] = totalSamples.ToString(),
        ["thetaRange"] = $"{thetaMin:0.##########} to {thetaMax:0.##########}",
        ["thetaEdgeInset"] = request.EdgeInset.ToString("0.##########"),
        ["edgePolicy"] = "R8 boundary-weighted sampler; medium center detail; higher edge density near ±π/2",
        ["thetaMax"] = thetaMax.ToString("0.##########"),
        ["outerThetaRatio"] = (thetaMax / TanSpace.HalfPi).ToString("0.##########"),
        ["tanThetaMax"] = tanThetaMax.ToString("0.##########"),
        ["edgeCollarSamplesPerSide"] = edgeCollarSamples.ToString(),
        ["fixedDisplayBounds"] = "X,Y,Z=[-π/2,π/2]; autoscale disabled"
    });

    try
    {
        CalculationResultDto? cached = storage.TryGetCachedResult(request);
        if (cached != null)
        {
            cached.FunctionId = request.FunctionId;
            cached.Label = request.Label;
            cached.ColorHex = request.ColorHex;
            cached.IsVisible = request.IsVisible;
            TestLog.Info("Calculation.CacheHit", "Cached result returned.", new Dictionary<string, string>
            {
                ["correlationId"] = correlationId,
                ["resultId"] = cached.ResultId,
                ["pointCount"] = cached.Points.Count.ToString(),
                ["sampler"] = request.SamplerName,
                ["lodName"] = request.LodName
            });
            return Results.Ok(cached);
        }

        var evaluator = new ExpressionEvaluator();
        string hash = GraphStorage.ExpressionHash(request.Expression);
        var result = new CalculationResultDto
        {
            ResultId = Guid.NewGuid().ToString("N"),
            FunctionId = request.FunctionId,
            Label = request.Label,
            Expression = request.Expression,
            ExpressionHash = hash,
            ColorHex = request.ColorHex,
            IsVisible = request.IsVisible,
            SamplerName = request.SamplerName,
            LodName = request.LodName,
            AxisSampleCount = request.AxisSampleCount,
            EdgeInset = request.EdgeInset,
            GridSize = request.AxisSampleCount,
            XMin = TanSpace.AxisMin,
            XMax = TanSpace.AxisMax,
            YMin = TanSpace.AxisMin,
            YMax = TanSpace.AxisMax,
            Status = "Ready",
            FromCache = false,
            CreatedUtc = DateTime.UtcNow.ToString("O")
        };

        int invalidCount = 0;

        for (int ix = 0; ix < axisP.Count; ix++)
        {
            double px = axisP[ix];
            double thetaX = Math.Asin(px);
            double realX = ProjectiveToReal(px);

            for (int iy = 0; iy < axisP.Count; iy++)
            {
                double py = axisP[iy];
                double thetaY = Math.Asin(py);
                double realY = ProjectiveToReal(py);

                bool valid = true;
                double thetaZ;
                try
                {
                    double realZ = evaluator.Evaluate(request.Expression, realX, realY, 0);
                    if (double.IsNaN(realZ) || double.IsInfinity(realZ))
                    {
                        valid = false;
                        thetaZ = 0;
                    }
                    else
                    {
                        thetaZ = Math.Atan(realZ);
                    }
                }
                catch
                {
                    valid = false;
                    thetaZ = 0;
                }

                if (!valid) invalidCount++;
                result.Points.Add(new Point3Dto { X = thetaX, Y = thetaY, Z = thetaZ, Valid = valid });
            }
        }

        AnalyzeBoundaryBehavior(result);
        storage.SaveResult(result, request);
        TestLog.Info("Calculation.Complete", "Calculation completed and stored.", new Dictionary<string, string>
        {
            ["correlationId"] = correlationId,
            ["resultId"] = result.ResultId,
            ["pointCount"] = result.Points.Count.ToString(),
            ["invalidCount"] = invalidCount.ToString(),
            ["boundaryBehavior"] = result.BoundaryBehavior,
            ["renderMode"] = result.RenderMode,
            ["boundarySampleCount"] = result.BoundarySampleCount.ToString(),
            ["largeJumpCount"] = result.LargeJumpCount.ToString(),
            ["boundaryZRange"] = $"{result.BoundaryZMin:0.####} to {result.BoundaryZMax:0.####}",
            ["sampler"] = request.SamplerName,
            ["lodName"] = request.LodName,
            ["axisSampleCount"] = request.AxisSampleCount.ToString(),
            ["edgeCollarSamplesPerSide"] = edgeCollarSamples.ToString(),
            ["tanThetaMax"] = tanThetaMax.ToString("0.##########"),
            ["dbPath"] = storage.DbPath
        });
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        TestLog.Error("Calculation.Failed", "Calculation failed.", ex, new Dictionary<string, string>
        {
            ["correlationId"] = correlationId,
            ["expression"] = request.Expression,
            ["sampler"] = request.SamplerName,
            ["lodName"] = request.LodName
        });

        return Results.BadRequest(new CalculationResultDto
        {
            FunctionId = request.FunctionId,
            Label = request.Label,
            Expression = request.Expression,
            ColorHex = request.ColorHex,
            IsVisible = request.IsVisible,
            SamplerName = request.SamplerName,
            LodName = request.LodName,
            AxisSampleCount = request.AxisSampleCount,
            EdgeInset = request.EdgeInset,
            GridSize = request.GridSize,
            XMin = TanSpace.AxisMin,
            XMax = TanSpace.AxisMax,
            YMin = TanSpace.AxisMin,
            YMax = TanSpace.AxisMax,
            Status = "Error",
            ErrorMessage = ex.Message
        });
    }
});

app.MapGet("/results/{resultId}", (string resultId) =>
{
    CalculationResultDto? result = storage.GetResult(resultId);
    if (result == null)
    {
        TestLog.Warn("Results.NotFound", "Result was not found.", new Dictionary<string, string>
        {
            ["resultId"] = resultId
        });
        return Results.NotFound();
    }

    TestLog.Info("Results.Read", "Result returned.", new Dictionary<string, string>
    {
        ["resultId"] = resultId,
        ["pointCount"] = result.Points.Count.ToString()
    });
    return Results.Ok(result);
});

app.Run();

static int NormalizeAxisSampleCount(int requested, int maxGridSize)
{
    int max = Math.Max(9, maxGridSize);
    int n = Math.Clamp(requested, 9, max);
    if (n % 2 == 0) n += 1;
    if (n > max) n -= 2;
    return Math.Max(9, n);
}

static double NormalizeEdgeInset(double requested)
{
    // R8 keeps EdgeInset as a TAN-angle inset: thetaMax = pi/2 - inset.
    // Smaller insets make the sampled horizon move closer to mathematical infinity.
    if (double.IsNaN(requested) || double.IsInfinity(requested)) return TanSpace.DefaultEdgeInset;
    return Math.Clamp(requested, 0.000001, 0.05);
}

static List<double> BuildBoundaryWeightedAxis(int sampleCount, double edgeInset, out int edgeCollarSamplesPerSide, out double tanThetaMax)
{
    // R8: deterministic boundary-weighted TAN sampler.
    // It keeps medium detail in the center and spends more samples near ±π/2,
    // where the finite TAN cube represents approach toward infinity.
    int n = Math.Max(9, sampleCount);
    if (n % 2 == 0) n++;

    double thetaMax = TanSpace.HalfPi - edgeInset;
    tanThetaMax = Math.Tan(thetaMax);
    const double gamma = 2.65;
    double denominator = n - 1.0;
    var thetaValues = new List<double>(n);

    for (int i = 0; i < n; i++)
    {
        double u = -1.0 + (2.0 * i / denominator);
        double sign = Math.Sign(u);
        double t = Math.Abs(u);
        double magnitude = thetaMax * (1.0 - Math.Pow(1.0 - t, gamma));
        thetaValues.Add(sign * magnitude);
    }

    // Force the center to be exact.
    thetaValues[n / 2] = 0.0;

    double[] landmarks =
    {
        -thetaMax, thetaMax,
        -31.0 * Math.PI / 64.0, 31.0 * Math.PI / 64.0,
        -15.0 * Math.PI / 32.0, 15.0 * Math.PI / 32.0,
        -7.0 * Math.PI / 16.0, 7.0 * Math.PI / 16.0,
        -3.0 * Math.PI / 8.0, 3.0 * Math.PI / 8.0,
        -Math.PI / 4.0, Math.PI / 4.0,
        -Math.PI / 8.0, Math.PI / 8.0,
        0.0
    };

    var used = new HashSet<int>();
    foreach (double target in landmarks)
    {
        if (target < -thetaMax || target > thetaMax) continue;
        int bestIndex = -1;
        double bestDistance = double.PositiveInfinity;
        for (int i = 0; i < thetaValues.Count; i++)
        {
            if (used.Contains(i)) continue;
            double distance = Math.Abs(thetaValues[i] - target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }
        if (bestIndex >= 0)
        {
            thetaValues[bestIndex] = target;
            used.Add(bestIndex);
        }
    }

    thetaValues.Sort();
    edgeCollarSamplesPerSide = thetaValues.Count(v => v >= 3.0 * Math.PI / 8.0);

    var values = new List<double>(thetaValues.Count);
    foreach (double theta in thetaValues)
    {
        values.Add(Math.Sin(theta));
    }
    return values;
}


static void AnalyzeBoundaryBehavior(CalculationResultDto result)
{
    int n = result.GridSize;
    if (n <= 1 || result.Points.Count < n * n)
    {
        result.BoundaryBehavior = "Unclassified";
        result.RenderMode = "Wireframe";
        return;
    }

    const double boundaryThreshold = 0.78;
    const double bigJumpThreshold = 0.35;
    const double nearSaturationThreshold = 0.94;
    const double boundedOscillationThreshold = 0.82;

    int boundarySamples = 0;
    int invalidSamples = 0;
    int largeJumpCount = 0;
    int boundaryLargeJumpCount = 0;
    int interiorLargeJumpCount = 0;
    double zMin = double.PositiveInfinity;
    double zMax = double.NegativeInfinity;
    double zAbsMax = 0.0;

    for (int ix = 0; ix < n; ix++)
    {
        for (int iy = 0; iy < n; iy++)
        {
            Point3Dto point = result.Points[ix * n + iy];
            if (!point.Valid)
            {
                invalidSamples++;
                continue;
            }

            double nx = TanSpace.ToNormalized(point.X);
            double ny = TanSpace.ToNormalized(point.Y);
            bool isBoundary = Math.Abs(nx) >= boundaryThreshold || Math.Abs(ny) >= boundaryThreshold;
            if (!isBoundary) continue;

            double nz = TanSpace.ToNormalized(point.Z);
            boundarySamples++;
            zMin = Math.Min(zMin, nz);
            zMax = Math.Max(zMax, nz);
            zAbsMax = Math.Max(zAbsMax, Math.Abs(nz));
        }
    }

    for (int ix = 0; ix < n; ix++)
    {
        for (int iy = 0; iy < n; iy++)
        {
            Point3Dto a = result.Points[ix * n + iy];
            if (!a.Valid) continue;
            if (ix + 1 < n) InspectSegment(a, result.Points[(ix + 1) * n + iy]);
            if (iy + 1 < n) InspectSegment(a, result.Points[ix * n + iy + 1]);
        }
    }

    if (boundarySamples == 0)
    {
        zMin = 0;
        zMax = 0;
    }

    double zRange = zMax - zMin;
    result.BoundarySampleCount = boundarySamples;
    result.InvalidSampleCount = invalidSamples;
    result.LargeJumpCount = largeJumpCount;
    result.BoundaryZMin = zMin;
    result.BoundaryZMax = zMax;
    result.BoundaryZAbsMax = zAbsMax;

    if (invalidSamples > Math.Max(2, n / 3) || interiorLargeJumpCount > Math.Max(6, n / 2))
    {
        result.BoundaryBehavior = "PoleLikeOrDiscontinuous";
        result.RenderMode = "SplitWireframe";
    }
    else if (boundarySamples > 0
        && zAbsMax < boundedOscillationThreshold
        && zRange > 0.45
        && boundaryLargeJumpCount > Math.Max(8, n / 2))
    {
        result.BoundaryBehavior = "BoundedOscillatory";
        result.RenderMode = "Wireframe";
    }
    else if (zAbsMax >= nearSaturationThreshold && zRange > 1.2)
    {
        result.BoundaryBehavior = "DivergentDirectional";
        result.RenderMode = "ClippedWireframe";
    }
    else if (largeJumpCount > Math.Max(12, n))
    {
        result.BoundaryBehavior = "SteepOrMixed";
        result.RenderMode = "SplitWireframe";
    }
    else
    {
        result.BoundaryBehavior = "SmoothMapped";
        result.RenderMode = "Wireframe";
    }

    void InspectSegment(Point3Dto a, Point3Dto b)
    {
        if (!b.Valid) return;
        double ax = TanSpace.ToNormalized(a.X);
        double ay = TanSpace.ToNormalized(a.Y);
        double bx = TanSpace.ToNormalized(b.X);
        double by = TanSpace.ToNormalized(b.Y);
        bool boundary = Math.Abs(ax) >= boundaryThreshold || Math.Abs(ay) >= boundaryThreshold
            || Math.Abs(bx) >= boundaryThreshold || Math.Abs(by) >= boundaryThreshold;
        double jump = Math.Abs(TanSpace.ToNormalized(a.Z) - TanSpace.ToNormalized(b.Z));
        if (jump <= bigJumpThreshold) return;
        largeJumpCount++;
        if (boundary) boundaryLargeJumpCount++;
        else interiorLargeJumpCount++;
    }
}

static double ProjectiveToReal(double p)
{
    double denominator = Math.Sqrt(Math.Max(0.000000000001, 1.0 - p * p));
    return p / denominator;
}
