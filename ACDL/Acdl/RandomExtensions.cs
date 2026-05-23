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
