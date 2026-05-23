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
