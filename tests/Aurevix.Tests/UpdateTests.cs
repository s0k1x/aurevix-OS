using System.Net;
using System.Text;
using System.Text.Json;
using Aurevix.Core;
using Aurevix.Update;
using Xunit;

namespace Aurevix.Tests;

public class UpdateTests
{
    private const string KeyId = "test-key";
    private static readonly (string Priv, string Pub) Keys = ManifestSigner.GenerateKeyPair();
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static UpdateClient Client(HttpClient? http = null) => new(new UpdateClientOptions
    {
        ManifestBaseUrl = new Uri("https://updates.example.com/manifests"),
        AllowedPackageHosts = ["updates.example.com"],
        TrustedKeys = new Dictionary<string, string> { [KeyId] = Keys.Pub }
    }, http ?? new HttpClient());

    private static UpdateManifest Manifest(string version = "0.2.0", long seq = 5, string url = "https://updates.example.com/p/AurevixSetup.exe",
        string? min = null, int rollout = 100, DateTimeOffset? expires = null, string sha = "", long size = 100) => new()
    {
        Channel = "stable", Sequence = seq, PublishedAt = Now.AddHours(-1), ExpiresAt = expires ?? Now.AddDays(30),
        Latest = new ReleaseInfo
        {
            Version = version, MinimumVersion = min, RolloutPercent = rollout, Title = "t", Notes = "n",
            Packages = [new PackageInfo { Id = "aurevix-app", Kind = PackageKind.AppInstaller, Url = url, Sha256 = sha.Length > 0 ? sha : new string('a', 64), Size = size }]
        }
    };

    private static (byte[] bytes, string sig) Signed(UpdateManifest m, string? privateKey = null)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m, JsonDefaults.Options));
        return (bytes, ManifestSigner.SerializeSignature(ManifestSigner.Sign(bytes, privateKey ?? Keys.Priv, KeyId)));
    }

    private static UpdateCheckResult Eval(UpdateManifest m, string current = "0.1.0", long lastSeq = 0, string? privateKey = null)
    {
        var (b, s) = Signed(m, privateKey);
        return Client().Evaluate(b, s, SemVersion.Parse(current), "stable", "install-1", lastSeq, Now);
    }

    [Fact] public void ValidUpdateIsAvailable() => Assert.Equal(UpdateStatus.Available, Eval(Manifest()).Status);

    [Fact] public void SameVersionIsUpToDate() => Assert.Equal(UpdateStatus.UpToDate, Eval(Manifest("0.1.0")).Status);

    [Fact] public void OlderVersionIsNeverOffered() => Assert.Equal(UpdateStatus.UpToDate, Eval(Manifest("0.0.9")).Status);

    [Fact]
    public void TamperedManifestIsRejected()
    {
        var (b, s) = Signed(Manifest());
        b[10] ^= 0x01;
        var r = Client().Evaluate(b, s, SemVersion.Parse("0.1.0"), "stable", "i", 0, Now);
        Assert.Equal(UpdateStatus.Error, r.Status);
        Assert.Contains("firma", r.Message);
    }

    [Fact]
    public void ForeignKeyIsRejected()
    {
        var (otherPriv, _) = ManifestSigner.GenerateKeyPair();
        Assert.Equal(UpdateStatus.Error, Eval(Manifest(), privateKey: otherPriv).Status);
    }

    [Fact] public void ExpiredManifestIsRejected() => Assert.Equal(UpdateStatus.Error, Eval(Manifest(expires: Now.AddMinutes(-1))).Status);

    [Fact] public void ReplayedOlderSequenceIsRejected() => Assert.Equal(UpdateStatus.Error, Eval(Manifest(seq: 3), lastSeq: 4).Status);

    [Fact] public void NonHttpsPackageIsRejected() => Assert.Equal(UpdateStatus.Error, Eval(Manifest(url: "http://updates.example.com/a.exe")).Status);

    [Fact] public void UnknownHostIsRejected() => Assert.Equal(UpdateStatus.Error, Eval(Manifest(url: "https://evil.example.net/a.exe")).Status);

    [Fact] public void TooOldClientMustUpdateManually() => Assert.Equal(UpdateStatus.RequiresManualUpdate, Eval(Manifest(min: "0.1.5")).Status);

    [Fact] public void ZeroRolloutIsNotEligible() => Assert.Equal(UpdateStatus.NotEligibleYet, Eval(Manifest(rollout: 0)).Status);

    [Fact]
    public void RolloutIsDeterministicAndRoughlyProportional()
    {
        var inRollout = Enumerable.Range(0, 2000).Count(i => UpdateClient.IsInRollout($"dev-{i}", "1.0.0", 25));
        Assert.InRange(inRollout, 400, 600);
        Assert.Equal(UpdateClient.IsInRollout("x", "1.0.0", 50), UpdateClient.IsInRollout("x", "1.0.0", 50));
    }

    [Fact]
    public async Task DownloadVerifiesHash()
    {
        var payload = Encoding.UTF8.GetBytes("instalador de prueba");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();
        var dir = Path.Combine(Path.GetTempPath(), "aurevix-test-" + Guid.NewGuid().ToString("N"));
        var client = Client(new HttpClient(new StaticHandler(payload)));

        var good = Manifest(sha: sha, size: payload.Length).Latest!.Packages[0];
        var path = await client.DownloadAsync(good, dir);
        Assert.Equal(payload, File.ReadAllBytes(path));

        var bad = Manifest(sha: new string('b', 64), size: payload.Length).Latest!.Packages[0];
        bad.Url = "https://updates.example.com/p/other.exe";
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(bad, dir));
        Assert.False(File.Exists(Path.Combine(dir, "other.exe")));
        Assert.False(File.Exists(Path.Combine(dir, "other.exe.partial")));
    }

    [Fact]
    public void EmbeddedReleaseKeyIsPresent() => Assert.Contains("aurevix-2026-01", ManifestSigner.LoadEmbeddedTrustedKeys().Keys);

    private sealed class StaticHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
