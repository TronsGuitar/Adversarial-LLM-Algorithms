using Acdl;
using Xunit;

namespace Acdl.Tests;

public class IntegrationTests
{
    [Fact]
    public void BoilingFrog_EvadesInsensitivePageHinkley()
    {
        var rng = new Random(11);
        var adv = new BoilingFrogAdversary(3.0, 0.0, stepSize: 0.001, noiseScale: 0.05, rng: rng);
        var stream = new AdversarialStream(adversary: adv, pMalicious: 1.0, rng: rng);
        var clf = new OnlineThresholdClassifier(threshold: 1.5, lr: 0.0);
        var ph = new PageHinkleyTest(delta: 0.05, threshold: 50.0);

        bool triggered = false;
        for (int t = 0; t < 2500; t++)
        {
            var (x, y) = stream.Sample();
            int err = clf.Predict(x) != y ? 1 : 0;
            if (ph.Update(err)) { triggered = true; break; }
        }
        Assert.False(triggered);
    }

    [Fact]
    public void SensitiveAdwin_CatchesWhatPageHinkleyMisses()
    {
        var rng = new Random(11);
        var adv = new BoilingFrogAdversary(3.0, 0.0, stepSize: 0.002, noiseScale: 0.05, rng: rng);
        var stream = new AdversarialStream(adversary: adv, pMalicious: 1.0, rng: rng);
        var clf = new OnlineThresholdClassifier(threshold: 1.5, lr: 0.0);
        var adw = new AdwinLite(windowSize: 200, confidence: 0.05);

        bool triggered = false;
        for (int t = 0; t < 4000; t++)
        {
            var (x, y) = stream.Sample();
            int err = clf.Predict(x) != y ? 1 : 0;
            if (adw.Update(err)) { triggered = true; break; }
        }
        Assert.True(triggered);
    }

    [Fact]
    public void DefendedEnsemble_AdvancesAllDetectors()
    {
        // Verifies the ensemble does NOT short-circuit on first trigger,
        // which would leave un-fed detectors in stale state.
        var d1 = new PageHinkleyTest(delta: 0.005, threshold: 5.0);
        var d2 = new PageHinkleyTest(delta: 0.005, threshold: 5.0);
        var ensemble = new DefendedDetector(new IDriftDetector[] { d1, d2 }, votesRequired: 2);

        for (int i = 0; i < 300; i++) ensemble.Update(0.0);
        bool triggered = false;
        for (int i = 0; i < 50; i++)
            if (ensemble.Update(1.0)) { triggered = true; break; }
        // Both detectors saw the same data, so a 2-of-2 vote must trigger.
        Assert.True(triggered);
    }
}
