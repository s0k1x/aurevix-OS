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
}

public class FakePower(params Guid[] plans) : IPowerPlanAccess
{
    public Guid Active = PowerPlans.Balanced;
    public IReadOnlyList<(Guid, string)> ListPlans() => plans.Select(p => (p, p.ToString())).ToList();
    public Guid? GetActivePlan() => Active;
    public void SetActivePlan(Guid id) => Active = id;
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
            Assert.NotEmpty(t.Registry);
            // Sin privilegios en esta fase: solo HKCU.
            if (!t.RequiresAdmin) Assert.All(t.Registry, a => Assert.Equal(RegistryHive.CurrentUser, a.Hive));
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
        var r = engine.Apply([Catalog[0]], "w10");
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
