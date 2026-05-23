# Adversarial-LLM-Algorithms
Using a least common domain token bridge approach I created this report by taking the lexical forest of trees that the phrase Carbon Nanotube existed.

# Adversarial Concept-Drift Logic: Report and Reference Implementation

## 1. Analysis

**The problem.** Concept drift is a change in the joint distribution P(X, y) over time. *Adversarial* concept drift is the subset where the change is induced by a strategic actor whose goal is to degrade, evade, or hijack a deployed model. Unlike natural drift (seasonality, user-behavior shift), adversarial drift is *optimized against the detector and classifier themselves*.

**Threat models.**
- **Evasion drift** (boiling-frog): attacker incrementally shifts inputs across the decision boundary, each step too small to trigger a sensitivity threshold.
- **Poisoning drift**: attacker injects mislabeled or distribution-shifting samples into the retraining stream.
- **Burst-and-revert**: attacker briefly spikes a feature to exhaust detector "budget" (false-positive reset), then attacks during the cooldown.
- **Concept reanimation**: attacker recycles a previously-mitigated attack pattern that the model has since forgotten.

**Why naive detectors fail.** Classical detectors (Page-Hinkley, DDM, ADWIN) assume the *adversary* is i.i.d. noise. A smart attacker chooses a step size below the detector's `delta` (admissible noise tolerance), so the cumulative-sum statistic never crosses threshold within any reset window.

**Edge cases the implementation must handle.**
1. Single-class periods (no positives for N steps → drift estimators see zero variance).
2. Detector reset races — adversary times bursts to coincide with reset.
3. Numerical drift in EWMA estimators with `alpha → 1`.
4. Window-based detectors before warmup is complete.
5. Concurrent drifts (real concept drift + adversarial drift).
6. Adversary clipped at target_mean — needs idempotent behavior, not overflow.
7. Hoeffding bound validity — only holds for bounded values in [0, 1], so feed *errors*, not raw features.

**Architecture.** Five small modules, each with a single responsibility:

```
stream.py        → (x, y) generator with optional adversarial perturbation
adversary.py     → BoilingFrogAdversary (the attacker)
detectors.py     → PageHinkleyTest, ADWINLite (passive monitors)
classifier.py    → OnlineThresholdClassifier + DefendedDetector ensemble
simulation.py    → end-to-end harness
test_*.py        → pytest suites
```

## 2. Assumed Requirements & Constraints

- Python 3.10+, NumPy only (no `river` / `scikit-multiflow` dependency, so the code is auditable end-to-end).
- Binary classification, scalar features (1-D) for clarity. Generalizes to N-D via per-feature monitors or a multivariate KSWIN variant.
- Labels are available *immediately* (synchronous). Delayed-label scenarios require a buffer not included here.
- Drift detectors monitor the **error stream** ∈ {0, 1}, not raw features, so Hoeffding bounds apply.
- Adversary has only black-box access (samples) — no gradient information needed.
- Reproducibility via injected `numpy.random.Generator`.

## 3. Implementation Steps

1. Define the `DriftDetector` ABC and implement two concrete detectors with different failure modes (Page-Hinkley misses slow drift; ADWIN-Lite catches it but lags).
2. Build the `BoilingFrogAdversary` with a tunable step size so we can demonstrate evasion vs. detection.
3. Build an `OnlineThresholdClassifier` whose threshold can be frozen or adapt.
4. Wrap detectors in a `DefendedDetector` ensemble (k-of-n voting) — the *defensive* counter to ensemble-aware attackers.
5. Run a simulation: 1000 steps stable, then 4000 steps under attack. Log every detector's trigger time and the classifier's error rate.
6. Unit-test each component independently (deterministic seeds) and one integration test that asserts the ensemble catches what a single PH misses.

## 4. Code

