namespace Acdl;

/// <summary>
/// Predicts y=1 when x &gt; Threshold. Threshold can be frozen (lr = 0) or
/// adaptive. A frozen threshold makes boiling-frog attacks observable as
/// rising error. An adaptive threshold can itself be 'pushed' into a useless
/// state by the adversary (the canonical Dalvi attack).
/// </summary>
public sealed class OnlineThresholdClassifier
{
    private readonly double _lr;
    private double _meanBenign;
    private double _meanMalicious;

    public double Threshold { get; private set; }

    public OnlineThresholdClassifier(double threshold = 1.5, double lr = 0.0)
    {
        if (lr < 0 || lr > 1)
            throw new ArgumentOutOfRangeException(nameof(lr), "must be in [0, 1]");
        Threshold = threshold;
        _lr = lr;
        _meanBenign = 0.0;
        _meanMalicious = 3.0;
    }

    public int Predict(double x) => x > Threshold ? 1 : 0;

    public void Update(double x, int y)
    {
        if (_lr <= 0) return; // frozen
        if (y == 0) _meanBenign = (1 - _lr) * _meanBenign + _lr * x;
        else _meanMalicious = (1 - _lr) * _meanMalicious + _lr * x;
        Threshold = 0.5 * (_meanBenign + _meanMalicious);
    }
}
