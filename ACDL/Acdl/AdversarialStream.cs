namespace Acdl;

/// <summary>
/// Emits (feature, label) pairs. Benign class is stationary; malicious class
/// is either stationary or driven by an attached adversary.
/// </summary>
public sealed class AdversarialStream
{
    private readonly double _benignMean;
    private readonly double _maliciousMean;
    private readonly double _noiseScale;
    private readonly BoilingFrogAdversary? _adversary;
    private readonly double _pMalicious;
    private readonly Random _rng;

    public AdversarialStream(
        double benignMean = 0.0,
        double maliciousMean = 3.0,
        double noiseScale = 0.3,
        BoilingFrogAdversary? adversary = null,
        double pMalicious = 0.5,
        Random? rng = null)
    {
        if (pMalicious < 0 || pMalicious > 1)
            throw new ArgumentOutOfRangeException(nameof(pMalicious), "must be in [0, 1]");
        _benignMean = benignMean;
        _maliciousMean = maliciousMean;
        _noiseScale = noiseScale;
        _adversary = adversary;
        _pMalicious = pMalicious;
        _rng = rng ?? new Random();
    }

    public (double Feature, int Label) Sample()
    {
        if (_rng.NextDouble() < _pMalicious)
        {
            double x = _adversary is not null
                ? _adversary.Sample()
                : _rng.NextGaussian(_maliciousMean, _noiseScale);
            return (x, 1);
        }
        return (_rng.NextGaussian(_benignMean, _noiseScale), 0);
    }

    public IEnumerable<(double Feature, int Label)> Stream(int n)
    {
        for (int i = 0; i < n; i++) yield return Sample();
    }
}
