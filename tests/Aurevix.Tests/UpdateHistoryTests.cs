using Aurevix.Update;
using Xunit;

namespace Aurevix.Tests;

public class UpdateHistoryTests
{
    [Fact]
    public void RollbackCandidateAcceptsLaunchedAndMarkResultConfirms()
    {
        var f = Path.GetTempFileName();
        try
        {
            var h = new UpdateHistory();
            h.Add(new(DateTime.UtcNow, "1.0.0", "1.1.0", "stable", "launched", f));
            Assert.NotNull(h.LastRollbackCandidate());
            Assert.True(h.MarkResult("1.1.0", "installed"));
            Assert.Equal("installed", h.Entries[0].Result);
            Assert.False(h.MarkResult("9.9.9", "installed"));
        }
        finally { File.Delete(f); }
    }
}
