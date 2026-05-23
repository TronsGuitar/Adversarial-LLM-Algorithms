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
