using Acdl;
using Xunit;

namespace Acdl.Tests;

public class PageHinkleyTests
{
    [Fact]
    public void StableStream_DoesNotTrigger()
    {
        var ph = new PageHinkleyTest(delta: 0.01, threshold: 50.0);
        var rng = new Random(42);
        bool triggered = false;
        for (int i = 0; i < 1000; i++)
        {
            if (ph.Update(rng.NextDouble() * 0.1)) { triggered = true; break; }
        }
        Assert.False(triggered);
    }

    [Fact]
    public void AbruptShift_Triggers()
    {
        var ph = new PageHinkleyTest(delta: 0.005, threshold: 5.0);
        for (int i = 0; i < 300; i++) ph.Update(0.0);
        bool triggered = false;
        for (int i = 0; i < 50; i++)
            if (ph.Update(1.0)) { triggered = true; break; }
        Assert.True(triggered);
    }

    [Fact]
    public void NonPositiveThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageHinkleyTest(threshold: 0));
    }

    [Fact]
    public void NegativeDelta_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageHinkleyTest(delta: -0.1));
    }

    [Fact]
    public void Reset_ClearsAccumulators()
    {
        var ph = new PageHinkleyTest(delta: 0.005, threshold: 5.0);
        for (int i = 0; i < 300; i++) ph.Update(0.0);
        for (int i = 0; i < 50; i++) ph.Update(1.0); // induce trigger; auto-resets
        // After auto-reset, a stable stream should not immediately re-trigger.
        bool triggered = false;
        for (int i = 0; i < 100; i++)
            if (ph.Update(0.0)) { triggered = true; break; }
        Assert.False(triggered);
    }
}
