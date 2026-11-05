using Aurevix.Core.Benchmark;
using Xunit;

namespace Aurevix.Tests;

public class BenchmarkTests
{
    [Fact]
    public void SteadySixtyFps()
    {
        var r = BenchmarkStats.Compute(Enumerable.Repeat(1000.0 / 60, 600).ToList());
        Assert.InRange(r.AvgFps, 59.9, 60.1);
        Assert.InRange(r.Low1Fps, 59.9, 60.1);
        Assert.InRange(r.Seconds, 9.99, 10.01);
    }

    [Fact]
    public void StuttersLowerThe1PercentLow()
    {
        var ft = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(50.0, 10)).ToList(); // 1 % de tirones a 20 FPS
        var r = BenchmarkStats.Compute(ft);
        Assert.InRange(r.AvgFps, 95, 97);
        Assert.InRange(r.Low1Fps, 19.9, 20.1);
        Assert.Equal(50, r.FrameTimeP99Ms);
    }

    [Fact]
    public void ParsesPresentMonCsvIgnoringNaAndOtherProcesses()
    {
        var csv = new[]
        {
            "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,MsBetweenPresents,MsBetweenDisplayChange",
            "game.exe,1,0x1,DXGI,0,0,16.6,16.6",
            "game.exe,1,0x1,DXGI,0,0,NA,16.6",
            "dwm.exe,2,0x2,Other,0,0,8.3,8.3",
            "game.exe,1,0x1,DXGI,0,0,17.0,16.6",
        };
        Assert.Equal([16.6, 17.0], BenchmarkStats.ParsePresentMonCsv(csv, "game.exe"));
    }

    [Fact]
    public void TooFewFramesIsAnError() => Assert.Throws<InvalidOperationException>(() => BenchmarkStats.Compute([16.0, 16.0]));

    [Fact]
    public void DeltaPercent() => Assert.Equal(10, BenchmarkStats.Delta(100, 110), 3);
}