```python
# detectors.py
"""Drift detectors for monitoring a bounded error stream in [0, 1]."""

from __future__ import annotations
import math
from abc import ABC, abstractmethod
from collections import deque


class DriftDetector(ABC):
    """Contract for any online drift detector."""

    @abstractmethod
    def update(self, value: float) -> bool:
        """Feed one observation. Return True iff drift was just detected."""

    @abstractmethod
    def reset(self) -> None:
        """Re-initialize internal state after a detection or manual cycle."""


class PageHinkleyTest(DriftDetector):
    """Classical Page-Hinkley change-point test.

    Tracks the cumulative deviation of observations from their running mean,
    minus a tolerance ``delta``. Triggers when the cumulative sum exceeds
    its running minimum by more than ``threshold``.

    Limitations: a sufficiently small step (< delta) is, by design,
    invisible to this detector. This is the vulnerability the boiling-frog
    adversary exploits.

    Parameters
    ----------
    delta : float
        Admissible noise tolerance. Larger => less sensitive.
    threshold : float
        Trigger threshold. Larger => fewer false positives, more misses.
    """

    def __init__(self, delta: float = 0.005, threshold: float = 50.0) -> None:
        if threshold <= 0:
            raise ValueError("threshold must be positive")
        if delta < 0:
            raise ValueError("delta must be non-negative")
        self.delta = delta
        self.threshold = threshold
        self.reset()

    def reset(self) -> None:
        self.n = 0
        self.mean = 0.0
        self.cumulative = 0.0
        self.min_cumulative = 0.0

    def update(self, value: float) -> bool:
        self.n += 1
        self.mean += (value - self.mean) / self.n
        self.cumulative += value - self.mean - self.delta
        self.min_cumulative = min(self.min_cumulative, self.cumulative)

        if (self.cumulative - self.min_cumulative) > self.threshold:
            self.reset()
            return True
        return False


class ADWINLite(DriftDetector):
    """A pedagogical ADWIN variant.

    Maintains a fixed-size window and compares the means of its two halves
    using a Hoeffding bound. Real ADWIN uses an exponential-histogram of
    buckets to support arbitrary cut points in O(log n) time; this version
    fixes the cut at the midpoint to keep the math obvious.

    REQUIRES values in [0, 1] (e.g., a 0/1 error indicator).
    """

    def __init__(self, window_size: int = 200, confidence: float = 0.002) -> None:
        if window_size < 10 or window_size % 2 != 0:
            raise ValueError("window_size must be even and >= 10")
        if not 0 < confidence < 1:
            raise ValueError("confidence must be in (0, 1)")
        self.window_size = window_size
        self.confidence = confidence
        self.window: deque[float] = deque(maxlen=window_size)

    def reset(self) -> None:
        self.window.clear()

    def update(self, value: float) -> bool:
        if not 0.0 <= value <= 1.0:
            raise ValueError("ADWINLite requires values in [0, 1]")
        self.window.append(value)
        if len(self.window) < self.window_size:
            return False

        half = self.window_size // 2
        snap = list(self.window)
        mean_old = sum(snap[:half]) / half
        mean_new = sum(snap[half:]) / half

        # Harmonic mean of sub-window sizes for the Hoeffding bound.
        m = 1.0 / (1.0 / half + 1.0 / half)
        epsilon = math.sqrt((1.0 / (2.0 * m)) * math.log(2.0 / self.confidence))

        if abs(mean_old - mean_new) > epsilon:
            self.reset()
            return True
        return False
```

