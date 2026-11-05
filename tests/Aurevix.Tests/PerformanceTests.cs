using Aurevix.Performance;
using Xunit;

namespace Aurevix.Tests;

public class FakeRegistry : IRegistryAccess
{
    public readonly Dictionary<string, string> Values = new();
    public string? FailOnName { get; set; }
    private static string K(RegistryHive h, string k, string n) => $"{h}\\{k}\\{n}";
    public string? GetValue(RegistryHive h, string k, string n) => Values.GetValueOrDefault(K(h, k, n));
    public void SetValue(RegistryHive h, string k, string n, RegistryValueKind kind, string v)
    {
        if (n == FailOnName) throw new UnauthorizedAccessException("simulado");
        Values[K(h, k, n)] = v;
    }
    public void DeleteValue(RegistryHive h, string k, string n) => Values.Remove(K(h, k, n));
    public readonly Dictionary<string, List<string>> SubKeys = new();
    public IReadOnlyList<string> ListSubKeys(RegistryHive h, string k) => SubKeys.GetValueOrDefault($"{h}\\{k}") ?? [];
}

public class FakePower(params Guid[] plans) : IPowerPlanAccess, IPowerSettingAccess
{
    public Guid Active = PowerPlans.Balanced;
    public readonly List<Guid> Plans = plans.ToList();
    public readonly Dictionary<(Guid, Guid), (uint, uint)> Settings = new();
    public IReadOnlyList<(Guid, string)> ListPlans() => Plans.Select(p => (p, p.ToString())).ToList();
    public Guid? GetActivePlan() => Active;
    public void SetActivePlan(Guid id) => Active = id;
    public Guid EnsurePlan(Guid template)
    {
        if (Plans.Contains(template)) return template;
        var d = PowerPlans.DerivedFrom(template);
        if (!Plans.Contains(d)) Plans.Add(d);
        return d;
    }
    public (uint Ac, uint Dc)? Get(Guid s, Guid k) => Settings.TryGetValue((s, k), out var v) ? v : (0u, 0u);
    public void Set(Guid s, Guid k, uint ac, uint? dc) => Settings[(s, k)] = (ac, dc ?? Get(s, k)!.Value.Dc);
}

public class FakeTasks : ITaskAccess
{
    public readonly Dictionary<string, bool> Tasks = new();
    public bool? IsEnabled(string path) => Tasks.TryGetValue(path, out var v) ? v : null;
    public void SetEnabled(string path, bool enabled) { if (Tasks.ContainsKey(path)) Tasks[path] = enabled; }
}

