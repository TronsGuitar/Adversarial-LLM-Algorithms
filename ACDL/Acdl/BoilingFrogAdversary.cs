namespace Acdl;

/// <summary>
/// Slowly migrates the malicious-class mean toward the benign region.
/// step_size below a detector's <c>delta</c> => evasion likely;
/// step_size above it => detection likely.
/// </summary>
public sealed class BoilingFrogAdversary
{
    private readonly double _targetMean;
    private readonly double _stepSize;
    private readonly double _noiseScale;
    private readonly Random _rng;
    private double _currentMean;

    public BoilingFrogAdversary(
        double initialMean = 3.0,
        double targetMean = 0.0,
        double stepSize = 0.001,
        double noiseScale = 0.3,
        Random? rng = null)
    {
        if (stepSize < 0)
            throw new ArgumentOutOfRangeException(nameof(stepSize), "must be non-negative");
        if (noiseScale < 0)
            throw new ArgumentOutOfRangeException(nameof(noiseScale), "must be non-negative");
        _currentMean = initialMean;
        _targetMean = targetMean;
        _stepSize = stepSize;
        _noiseScale = noiseScale;
        _rng = rng ?? new Random();
    }

    public double CurrentMean => _currentMean;

    public double Sample()
    {
        int direction = Math.Sign(_targetMean - _currentMean);
        double proposed = _currentMean + direction * _stepSize;
        // Clip so we never overshoot the target (idempotent once arrived).
        if (direction > 0) _currentMean = Math.Min(proposed, _targetMean);
        else if (direction < 0) _currentMean = Math.Max(proposed, _targetMean);
        return _rng.NextGaussian(_currentMean, _noiseScale);
    }
}
