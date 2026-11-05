using Aurevix.Core;
using Aurevix.Core.Diagnostics;
using Xunit;

namespace Aurevix.Tests;

public class SemVersionTests
{
    [Theory]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.2.0", "0.1.10")]
    [InlineData("1.0.0", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta", "1.0.0-alpha")]
    public void Greater(string a, string b) => Assert.True(SemVersion.Parse(a) > SemVersion.Parse(b));

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("0.1.0+build.5")]
    public void ParsesVariants(string s) => Assert.Equal(new SemVersion(0, 1, 0), SemVersion.Parse(s));

    [Theory]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.x")]
    [InlineData("1.0.0-")]
    public void RejectsInvalid(string s) => Assert.False(SemVersion.TryParse(s, out _));
}

public class DiagnosticAnalyzerTests
{
    [Fact]
    public void NeverInventsTemperatures()
    {
        var r = DiagnosticAnalyzer.Analyze(new SystemSnapshot());
        var t = Assert.Single(r.Findings, f => f.Area == "Temperaturas");
        Assert.Equal(FindingLevel.Unavailable, t.Level);
    }

    [Fact]
    public void LowDiskSpaceRequiresAttention()
    {
        var gb = 1024L * 1024 * 1024;
        var r = DiagnosticAnalyzer.Analyze(new SystemSnapshot { Volumes = [new VolumeInfo("C:", null, "NTFS", 500 * gb, 5 * gb)] });
        Assert.Equal(FindingLevel.Attention, r.Findings.Single(f => f.Area == "Almacenamiento").Level);
        Assert.Equal(FindingLevel.Attention, r.Overall);
    }

    [Fact]
    public void UnknownDiskHealthIsUnavailableNotFailure()
    {
        var r = DiagnosticAnalyzer.Analyze(new SystemSnapshot { Disks = [new DiskInfo("X", "SSD", 1, null, "NVMe")] });
        Assert.Equal(FindingLevel.Unavailable, r.Findings.Single(f => f.Title.StartsWith("Disco")).Level);
    }

    [Fact]
    public void OldGpuDriverIsReview()
    {
        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var r = DiagnosticAnalyzer.Analyze(new SystemSnapshot { Gpus = [new GpuInfo("Radeon 780M", "31.0", now.AddYears(-3), null)] }, now);
        Assert.Equal(FindingLevel.Review, r.Findings.Single(f => f.Area == "Gráficos").Level);
    }
}
