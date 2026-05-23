Here's the full paste-ready dump. Structure first, then each file. Filenames are paths relative to the project root.

```
ACDL/
├── Acdl.sln
├── README.md
├── Acdl/
│   ├── Acdl.csproj
│   ├── IDriftDetector.cs
│   ├── PageHinkleyTest.cs
│   ├── AdwinLite.cs
│   ├── RandomExtensions.cs
│   ├── BoilingFrogAdversary.cs
│   ├── AdversarialStream.cs
│   ├── OnlineThresholdClassifier.cs
│   ├── DefendedDetector.cs
│   └── Program.cs
└── Acdl.Tests/
    ├── Acdl.Tests.csproj
    ├── PageHinkleyTests.cs
    ├── AdwinLiteTests.cs
    ├── BoilingFrogAdversaryTests.cs
    └── IntegrationTests.cs
```

---

### `Acdl.sln`

```text
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Acdl", "Acdl\Acdl.csproj", "{11111111-1111-1111-1111-111111111111}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Acdl.Tests", "Acdl.Tests\Acdl.Tests.csproj", "{22222222-2222-2222-2222-222222222222}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{11111111-1111-1111-1111-111111111111}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{11111111-1111-1111-1111-111111111111}.Release|Any CPU.Build.0 = Release|Any CPU
		{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{22222222-2222-2222-2222-222222222222}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{22222222-2222-2222-2222-222222222222}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
```

---

### `README.md`

````markdown
# Adversarial Concept-Drift Logic (ACDL)

A .NET 8 reference implementation of online concept-drift detectors under
adversarial conditions. Ports the Python prototype.

## Layout

- `Acdl/` — library + console demo (the simulation harness)
- `Acdl.Tests/` — xUnit tests

## Run

```bash
dotnet run --project Acdl
dotnet test
```

## Components

| Type | Role |
|---|---|
| `IDriftDetector` | Contract for online detectors |
| `PageHinkleyTest` | Cumulative-deviation change-point test |
| `AdwinLite` | Windowed Hoeffding-bound mean-shift test |
| `BoilingFrogAdversary` | Incremental evasion attacker |
| `AdversarialStream` | (feature, label) source |
| `OnlineThresholdClassifier` | Binary classifier (frozen or adaptive) |
| `DefendedDetector` | k-of-n voting ensemble of detectors |

## Notes

- All detectors monitor a **bounded error stream** in [0, 1], not raw features,
  so the Hoeffding bound applies.
- `Random` seeds will not produce identical streams to the NumPy version
  (different RNG algorithm). Behavior under the same parameters is equivalent
  in expectation.
- See `Program.cs` for the end-to-end demo.
````

---

### `Acdl/Acdl.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Acdl</RootNamespace>
    <AssemblyName>Acdl</AssemblyName>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

---

### `Acdl/IDriftDetector.cs`

```csharp
namespace Acdl;

/// <summary>Contract for any online drift detector.</summary>
public interface IDriftDetector
{
    /// <summary>Feed one observation. Returns true iff drift was just detected.</summary>
    bool Update(double value);

    /// <summary>Re-initialize internal state after a detection or manual cycle.</summary>
    void Reset();
}
```

---

### `Acdl/PageHinkleyTest.cs`

```csharp
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
```

---

### `Acdl/AdwinLite.cs`

```csharp
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
```

---

### `Acdl/RandomExtensions.cs`

```csharp
namespace Acdl;

public static class RandomExtensions
{
    /// <summary>Sample from a normal distribution via the Box-Muller transform.</summary>
    public static double NextGaussian(this Random rng, double mean = 0.0, double stdDev = 1.0)
    {
        // 1.0 - NextDouble() avoids the rare zero that would blow up the log.
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * z;
    }
}
```

---

### `Acdl/BoilingFrogAdversary.cs`

```csharp
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
```

---

### `Acdl/AdversarialStream.cs`

```csharp
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
```

---

### `Acdl/OnlineThresholdClassifier.cs`

```csharp
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
```

---

### `Acdl/DefendedDetector.cs`

```csharp
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
```

---

### `Acdl/Program.cs`

