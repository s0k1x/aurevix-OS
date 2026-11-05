using Aurevix.Performance;
using Xunit;

namespace Aurevix.Tests;

public class CoreServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurevix-svc-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private readonly FakeRegistry _reg = new();
    private CoreRequestHandler Handler() => new(TweakEngine.LoadCatalog(),
        sid => new TweakEngine(new TweakContext { Registry = _reg, BackupDirectory = _dir, Scope = TweakScope.Machine }), null);

    [Fact]
    public void RejectsUnknownTweakIds()
    {
        var r = Handler().Handle(new CoreRequest { Op = "apply", Ids = ["evil.run-as-system"] }, "S-1-5-21-1");
        Assert.False(r.Ok);
        Assert.Empty(_reg.Values);
    }

    [Fact]
    public void RejectsArbitraryOperationsAndMaintenance()
    {
        var h = Handler();
        Assert.False(h.Handle(new CoreRequest { Op = "exec" }, "S-1").Ok);
        Assert.False(h.Handle(new CoreRequest { Op = "maintenance", Action = "format c:" }, "S-1").Ok);
        Assert.Contains("formato", h.HandleJson("{not json", "S-1"));
    }

    [Fact]
    public void AppliesOnlyMachinePartAndOnlyOwnerCanRevert()
    {
        var h = Handler();
        var r = h.Handle(new CoreRequest { Op = "apply", Ids = ["privacy.diagtrack.off", "gaming.game-mode.on"], Label = "prueba" }, "S-1-5-21-ALICE");
        Assert.True(r.Ok, r.Error);
        Assert.True(r.RequiresRestart);
        Assert.All(_reg.Values.Keys, k => Assert.StartsWith("LocalMachine", k));

        var other = h.Handle(new CoreRequest { Op = "revert", BackupId = r.BackupId }, "S-1-5-21-BOB");
        Assert.False(other.Ok);
        Assert.NotEmpty(_reg.Values);

        Assert.True(h.Handle(new CoreRequest { Op = "revert", BackupId = r.BackupId }, "S-1-5-21-ALICE").Ok);
        Assert.Empty(_reg.Values);
    }

    [Fact]
    public void RejectsPathTraversalInBackupId() =>
        Assert.False(Handler().Handle(new CoreRequest { Op = "revert", BackupId = "..\\..\\x" }, "S-1").Ok);

    [Fact]
    public void StatusReportsMachineState()
    {
        var h = Handler();
        h.Handle(new CoreRequest { Op = "apply", Ids = ["gpu.hags.on"] }, "S-1");
        var s = h.Handle(new CoreRequest { Op = "status", Ids = ["gpu.hags.on", "gaming.game-mode.on"] }, "S-1");
        Assert.Equal(TweakState.Applied, s.Statuses!["gpu.hags.on"].State);
        Assert.Equal(TweakState.NotApplicable, s.Statuses["gaming.game-mode.on"].State);
    }
}

public class CoreNetworkTests
{
    private sealed class FakeNet : INetworkConfigurator
    {
        public readonly List<string> Calls = [];
        public (bool ok, string message) SetDns(DnsProvider p) { Calls.Add("dns:" + p.Id); return (true, "ok"); }
        public (bool ok, string message) RestoreDns() { Calls.Add("restore"); return (true, "ok"); }
        public (bool ok, string message) AddGameQos(string exe) { Calls.Add("qos+" + exe); return (true, "ok"); }
        public (bool ok, string message) RemoveGameQos(string exe) { Calls.Add("qos-" + exe); return (true, "ok"); }
        public IReadOnlyList<string> ListGameQos() => [];
    }

    private static (CoreRequestHandler h, FakeNet n) Make()
    {
        var n = new FakeNet();
        var dir = Path.Combine(Path.GetTempPath(), "aurevix-net-" + Guid.NewGuid().ToString("N"));
        return (new CoreRequestHandler(TweakEngine.LoadCatalog(), _ => new TweakEngine(new TweakContext { Registry = new FakeRegistry(), BackupDirectory = dir }), null, n), n);
    }

    [Fact]
    public void OnlyAllowlistedDnsProviders()
    {
        var (h, n) = Make();
        Assert.True(h.Handle(new CoreRequest { Op = "dns-set", Action = "cloudflare" }, "S-1").Ok);
        Assert.False(h.Handle(new CoreRequest { Op = "dns-set", Action = "6.6.6.6" }, "S-1").Ok);
        Assert.Equal(["dns:cloudflare"], n.Calls);
    }

    [Theory]
    [InlineData("game.exe", true)]
    [InlineData("Valorant-Win64-Shipping.exe", true)]
    [InlineData("C:\\Windows\\cmd.exe", false)]
    [InlineData("a.exe; rm -rf", false)]
    [InlineData("x'.exe", false)]
    [InlineData("notepad", false)]
    public void QosExeNamesAreValidated(string exe, bool valid)
    {
        var (h, n) = Make();
        Assert.Equal(valid, h.Handle(new CoreRequest { Op = "qos-add", Action = exe }, "S-1").Ok);
        Assert.Equal(valid ? 1 : 0, n.Calls.Count);
    }
}
