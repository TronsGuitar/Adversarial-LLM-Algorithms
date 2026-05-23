using Acdl;
using Xunit;

namespace Acdl.Tests;

public class AdwinLiteTests
{
    [Fact]
    public void OutOfRangeValue_Throws()
    {
        var adw = new AdwinLite(windowSize: 20, confidence: 0.05);
        Assert.Throws<ArgumentOutOfRangeException>(() => adw.Update(1.5));
    }

    [Fact]
    public void DetectsMeanShift()
    {
        var adw = new AdwinLite(windowSize: 200, confidence: 0.05);
        for (int i = 0; i < 150; i++) adw.Update(0.05);
        bool triggered = false;
        for (int i = 0; i < 400; i++)
            if (adw.Update(0.95)) { triggered = true; break; }
        Assert.True(triggered);
    }

    [Fact]
    public void OddWindowSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdwinLite(windowSize: 11));
    }

    [Fact]
    public void TinyWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdwinLite(windowSize: 8));
    }

    [Fact]
    public void StableStream_DoesNotTrigger()
    {
        var adw = new AdwinLite(windowSize: 100, confidence: 0.001);
        var rng = new Random(99);
        bool triggered = false;
        for (int i = 0; i < 2000; i++)
        {
            double v = rng.NextDouble() < 0.1 ? 1.0 : 0.0;
            if (adw.Update(v)) { triggered = true; break; }
        }
        Assert.False(triggered);
    }
}
