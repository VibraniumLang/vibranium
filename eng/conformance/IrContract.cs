using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vibranium.Conformance;

/// <summary>
/// Static lowering contract of the emitted LLVM IR (release-7, SPEC §5.3): checks, without compiling or running
/// anything, the places where C# defines a result that LLVM leaves undefined. (1) A non-constant shift count must be
/// computed by an <c>and</c> with the operand width minus one (C# masks the count; a count of the width or more is
/// poison in LLVM), and a constant count must be smaller than the width. (2) An integer division or remainder by a
/// non-constant divisor must be guarded by a zero test that reaches a branch (the `attempt to divide by zero` panic;
/// division by zero is undefined behaviour). (3) A signed division or remainder by a non-constant divisor or by -1 must
/// be guarded by a test of the divisor against -1 and a test of the dividend against the type's minimum, both reaching
/// a branch (the `arithmetic overflow` panic; MIN / -1 is undefined behaviour). Runtime functions (<c>@__vib_*</c>) are not
/// checked. Returns one entry per violated rule, with the first offending instruction.
/// </summary>
internal static partial class IrContract
{
    [GeneratedRegex(@"^\s*(%[-\w.$]+) = (.*?)\s*(?:,\s*!.*)?$")]
    private static partial Regex DefRx();

    [GeneratedRegex(@"^(shl|lshr|ashr)(?: nuw| nsw| exact)* i(\d+) ([^,]+), (\S+)$")]
    private static partial Regex ShiftRx();

    [GeneratedRegex(@"^and i(\d+) (\S+), (-?\d+)$")]
    private static partial Regex AndConstRx();

    [GeneratedRegex(@"^(sdiv|udiv|srem|urem)(?: exact)? i(\d+) ([^,]+), (\S+)$")]
    private static partial Regex DivRx();

    [GeneratedRegex(@"^icmp (?:eq|ne) i(\d+) (\S+), (-?\d+)$")]
    private static partial Regex IcmpConstRx();

    [GeneratedRegex(@"^(?:or|and|xor) i1 (\S+), (\S+)$")]
    private static partial Regex BoolOpRx();

    [GeneratedRegex(@"^load (i\d+), ptr (\S+?)(?:, align \d+)?$")]
    private static partial Regex LoadRx();

    [GeneratedRegex(@"^\s*br i1 (%[-\w.$]+),")]
    private static partial Regex BrRx();

    [GeneratedRegex(@"^define [^@]*@(""[^""]+""|[-\w.$]+)\(")]
    private static partial Regex DefineRx();

    public static List<(string Rule, string Example)> Check(string ir)
    {
        var res = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, body) in Functions(ir))
        {
            if (name.StartsWith("__vib_", StringComparison.Ordinal) || name.StartsWith("\"__vib_", StringComparison.Ordinal))
            {
                continue;
            }
            var defs = new Dictionary<string, string>(StringComparer.Ordinal);
            var branched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in body)
            {
                var d = DefRx().Match(line);
                if (d.Success)
                {
                    defs[d.Groups[1].Value] = d.Groups[2].Value;
                }
                var b = BrRx().Match(line);
                if (b.Success)
                {
                    branched.Add(b.Groups[1].Value);
                }
            }
            // a condition guards when a branch tests it directly or through i1 or/and/xor combinations
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var (n, rhs) in defs)
                {
                    if (!branched.Contains(n))
                    {
                        continue;
                    }
                    var m = BoolOpRx().Match(rhs);
                    if (m.Success)
                    {
                        changed |= branched.Add(m.Groups[1].Value);
                        changed |= branched.Add(m.Groups[2].Value);
                    }
                }
            }
            // two loads of the same pointer are the same divisor (the test and the division may load it separately)
            string Key(string v) => defs.TryGetValue(v, out var r) && LoadRx().Match(r) is { Success: true } lm ? "load " + lm.Groups[1].Value + " " + lm.Groups[2].Value : v;
            // (operand, constant) -> the i1 values that test the operand for equality with the constant
            var constTests = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (n, rhs) in defs)
            {
                var z = IcmpConstRx().Match(rhs);
                if (z.Success)
                {
                    string key = Key(z.Groups[2].Value) + " == " + z.Groups[3].Value;
                    if (!constTests.TryGetValue(key, out var l))
                    {
                        constTests[key] = l = [];
                    }
                    l.Add(n);
                }
            }
            bool Guarded(string operand, string constant) =>
                constTests.TryGetValue(Key(operand) + " == " + constant, out var tests) && tests.Any(branched.Contains);
            foreach (var (n, rhs) in defs)
            {
                var s = ShiftRx().Match(rhs);
                if (s.Success)
                {
                    int width = int.Parse(s.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string count = s.Groups[4].Value;
                    if (long.TryParse(count, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long k))
                    {
                        if (k < 0 || k >= width)
                        {
                            res.TryAdd("shift-count-out-of-range", $"@{name}: {n} = {rhs}");
                        }
                    }
                    else if (!defs.TryGetValue(count, out var cdef))
                    {
                        res.TryAdd("shift-mask", $"@{name}: unmasked count (not defined in the function); {n} = {rhs}");
                    }
                    else if (AndConstRx().Match(cdef) is not { Success: true } am)
                    {
                        res.TryAdd("shift-mask", $"@{name}: unmasked count {count} = {cdef}; {n} = {rhs}");
                    }
                    else if (long.Parse(am.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) != width - 1)
                    {
                        res.TryAdd("shift-mask", $"@{name}: {count} = {cdef}; {n} = {rhs}");
                    }
                    continue;
                }
                var dv = DivRx().Match(rhs);
                if (dv.Success)
                {
                    string op = dv.Groups[1].Value;
                    int width = int.Parse(dv.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    string dividend = dv.Groups[3].Value.Trim();
                    string divisor = dv.Groups[4].Value;
                    bool variable = divisor.StartsWith('%');
                    if (variable && !Guarded(divisor, "0"))
                    {
                        res.TryAdd("division-unguarded", $"@{name}: {n} = {rhs}");
                    }
                    if (op is "sdiv" or "srem" && (variable || divisor == "-1") && width is 8 or 16 or 32 or 64
                        && !(Guarded(divisor, "-1") && Guarded(dividend, MinValue(width))))
                    {
                        res.TryAdd("signed-overflow-unguarded", $"@{name}: {n} = {rhs}");
                    }
                }
            }
        }
        return [.. res.Select(kv => (kv.Key, kv.Value))];
    }

    private static string MinValue(int width) => width switch
    {
        8 => "-128",
        16 => "-32768",
        32 => "-2147483648",
        _ => "-9223372036854775808",
    };

    private static IEnumerable<(string Name, List<string> Body)> Functions(string ir)
    {
        string? name = null;
        List<string>? body = null;
        foreach (var raw in ir.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (name is null)
            {
                var m = DefineRx().Match(line);
                if (m.Success)
                {
                    name = m.Groups[1].Value;
                    body = [];
                }
                continue;
            }
            if (line == "}")
            {
                yield return (name, body!);
                name = null;
                body = null;
                continue;
            }
            body!.Add(line);
        }
    }
}
