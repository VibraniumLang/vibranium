using System;
using System.Collections.Generic;

namespace Vibranium.Conformance;

/// <summary>Deterministic SplitMix64 generator: same seed, same program, on every machine.</summary>
internal sealed class Rng(ulong seed)
{
    private ulong _state = seed;

    public static ulong Mix(ulong a, ulong b)
    {
        ulong z = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform integer in [0, n).</summary>
    public int Int(int n) => n <= 1 ? 0 : (int)(Next() % (ulong)n);

    /// <summary>Uniform integer in [lo, hi].</summary>
    public int Range(int lo, int hi) => lo + Int(hi - lo + 1);

    public bool Chance(double p) => (Next() >> 11) * (1.0 / (1UL << 53)) < p;

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(items.Count)];

    /// <summary>Weighted pick over (weight, value) pairs.</summary>
    public T Weighted<T>(params (int W, T V)[] items)
    {
        int total = 0;
        foreach (var (w, _) in items)
        {
            total += w;
        }
        int r = Int(total);
        foreach (var (w, v) in items)
        {
            if (r < w)
            {
                return v;
            }
            r -= w;
        }
        return items[^1].V;
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Int(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
