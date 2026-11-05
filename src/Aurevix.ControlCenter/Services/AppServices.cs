using System.Security.Principal;
using Aurevix.Core;
using Aurevix.Core.Diagnostics;
using Aurevix.Diagnostics;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

/// <summary>Contenedor sencillo de servicios compartidos por toda la aplicación.</summary>
public static class AppServices
{
    /// <summary>Versión desde InformationalVersion (admite prerelease, p. ej. 0.2.0-beta.1; se ignora el +commit).</summary>
    public static SemVersion CurrentVersion { get; } = ReadVersion();

    private static SemVersion ReadVersion()
    {
        var asm = typeof(AppServices).Assembly;
        var info = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        if (SemVersion.TryParse(info?.InformationalVersion, out var v)) return v!;
        var av = asm.GetName().Version;
        return av is null ? new SemVersion(0, 1, 0) : new SemVersion(av.Major, av.Minor, av.Build);
    }

    public static AurevixSettings Settings { get; private set; } = new();
    public static AurevixState State { get; private set; } = new();

    public static IReadOnlyList<TweakDefinition> Catalog { get; private set; } = [];
    public static IReadOnlyList<ProfileDefinition> Profiles { get; private set; } = [];
    public static TweakEngine Tweaks { get; private set; } = null!;
    public static ProfileManager ProfileManager { get; private set; } = null!;
    public static UpdateService Updates { get; private set; } = null!;
    public static DiagnosticReport? LastReport { get; set; }

    public static int WindowsBuild => Environment.OSVersion.Version.Build;
    public static SystemProfile Hardware { get; private set; } = SystemProfile.Unknown;

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
        Hardware = SystemProfileDetector.Detect();
        // Ámbito de usuario: lo de sistema lo aplica AUREVIX Core Service.
        Tweaks = new TweakEngine(new TweakContext
        {
            Registry = new WindowsRegistryAccess(), PowerPlans = power, BackupDirectory = AurevixPaths.Backups,
            WindowsBuild = WindowsBuild, Scope = TweakScope.User, System = Hardware
        });
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