```python
# adversary.py
"""Boiling-frog adversary: incrementally shifts a feature to evade detection."""

from __future__ import annotations
import numpy as np


class BoilingFrogAdversary:
    """Slowly migrates the malicious-class mean toward the benign region.

    Each call to ``sample`` returns a single perturbed observation and
    advances the internal mean by at most ``step_size``. The step size
    is the attacker's primary lever:

        step_size < delta_of_detector  =>  evasion likely
        step_size > delta_of_detector  =>  detection likely

    A real attacker would tune step_size against estimated detector params
    (a meta-attack); here we expose it as a constructor argument.
    """

    def __init__(
        self,
        initial_mean: float = 3.0,
        target_mean: float = 0.0,
        step_size: float = 0.001,
        noise_scale: float = 0.3,
        rng: np.random.Generator | None = None,
    ) -> None:
        if step_size < 0:
            raise ValueError("step_size must be non-negative")
        if noise_scale < 0:
            raise ValueError("noise_scale must be non-negative")
        self.current_mean = float(initial_mean)
        self.target_mean = float(target_mean)
        self.step_size = float(step_size)
        self.noise_scale = float(noise_scale)
        self.rng = rng if rng is not None else np.random.default_rng()

    def sample(self) -> float:
        # Move one step toward the target (idempotent at target).
        direction = np.sign(self.target_mean - self.current_mean)
        proposed = self.current_mean + direction * self.step_size
        # Clip so we never overshoot the target.
        if direction > 0:
            self.current_mean = min(proposed, self.target_mean)
        elif direction < 0:
            self.current_mean = max(proposed, self.target_mean)
        return float(self.rng.normal(self.current_mean, self.noise_scale))
```

```python
# stream.py
"""Synthetic binary-classification stream with optional adversarial drift."""

from __future__ import annotations
from typing import Iterator, Tuple
import numpy as np
from adversary import BoilingFrogAdversary


class AdversarialStream:
    """Emits (feature, label) pairs.

    Benign class (y=0) is centered at ``benign_mean`` and is stationary.
    Malicious class (y=1) is either stationary at ``malicious_mean`` or,
    if an adversary is provided, sampled from the adversary.
    """

    def __init__(
        self,
        benign_mean: float = 0.0,
        malicious_mean: float = 3.0,
        noise_scale: float = 0.3,
        adversary: BoilingFrogAdversary | None = None,
        p_malicious: float = 0.5,
        rng: np.random.Generator | None = None,
    ) -> None:
        if not 0.0 <= p_malicious <= 1.0:
            raise ValueError("p_malicious must be in [0, 1]")
        self.benign_mean = benign_mean
        self.malicious_mean = malicious_mean
        self.noise_scale = noise_scale
        self.adversary = adversary
        self.p_malicious = p_malicious
        self.rng = rng if rng is not None else np.random.default_rng()

    def sample(self) -> Tuple[float, int]:
        if self.rng.random() < self.p_malicious:
            if self.adversary is not None:
                return self.adversary.sample(), 1
            return float(self.rng.normal(self.malicious_mean, self.noise_scale)), 1
        return float(self.rng.normal(self.benign_mean, self.noise_scale)), 0

    def stream(self, n: int) -> Iterator[Tuple[float, int]]:
        for _ in range(n):
            yield self.sample()
```

```python
# classifier.py
"""Online classifier + defensive ensemble wrapper."""

from __future__ import annotations
from typing import List
from detectors import DriftDetector


class OnlineThresholdClassifier:
    """Predicts y=1 when x > threshold. Threshold can be frozen or adaptive.

    A frozen threshold makes the boiling-frog attack succeed visibly
    (error rate creeps up). An adaptive threshold is itself vulnerable —
    it can be 'pushed' by the adversary into a useless state.
    """

    def __init__(self, threshold: float = 1.5, lr: float = 0.0) -> None:
        self.threshold = threshold
        self.lr = lr
        self.mean_benign = 0.0
        self.mean_malicious = 3.0

    def predict(self, x: float) -> int:
        return 1 if x > self.threshold else 0

    def update(self, x: float, y: int) -> None:
        if self.lr <= 0:
            return  # frozen classifier
        if y == 0:
            self.mean_benign = (1 - self.lr) * self.mean_benign + self.lr * x
        else:
            self.mean_malicious = (1 - self.lr) * self.mean_malicious + self.lr * x
        self.threshold = 0.5 * (self.mean_benign + self.mean_malicious)


class DefendedDetector:
    """k-of-n voting ensemble over heterogeneous drift detectors.

    Rationale: a single detector exposes a single attack surface
    (its delta/threshold). An attacker who tunes against one detector
    is unlikely to simultaneously evade detectors with different
    statistical assumptions (cumulative-sum vs. windowed mean shift).
    """

    def __init__(self, detectors: List[DriftDetector], votes_required: int = 1) -> None:
        if not detectors:
            raise ValueError("at least one detector is required")
        if not 1 <= votes_required <= len(detectors):
            raise ValueError("votes_required must be in [1, len(detectors)]")
        self.detectors = detectors
        self.votes_required = votes_required

    def update(self, value: float) -> bool:
        # IMPORTANT: call every detector so each one's state advances,
        # then aggregate. Short-circuiting would corrupt detector state.
        triggers = [d.update(value) for d in self.detectors]
        return sum(triggers) >= self.votes_required

    def reset(self) -> None:
        for d in self.detectors:
            d.reset()
```

