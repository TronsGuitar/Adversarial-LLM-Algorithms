namespace Acdl;

/// <summary>
/// Classical Page-Hinkley change-point test. Tracks the cumulative deviation
/// of observations from their running mean, minus a tolerance <c>delta</c>.
/// Triggers when the cumulative sum exceeds its running minimum by more than
/// <c>threshold</c>.
///
/// Vulnerability by design: a step smaller than <c>delta</c> is invisible.
/// This is the gap the boiling-frog adversary exploits.
/// </summary>
public sealed class PageHinkleyTest : IDriftDetector
{
    private readonly double _delta;
    private readonly double _threshold;
    private long _n;
    private double _mean;
    private double _cumulative;
    private double _minCumulative;

    public PageHinkleyTest(double delta = 0.005, double threshold = 50.0)
    {
        if (threshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(threshold), "must be positive");
        if (delta < 0)
            throw new ArgumentOutOfRangeException(nameof(delta), "must be non-negative");
        _delta = delta;
        _threshold = threshold;
        Reset();
    }

    public void Reset()
    {
        _n = 0;
        _mean = 0.0;
        _cumulative = 0.0;
        _minCumulative = 0.0;
    }

    public bool Update(double value)
    {
        _n++;
        _mean += (value - _mean) / _n;
        _cumulative += value - _mean - _delta;
        _minCumulative = Math.Min(_minCumulative, _cumulative);

        if ((_cumulative - _minCumulative) > _threshold)
        {
            Reset();
            return true;
        }
        return false;
    }
}
