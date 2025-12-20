using System.Text;

namespace Replane;

/// <summary>
/// FNV-1a hashing utilities for deterministic percentage-based bucketing.
/// </summary>
public static class Fnv1a
{
    // FNV-1a 32-bit constants
    private const uint FnvPrime = 0x01000193;
    private const uint FnvOffsetBasis = 0x811C9DC5;

    /// <summary>
    /// Compute FNV-1a 32-bit hash of a string.
    /// This hash function is used for deterministic percentage-based bucketing
    /// in segmentation conditions. The same input always produces the same hash,
    /// ensuring users consistently see the same variant.
    /// </summary>
    public static uint Hash32(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        return Hash32(bytes);
    }

    /// <summary>
    /// Compute FNV-1a 32-bit hash of a byte array.
    /// </summary>
    public static uint Hash32(ReadOnlySpan<byte> data)
    {
        var hash = FnvOffsetBasis;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }

    /// <summary>
    /// Convert a value to a percentage (0-100) using FNV-1a hash.
    /// </summary>
    /// <param name="value">The value to hash (e.g., user ID).</param>
    /// <param name="seed">Salt to ensure different configs get different distributions.</param>
    /// <returns>A percentage between 0 and 100.</returns>
    public static double HashToPercentage(string value, string seed)
    {
        var combined = $"{seed}:{value}";
        var hash = Hash32(combined);
        return (hash % 10000) / 100.0;
    }
}
