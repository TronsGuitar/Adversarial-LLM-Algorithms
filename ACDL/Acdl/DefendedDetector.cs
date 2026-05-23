namespace Acdl;

/// <summary>
/// k-of-n voting ensemble over heterogeneous drift detectors. A single
/// detector exposes a single attack surface (its delta/threshold). An attacker
/// who tunes against one detector is unlikely to simultaneously evade
/// detectors with different statistical assumptions.
/// </summary>
public sealed class DefendedDetector : IDriftDetector
{
    private readonly IReadOnlyList<IDriftDetector> _detectors;
    private readonly int _votesRequired;

    public DefendedDetector(IReadOnlyList<IDriftDetector> detectors, int votesRequired = 1)
    {
        if (detectors is null || detectors.Count == 0)
            throw new ArgumentException("at least one detector is required", nameof(detectors));
        if (votesRequired < 1 || votesRequired > detectors.Count)
            throw new ArgumentOutOfRangeException(nameof(votesRequired),
                "must be in [1, detectors.Count]");
        _detectors = detectors;
        _votesRequired = votesRequired;
    }

    public bool Update(double value)
    {
        // IMPORTANT: call every detector so each one's state advances; do NOT
        // short-circuit on first trigger, that would corrupt detector state.
        int triggers = 0;
        foreach (var d in _detectors)
        {
            if (d.Update(value)) triggers++;
        }
        return triggers >= _votesRequired;
    }

    public void Reset()
    {
        foreach (var d in _detectors) d.Reset();
    }
}