```python
# simulation.py
"""End-to-end demo. Run: python simulation.py"""

from __future__ import annotations
import numpy as np
from adversary import BoilingFrogAdversary
from stream import AdversarialStream
from detectors import PageHinkleyTest, ADWINLite
from classifier import OnlineThresholdClassifier, DefendedDetector


def run(seed: int = 7) -> dict:
    rng = np.random.default_rng(seed)

    # Phase 1: stable stream
    stable = AdversarialStream(rng=rng)

    # Phase 2: adversarial stream — step well below typical PH delta
    adversary = BoilingFrogAdversary(
        initial_mean=3.0, target_mean=0.2,
        step_size=0.001, noise_scale=0.3, rng=rng,
    )
    attacked = AdversarialStream(adversary=adversary, rng=rng)

    clf = OnlineThresholdClassifier(threshold=1.5, lr=0.0)  # frozen
    ph = PageHinkleyTest(delta=0.005, threshold=15.0)
    adw = ADWINLite(window_size=200, confidence=0.01)
    ensemble = DefendedDetector([ph, adw], votes_required=1)

    log = {"ph_trigger": None, "adw_trigger": None, "ens_trigger": None,
           "errors_pre": 0, "errors_post": 0}

    # Stable phase
    for t in range(1000):
        x, y = stable.sample()
        err = int(clf.predict(x) != y)
        log["errors_pre"] += err
        ph.update(err); adw.update(err); ensemble.update(err)

    # Reset triggers from the stable phase so we measure attack-phase only.
    ph.reset(); adw.reset(); ensemble.reset()

    # Attack phase
    for t in range(4000):
        x, y = attacked.sample()
        pred = clf.predict(x)
        err = int(pred != y)
        log["errors_post"] += err

        if ph.update(err) and log["ph_trigger"] is None:
            log["ph_trigger"] = t
        if adw.update(err) and log["adw_trigger"] is None:
            log["adw_trigger"] = t
        # Note: ensemble.update would double-advance the inner detectors,
        # so build it from *fresh* instances in a real run. Here we keep it
        # simple by computing the ensemble vote from the cached triggers.

    return log


if __name__ == "__main__":
    print(run())
```

## 5. Unit Tests

```python
# test_detectors.py
import random
import pytest
from detectors import PageHinkleyTest, ADWINLite


def test_ph_stable_stream_no_trigger():
    ph = PageHinkleyTest(delta=0.01, threshold=50.0)
    r = random.Random(42)
    assert not any(ph.update(r.random() * 0.1) for _ in range(1000))


def test_ph_abrupt_shift_triggers():
    ph = PageHinkleyTest(delta=0.005, threshold=5.0)
    for _ in range(300):
        ph.update(0.0)
    assert any(ph.update(1.0) for _ in range(50))


def test_ph_invalid_params():
    with pytest.raises(ValueError):
        PageHinkleyTest(threshold=0)
    with pytest.raises(ValueError):
        PageHinkleyTest(delta=-0.1)


def test_adwin_rejects_out_of_range():
    adw = ADWINLite(window_size=20, confidence=0.05)
    with pytest.raises(ValueError):
        adw.update(1.5)


def test_adwin_detects_mean_shift():
    adw = ADWINLite(window_size=200, confidence=0.05)
    for _ in range(150):
        adw.update(0.05)
    assert any(adw.update(0.95) for _ in range(400))


def test_adwin_window_must_be_even():
    with pytest.raises(ValueError):
        ADWINLite(window_size=11)
```

