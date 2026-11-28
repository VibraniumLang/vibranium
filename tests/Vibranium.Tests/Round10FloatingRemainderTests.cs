using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Vibranium.Cli;
using Xunit;

namespace Vibranium.Tests;

public sealed partial class VibraniumEndToEndTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Round10_FloatingRemainder_ActualCliBuilds_AreExactAndLibcOnly(bool single)
    {
        string source = CreateRound10RemainderProgram(single, out int checks);
        string expected = string.Concat(Enumerable.Repeat("True\n", checks));
        // Reuse the managed/Analyzer/Stage0/self differential and sanitizer harness.
        await VerifyRound5TrackAFourWayDifferentialAsync(source, expected);

        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-remainder-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string input = Path.Combine(temp, "Program.cs");
            File.WriteAllText(input, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(input, includeStd: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
            string sysroot = Path.Combine(RepoRoot, "src");
            foreach (string compiler in new[] { typeof(VibraniumCompilerDriver).Assembly.Location, ResolveRound5VibSelfDllPath() })
            {
                string prefix = Path.GetFileNameWithoutExtension(compiler);
                foreach (int optimization in new[] { 0, 3 })
                {
                    string binary = Path.Combine(temp, $"{prefix}-O{optimization}");
                    var build = RunProcess(DotnetExe, compiler, "build", input, "--sysroot", sysroot,
                        $"-O{optimization}", "-o", binary);
                    Assert.True(build.ExitCode == 0, build.Stdout + build.Stderr);
                    AssertLibcOnlyDependencies(binary);
                    Assert.Equal((0, expected, ""), RunProcess(binary));
                }

                string ir = Path.Combine(temp, prefix + ".ll");
                var emit = RunProcess(DotnetExe, compiler, "emit-ll", input, "--sysroot", sysroot, "--asan", "-o", ir);
                Assert.True(emit.ExitCode == 0, emit.Stdout + emit.Stderr);
                string text = File.ReadAllText(ir);
                Assert.Contains(single ? "call float @__vib_rem_f32(" : "call double @__vib_rem_f64(", text);
                Assert.DoesNotContain(" = frem ", text, StringComparison.Ordinal);
                Assert.Equal(1, text.Split("define internal double @__vib_rem_f64(", StringSplitOptions.None).Length - 1);
                Assert.Equal(1, text.Split("define internal float @__vib_rem_f32(", StringSplitOptions.None).Length - 1);
                Assert.Equal(1, text.Split("declare i64 @llvm.ctlz.i64(", StringSplitOptions.None).Length - 1);
                var verify = RunProcess("opt-18", "-passes=verify", "-disable-output", ir);
                Assert.True(verify.ExitCode == 0, verify.Stderr);
                string sanitized = Path.Combine(temp, prefix + ".asan");
                var link = RunProcess("clang-18", "-O3", "-fsanitize=address,leak", ir, "-o", sanitized);
                Assert.True(link.ExitCode == 0, link.Stderr);
                // Sanitizer dependencies are deliberately not subject to the production allowlist.
                Assert.Equal((0, expected, ""), RunProcess(sanitized));
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static string CreateRound10RemainderProgram(bool single, out int checks)
    {
        string type = single ? "float" : "double";
        string one = single ? "1.0f" : "1.0";
        string zero = single ? "0.0f" : "0.0";
        // Env.Args is an external runtime input: LLVM cannot fold these operands,
        // even at -O3. Both managed and native expose user arguments only.
        var source = new StringBuilder("var arguments = Vibranium.Std.Env.Args().Unwrap();\nint scale = arguments.Count + 1;\n");
        (double X, double Y)[] pairs = single
            ? [(5.5, 2), (-5.5, 2), (5.5, -2), (-5.5, -2), (4, 2), (-4, 2), (4, -2), (-4, -2),
               (0.0, 2), (-0.0, 2), (1, 0), (1, -0.0), (0, 0), (float.NaN, 2), (2, float.NaN),
               (float.PositiveInfinity, 2), (float.NegativeInfinity, -2), (2, float.PositiveInfinity),
               (-2, float.NegativeInfinity), (float.PositiveInfinity, float.PositiveInfinity),
               (float.Epsilon, 2 * float.Epsilon), (7 * float.Epsilon, 3 * float.Epsilon),
               (float.MaxValue, float.Epsilon), (float.MaxValue, 3), (16777216, 3), (1e-40f, 3e-45f),
               (float.MinValue, 1e-38f), (1.1754944e-38f, 1e-38f)]
            : [(5.5, 2), (-5.5, 2), (5.5, -2), (-5.5, -2), (4, 2), (-4, 2), (4, -2), (-4, -2),
               (0.0, 2), (-0.0, 2), (1, 0), (1, -0.0), (0, 0), (double.NaN, 2), (2, double.NaN),
               (double.PositiveInfinity, 2), (double.NegativeInfinity, -2), (2, double.PositiveInfinity),
               (-2, double.NegativeInfinity), (double.PositiveInfinity, double.PositiveInfinity),
               (double.Epsilon, 2 * double.Epsilon), (7 * double.Epsilon, 3 * double.Epsilon),
               (double.MaxValue, double.Epsilon), (1e308, 3), (1e308, 1e-308), (1e-310, 3e-320),
               (double.MinValue, 1e-308), (2.2250738585072014e-308, 2e-308)];
        foreach (var (x, y) in pairs)
        {
            double expected = single ? (float)x % (float)y : x % y;
            string call = double.IsNaN(expected) ? "CheckNan" : "Check";
            source.Append($"WriteLine({call}({Round10Literal(x, single)} * scale, {Round10Literal(y, single)} * scale");
            if (!double.IsNaN(expected)) source.Append($", {Round10Literal(expected, single)} * scale");
            source.AppendLine("));");
        }
        source.AppendLine($"const {type} folded = {Round10Literal(5.5, single)} % {Round10Literal(2, single)};");
        source.AppendLine($"WriteLine(folded == {Round10Literal(1.5, single)});");
        source.AppendLine($"const {type} foldedZero = {Round10Literal(-4, single)} % {Round10Literal(2, single)};");
        source.AppendLine($"WriteLine({one} / foldedZero < {zero});");
        string dividend = Round10Literal(5.5, single) + " * scale";
        string divisor = Round10Literal(2, single) + " * scale";
        string remainder = Round10Literal(1.5, single) + " * scale";
        source.AppendLine($"{type} compound = {dividend}; compound %= {divisor}; WriteLine(compound == {remainder});");
        source.AppendLine($"Span<{type}> slots = [{dividend}]; slots[0] %= {divisor}; WriteLine(slots[0] == {remainder});");
        source.AppendLine($"var items = new Vec<{type}>(); items.Add({dividend}); items[0] %= {divisor}; WriteLine(items[0] == {remainder});");
        source.AppendLine($"WriteLine(Generic({dividend}, {divisor}, 0) == {remainder});");
        source.AppendLine($"static {type} Generic<T>({type} x, {type} y, T unused) {{ return x % y; }}");
        source.AppendLine($"static bool Check({type} x, {type} y, {type} expected) {{ {type} r = x % y; return r == expected && (r != {zero} || {one} / r == {one} / expected); }}");
        source.AppendLine($"static bool CheckNan({type} x, {type} y) {{ {type} r = x % y; return !(r <= {zero} || r >= {zero}); }}");
        checks = pairs.Length + 6;
        return source.ToString();
    }

    private static string Round10Literal(double value, bool single)
    {
        string type = single ? "float" : "double";
        if (double.IsNaN(value)) return type + ".NaN";
        if (double.IsPositiveInfinity(value)) return type + ".PositiveInfinity";
        if (double.IsNegativeInfinity(value)) return type + ".NegativeInfinity";
        string text = single ? ((float)value).ToString("R", CultureInfo.InvariantCulture) : value.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E')) text += ".0";
        return text + (single ? "f" : "d");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong RemainderBits64(ulong x, ulong y);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint RemainderBits32(uint x, uint y);

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Round10_FloatingRemainder_RawBits_MatchDotnetOracle(int optimization)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-remainder-bits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            // Test the actual runtime text, not a second implementation of the algorithm.
            string runtime = File.ReadAllText(Path.Combine(RepoRoot, "src", "Vibranium.Core", "Native", "x86_64-linux-gnu", "vib_rt.ll"));
            int start = runtime.IndexOf("declare i64 @llvm.ctlz.i64", StringComparison.Ordinal);
            int end = runtime.IndexOf("define internal void @__vib_write_all", start, StringComparison.Ordinal);
            string ir = Path.Combine(temp, "remainder.ll");
            File.WriteAllText(ir, "target triple = \"x86_64-pc-linux-gnu\"\n" + runtime[start..end] + """
                define i64 @remainder64(i64 %x, i64 %y) {
                  %a = bitcast i64 %x to double
                  %b = bitcast i64 %y to double
                  %r = call double @__vib_rem_f64(double %a, double %b)
                  %bits = bitcast double %r to i64
                  ret i64 %bits
                }
                define i32 @remainder32(i32 %x, i32 %y) {
                  %a = bitcast i32 %x to float
                  %b = bitcast i32 %y to float
                  %r = call float @__vib_rem_f32(float %a, float %b)
                  %bits = bitcast float %r to i32
                  ret i32 %bits
                }
                """);
            var verify = RunProcess("opt-18", "-passes=verify", "-disable-output", ir);
            Assert.True(verify.ExitCode == 0, verify.Stderr);
            string library = Path.Combine(temp, "remainder.so");
            var build = RunProcess("clang-18", $"-O{optimization}", "-shared", "-fPIC", ir, "-o", library);
            Assert.True(build.ExitCode == 0, build.Stderr);
            var imports = RunProcess("nm", "-D", "--undefined-only", library);
            Assert.Equal(0, imports.ExitCode);
            Assert.DoesNotContain("fmod", imports.Stdout, StringComparison.Ordinal);
            nint handle = NativeLibrary.Load(library);
            try
            {
                var rem64 = Marshal.GetDelegateForFunctionPointer<RemainderBits64>(NativeLibrary.GetExport(handle, "remainder64"));
                var rem32 = Marshal.GetDelegateForFunctionPointer<RemainderBits32>(NativeLibrary.GetExport(handle, "remainder32"));
                int doubleCount = 0;
                foreach (var (x, y) in Round10DoubleVectors())
                {
                    doubleCount++;
                    double expected = BitConverter.UInt64BitsToDouble(x) % BitConverter.UInt64BitsToDouble(y);
                    ulong actual = rem64(x, y);
                    Assert.True(double.IsNaN(expected) ? double.IsNaN(BitConverter.UInt64BitsToDouble(actual)) : BitConverter.DoubleToUInt64Bits(expected) == actual,
                        $"binary64 O{optimization}: x={x:X16}, y={y:X16}, expected={BitConverter.DoubleToUInt64Bits(expected):X16}, actual={actual:X16}");
                }
                Assert.Equal(74636, doubleCount);
                int singleCount = 0;
                foreach (var (x, y) in Round10SingleVectors())
                {
                    singleCount++;
                    float expected = BitConverter.UInt32BitsToSingle(x) % BitConverter.UInt32BitsToSingle(y);
                    uint actual = rem32(x, y);
                    Assert.True(float.IsNaN(expected) ? float.IsNaN(BitConverter.UInt32BitsToSingle(actual)) : BitConverter.SingleToUInt32Bits(expected) == actual,
                        $"binary32 O{optimization}: x={x:X8}, y={y:X8}, expected={BitConverter.SingleToUInt32Bits(expected):X8}, actual={actual:X8}");
                }
                Assert.Equal(67468, singleCount);
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static IEnumerable<(ulong X, ulong Y)> Round10DoubleVectors()
    {
        ulong[] magnitudes = [0, 1, 2, 3, 0xFFFFFFFFFFFFF, 0x10000000000000, 0x10000000000001,
            0x3FEFFFFFFFFFFFFF, 0x3FF0000000000000, 0x4000000000000000, 0x4008000000000000,
            0x7FEFFFFFFFFFFFFF, 0x7FF0000000000000, 0x7FF0000000000001, 0x7FF8000000000000];
        foreach (ulong x in magnitudes)
            foreach (ulong y in magnitudes)
                for (int signs = 0; signs < 4; signs++)
                    yield return (x | ((ulong)(signs & 1) << 63), y | ((ulong)(signs >> 1) << 63));
        // Reduction (not the |x| < |y| shortcut) lands exactly at the normal
        // boundary, and one ULP below it, for all operand signs.
        for (int signs = 0; signs < 4; signs++)
        {
            yield return (0x28000000000000 | ((ulong)(signs & 1) << 63), 0x20000000000000 | ((ulong)(signs >> 1) << 63));
            yield return (0x27FFFFFFFFFFFF | ((ulong)(signs & 1) << 63), 0x20000000000000 | ((ulong)(signs >> 1) << 63));
        }
        // Every exponent field, near-boundary significands, and both huge and
        // tiny exponent gaps. The fixed sequence below is not a statistical claim.
        for (ulong e = 0; e < 2048; e++)
        {
            yield return ((e << 52) | 1, 3);
            yield return ((e << 52) | 0xFFFFFFFFFFFFF, 0x10000000000001);
            yield return (0x7FEFFFFFFFFFFFFF, (e << 52) | 1);
            yield return ((e << 52) | 0xFFFFFFFFFFFFF, (e << 52) | 1);
        }
        ulong state = 0xA0761D6478BD642F;
        for (int i = 0; i < 65536; i++) yield return (Round10NextBits(ref state), Round10NextBits(ref state));
    }

    private static IEnumerable<(uint X, uint Y)> Round10SingleVectors()
    {
        uint[] magnitudes = [0, 1, 2, 3, 0x7FFFFF, 0x800000, 0x800001, 0x3F7FFFFF,
            0x3F800000, 0x40000000, 0x40400000, 0x7F7FFFFF, 0x7F800000, 0x7F800001, 0x7FC00000];
        foreach (uint x in magnitudes)
            foreach (uint y in magnitudes)
                for (int signs = 0; signs < 4; signs++)
                    yield return (x | ((uint)(signs & 1) << 31), y | ((uint)(signs >> 1) << 31));
        for (int signs = 0; signs < 4; signs++)
        {
            yield return (0x1400000 | ((uint)(signs & 1) << 31), 0x1000000 | ((uint)(signs >> 1) << 31));
            yield return (0x13FFFFF | ((uint)(signs & 1) << 31), 0x1000000 | ((uint)(signs >> 1) << 31));
        }
        for (uint e = 0; e < 256; e++)
        {
            yield return ((e << 23) | 1, 3);
            yield return ((e << 23) | 0x7FFFFF, 0x800001);
            yield return (0x7F7FFFFF, (e << 23) | 1);
            yield return ((e << 23) | 0x7FFFFF, (e << 23) | 1);
        }
        ulong state = 0xE7037ED1A0B428DB;
        for (int i = 0; i < 65536; i++) yield return ((uint)Round10NextBits(ref state), (uint)Round10NextBits(ref state));
    }

    private static ulong Round10NextBits(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}
