using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfNaturalBranchTypeTests
{
    [Theory]
    [InlineData("bool b = false; Option<int> o = b ? Some(2) : default; WriteLine(o.IsSome);", "VIB008")]
    [InlineData("bool b = true; Option<int> o = b ? default : Some(2); WriteLine(o.IsSome);", "VIB008")]
    [InlineData("bool b = false; bool c = true; Option<int> o = b ? Some(2) : (c ? default : Some(3)); WriteLine(o.IsSome);", "VIB008")]
    [InlineData("bool b = false; WriteLine(Read(b ? Some(2) : default)); static bool Read(Option<int> o) => o.IsSome;", "VIB008")]
    [InlineData("WriteLine(G(false).IsSome); static Option<int> G(bool b) => b ? Some(2) : default;", "VIB008")]
    [InlineData("bool b = false; Option<int> o = b switch { true => Some(2), false => default }; WriteLine(o.IsSome);", "VIB008")]
    [InlineData("bool b = true; Option<int> o = b switch { true => default, false => Some(2) }; WriteLine(o.IsSome);", "VIB008")]
    [InlineData("bool b = false; Result<int,int> r = b ? Ok(2) : default; WriteLine(r.IsOk);", "VIB008")]
    [InlineData("bool b = true; Result<int,int> r = b ? default : Err(2); WriteLine(r.IsOk);", "VIB008")]
    public Task RejectsWithExactFrontendParity(string source, string expected) => Round12RuleWitness.RejectAsync(source, expected, exact: true);

    [Theory(Skip = "Pending C23 in self-fix-z; field initializers containing ?: will have exactly VIB000.")]
    [InlineData("WriteLine(new C().B.IsSome); class C { public readonly Option<int> B = Pick() ? Some(2) : default; static bool Pick() => false; }")]
    [InlineData("WriteLine(new S().B.IsSome); struct S { public readonly Option<int> B = Pick() ? Some(2) : default; public S() { } static bool Pick() => false; }")]
    public Task FieldInitializerPendingC23(string source) => Round12RuleWitness.RejectAsync(source, "VIB000", exact: true);

    [Theory]
    [InlineData("bool b = false; int n = 2; byte x = b ? (byte)1 : n; WriteLine(x);", "CS0266")]
    [InlineData("bool b = false; byte x = b ? 1 : 2; WriteLine(x);", "CS0266")]
    [InlineData("int k = 2; int n = 2; byte x = k switch { 1 => (byte)1, _ => n }; WriteLine(x);", "CS0029")]
    public Task RejectsWhenAnArmDoesNotConvert(string source, string id) => Round12SelfCSharpValidity.RejectAsync(source, id);

    [Theory]
    [InlineData("bool b = false; Option<int> o = b ? Some(2) : None; WriteLine(o.IsSome);", "False\n")]
    [InlineData("bool b = false; Result<int,int> r = b ? Ok(2) : Err(3); WriteLine(r.IsOk);", "False\n")]
    [InlineData("bool b = false; Option<int> o = b switch { true => Some(2), false => None }; WriteLine(o.IsSome);", "False\n")]
    [InlineData("bool b = true; Result<int,int> r = b switch { true => Ok(2), false => Err(3) }; WriteLine(r.IsOk);", "True\n")]
    [InlineData("bool b = true; long n = 16777217; float f = 0; double d = b ? n : f; WriteLine((long)d);", "16777216\n")]
    [InlineData("bool b = false; byte n = 7; long d = b ? n : default; WriteLine(d);", "0\n")]
    [InlineData("int k = 1; long n = 16777217; float f = 0; double d = k switch { 1 => n, 2 => f, _ => default }; WriteLine((long)d);", "16777216\n")]
    [InlineData("bool b = false; S s = b ? new S(7) : new(); WriteLine(s.X); struct S { public int X; public S(int x) { X = x; } }", "0\n")]
    [InlineData("bool t = true; uint u = 4000000000; WriteLine(t ? u : -1); short s = -5; WriteLine(t ? s : u);", "4000000000\n-5\n")]
    [InlineData("bool t = true; uint u = 4000000000; WriteLine(C.F(t ? u : -1)); static class C { public static int F(int x) => 1; public static int F(long x) => 2; public static int F(double x) => 3; }", "2\n")]
    [InlineData("bool t = true; uint u = 4000000000; WriteLine(F(t switch { true => u, false => -1 })); static long F(long x) => x;", "4000000000\n")]
    [InlineData("bool t = true; bool q = false; uint u = 4000000000; short s = -5; WriteLine(F(t ? (q ? u : -1) : s)); static long F(long x) => x;", "-1\n")]
    [InlineData("int k = 1; byte x = k switch { 1 => (byte)1, _ => 2 }; WriteLine((int)x);", "1\n")]
    [InlineData("bool b = false; int n = 1; uint u = 2; long x = b ? n : u; WriteLine(x);", "2\n")]
    [InlineData("bool b = false; int n = 3; uint u = 4; long x = 0; x = (b ? n : u); WriteLine(x);", "4\n")]
    [InlineData("int k = 2; byte x = 0; x = k switch { 1 => 1, _ => 2 }; WriteLine(x);", "2\n")]
    [InlineData("WriteLine(F(false)); static long F(bool b) { int n = 5; uint u = 6; return b ? n : u; }", "6\n")]
    [InlineData("WriteLine(F(2)); static byte F(int k) { byte n = 5; return k switch { 1 => 1, _ => n }; }", "5\n")]
    [InlineData("bool b = true; int n = 3; uint u = 4; WriteLine(F(b ? n : u)); static long F(long x) => x;", "3\n")]
    [InlineData("int k = 2; WriteLine(F(k switch { 1 => 3, _ => 4 })); static byte F(byte x) => x;", "4\n")]
    [InlineData("bool b = false; byte n = 7; WriteLine(C.F(b ? n : 8)); WriteLine(C.F(b switch { true => n, false => 8 })); static class C { public static int F(byte x) => 1; public static int F(long x) => 2; }", "2\n1\n")]
    [InlineData("bool b = false; int k = 2; byte x = k switch { 1 => 1, _ => b ? default : (byte)2 }; WriteLine(x);", "2\n")]
    [InlineData("bool t = true;\nuint u = 4000000000;\nOption<long> o = Some<long>(t ? u : -1);\nWriteLine(o.Unwrap());\n", "4000000000\n")]
    [InlineData("int k = 1;\nOption<byte> o = Some<byte>(k switch { 1 => (byte)1, _ => 2 });\nWriteLine((int)o.Unwrap());\n", "1\n")]
    [InlineData("bool t = true;\nuint u = 4000000000;\nResult<long, int> r1 = Ok<long>(t ? u : -1);\nResult<int, long> r2 = Err<long>(t ? u : -1);\nlong m = Move<long>(t ? u : -1);\nWriteLine(r1.Unwrap());\nWriteLine(r2.IsOk);\nWriteLine(m);\n", "4000000000\nFalse\n4000000000\n")]
    [InlineData("bool b = false;\nint k = 2;\nOption<int> a = Some(1);\nvar o = k switch { 1 => a, _ => b ? Some(2) : None };\nWriteLine(o.IsSome);\n", "False\n")]
    [InlineData("bool b = true;\nint k = 2;\nuint u = 4000000000;\nvar x = k switch { 1 => 10L, _ => b ? u : -1 };\nWriteLine(x);\n", "4000000000\n")]
    [InlineData("bool t = true;\nuint u = 4000000000;\nvar acc = new Acc();\nacc += (t ? u : -1);\nWriteLine(acc.Sum);\nsealed class Acc { public long Sum; [Mut] public void operator +=(long v) { Sum += v; } }\n", "4000000000\n")]
    [InlineData("int k = 2;\nvar acc = new Acc();\nacc += (k switch { 1 => (byte)1, _ => 2 });\nWriteLine(acc.Sum);\nsealed class Acc { public int Sum; [Mut] public void operator +=(byte v) { Sum += v; } }\n", "2\n")]
    [InlineData("Span<int> s = [10, 20, 30];\nbool t = true;\nbyte b = 1;\nsbyte sb = 2;\nWriteLine(s.Slice(t ? b : sb)[0]);\n", "20\n")]
    [InlineData("bool t = true;\nbyte b = 4;\nsbyte sb = 2;\nvar v = new Vec<int>(t ? b : sb);\nWriteLine(v.Capacity >= 4);\n", "True\n")]
    [InlineData("bool t = false; uint u = 4000000000;\nOption<long> x = Prelude.Some<long>(t ? u : -1);\nWriteLine(x.Unwrap());\n", "-1\n")]
    [InlineData("bool t = true; byte b = 1; sbyte signed = 2;\nReadOnlySpan<int> s = [10, 20, 30];\nWriteLine(s.Slice(start: t ? b : signed, length: t ? b : signed)[0]);\n", "20\n")]
    [InlineData("bool t = true; int k = 1; long selector = 3; uint u = 4000000000;\nvar x = selector switch { var i when i != k => (t ? u : -1), _ => 9L };\nWriteLine(x);\n", "4000000000\n")]
    public Task AcceptedControlsHaveNativeParity(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);

    [Theory]
    [InlineData("bool t = true; uint u = 8; WriteLine((long)(t ? u : -1));", "8\n")]
    [InlineData("bool t = true; uint u = 8; WriteLine((long)(t switch { true => u, false => -1 }));", "8\n")]
    [InlineData("bool t = false; sbyte a = -2; byte b = 8; int n = (int)(t ? a : b); WriteLine(n);", "8\n")]
    [InlineData("bool t = true; sbyte a = -2; byte b = 8; WriteLine((int)(short)(t ? a : b));", "-2\n")]
    [InlineData("bool t = false; sbyte a = -2; byte b = 8; WriteLine((int)(short)(t switch { true => a, false => b }));", "8\n")]
    [InlineData("bool t = false; long l = -2; ulong u = 8; double d = (double)(t ? l : u); WriteLine((long)d);", "8\n")]
    [InlineData("bool t = true; long l = -2; ulong u = 8; double d = (double)(t switch { true => l, false => u }); WriteLine((long)d);", "-2\n")]
    [InlineData("bool t = false; uint u = 8; WriteLine(F((long)(t ? u : -1))); static long F(long n) => n;", "-1\n")]
    [InlineData("WriteLine(F(true)); static long F(bool t) { uint u = 8; return (long)(t switch { true => u, false => -1 }); }", "8\n")]
    [InlineData("bool t = false; uint u = 8; WriteLine(unchecked((long)(t ? u : -1)));", "-1\n")]
    [InlineData("bool t = false; uint u = 8; WriteLine(unchecked((long)(t switch { true => u, false => -1 })));", "-1\n")]
    [InlineData("bool t = true; long n = 16777217; float f = 0; WriteLine((long)(double)(t ? n : f));", "16777216\n")]
    [InlineData("bool t = true; long n = 16777217; float f = 0; WriteLine((long)(double)(t switch { true => n, false => f }));", "16777216\n")]
    public Task CastTargetsTypelessBranchesButPreservesExistingResultConversion(string source, string expectedOutput) =>
        Round12RuleWitness.AcceptAsync(source, expectedOutput);

    [Theory]
    [InlineData("bool t = true; uint u = 8; WriteLine((int)(t ? u : -1));")]
    [InlineData("bool t = true; uint u = 8; WriteLine((int)(t switch { true => u, false => -1 }));")]
    public Task CastCannotSupplyAnExplicitConversionToIndividualArms(string source) =>
        Round12SelfCSharpValidity.RejectAsync(source, "CS0029");

    [Theory]
    [InlineData("bool t = true; uint u = 8; WriteLine(checked((long)(t ? u : -1)));")]
    [InlineData("bool t = true; uint u = 8; WriteLine(checked((long)(t switch { true => u, false => -1 }))); ")]
    public Task CheckedCastContextRemainsCut(string source) =>
        Round12RuleWitness.RejectAsync(source, "VIB000", exact: true);


    private static string BuildNestedGuardSwitch(string shape, int depth)
    {
        var sb = new StringBuilder();
        switch (shape)
        {
            case "option-paren":
                sb.AppendLine("Option<int> o = Some(1);");
                sb.AppendLine("int k = 1;");
                sb.Append("WriteLine(");
                for (int i = depth - 1; i >= 0; i--) sb.Append($"(o switch {{ Some<int>(var x{i}) when ");
                sb.Append("k > 0");
                for (int i = 0; i < depth; i++) sb.Append(" => true, _ => false })");
                sb.AppendLine(");");
                break;
            case "int-paren":
                sb.AppendLine("int k = 1;");
                sb.Append("WriteLine(");
                for (int i = depth - 1; i >= 0; i--) sb.Append("(k switch { 1 when ");
                sb.Append("k > 0");
                for (int i = 0; i < depth; i++) sb.Append(" => true, _ => false })");
                sb.AppendLine(");");
                break;
            case "call-wrapped":
                sb.AppendLine("Option<int> o = Some(1);");
                sb.AppendLine("int k = 1;");
                sb.Append("WriteLine(");
                for (int i = depth - 1; i >= 0; i--) sb.Append($"Id(o switch {{ Some<int>(var x{i}) when ");
                sb.Append("k > 0");
                for (int i = 0; i < depth; i++) sb.Append(" => true, _ => false })");
                sb.AppendLine(");");
                sb.AppendLine("static bool Id(bool b) => b;");
                break;
            case "option-bare":
                sb.AppendLine("Option<int> o = Some(1);");
                sb.AppendLine("int k = 1;");
                sb.Append("bool r = ");
                for (int i = depth - 1; i >= 0; i--) sb.Append($"o switch {{ Some<int>(var x{i}) when ");
                sb.Append("k > 0");
                for (int i = 0; i < depth; i++) sb.Append(" => true, _ => false }");
                sb.AppendLine(";");
                sb.AppendLine("WriteLine(r);");
                break;
            case "rval-bare":
                sb.AppendLine("int k = 1;");
                sb.Append("bool r = ");
                for (int i = depth - 1; i >= 0; i--) sb.Append("(k + 0) switch { 1 when ");
                sb.Append("k > 0");
                for (int i = 0; i < depth; i++) sb.Append(" => true, _ => false }");
                sb.AppendLine(";");
                sb.AppendLine("WriteLine(r);");
                break;
            // Target-typed classification of a switch or conditional must not
            // re-infer the guards, conditions and arms below it once per
            // overload or per target type (exponential before the memo).
            case "overload-guard":
                sb.AppendLine("Option<int> o = Some(1);");
                sb.AppendLine("bool flag = true;");
                sb.Append("WriteLine(");
                for (int i = depth - 1; i >= 0; i--) sb.Append($"H.Check(o switch {{ Some<int>(var x{i}) when ");
                sb.Append("flag");
                for (int i = 0; i < depth; i++) sb.Append(" => 1, _ => 2 })");
                sb.AppendLine(");");
                sb.AppendLine("static class H");
                sb.AppendLine("{");
                sb.AppendLine("    public static bool Check(int x) => x > 0;");
                sb.AppendLine("    public static bool Check(bool b) => b;");
                sb.AppendLine("    public static bool Check(string s) => s.AsUtf8().Length > 0;");
                sb.AppendLine("}");
                break;
            case "typeless-guard":
                sb.AppendLine("Option<int> o = Some(1);");
                sb.AppendLine("bool flag = true;");
                sb.Append("WriteLine(");
                for (int i = depth - 1; i >= 0; i--) sb.Append($"Check(o switch {{ Some<int>(var x{i}) when ");
                sb.Append("flag");
                for (int i = 0; i < depth; i++) sb.Append(" => None, _ => Some(1) })");
                sb.AppendLine(");");
                sb.AppendLine("static bool Check(Option<int> x) => x.IsSome;");
                break;
            case "typeless-cond":
                sb.AppendLine("bool flag = true;");
                sb.Append("WriteLine(");
                for (int i = 0; i < depth; i++) sb.Append("Check(");
                sb.Append("flag");
                for (int i = 0; i < depth; i++) sb.Append(" ? None : Some(1))");
                sb.AppendLine(");");
                sb.AppendLine("static bool Check(Option<int> x) => x.IsSome;");
                break;
            case "typeless-arm":
                sb.AppendLine("Option<int> o = Some(1);");
                sb.AppendLine("bool flag = true;");
                sb.Append("WriteLine(Check(");
                for (int i = depth - 1; i >= 0; i--) sb.Append($"(o switch {{ Some<int>(var x{i}) when flag => ");
                sb.Append("Some(1)");
                for (int i = 0; i < depth; i++) sb.Append(", _ => None })");
                sb.AppendLine("));");
                sb.AppendLine("static bool Check(Option<int> x) => x.IsSome;");
                break;
            // The same with a pattern or out variable declared at every level,
            // which inference leaves bound for the classification after it.
            case "leaky-cond":
                sb.AppendLine("Option<int> o = Some(1);");
                sb.AppendLine("bool flag = true;");
                sb.Append("WriteLine(");
                for (int i = depth - 1; i >= 0; i--) sb.Append($"Check(o is Some<int>(var a{i}) && ");
                sb.Append("flag");
                for (int i = 0; i < depth; i++) sb.Append(" ? None : Some(1))");
                sb.AppendLine(");");
                sb.AppendLine("static bool Check(Option<int> x) => x.IsSome;");
                break;
            case "leaky-scrutinee":
                sb.AppendLine("bool flag = true;");
                sb.Append("WriteLine(");
                for (int i = depth - 1; i >= 0; i--) sb.Append($"H.Check(H.Get(out int q{i}) switch {{ Some<int>(var x{i}) when ");
                sb.Append("flag");
                for (int i = 0; i < depth; i++) sb.Append(" => 1, _ => 2 })");
                sb.AppendLine(");");
                sb.AppendLine("static class H");
                sb.AppendLine("{");
                sb.AppendLine("    public static Option<int> Get(out int q) { q = 1; return Some(1); }");
                sb.AppendLine("    public static bool Check(int x) => x > 0;");
                sb.AppendLine("    public static bool Check(bool b) => b;");
                sb.AppendLine("    public static bool Check(string s) => s.AsUtf8().Length > 0;");
                sb.AppendLine("}");
                break;
            case "rleak-cond":
                sb.AppendLine("bool flag = true;");
                sb.Append("WriteLine(");
                for (int i = 0; i < depth; i++) sb.Append("Check(");
                sb.Append("Check(flag ? None : Some(1))");
                for (int i = 0; i < depth; i++) sb.Append($" && H.Out(out int q{i}) ? None : Some(1))");
                sb.AppendLine(");");
                sb.AppendLine("static bool Check(Option<int> x) => x.IsSome;");
                sb.AppendLine("static class H");
                sb.AppendLine("{");
                sb.AppendLine("    public static bool Out(out int q) { q = 1; return true; }");
                sb.AppendLine("}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData("option-paren", 98)]
    [InlineData("int-paren", 98)]
    [InlineData("call-wrapped", 98)]
    [InlineData("option-bare", 198)]
    [InlineData("rval-bare", 198)]
    [InlineData("overload-guard", 60)]
    [InlineData("typeless-guard", 60)]
    [InlineData("typeless-cond", 60)]
    [InlineData("typeless-arm", 60)]
    [InlineData("overload-guard", 98)]
    [InlineData("typeless-guard", 98)]
    [InlineData("typeless-cond", 196)]
    [InlineData("typeless-arm", 97)]
    [InlineData("leaky-cond", 60)]
    [InlineData("leaky-scrutinee", 60)]
    [InlineData("rleak-cond", 60)]
    [InlineData("leaky-cond", 196)]
    [InlineData("leaky-scrutinee", 98)]
    [InlineData("rleak-cond", 195)]
    public Task DeepSwitchGuardNesting_MaxNesting_ChecksWithinTimeBound(string shape, int depth) =>
        CheckWithinTimeBoundAsync(BuildNestedGuardSwitch(shape, depth), $"{shape} at depth {depth}");

    // `vib-self check` must accept the program within 45 seconds.
    internal static async Task CheckWithinTimeBoundAsync(string source, string label)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-y4-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string programPath = Path.Combine(temp, "Program.cs");
            File.WriteAllText(programPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var psi = new ProcessStartInfo(Round11AuditAssertions.DotnetExe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(Round11AuditAssertions.SelfCompiler);
            psi.ArgumentList.Add("check");
            psi.ArgumentList.Add(programPath);
            psi.ArgumentList.Add("--sysroot");
            psi.ArgumentList.Add(Path.Combine(Round11AuditAssertions.RepoRoot, "src"));
            var sw = Stopwatch.StartNew();
            using var proc = Process.Start(psi)!;
            Task<string> outTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> errTask = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                Assert.Fail($"vib-self check hung on {label} (> 45 s).");
            }
            sw.Stop();
            string output = await outTask + await errTask;
            Assert.True(proc.ExitCode == 0, $"vib-self check rejected {label}:\n{output}");
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(45), $"vib-self check too slow on {label}: {sw.Elapsed}");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Theory]
    [InlineData("option-paren", 60)]
    [InlineData("int-paren", 60)]
    [InlineData("call-wrapped", 60)]
    [InlineData("option-bare", 60)]
    [InlineData("overload-guard", 8)]
    [InlineData("typeless-guard", 8)]
    [InlineData("typeless-cond", 8)]
    [InlineData("typeless-arm", 8)]
    [InlineData("leaky-cond", 8)]
    [InlineData("leaky-scrutinee", 8)]
    [InlineData("rleak-cond", 7)]
    public Task DeepSwitchGuardNesting_AcceptsWithNativeParity(string shape, int depth) =>
        Round12RuleWitness.AcceptAsync(BuildNestedGuardSwitch(shape, depth), "True\n");
}
