using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.ControlCenter.Views.Pages;

namespace Aurevix.ControlCenter;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Func<FrameworkElement>> _pages;
    private readonly Dictionary<string, RadioButton> _nav;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Versión {AppServices.CurrentVersion} · canal {AppServices.Settings.UpdateChannel}";

        _pages = new()
        {
            ["home"] = () => new HomePage(this),
            ["diagnostics"] = () => new DiagnosticsPage(),
            ["performance"] = () => new PerformancePage(),
            ["gameboost"] = () => new GameBoostPage(),
            ["benchmark"] = () => new BenchmarkPage(),
            ["network"] = () => new NetworkPage(),
            ["profiles"] = () => new ProfilesPage(),
            ["optimization"] = () => new OptimizationPage(),
            ["cleanup"] = () => new CleanupPage(),
            ["tools"] = () => new ToolsPage(),
            ["apps"] = () => new AppsPage(),
            ["appearance"] = () => new AppearancePage(),
            ["updates"] = () => new UpdatesPage(),
            ["recovery"] = () => new RecoveryPage(),
            ["settings"] = () => new SettingsPage(),
        };
        _nav = new()
        {
            ["home"] = NavHome, ["diagnostics"] = NavDiagnostics, ["performance"] = NavPerformance,
            ["benchmark"] = NavBenchmark, ["cleanup"] = NavCleanup, ["gameboost"] = NavGameBoost, ["network"] = NavNetwork, ["tools"] = NavTools, ["apps"] = NavApps,
            ["profiles"] = NavProfiles, ["optimization"] = NavOptimization, ["appearance"] = NavAppearance,
            ["updates"] = NavUpdates, ["recovery"] = NavRecovery, ["settings"] = NavSettings,
        };
        PageHost.Content = _pages["home"]();
    }

    public void Navigate(string page)
    {
        if (_nav.TryGetValue(page, out var rb))
        {
            if (rb.IsChecked == true) PageHost.Content = _pages[page]();
            else rb.IsChecked = true;
        }
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (_pages is null || sender is not RadioButton rb) return;
        var key = _nav.FirstOrDefault(kv => kv.Value == rb).Key;
        if (key is null) return;
        if (PageHost.Content is IDisposable d) d.Dispose();
        PageHost.Content = _pages[key]();
        Views.Ui.Enter(PageHost, 0, 10);
    }
}
