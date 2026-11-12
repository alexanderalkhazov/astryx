namespace Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

/// <summary>
/// Deterministic decimal noise for synthetic prices. Seeded <see cref="Random"/> is stable across runs; values stay
/// decimal so no binary floating point touches a price.
/// </summary>
internal sealed class SeededRandom(int seed)
{
    private const int Resolution = 1_000_000;
    private readonly Random _random = new(seed);

    /// <summary>Uniform in [0, 1).</summary>
    public decimal NextUnit() => _random.Next(Resolution) / (decimal)Resolution;

    public decimal Between(decimal min, decimal max) => min + ((max - min) * NextUnit());

    /// <summary>Approximately standard normal (Irwin–Hall: sum of 12 uniforms minus 6).</summary>
    public decimal NextGaussian()
    {
        var sum = 0m;
        for (var i = 0; i < 12; i++)
        {
            sum += NextUnit();
        }

        return sum - 6m;
    }

    /// <summary>Stable 32-bit FNV-1a hash; <see cref="string.GetHashCode()"/> differs per process.</summary>
    public static int Hash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }

    public static SeededRandom For(int seed, params object[] parts) =>
        new(parts.Aggregate(seed, (acc, part) => unchecked((acc * 31) ^ Hash(part.ToString() ?? string.Empty))));
}
