using System.Security.Principal;
using Aurevix.Core;
using Aurevix.Core.Diagnostics;
using Aurevix.Diagnostics;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

/// <summary>Contenedor sencillo de servicios compartidos por toda la aplicación.</summary>
public static class AppServices
{
    public static SemVersion CurrentVersion { get; } =
        SemVersion.TryParse(typeof(AppServices).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : null, out var s) ? s! : new SemVersion(0, 1, 0);

    public static AurevixSettings Settings { get; private set; } = new();
    public static AurevixState State { get; private set; } = new();

    public static IReadOnlyList<TweakDefinition> Catalog { get; private set; } = [];
    public static IReadOnlyList<ProfileDefinition> Profiles { get; private set; } = [];
    public static TweakEngine Tweaks { get; private set; } = null!;
    public static ProfileManager ProfileManager { get; private set; } = null!;
    public static UpdateService Updates { get; private set; } = null!;
    public static DiagnosticReport? LastReport { get; set; }

    public static int WindowsBuild => Environment.OSVersion.Version.Build;

    public static bool IsAdmin
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static void Initialize()
    {
        AurevixPaths.EnsureCreated();
        Settings = JsonStore.LoadOrDefault<AurevixSettings>(AurevixPaths.SettingsFile);
        State = JsonStore.LoadOrDefault<AurevixState>(AurevixPaths.StateFile);
        Core.Logging.AurevixLog.RetentionDays = Settings.LogRetentionDays;
        Core.Logging.AurevixLog.Initialize(AurevixPaths.Logs);

        Catalog = TweakEngine.LoadCatalog();
        Profiles = TweakEngine.LoadProfiles();
        var power = new PowercfgPowerPlanAccess();
        Tweaks = new TweakEngine(new WindowsRegistryAccess(), power, AurevixPaths.Backups, WindowsBuild, IsAdmin);
        ProfileManager = new ProfileManager(Tweaks, Catalog, Profiles, power);
        Updates = new UpdateService();
    }

    public static void SaveSettings() => JsonStore.Save(AurevixPaths.SettingsFile, Settings);
    public static void SaveState() => JsonStore.Save(AurevixPaths.StateFile, State);

    public static async Task<DiagnosticReport> RunDiagnosticsAsync(bool includeWindowsUpdate = true)
    {
        var snapshot = await new WindowsSystemCollector().CollectAsync(includeWindowsUpdate);
        var report = DiagnosticAnalyzer.Analyze(snapshot);
        ReportWriter.Save(report, AurevixPaths.Reports);
        LastReport = report;
        return report;
    }
}
