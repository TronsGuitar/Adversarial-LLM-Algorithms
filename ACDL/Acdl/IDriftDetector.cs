namespace Acdl;

/// <summary>Contract for any online drift detector.</summary>
public interface IDriftDetector
{
    /// <summary>Feed one observation. Returns true iff drift was just detected.</summary>
    bool Update(double value);

    /// <summary>Re-initialize internal state after a detection or manual cycle.</summary>
    void Reset();
}
