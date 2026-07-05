using System.Collections.Concurrent;
using System.Text;

namespace GraphCalc.Shared;

public static class TestLog
{
    private static readonly object Gate = new();
    private static string _component = "GraphCalc";
    private static string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GraphCalc",
        "logs");

    public static bool TestMode { get; private set; }
    public static string LogDirectory => _logDirectory;

    public static void Initialize(string component, bool defaultEnabled = false, string? logDirectory = null)
    {
        _component = string.IsNullOrWhiteSpace(component) ? "GraphCalc" : component.Trim();
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? _logDirectory : logDirectory.Trim();
        Directory.CreateDirectory(_logDirectory);

        string? env = Environment.GetEnvironmentVariable("GRAPHCALC_TEST_MODE");
        TestMode = defaultEnabled;
        if (!string.IsNullOrWhiteSpace(env))
        {
            TestMode = env.Equals("1", StringComparison.OrdinalIgnoreCase)
                || env.Equals("true", StringComparison.OrdinalIgnoreCase)
                || env.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        Info("TestLog.Initialize", "Logger initialized.", new Dictionary<string, string>
        {
            ["component"] = _component,
            ["testMode"] = TestMode.ToString(),
            ["logDirectory"] = _logDirectory
        });
    }

    public static void SetTestMode(bool enabled)
    {
        TestMode = enabled;
        WriteLine("TestLog.ModeChanged", "Logger test mode changed.", "INFO", new Dictionary<string, string>
        {
            ["testMode"] = enabled.ToString()
        }, force: true);
    }

    public static void Info(string eventName, string message, IDictionary<string, string>? data = null)
    {
        if (!TestMode) return;
        WriteLine(eventName, message, "INFO", data, force: false);
    }

    public static void Warn(string eventName, string message, IDictionary<string, string>? data = null)
    {
        if (!TestMode) return;
        WriteLine(eventName, message, "WARN", data, force: false);
    }

    public static void Error(string eventName, string message, Exception? exception = null, IDictionary<string, string>? data = null)
    {
        var merged = new Dictionary<string, string>();
        if (data != null)
        {
            foreach (var pair in data) merged[pair.Key] = pair.Value;
        }

        if (exception != null)
        {
            merged["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
            merged["exceptionMessage"] = exception.Message;
            merged["stackTrace"] = exception.StackTrace ?? "";
        }

        WriteLine(eventName, message, "ERROR", merged, force: true);
    }

    private static void WriteLine(string eventName, string message, string level, IDictionary<string, string>? data, bool force)
    {
        if (!force && !TestMode) return;

        lock (Gate)
        {
            Directory.CreateDirectory(_logDirectory);
            string file = Path.Combine(_logDirectory, $"{_component}-{DateTime.UtcNow:yyyyMMdd}.log");
            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTime.UtcNow:O}] LEVEL={level} COMPONENT={_component} EVENT={eventName}");
            builder.AppendLine($"MESSAGE: {message}");
            if (data != null)
            {
                foreach (var pair in data.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string value = pair.Value?.Replace("\r", " ").Replace("\n", " ") ?? "";
                    builder.AppendLine($"  {pair.Key}: {value}");
                }
            }
            builder.AppendLine(new string('-', 96));
            File.AppendAllText(file, builder.ToString());
        }
    }
}
