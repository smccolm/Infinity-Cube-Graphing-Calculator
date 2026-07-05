using System.Windows;
using GraphCalc.Shared;

namespace GraphCalc.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        TestLog.Initialize("GraphCalc.UI", defaultEnabled: true);
        TestLog.Info("UI.Startup", "WPF app startup.");
        base.OnStartup(e);
    }
}
