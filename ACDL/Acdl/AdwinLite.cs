namespace Acdl;

/// <summary>
/// Pedagogical ADWIN variant. Compares means of the two halves of a fixed
/// window using a Hoeffding bound. Requires values in [0, 1].
/// Backed by a circular buffer for zero per-call allocations after warmup.
///
/// Real ADWIN supports arbitrary cut points via exponential-histogram buckets
/// in O(log n); this version fixes the cut at the midpoint to keep the math
/// transparent.
/// </summary>
public sealed class AdwinLite : IDriftDetector
{
    private readonly int _windowSize;
    private readonly double _confidence;
    private readonly double[] _buffer;
    private int _count;
    private int _head;

    public AdwinLite(int windowSize = 200, double confidence = 0.002)
    {
        if (windowSize < 10 || windowSize % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "must be even and >= 10");
        if (confidence <= 0 || confidence >= 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "must be in (0, 1)");
        _windowSize = windowSize;
        _confidence = confidence;
        _buffer = new double[windowSize];
        Reset();
    }

    public void Reset()
    {
        _count = 0;
        _head = 0;
        Array.Clear(_buffer);
    }

    public bool Update(double value)
    {
        if (value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(nameof(value), "AdwinLite requires values in [0, 1]");

        _buffer[_head] = value;
        _head = (_head + 1) % _windowSize;
        if (_count < _windowSize) { _count++; return false; }

        // When full, _head points to the oldest element (next slot to overwrite).
        // Walk i = 0..N-1 chronologically, oldest first.
        int half = _windowSize / 2;
        double oldSum = 0.0, newSum = 0.0;
        for (int i = 0; i < _windowSize; i++)
        {
            double v = _buffer[(_head + i) % _windowSize];
            if (i < half) oldSum += v;
            else newSum += v;
        }
        double meanOld = oldSum / half;
        double meanNew = newSum / half;

        // Harmonic mean of equal sub-window sizes.
        double m = 1.0 / (1.0 / half + 1.0 / half);
        double epsilon = Math.Sqrt((1.0 / (2.0 * m)) * Math.Log(2.0 / _confidence));

        if (Math.Abs(meanOld - meanNew) > epsilon)
        {
            Reset();
            return true;
        }
        return false;
    }
}