public class PerformanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurevix-perf-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static readonly IReadOnlyList<TweakDefinition> Catalog = TweakEngine.LoadCatalog();
    private static readonly IReadOnlyList<ProfileDefinition> Profiles = TweakEngine.LoadProfiles();

    [Fact]
    public void CatalogIsWellFormed()
    {
        Assert.NotEmpty(Catalog);
        Assert.Equal(Catalog.Count, Catalog.Select(t => t.Id).Distinct().Count());
        foreach (var t in Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            Assert.False(string.IsNullOrWhiteSpace(t.ExpectedEffect));
            Assert.False(string.IsNullOrWhiteSpace(t.PossibleImpact));
            Assert.True(t.Registry.Count + t.Tasks.Count + t.Power.Count > 0 || t.PowerPlan is not null, t.Id);
            // Lo que no es de usuario lo hace el servicio.
            if (!t.RequiresAdmin) Assert.All(t.Registry, a => Assert.Equal(RegistryHive.CurrentUser, a.Hive));
            // Los ajustes que reducen la seguridad nunca forman parte de un nivel automático.
            if (t.Category == TweakCategory.Security) { Assert.True(t.OptIn, t.Id); Assert.Empty(t.Levels); Assert.False(string.IsNullOrWhiteSpace(t.Warning)); }
            // Ningún ajuste toca Defender, Firewall, Windows Update, SmartScreen o UAC.
            foreach (var a in t.Registry)
                Assert.DoesNotMatch("(?i)Windows Defender|WinDefend|MpsSvc|wuauserv|SmartScreen|EnableLUA|ConsentPromptBehavior", a.Key + "\\" + a.Name);
        }
    }

    [Fact]
    public void ProfilesReferenceExistingTweaks()
    {
        Assert.Equal(["balanced", "gaming", "productivity", "efficiency"], Profiles.Select(p => p.Id));
        var ids = Catalog.Select(t => t.Id).ToHashSet();
        Assert.All(Profiles.SelectMany(p => p.Tweaks), id => Assert.Contains(id, ids));
    }

    [Fact]
    public void ApplyThenRevertRestoresExactPreviousState()
    {
        var reg = new FakeRegistry();
        reg.Values[@"CurrentUser\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt"] = "1";
        var engine = new TweakEngine(reg, null, _dir, 22631, false);
        var tweaks = Catalog.Where(t => t.Id is "interface.file-extensions.show" or "gaming.game-mode.on").ToList();
        var before = new Dictionary<string, string>(reg.Values);

        var r = engine.Apply(tweaks, "prueba");
        Assert.True(r.Success);
        Assert.All(tweaks, t => Assert.Equal(TweakState.Applied, engine.GetStatus(t).State));

        engine.Revert(r.Backup!);
        Assert.Equal(before, reg.Values); // valor previo restaurado y valor inexistente eliminado
    }

    [Fact]
    public void FailureRollsBackAutomatically()
    {
        var reg = new FakeRegistry { FailOnName = "AppCaptureEnabled" };
        var engine = new TweakEngine(reg, null, _dir, 22631, false);
        var tweaks = Catalog.Where(t => t.Id is "gaming.game-mode.on" or "gaming.background-recording.off").ToList();
        var r = engine.Apply(tweaks, "fallo");
        Assert.False(r.Success);
        Assert.Empty(reg.Values);
    }

    [Fact]
    public void IncompatibleBuildIsRefused()
    {
        var engine = new TweakEngine(new FakeRegistry(), null, _dir, 19045, false); // Windows 10
        var r = engine.Apply([Catalog.Single(t => t.Id == "gpu.windowed-optimizations.on")], "w10");
        Assert.False(r.Success);
    }

    [Fact]
    public void GamingUsesHighPerformanceOnlyIfAvailable()
    {
        var reg = new FakeRegistry();
        var withHp = new FakePower(PowerPlans.Balanced, PowerPlans.HighPerformance);
        new ProfileManager(new TweakEngine(reg, withHp, _dir, 22631, false), Catalog, Profiles, withHp).Switch("gaming");
        Assert.Equal(PowerPlans.HighPerformance, withHp.Active);

        var onlyBalanced = new FakePower(PowerPlans.Balanced) { Active = PowerPlans.Balanced };
        var dir2 = _dir + "-2";
        new ProfileManager(new TweakEngine(new FakeRegistry(), onlyBalanced, dir2, 22631, false), Catalog, Profiles, onlyBalanced).Switch("gaming");
        Assert.Equal(PowerPlans.Balanced, onlyBalanced.Active);
    }

    [Fact]
    public void SwitchingProfileUndoesPreviousOne()
    {
        var reg = new FakeRegistry();
        var power = new FakePower(PowerPlans.Balanced, PowerPlans.PowerSaver);
        var pm = new ProfileManager(new TweakEngine(reg, power, _dir, 22631, false), Catalog, Profiles, power);

        pm.Switch("efficiency");
        Assert.Equal("0", reg.GetValue(RegistryHive.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate"));
        Assert.Equal(PowerPlans.PowerSaver, power.Active);

        pm.Switch("productivity");
        Assert.Null(reg.GetValue(RegistryHive.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate"));
        Assert.Equal(PowerPlans.Balanced, power.Active);
    }
}


public class EngineV2Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurevix-v2-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private static readonly IReadOnlyList<TweakDefinition> Catalog = TweakEngine.LoadCatalog();
    private static TweakDefinition T(string id) => Catalog.Single(t => t.Id == id);

    private TweakEngine Engine(TweakScope scope, FakeRegistry reg, FakePower? power = null, FakeTasks? tasks = null, SystemProfile? sys = null) =>
        new(new TweakContext { Registry = reg, PowerPlans = power, PowerSettings = power, Tasks = tasks, BackupDirectory = Path.Combine(_dir, scope.ToString()), Scope = scope, System = sys ?? SystemProfile.Unknown });

    [Fact]
    public void UserScopeNeverTouchesMachineValues()
    {
        var reg = new FakeRegistry();
        var r = Engine(TweakScope.User, reg).Apply([T("privacy.diagtrack.off"), T("gaming.game-mode.on")], "x");
        Assert.True(r.Success);
        Assert.All(reg.Values.Keys, k => Assert.StartsWith("CurrentUser", k));
    }

    [Fact]
    public void MachineScopeAppliesRegistryTasksAndPowerAndReverts()
    {
        var reg = new FakeRegistry();
        var power = new FakePower(PowerPlans.Balanced);
        var tasks = new FakeTasks();
        tasks.Tasks[@"\Microsoft\Windows\Autochk\Proxy"] = true; // las demás no existen: se omiten
        var e = Engine(TweakScope.Machine, reg, power, tasks);
        var list = new[] { T("privacy.diagtrack.off"), T("privacy.telemetry-tasks.off"), T("power.usb-selective-suspend.off"), T("gaming.game-mode.on") };
        var r = e.Apply(list, "m");
        Assert.True(r.Success, string.Join(" ", r.Messages));
        Assert.Equal("4", reg.GetValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"));
        Assert.False(tasks.Tasks[@"\Microsoft\Windows\Autochk\Proxy"]);
        Assert.Equal(TweakState.Applied, e.GetStatus(T("power.usb-selective-suspend.off")).State);
        Assert.DoesNotContain(reg.Values.Keys, k => k.StartsWith("CurrentUser"));
        Assert.True(r.Backup!.RequiresRestart);

        e.Revert(r.Backup!);
        Assert.Empty(reg.Values);
        Assert.True(tasks.Tasks[@"\Microsoft\Windows\Autochk\Proxy"]);
    }

    [Fact]
    public void UltimatePlanIsCreatedOnceAndActivated()
    {
        var power = new FakePower(PowerPlans.Balanced);
        var e = Engine(TweakScope.User, new FakeRegistry(), power, sys: new SystemProfile(32, true, false, "AMD"));
        Assert.True(e.Apply([T("power.plan.ultimate")], "u").Success);
        Assert.True(e.Apply([T("power.plan.ultimate")], "u2").Success);
        Assert.Single(power.Plans, p => p == PowerPlans.DerivedFrom(PowerPlans.Ultimate));
        Assert.Equal(PowerPlans.DerivedFrom(PowerPlans.Ultimate), power.Active);
        Assert.Equal(TweakState.Applied, e.GetStatus(T("power.plan.ultimate")).State);
    }

    [Fact]
    public void HardwareRequirementsAreRespected()
    {
        var laptop = Engine(TweakScope.All, new FakeRegistry(), sys: new SystemProfile(8, false, true, null));
        Assert.NotNull(laptop.CompatibilityProblem(T("power.plan.ultimate")));
        Assert.NotNull(laptop.CompatibilityProblem(T("system.sysmain.off")));
        Assert.Null(laptop.CompatibilityProblem(T("gaming.game-mode.on")));
    }

    [Fact]
    public void LevelsAreCumulativeAndNeverIncludeOptIn()
    {
        var rec = TweakEngine.ForLevel(Catalog, OptimizationLevel.Recommended);
        var perf = TweakEngine.ForLevel(Catalog, OptimizationLevel.Performance);
        var max = TweakEngine.ForLevel(Catalog, OptimizationLevel.Maximum);
        Assert.NotEmpty(rec);
        Assert.True(rec.Count < perf.Count && perf.Count < max.Count);
        Assert.DoesNotContain(max, t => t.OptIn);
        Assert.DoesNotContain(max, t => t.Id == "security.memory-integrity.off");
        // Recomendado es de riesgo bajo.
        Assert.All(rec, t => Assert.Equal(RiskLevel.Low, t.Risk));
    }

    [Fact]
    public void DerivedPlanGuidIsStable() =>
        Assert.Equal(PowerPlans.DerivedFrom(PowerPlans.Ultimate), PowerPlans.DerivedFrom(new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61")));
}

public class WildcardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurevix-wc-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void NagleAppliesToEveryInterfaceAndReverts()
    {
        var reg = new FakeRegistry();
        const string ifaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
        reg.SubKeys[$@"LocalMachine\{ifaces}"] = ["{AAA}", "{BBB}"];
        var e = new TweakEngine(new TweakContext { Registry = reg, BackupDirectory = _dir, Scope = TweakScope.Machine });
        var t = TweakEngine.LoadCatalog().Single(x => x.Id == "network.nagle.off");
        var r = e.Apply([t], "nagle");
        Assert.True(r.Success, string.Join(" ", r.Messages));
        Assert.Equal("1", reg.GetValue(RegistryHive.LocalMachine, $@"{ifaces}\{{AAA}}", "TCPNoDelay"));
        Assert.Equal("1", reg.GetValue(RegistryHive.LocalMachine, $@"{ifaces}\{{BBB}}", "TcpAckFrequency"));
        Assert.Equal(4, reg.Values.Count);
        Assert.Equal(TweakState.Applied, e.GetStatus(t).State);
        e.Revert(r.Backup!);
        Assert.Empty(reg.Values);
    }
}
