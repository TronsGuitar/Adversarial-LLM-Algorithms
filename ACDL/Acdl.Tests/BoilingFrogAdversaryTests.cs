using Acdl;
using Xunit;

namespace Acdl.Tests;

public class BoilingFrogAdversaryTests
{
    [Fact]
    public void ProgressesTowardTarget()
    {
        var adv = new BoilingFrogAdversary(
            initialMean: 3.0, targetMean: 0.0,
            stepSize: 0.1, noiseScale: 0.0, rng: new Random(0));
        // noise=0, so sample == currentMean each step.
        double first = adv.Sample();
        Assert.Equal(2.9, first, 9);
        for (int i = 0; i < 100; i++) adv.Sample();
        Assert.Equal(0.0, adv.CurrentMean, 9);
    }

    [Fact]
    public void IdempotentAtTarget()
    {
        var adv = new BoilingFrogAdversary(
            initialMean: 0.0, targetMean: 0.0,
            stepSize: 0.5, noiseScale: 0.0, rng: new Random(1));
        for (int i = 0; i < 10; i++) Assert.Equal(0.0, adv.Sample(), 9);
    }

    [Fact]
    public void NegativeStepSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoilingFrogAdversary(stepSize: -0.1));
    }

    [Fact]
    public void NegativeNoiseScale_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoilingFrogAdversary(noiseScale: -0.1));
    }
}
