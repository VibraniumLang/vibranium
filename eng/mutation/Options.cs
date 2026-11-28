using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vibranium.Mutation;

/// <summary><c>--name value</c> command-line options; a flag without a value is <c>true</c>.</summary>
internal sealed class Options
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public Options(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Expected --option value, got " + args[i]);
            string key = args[i][2..];
            _values[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
        }
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public string? Find(string key) => _values.TryGetValue(key, out string? value) ? value : null;

    public string Get(string key, string fallback) => _values.TryGetValue(key, out string? value) ? value : fallback;

    public int Number(string key, int fallback) => int.Parse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

    public void SetDefault(string key, string value) => _values.TryAdd(key, value);
}
