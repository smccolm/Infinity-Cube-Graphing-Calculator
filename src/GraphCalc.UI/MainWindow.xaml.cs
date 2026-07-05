using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GraphCalc.Shared;

namespace GraphCalc.UI;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new("http://127.0.0.1:8765");
    private readonly List<FunctionRow> _rows = new();
    private readonly Dictionary<string, SurfaceRenderData> _surfaces = new();
    private bool _testMode = true;
    private LodProfile _lod = LodProfile.Standard;
    private GraphRenderStyle _renderStyle = GraphRenderStyle.Mesh;
    private SurfaceQuality _surfaceQuality = SurfaceQuality.Smooth;
    private bool _showSurfaceTriangleEdges;

    private static readonly Color[] FunctionColors =
    {
        Colors.Red,
        Colors.Orange,
        Colors.Blue,
        Colors.Green,
        Colors.Purple,
        Colors.Teal,
        Colors.Brown,
        Colors.DeepPink,
        Colors.DarkCyan,
        Colors.Goldenrod,
        Colors.DarkViolet,
        Colors.OliveDrab,
        Colors.Crimson,
        Colors.SteelBlue,
        Colors.Black
    };

    public MainWindow()
    {
        InitializeComponent();
        TanCubeView.RotationChanged += orientation => GimbalView.SetOrientation(orientation);
        GimbalView.SetOrientation(TanCubeView.Orientation);
        BuildFunctionRows();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TestLog.Info("UI.Loaded", "Main window loaded.", new Dictionary<string, string>
        {
            ["defaultLod"] = _lod.Name,
            ["axisSampleCount"] = _lod.AxisSamples.ToString(),
            ["sampler"] = TanSpace.SamplerName,
            ["fixedBounds"] = "X,Y,Z=[-π/2,π/2]; autoscale disabled"
        });
        HealthResponse? health = await CheckApiHealthAsync(showMessageBox: false);
        if (health == null)
        {
            SetApiUnavailableOnVisibleDefaults();
            return;
        }

        await CalculateVisibleFunctionsAsync();
    }

    private void BuildFunctionRows()
    {
        string[] defaults = new string[15];
        defaults[0] = "sin(x)+cos(y)";
        defaults[1] = "0.12*(x*x-y*y)";
        defaults[2] = "tan(x/4)+sin(y/2)";

        for (int i = 0; i < 15; i++)
        {
            string label = "F" + (i + 1).ToString(CultureInfo.InvariantCulture);
            bool isVisible = i < 3;
            AddFunctionRow(label, defaults[i], FunctionColors[i], isVisible);
        }
    }

    private void AddFunctionRow(string label, string expression, Color color, bool isVisible)
    {
        var rowBorder = new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(8),
            MinHeight = 52
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var visible = new CheckBox { IsChecked = isVisible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var swatch = new Rectangle { Width = 24, Height = 24, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 0) };
        var labelBlock = new TextBlock { Text = label + ":", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        var expressionBox = new TextBox { Text = expression, MinWidth = 260, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var calculate = new Button { Content = "Calc", Padding = new Thickness(8, 2, 8, 2) };
        var status = new TextBlock { Text = string.IsNullOrWhiteSpace(expression) ? "Empty" : "Not calculated", Foreground = Brushes.Gray, Margin = new Thickness(56, 6, 0, 0), TextWrapping = TextWrapping.Wrap };

        var inner = new DockPanel();
        DockPanel.SetDock(labelBlock, Dock.Left);
        inner.Children.Add(labelBlock);
        inner.Children.Add(expressionBox);

        Grid.SetColumn(visible, 0);
        Grid.SetColumn(swatch, 1);
        Grid.SetColumn(inner, 2);
        Grid.SetColumn(calculate, 3);
        Grid.SetColumn(status, 2);
        Grid.SetColumnSpan(status, 2);
        Grid.SetRow(status, 1);

        grid.Children.Add(visible);
        grid.Children.Add(swatch);
        grid.Children.Add(inner);
        grid.Children.Add(calculate);
        grid.Children.Add(status);
        rowBorder.Child = grid;
        FunctionStackPanel.Children.Add(rowBorder);

        var row = new FunctionRow(label, expressionBox, visible, swatch, status, color);
        _rows.Add(row);

        visible.Checked += async (_, _) => await OnVisibilityChangedAsync(row);
        visible.Unchecked += async (_, _) => await OnVisibilityChangedAsync(row);
        expressionBox.TextChanged += (_, _) =>
        {
            row.StatusText.Text = string.IsNullOrWhiteSpace(row.ExpressionBox.Text) ? "Empty" : "Edited. Click Calc.";
            TestLog.Info("UI.Function.ExpressionChanged", "Function expression changed.", new Dictionary<string, string>
            {
                ["label"] = row.Label,
                ["expressionLength"] = row.ExpressionBox.Text.Length.ToString(CultureInfo.InvariantCulture)
            });
        };
        calculate.Click += async (_, _) => await CalculateRowAsync(row);
    }

    private async Task CalculateVisibleFunctionsAsync()
    {
        TestLog.Info("UI.CalculateVisible.Start", "Calculate visible functions requested.", new Dictionary<string, string>
        {
            ["lodName"] = _lod.Name,
            ["axisSampleCount"] = _lod.AxisSamples.ToString(CultureInfo.InvariantCulture),
            ["visibleRows"] = _rows.Count(r => r.VisibleCheckBox.IsChecked == true).ToString(CultureInfo.InvariantCulture)
        });

        foreach (FunctionRow row in _rows.Where(r => r.VisibleCheckBox.IsChecked == true))
        {
            await CalculateRowAsync(row);
        }
    }

    private async Task CalculateRowAsync(FunctionRow row)
    {
        string expression = row.ExpressionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(expression))
        {
            row.StatusText.Text = "Skipped: empty expression.";
            TestLog.Info("UI.Calculate.SkippedEmpty", "Calculation skipped because expression is empty.", new Dictionary<string, string>
            {
                ["label"] = row.Label
            });
            return;
        }

        row.StatusText.Text = "Calculating...";
        StatusText.Text = $"Calculating {row.Label} at {_lod.Name} LOD...";
        TestLog.Info("UI.Calculate.Start", "User requested calculation.", new Dictionary<string, string>
        {
            ["label"] = row.Label,
            ["expression"] = expression,
            ["lodName"] = _lod.Name,
            ["axisSampleCount"] = _lod.AxisSamples.ToString(CultureInfo.InvariantCulture),
            ["sampler"] = TanSpace.SamplerName,
            ["fixedBounds"] = "X,Y,Z=[-π/2,π/2]; autoscale disabled"
        });

        try
        {
            await _api.SetTestModeAsync(_testMode);
            GraphFunctionDto function = await _api.UpsertFunctionAsync(new UpsertFunctionRequest
            {
                Id = row.FunctionId,
                Label = row.Label,
                Expression = expression,
                ColorHex = ColorToHex(row.Color),
                IsVisible = row.VisibleCheckBox.IsChecked == true
            });
            row.FunctionId = function.Id;

            CalculationResultDto result = await _api.CalculateAsync(new CalculationRequest
            {
                FunctionId = function.Id,
                Label = row.Label,
                Expression = expression,
                ColorHex = ColorToHex(row.Color),
                IsVisible = row.VisibleCheckBox.IsChecked == true,
                SamplerName = TanSpace.SamplerName,
                LodName = _lod.Name,
                AxisSampleCount = _lod.AxisSamples,
                EdgeInset = _lod.EdgeInset,
                GridSize = _lod.AxisSamples,
                XMin = TanSpace.AxisMin,
                XMax = TanSpace.AxisMax,
                YMin = TanSpace.AxisMin,
                YMax = TanSpace.AxisMax
            });

            _surfaces[row.Label] = new SurfaceRenderData
            {
                FunctionId = function.Id,
                Label = row.Label,
                Expression = expression,
                Color = row.Color,
                IsVisible = row.VisibleCheckBox.IsChecked == true,
                GridSize = result.GridSize,
                BoundaryBehavior = result.BoundaryBehavior,
                RenderMode = result.RenderMode,
                InvalidSampleCount = result.InvalidSampleCount,
                LargeJumpCount = result.LargeJumpCount,
                BoundaryZMin = result.BoundaryZMin,
                BoundaryZMax = result.BoundaryZMax,
                Points = result.Points
            };

            RefreshViews();
            string sampleText = result.Points.Count.ToString("N0", CultureInfo.InvariantCulture);
            row.StatusText.Text = result.FromCache
                ? $"Rendered from cache. {sampleText} samples. Result {result.ResultId[..8]}."
                : $"Rendered. {sampleText} samples. Result {result.ResultId[..8]}.";
            StatusText.Text = $"{row.Label} ready.";
            TestLog.Info("UI.Calculate.Complete", "Calculation result rendered.", new Dictionary<string, string>
            {
                ["label"] = row.Label,
                ["resultId"] = result.ResultId,
                ["points"] = result.Points.Count.ToString(CultureInfo.InvariantCulture),
                ["fromCache"] = result.FromCache.ToString(),
                ["lodName"] = _lod.Name,
                ["axisSampleCount"] = _lod.AxisSamples.ToString(CultureInfo.InvariantCulture),
                ["boundaryBehavior"] = result.BoundaryBehavior,
                ["renderMode"] = result.RenderMode,
                ["invalidSamples"] = result.InvalidSampleCount.ToString(CultureInfo.InvariantCulture),
                ["largeJumpCount"] = result.LargeJumpCount.ToString(CultureInfo.InvariantCulture),
                ["graphRenderStyle"] = _renderStyle.ToString(),
                ["renderInstanceKey"] = row.Label
            });
        }
        catch (Exception ex)
        {
            row.StatusText.Text = ex is System.Net.Http.HttpRequestException || ex.InnerException is System.Net.Http.HttpRequestException
                ? "API unavailable"
                : "Error: " + ex.Message;
            StatusText.Text = "Error. Start or check the API process, then check File > Test Mode.";
            TestLog.Error("UI.Calculate.Failed", "UI calculation call failed.", ex, new Dictionary<string, string>
            {
                ["label"] = row.Label,
                ["expression"] = expression,
                ["lodName"] = _lod.Name
            });
        }
    }

    private async Task OnVisibilityChangedAsync(FunctionRow row)
    {
        ApplyVisibilityAndRefresh(row);

        if (row.VisibleCheckBox.IsChecked == true
            && !_surfaces.ContainsKey(row.Label)
            && !string.IsNullOrWhiteSpace(row.ExpressionBox.Text))
        {
            await CalculateRowAsync(row);
        }
    }

    private void SetApiUnavailableOnVisibleDefaults()
    {
        foreach (FunctionRow row in _rows.Where(r => r.VisibleCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(r.ExpressionBox.Text)))
        {
            row.StatusText.Text = "API unavailable";
        }
    }

    private void ApplyVisibilityAndRefresh(FunctionRow row)
    {
        if (_surfaces.TryGetValue(row.Label, out SurfaceRenderData? surface))
        {
            surface.IsVisible = row.VisibleCheckBox.IsChecked == true;
            RefreshViews();
        }
        TestLog.Info("UI.Function.VisibilityChanged", "Function visibility changed. Fixed TAN bounds are unchanged.", new Dictionary<string, string>
        {
            ["label"] = row.Label,
            ["isVisible"] = (row.VisibleCheckBox.IsChecked == true).ToString(),
            ["fixedBounds"] = "X,Y,Z=[-π/2,π/2]; autoscale disabled"
        });
    }

    private void RefreshViews()
    {
        List<SurfaceRenderData> data = _surfaces.Values.ToList();
        TanCubeView.SetRenderStyle(_renderStyle);
        XYView.SetRenderStyle(_renderStyle);
        XZView.SetRenderStyle(_renderStyle);
        YZView.SetRenderStyle(_renderStyle);
        TanCubeView.SetSurfaceQuality(_surfaceQuality);
        XYView.SetSurfaceQuality(_surfaceQuality);
        XZView.SetSurfaceQuality(_surfaceQuality);
        YZView.SetSurfaceQuality(_surfaceQuality);
        TanCubeView.SetSurfaceTriangleEdges(_showSurfaceTriangleEdges);
        XYView.SetSurfaceTriangleEdges(_showSurfaceTriangleEdges);
        XZView.SetSurfaceTriangleEdges(_showSurfaceTriangleEdges);
        YZView.SetSurfaceTriangleEdges(_showSurfaceTriangleEdges);
        TanCubeView.SetSurfaces(data);
        XYView.SetSurfaces(data);
        XZView.SetSurfaces(data);
        YZView.SetSurfaces(data);
        TestLog.Info("UI.Render.Refresh", "Renderer data refreshed with fixed TAN bounds.", new Dictionary<string, string>
        {
            ["surfaceCount"] = data.Count.ToString(CultureInfo.InvariantCulture),
            ["visibleSurfaceCount"] = data.Count(s => s.IsVisible).ToString(CultureInfo.InvariantCulture),
            ["graphRenderStyle"] = _renderStyle.ToString(),
            ["surfaceQuality"] = _surfaceQuality.ToString(),
            ["surfaceTriangleEdges"] = _showSurfaceTriangleEdges.ToString(),
            ["estimatedSurfaceTriangles"] = EstimateVisibleTriangleCount(data).ToString(CultureInfo.InvariantCulture),
            ["fixedBounds"] = "X,Y,Z=[-π/2,π/2]; autoscale disabled"
        });
    }

    private async Task<HealthResponse?> CheckApiHealthAsync(bool showMessageBox)
    {
        try
        {
            HealthResponse health = await _api.HealthAsync();
            _testMode = health.TestMode;
            TestLog.SetTestMode(_testMode);
            StatusText.Text = "API health succeeded.";
            TestLog.Info("UI.Api.Health.Success", "API health check succeeded.", new Dictionary<string, string>
            {
                ["dbPath"] = health.DbPath,
                ["logPath"] = health.LogPath,
                ["testMode"] = health.TestMode.ToString()
            });
            if (showMessageBox)
            {
                MessageBox.Show(this,
                    $"API OK.\n\nDatabase:\n{health.DbPath}\n\nLogs:\n{health.LogPath}",
                    "GraphCalc API",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return health;
        }
        catch (Exception ex)
        {
            StatusText.Text = "API not reachable. Start GraphCalc.Api first, then retry.";
            TestLog.Error("UI.Api.Health.Failed", "API health check failed.", ex);
            if (showMessageBox)
            {
                MessageBox.Show(this,
                    "The API is not reachable. Start GraphCalc.Api first.\n\n" + ex.Message,
                    "GraphCalc API",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return null;
        }
    }

    private async Task SetTestModeAsync(bool enabled)
    {
        _testMode = enabled;
        TestLog.SetTestMode(enabled);
        try
        {
            await _api.SetTestModeAsync(enabled);
            StatusText.Text = enabled ? "Test Mode enabled." : "Test Mode disabled.";
        }
        catch
        {
            StatusText.Text = enabled
                ? "UI Test Mode enabled. API not reachable yet."
                : "UI Test Mode disabled. API not reachable yet.";
        }
    }

    private void TestModeMenu_Click(object sender, RoutedEventArgs e)
    {
        OpenTestModeDialog();
    }

    private void OpenTestModeDialog()
    {
        string logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GraphCalc", "logs");
        System.IO.Directory.CreateDirectory(logPath);

        var window = new Window
        {
            Owner = this,
            Title = "GraphCalc Test Mode",
            Width = 560,
            Height = 550,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        var testMode = new CheckBox { Content = "Enable Test Mode", IsChecked = _testMode, Margin = new Thickness(0, 0, 0, 14) };
        var apiStatus = new TextBlock { Text = "API status: not checked", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var logText = new TextBlock { Text = logPath, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 10) };

        var samplerText = new TextBlock
        {
            Text = CurrentSamplerReadout() + "\n" + CurrentRenderReadout(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 10)
        };

        var apiButton = new Button { Content = "Check API Health", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var calcVisibleButton = new Button { Content = "Calculate Visible Functions", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var surfaceEdges = new CheckBox { Content = "Show surface triangle edges", IsChecked = _showSurfaceTriangleEdges, Margin = new Thickness(0, 0, 0, 10) };
        var openLogsButton = new Button { Content = "Open Log Folder", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var copyLogsButton = new Button { Content = "Copy Log Folder Path", Padding = new Thickness(10, 4, 10, 4) };
        var closeButton = new Button { Content = "Close", Width = 90, Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Right };

        testMode.Checked += async (_, _) => await SetTestModeAsync(true);
        testMode.Unchecked += async (_, _) => await SetTestModeAsync(false);
        apiButton.Click += async (_, _) =>
        {
            apiStatus.Text = "API status: checking...";
            HealthResponse? health = await CheckApiHealthAsync(showMessageBox: false);
            apiStatus.Text = health == null
                ? "API status: failed. Start GraphCalc.Api and retry."
                : $"API status: healthy. Test Mode: {health.TestMode}.";
        };
        calcVisibleButton.Click += async (_, _) => await CalculateVisibleFunctionsAsync();
        surfaceEdges.Checked += (_, _) =>
        {
            SetSurfaceTriangleEdges(true);
            samplerText.Text = CurrentSamplerReadout() + "\n" + CurrentRenderReadout();
        };
        surfaceEdges.Unchecked += (_, _) =>
        {
            SetSurfaceTriangleEdges(false);
            samplerText.Text = CurrentSamplerReadout() + "\n" + CurrentRenderReadout();
        };
        openLogsButton.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logPath}\"") { UseShellExecute = true });
        copyLogsButton.Click += (_, _) => Clipboard.SetText(logPath);
        closeButton.Click += (_, _) => window.Close();

        root.Children.Add(testMode);
        root.Children.Add(new TextBlock { Text = "API", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(apiStatus);
        root.Children.Add(apiButton);
        root.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
        root.Children.Add(new TextBlock { Text = "Calculation", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(calcVisibleButton);
        root.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
        root.Children.Add(new TextBlock { Text = "Sampler and rendering", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(samplerText);
        root.Children.Add(surfaceEdges);
        root.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
        root.Children.Add(new TextBlock { Text = "Logs", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(logText);

        var logButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        logButtons.Children.Add(openLogsButton);
        logButtons.Children.Add(copyLogsButton);
        root.Children.Add(logButtons);
        root.Children.Add(closeButton);

        window.Content = root;
        TestLog.Info("UI.TestModeDialog.Opened", "Test Mode dialog opened.");
        window.ShowDialog();
    }

    private async void LevelOfDetailMenu_Click(object sender, RoutedEventArgs e)
    {
        LodProfile? selected = ShowLevelOfDetailDialog();
        if (selected == null) return;
        if (selected.AxisSamples == _lod.AxisSamples && selected.Name == _lod.Name) return;

        LodProfile old = _lod;
        _lod = selected;
        StatusText.Text = $"LOD changed to {_lod.Name}. Recalculating visible functions.";
        TestLog.Info("UI.Lod.Changed", "Level of Detail changed.", new Dictionary<string, string>
        {
            ["oldLod"] = old.Name,
            ["oldAxisSampleCount"] = old.AxisSamples.ToString(CultureInfo.InvariantCulture),
            ["newLod"] = _lod.Name,
            ["newAxisSampleCount"] = _lod.AxisSamples.ToString(CultureInfo.InvariantCulture),
            ["sampler"] = TanSpace.SamplerName,
            ["fixedBounds"] = "X,Y,Z=[-π/2,π/2]; autoscale disabled"
        });

        foreach (FunctionRow row in _rows)
        {
            if (!string.IsNullOrWhiteSpace(row.ExpressionBox.Text))
            {
                row.StatusText.Text = row.VisibleCheckBox.IsChecked == true
                    ? "Stale. Recalculating..."
                    : "Stale. Hidden. Recalculate pending.";
            }
        }

        await CalculateVisibleFunctionsAsync();
    }

    private LodProfile? ShowLevelOfDetailDialog()
    {
        var profiles = LodProfile.DefaultOptions.ToList();
        var heavyProfiles = LodProfile.HeavyOptions.ToList();
        var window = new Window
        {
            Owner = this,
            Title = "GraphCalc Level of Detail",
            Width = 560,
            Height = 470,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "LOD controls the deterministic boundary-weighted TAN sampler. Changing it recalculates visible non-empty functions.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var list = new ListBox { Height = 155, Margin = new Thickness(0, 0, 0, 8) };
        foreach (LodProfile profile in profiles) list.Items.Add(profile);
        list.SelectedItem = profiles.FirstOrDefault(p => p.AxisSamples == _lod.AxisSamples) ?? profiles[1];
        root.Children.Add(list);

        var allowHeavy = new CheckBox
        {
            Content = "Allow heavy LOD modes",
            IsChecked = LodProfile.HeavyOptions.Any(p => p.AxisSamples == _lod.AxisSamples),
            Margin = new Thickness(0, 0, 0, 12)
        };
        allowHeavy.Checked += (_, _) =>
        {
            foreach (LodProfile profile in heavyProfiles)
            {
                if (!list.Items.Contains(profile)) list.Items.Add(profile);
            }
        };
        allowHeavy.Unchecked += (_, _) =>
        {
            foreach (LodProfile profile in heavyProfiles)
            {
                if (list.SelectedItem == profile) list.SelectedItem = profiles.Last();
                list.Items.Remove(profile);
            }
        };
        if (allowHeavy.IsChecked == true)
        {
            foreach (LodProfile profile in heavyProfiles)
            {
                if (!list.Items.Contains(profile)) list.Items.Add(profile);
            }
            list.SelectedItem = LodProfile.BuiltIns.FirstOrDefault(p => p.AxisSamples == _lod.AxisSamples) ?? profiles.Last();
        }
        root.Children.Add(allowHeavy);

        root.Children.Add(new TextBlock { Text = "Custom odd sample count per axis, optional. Range: 9 to 513.", Margin = new Thickness(0, 0, 0, 4) });
        var customBox = new TextBox { Text = "", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(customBox);

        var detail = new TextBlock
        {
            Text = CurrentSamplerReadout() + " R12 preserves the R8 boundary-weighted sampler and adds cinematic rasterized Surface rendering. Heavy modes may be slow in the WPF renderer.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(detail);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 88, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 88, Padding = new Thickness(10, 4, 10, 4) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        LodProfile? selected = null;
        ok.Click += (_, _) =>
        {
            string custom = customBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(custom))
            {
                if (!int.TryParse(custom, NumberStyles.Integer, CultureInfo.InvariantCulture, out int customN))
                {
                    MessageBox.Show(window, "Custom LOD must be a whole number.", "Level of Detail", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (customN < 9 || customN > 513)
                {
                    MessageBox.Show(window, "Custom LOD must be between 9 and 513.", "Level of Detail", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (customN % 2 == 0)
                {
                    MessageBox.Show(window, "Custom LOD must be odd so the center sample is exactly 0.", "Level of Detail", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                selected = new LodProfile("Custom", customN, TanSpace.DefaultEdgeInset);
            }
            else
            {
                selected = list.SelectedItem as LodProfile;
            }
            window.DialogResult = true;
            window.Close();
        };
        cancel.Click += (_, _) =>
        {
            window.DialogResult = false;
            window.Close();
        };

        window.Content = root;
        bool? result = window.ShowDialog();
        return result == true ? selected : null;
    }


    private string CurrentSamplerReadout()
    {
        double thetaMax = TanSpace.HalfPi - _lod.EdgeInset;
        double ratio = thetaMax / TanSpace.HalfPi;
        double tanThetaMax = Math.Tan(thetaMax);
        int edgeCollarEstimate = Math.Max(1, _lod.AxisSamples / 4);
        return $"LOD: {_lod.Name}; sampler: {TanSpace.SamplerName}; axis samples: {_lod.AxisSamples}; samples per function: {_lod.TotalSamples:N0}; thetaMax: π/2 - {_lod.EdgeInset:0.########}; outer theta ratio: {ratio:0.########}; tan(thetaMax): {tanThetaMax:0.###}; estimated edge-collar samples per side: {edgeCollarEstimate}.";
    }

    private string CurrentRenderReadout()
    {
        List<SurfaceRenderData> visible = _surfaces.Values.Where(s => s.IsVisible).ToList();
        return $"Render mode: {_renderStyle}; surface quality: {_surfaceQuality}; visible functions: {visible.Count}; estimated surface triangles: {EstimateVisibleTriangleCount(visible):N0}; surface triangle edges: {_showSurfaceTriangleEdges}; cached results render as per-function instances.";
    }

    private static int EstimateVisibleTriangleCount(IEnumerable<SurfaceRenderData> surfaces)
    {
        int total = 0;
        foreach (SurfaceRenderData surface in surfaces.Where(s => s.IsVisible))
        {
            int n = surface.GridSize;
            if (n > 1) total += Math.Max(0, (n - 1) * (n - 1) * 2);
        }
        return total;
    }

    private void GimbalCloseButton_Click(object sender, RoutedEventArgs e)
    {
        GimbalPanel.Visibility = Visibility.Collapsed;
        TestLog.Info("UI.Gimbal.Hidden", "Gimbal was hidden by the user.");
    }

    private void MeshMenu_Click(object sender, RoutedEventArgs e)
    {
        SetGraphRenderStyle(GraphRenderStyle.Mesh);
    }

    private void SurfaceMenu_Click(object sender, RoutedEventArgs e)
    {
        SetGraphRenderStyle(GraphRenderStyle.Surface);
    }

    private void SetGraphRenderStyle(GraphRenderStyle style)
    {
        _renderStyle = style;
        MeshMenuItem.Header = style == GraphRenderStyle.Mesh ? "✓ Mesh" : "Mesh";
        SurfaceMenuItem.Header = style == GraphRenderStyle.Surface ? "✓ Surface" : "Surface";
        StatusText.Text = style == GraphRenderStyle.Surface ? "Surface view selected." : "Mesh view selected.";
        TestLog.Info("UI.View.RenderStyleChanged", "Graph render style changed without recalculating samples.", new Dictionary<string, string>
        {
            ["graphRenderStyle"] = style.ToString(),
            ["visibleSurfaceCount"] = _surfaces.Values.Count(s => s.IsVisible).ToString(CultureInfo.InvariantCulture),
            ["estimatedSurfaceTriangles"] = EstimateVisibleTriangleCount(_surfaces.Values).ToString(CultureInfo.InvariantCulture),
            ["surfaceQuality"] = _surfaceQuality.ToString()
        });
        RefreshViews();
    }

    private void SetSurfaceTriangleEdges(bool enabled)
    {
        _showSurfaceTriangleEdges = enabled;
        TestLog.Info("UI.View.SurfaceTriangleEdgesChanged", "Surface triangle edge debug overlay changed.", new Dictionary<string, string>
        {
            ["surfaceTriangleEdges"] = enabled.ToString(),
            ["graphRenderStyle"] = _renderStyle.ToString()
        });
        RefreshViews();
    }

    private void SurfaceQualityFastMenu_Click(object sender, RoutedEventArgs e)
    {
        SetSurfaceQuality(SurfaceQuality.Fast);
    }

    private void SurfaceQualitySmoothMenu_Click(object sender, RoutedEventArgs e)
    {
        SetSurfaceQuality(SurfaceQuality.Smooth);
    }

    private void SurfaceQualityCinematicMenu_Click(object sender, RoutedEventArgs e)
    {
        SetSurfaceQuality(SurfaceQuality.Cinematic);
    }

    private void SetSurfaceQuality(SurfaceQuality quality)
    {
        _surfaceQuality = quality;
        SurfaceQualityFastMenuItem.Header = quality == SurfaceQuality.Fast ? "✓ Fast" : "Fast";
        SurfaceQualitySmoothMenuItem.Header = quality == SurfaceQuality.Smooth ? "✓ Smooth" : "Smooth";
        SurfaceQualityCinematicMenuItem.Header = quality == SurfaceQuality.Cinematic ? "✓ Cinematic" : "Cinematic";
        TestLog.Info("UI.View.SurfaceQualityChanged", "Surface quality changed without recalculating samples.", new Dictionary<string, string>
        {
            ["surfaceQuality"] = quality.ToString(),
            ["graphRenderStyle"] = _renderStyle.ToString(),
            ["visibleSurfaceCount"] = _surfaces.Values.Count(s => s.IsVisible).ToString(CultureInfo.InvariantCulture),
            ["estimatedSurfaceTriangles"] = EstimateVisibleTriangleCount(_surfaces.Values).ToString(CultureInfo.InvariantCulture)
        });
        RefreshViews();
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        TanCubeView.ResetView();
        GimbalView.SetOrientation(TanCubeView.Orientation);
        TestLog.Info("UI.View.Reset", "Cube view reset to the preferred startup orientation.");
    }

    private void ToggleGimbal_Click(object sender, RoutedEventArgs e)
    {
        GimbalPanel.Visibility = GimbalPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        TestLog.Info("UI.Gimbal.Toggled", "Gimbal visibility toggled.", new Dictionary<string, string>
        {
            ["visibility"] = GimbalPanel.Visibility.ToString()
        });
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private sealed class FunctionRow
    {
        public FunctionRow(string label, TextBox expressionBox, CheckBox visibleCheckBox, Rectangle colorSwatch, TextBlock statusText, Color color)
        {
            Label = label;
            ExpressionBox = expressionBox;
            VisibleCheckBox = visibleCheckBox;
            ColorSwatch = colorSwatch;
            StatusText = statusText;
            Color = color;
        }

        public string Label { get; }
        public string? FunctionId { get; set; }
        public TextBox ExpressionBox { get; }
        public CheckBox VisibleCheckBox { get; }
        public Rectangle ColorSwatch { get; }
        public TextBlock StatusText { get; }
        public Color Color { get; }
    }

    private sealed class LodProfile
    {
        public LodProfile(string name, int axisSamples, double edgeInset)
        {
            Name = name;
            AxisSamples = axisSamples;
            EdgeInset = edgeInset;
        }

        public string Name { get; }
        public int AxisSamples { get; }
        public double EdgeInset { get; }
        public int TotalSamples => AxisSamples * AxisSamples;

        public static LodProfile Draft => new("Draft", 81, 0.001);
        public static LodProfile Standard => new("Standard", 161, 0.0001);
        public static LodProfile High => new("High", 257, 0.00002);
        public static LodProfile Ultra => new("Ultra", 385, 0.000005);
        public static LodProfile Insane => new("Insane", 513, 0.000001);

        public static IReadOnlyList<LodProfile> DefaultOptions => new[] { Draft, Standard, High };
        public static IReadOnlyList<LodProfile> HeavyOptions => new[] { Ultra, Insane };
        public static IReadOnlyList<LodProfile> BuiltIns => new[] { Draft, Standard, High, Ultra, Insane };

        public override string ToString()
        {
            return $"{Name}: {AxisSamples} x {AxisSamples} = {TotalSamples:N0} samples per function, edge inset {EdgeInset:0.########}";
        }
    }
}