```csharp
using Acdl;

// End-to-end demo: stable phase, then boiling-frog attack phase.
var rng = new Random(7);

var stable = new AdversarialStream(rng: rng);

var adversary = new BoilingFrogAdversary(
    initialMean: 3.0, targetMean: 0.2, stepSize: 0.001, noiseScale: 0.3, rng: rng);
var attacked = new AdversarialStream(adversary: adversary, rng: rng);

var clf = new OnlineThresholdClassifier(threshold: 1.5, lr: 0.0); // frozen
var ph = new PageHinkleyTest(delta: 0.005, threshold: 15.0);
var adw = new AdwinLite(windowSize: 200, confidence: 0.01);

int errorsPre = 0, errorsPost = 0;
int? phTrigger = null, adwTrigger = null;

// --- Stable phase ---
for (int t = 0; t < 1000; t++)
{
    var (x, y) = stable.Sample();
    int err = clf.Predict(x) != y ? 1 : 0;
    errorsPre += err;
    ph.Update(err);
    adw.Update(err);
}

// Reset so we measure attack-phase triggers independently.
ph.Reset();
adw.Reset();

// --- Attack phase ---
for (int t = 0; t < 4000; t++)
{
    var (x, y) = attacked.Sample();
    int err = clf.Predict(x) != y ? 1 : 0;
    errorsPost += err;
    if (ph.Update(err) && phTrigger is null) phTrigger = t;
    if (adw.Update(err) && adwTrigger is null) adwTrigger = t;
}

Console.WriteLine($"Errors pre-attack (1000 steps): {errorsPre}");
Console.WriteLine($"Errors during attack (4000):    {errorsPost}");
Console.WriteLine($"Page-Hinkley first trigger:     {phTrigger?.ToString() ?? "<never>"}");
Console.WriteLine($"ADWIN-Lite first trigger:       {adwTrigger?.ToString() ?? "<never>"}");
Console.WriteLine($"Adversary final mean:           {adversary.CurrentMean:F4}");
```

---

### `Acdl.Tests/Acdl.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Acdl\Acdl.csproj" />
  </ItemGroup>
</Project>
```

---

### `Acdl.Tests/PageHinkleyTests.cs`

```csharp
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
```

---

### `Acdl.Tests/AdwinLiteTests.cs`

```csharp
using Acdl;
using Xunit;

namespace Acdl.Tests;

public class AdwinLiteTests
{
    [Fact]
    public void OutOfRangeValue_Throws()
    {
        var adw = new AdwinLite(windowSize: 20, confidence: 0.05);
        Assert.Throws<ArgumentOutOfRangeException>(() => adw.Update(1.5));
    }

    [Fact]
    public void DetectsMeanShift()
    {
        var adw = new AdwinLite(windowSize: 200, confidence: 0.05);
        for (int i = 0; i < 150; i++) adw.Update(0.05);
        bool triggered = false;
        for (int i = 0; i < 400; i++)
            if (adw.Update(0.95)) { triggered = true; break; }
        Assert.True(triggered);
    }

    [Fact]
    public void OddWindowSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdwinLite(windowSize: 11));
    }

    [Fact]
    public void TinyWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdwinLite(windowSize: 8));
    }

    [Fact]
    public void StableStream_DoesNotTrigger()
    {
        var adw = new AdwinLite(windowSize: 100, confidence: 0.001);
        var rng = new Random(99);
        bool triggered = false;
        for (int i = 0; i < 2000; i++)
        {
            double v = rng.NextDouble() < 0.1 ? 1.0 : 0.0;
            if (adw.Update(v)) { triggered = true; break; }
        }
        Assert.False(triggered);
    }
}
```

---

### `Acdl.Tests/BoilingFrogAdversaryTests.cs`

```csharp
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
```

---

### `Acdl.Tests/IntegrationTests.cs`

```csharp
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
```

---

That's the full set. Once you've laid the files out, from the `ACDL/` root:

```bash
dotnet build
dotnet test
dotnet run --project Acdl
```

Heads-up on one thing: the `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` line in `Acdl.csproj` is strict — if your environment is picky about, say, unused parameter warnings, you can drop it. The rest should build clean on any .NET 8 SDK.