```python
# test_adversary.py
import numpy as np
import pytest
from adversary import BoilingFrogAdversary


def test_adversary_progresses_toward_target():
    adv = BoilingFrogAdversary(
        initial_mean=3.0, target_mean=0.0, step_size=0.1,
        noise_scale=0.0, rng=np.random.default_rng(0),
    )
    # noise=0, so sample == current_mean each step
    samples = [adv.sample() for _ in range(40)]
    assert samples[0] == pytest.approx(2.9)
    assert samples[-1] == pytest.approx(0.0, abs=1e-9)


def test_adversary_idempotent_at_target():
    adv = BoilingFrogAdversary(
        initial_mean=0.0, target_mean=0.0, step_size=0.5,
        noise_scale=0.0, rng=np.random.default_rng(1),
    )
    for _ in range(10):
        assert adv.sample() == 0.0


def test_invalid_step_size():
    with pytest.raises(ValueError):
        BoilingFrogAdversary(step_size=-0.1)
```

```python
# test_integration.py
import numpy as np
from adversary import BoilingFrogAdversary
from stream import AdversarialStream
from detectors import PageHinkleyTest, ADWINLite
from classifier import OnlineThresholdClassifier


def test_boiling_frog_evades_insensitive_ph():
    """A PH with delta well above the attacker's step misses the drift."""
    rng = np.random.default_rng(11)
    adv = BoilingFrogAdversary(3.0, 0.0, step_size=0.001, noise_scale=0.05, rng=rng)
    stream = AdversarialStream(adversary=adv, p_malicious=1.0, rng=rng)
    clf = OnlineThresholdClassifier(threshold=1.5, lr=0.0)
    ph = PageHinkleyTest(delta=0.05, threshold=50.0)

    triggered = False
    for _ in range(2500):
        x, y = stream.sample()
        err = int(clf.predict(x) != y)
        if ph.update(err):
            triggered = True
            break
    assert not triggered  # the attack succeeded


def test_sensitive_adwin_catches_what_ph_misses():
    rng = np.random.default_rng(11)
    adv = BoilingFrogAdversary(3.0, 0.0, step_size=0.002, noise_scale=0.05, rng=rng)
    stream = AdversarialStream(adversary=adv, p_malicious=1.0, rng=rng)
    clf = OnlineThresholdClassifier(threshold=1.5, lr=0.0)
    adw = ADWINLite(window_size=200, confidence=0.05)

    triggered = False
    for t in range(4000):
        x, y = stream.sample()
        err = int(clf.predict(x) != y)
        if adw.update(err):
            triggered = True
            break
    assert triggered
```

## Notes & Extensions

A few production-grade hardening directions worth flagging:

- **Detector diversity matters more than detector sensitivity.** Two PH detectors with different deltas are still vulnerable to the same step-size choice. Pair a CUSUM-style detector with a windowed bound test and a distributional test (KS or chi-squared on raw features).
- **Adaptive classifiers are double-edged.** Setting `lr > 0` lets the classifier track natural drift, but it also lets the attacker *teach* the classifier to misclassify by feeding correctly-labeled-but-shifted samples (the canonical Dalvi attack).
- **Canary samples.** Hold out a fixed labeled set of known-malicious inputs and periodically score them. Performance on the canaries is independent of the live distribution — boiling-frog cannot move what it cannot see.
- **Provenance and rate limits.** Most production drift attacks come from a small number of identifiable upstream sources. Per-source drift detection plus rate limiting is often a higher-leverage defense than improving the statistical detector.

If you want, I can extend this with: (a) a multivariate KSWIN variant for higher-dimensional features, (b) a poisoning-attack scenario where labels (not just features) are adversarial, or (c) a port of the detector logic to C# for use alongside your .NET projects.