using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class RoslynSubsetGateTests
{
    [Fact]
    public void EveryInstalledRoslynSyntaxKindHasAnExplicitDisposition()
    {
        var unclassifiedKinds = Enum.GetNames<SyntaxKind>().Where(name =>
            SyntaxAndSymbolGatekeeper.ClassifySyntaxKind(Enum.Parse<SyntaxKind>(name)) ==
            SyntaxAndSymbolGatekeeper.SyntaxKindSupport.Unclassified);
        Assert.Empty(unclassifiedKinds);
    }

    [Fact]
    public void AKindMissingFromTheTableIsRejectedByTheProductionGate()
    {
        var unknownKind = (SyntaxKind)ushort.MaxValue;
        Assert.False(Enum.IsDefined(unknownKind));
        Assert.True(SyntaxAndSymbolGatekeeper.RejectsSyntaxKind(unknownKind));
    }

    [Theory]
    [InlineData("WriteLine(0); static class K { public static void F(__arglist) {} }", "__arglist")]
    [InlineData("K.F(__arglist()); static class K { public static void F(__arglist) {} }", "__arglist")]
    [InlineData("K.F(__arglist(7)); K.F(__arglist(8)); static class K { public static void F(__arglist) {} }", "__arglist")]
    [InlineData("WriteLine(0); static class K { public static void F(__arglist) { _ = __arglist; } }", "__arglist")]
    [InlineData("int n = 7; _ = __makeref(n);", "__makeref")]
    [InlineData("int n = 7; _ = __reftype(__makeref(n));", "__reftype")]
    [InlineData("int n = 7; WriteLine(__refvalue(__makeref(n), int));", "__refvalue")]
    [InlineData("int n = 7; __refvalue(__makeref(n), int) = 8; WriteLine(n);", "__refvalue")]
    public async Task TypedReferenceKeywordsReportVib000AtFirstToken(string source, string keyword)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-subset-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SubsetGate", includeStd: true);
            var csharpErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Empty(csharpErrors);

            var checkedProgram = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(checkedProgram.Success);
            Assert.Equal(new[] { "VIB000" }, checkedProgram.Errors.SelectMany(message =>
                Regex.Matches(message, @"\b(?:VIB\d{3}|CS\d{4})\b").Select(match => match.Value)).Distinct());

            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(new[] { "VIB000" }, stage0.Select(diagnostic => diagnostic.Id).Distinct());
            Assert.Equal(new[] { "VIB000" }, analyzer.Select(diagnostic => diagnostic.Id).Distinct());
            string message = $"'{keyword}' is not supported in Vibranium.";
            int start = source.IndexOf(keyword, StringComparison.Ordinal);
            var stage0Keyword = Assert.Single(stage0, diagnostic => diagnostic.Message == message);
            Assert.Equal(start, stage0Keyword.Span.Start);
            Assert.Equal(keyword.Length, stage0Keyword.Span.Length);
            var analyzerKeyword = Assert.Single(analyzer, diagnostic => diagnostic.GetMessage() == message);
            Assert.Equal(start, analyzerKeyword.Location.SourceSpan.Start);
            Assert.Equal(keyword.Length, analyzerKeyword.Location.SourceSpan.Length);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public Task ConditionalLocalFunctionDoesNotSilentlyChangeCallEffects() => CheckAttributeAsync("""
            int n = 0;
            Log(Bump(ref n));
            WriteLine(n);
            Log(Bump(ref n) + Bump(ref n));
            WriteLine(n);
            [System.Diagnostics.Conditional("NEVER")]
            static void Log(int x) { WriteLine(x); }
            static int Bump(ref int n) { n++; return n; }
            """, "System.Diagnostics.ConditionalAttribute", csharpValid: true);

    [Fact]
    public Task UserModuleInitializerIsNotSilentlyIgnored() => CheckAttributeAsync("""
            WriteLine("main");
            static class M
            {
                [System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Init() { WriteLine("init"); }
            }
            """, "System.Runtime.CompilerServices.ModuleInitializerAttribute", csharpValid: true);

    [Fact]
    public Task OverloadResolutionPriorityDoesNotSilentlySelectDifferentOverloads() => CheckAttributeAsync("""
            using System.Runtime.CompilerServices;
            int value = 11;
            WriteLine(Probe.Choose(value));
            static class Probe {
                public static int Choose(int value) => 1;
                [OverloadResolutionPriority(1)] public static int Choose(long value) => 2;
            }
            """, "System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute", csharpValid: true);

    [Fact]
    public Task ExtendedLayoutCUnionDoesNotSilentlyUseIndependentFields() => CheckAttributeAsync("""
            using System.Runtime.InteropServices;
            Probe pair = default;
            pair.First = 49;
            WriteLine(pair.Second);
            [ExtendedLayout(ExtendedLayoutKind.CUnion)] struct Probe
            {
                public int First;
                public int Second;
            }
            """, "System.Runtime.InteropServices.ExtendedLayoutAttribute", csharpValid: true);

    public static TheoryData<string, string> UnsupportedAttributes => new()
    {
        {
            "using Skip = global::System.Diagnostics.ConditionalAttribute; WriteLine(0); [Skip(\"NEVER\")] static void Log() {}",
            "System.Diagnostics.ConditionalAttribute"
        },
        {
            "WriteLine(0); [global::System.Diagnostics.ConditionalAttribute(\"NEVER\")] static void Log() {}",
            "System.Diagnostics.ConditionalAttribute"
        },
        {
            "WriteLine(0); [System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] sealed class K {}",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "WriteLine(0); struct K { [System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] public K(int n) {} }",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "WriteLine(0); struct K { public int Value { [System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] get => 7; } }",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "WriteLine(F(7)); static int F([System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] int x) => x;",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "WriteLine(F()); [return: System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] static int F() => 7;",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "WriteLine(F(7)); static T F<[System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] T>(T x) => x;",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "_ = (System.Func<int>)([System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] () => 7);",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "WriteLine(0); struct K { [field: System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] public int Value { get; set; } }",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "WriteLine(0); struct K { [property: System.Diagnostics.CodeAnalysis.SuppressMessage(\"A\", \"B\")] public int Value => 7; }",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        },
        {
            "WriteLine(0); [System.Runtime.CompilerServices.CollectionBuilder(typeof(K), \"Create\")] struct K {}",
            "System.Runtime.CompilerServices.CollectionBuilderAttribute"
        },
        {
            "WriteLine(0); [System.AttributeUsage(System.AttributeTargets.Method)] sealed class UserAttribute : System.Attribute {}",
            "System.AttributeUsageAttribute"
        },
        {
            "WriteLine(0); [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)] sealed class K {}",
            "System.ComponentModel.EditorBrowsableAttribute"
        },
        {
            "WriteLine(0); [System.Obsolete(\"unused\", error: true)] sealed class K {}",
            "System.ObsoleteAttribute"
        },
        {
            "WriteLine(0); [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct K { public int N; }",
            "System.Runtime.InteropServices.StructLayoutAttribute"
        },
        {
            "WriteLine(0); [System.Diagnostics.CodeAnalysis.DoesNotReturn] static void F() { VibraniumRuntime.Panic(\"stop\"); }",
            "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute"
        },
        {
            "WriteLine(F()); [System.Runtime.CompilerServices.MethodImpl] static int F() => 7;",
            "System.Runtime.CompilerServices.MethodImplAttribute"
        },
        {
            "WriteLine(F()); [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] static int F() => 7;",
            "System.Runtime.CompilerServices.MethodImplAttribute"
        },
        {
            "WriteLine(F()); [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] static int F() => 7;",
            "System.Runtime.CompilerServices.MethodImplAttribute"
        },
    };

    [Theory]
    [MemberData(nameof(UnsupportedAttributes))]
    public Task ClosedAttributesRejectEverySourceApplication(string source, string type) =>
        CheckAttributeAsync(source, type, csharpValid: true);

    [Fact]
    public Task AssemblyMetadataOnNonAssemblyTargetIsRejected() =>
        CheckAttributeAsync("WriteLine(0); [System.Reflection.AssemblyTitle(\"Title\")] sealed class K {}",
            "System.Reflection.AssemblyTitleAttribute", csharpValid: false);

    private static async Task CheckAttributeAsync(string source, string type, bool csharpValid)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-subset-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SubsetGate", includeStd: true);
            var csharpErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (csharpValid)
            {
                Assert.Empty(csharpErrors);
            }
            else
            {
                Assert.Contains(csharpErrors, diagnostic => diagnostic.Id == "CS0592");
            }
            var checkedProgram = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(checkedProgram.Success);
            if (csharpValid)
            {
                Assert.Equal(new[] { "VIB000" }, checkedProgram.Errors.SelectMany(message =>
                    Regex.Matches(message, @"\b(?:VIB\d{3}|CS\d{4})\b").Select(match => match.Value)).Distinct());
            }
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(new[] { "VIB000" }, stage0.Select(diagnostic => diagnostic.Id).Distinct());
            Assert.Equal(new[] { "VIB000" }, analyzer.Select(diagnostic => diagnostic.Id).Distinct());
            string message = $"Attribute '{type}' is not supported at this location in Vibranium.";
            var stage0Attribute = Assert.Single(stage0, diagnostic => diagnostic.Message == message);
            var analyzerAttribute = Assert.Single(analyzer, diagnostic => diagnostic.GetMessage() == message);
            Assert.Equal(stage0Attribute.Span.Start, analyzerAttribute.Location.SourceSpan.Start);
            Assert.Equal(stage0Attribute.Span.Length, analyzerAttribute.Location.SourceSpan.Length);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public Task AllowedMutAndUnscopedRefPreserveNativeOutput() => Round12RuleWitness.AcceptAsync("""
        using System.Diagnostics.CodeAnalysis;
        var c = new C(7);
        c.Add(3);
        S s = new S(c.Value);
        ref int x = ref s.ValueRef();
        x += 4;
        WriteLine(s.Value);
        sealed class C(int n)
        {
            public int Value = n;
            [Mut] public void Add(int d) { Value += d; }
        }
        struct S(int n)
        {
            public int Value = n;
            [UnscopedRef] public ref int ValueRef() => ref Value;
        }
        """, "14\n");

    [Fact]
    public Task KeywordSpellingsInCommentsStringsAndIdentifiersAreNotBanned() => Round12RuleWitness.AcceptAsync("""
        // __arglist __makeref __reftype __refvalue
        int __arglistValue = 7;
        WriteLine("__makeref __reftype __refvalue");
        WriteLine(__arglistValue);
        """, "__makeref __reftype __refvalue\n7\n");


    [Theory]
    [InlineData("using Mine = N; WriteLine(Mine::C.Get()); namespace N { static class C { public static int Get() => 7; } }", "User alias-qualified names")]
    [InlineData("WriteLine(0); sealed class K { private volatile int n; public K(int x) { n = x; } public int Read() => n; }", "'volatile' fields")]
    [InlineData("WriteLine(0); sealed class K { public required int X; }", "'required' members")]
    [InlineData("WriteLine(0); struct K { private int x; public required int X { get => x; set { x = value; } } }", "'required' members")]
    [InlineData("WriteLine(0); sealed class K { public required int X; [System.Diagnostics.CodeAnalysis.SetsRequiredMembers] public K(int n) { X = n; } }", "'required' members")]
    [InlineData("var x = new C(); _ = x as C; WriteLine(7); class C { }", "'as' is not supported")]
    [InlineData("WriteLine(C.Get(7)); static class C { public static T Get<T>(T x) where T : notnull => x; }", "The 'notnull' constraint")]
    public Task ConfirmedSyntaxBoundariesRejectInBothRoslynFrontends(string source, string message) =>
        CheckUnsupportedSyntaxAsync(source, message, csharpValid: true);

    [Theory]
    [InlineData("[assembly: System.Reflection.AssemblyVersion(\"1.0\")] WriteLine(0);", "Assembly-level attributes", false)]
    [InlineData("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"X\")] WriteLine(0);", "Assembly-level attributes", false)]
    [InlineData("[module: System.Runtime.CompilerServices.SkipLocalsInit] WriteLine(0);", "Module-level attributes", true)]
    public Task AssemblyAndModuleTargetsAreNotAllowedInUserCode(string source, string message, bool allowUnsafe) =>
        CheckUnsupportedSyntaxAsync(source, message, csharpValid: true, allowUnsafe: allowUnsafe);

    [Fact]
    public Task ExternAliasIsOutsideTheSubset() =>
        CheckUnsupportedSyntaxAsync("extern alias Missing; WriteLine(0);", "'extern alias' directives", csharpValid: false);

    private static async Task CheckUnsupportedSyntaxAsync(
        string source, string message, bool csharpValid, bool allowUnsafe = false,
        string? expectedCSharpError = null, string? anchor = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-subset-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SubsetGate", includeStd: true);
            if (allowUnsafe)
            {
                // SkipLocalsInit itself requires /unsafe in C#. Permit that option
                // in the harness so the shared gate, not CS0227, makes the refusal.
                compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
            }
            var csharpErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Equal(csharpValid, !csharpErrors.Any());
            if (expectedCSharpError is not null)
            {
                Assert.Equal(new[] { expectedCSharpError }, csharpErrors.Select(diagnostic => diagnostic.Id).Distinct());
            }
            var checkedProgram = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
            Assert.False(checkedProgram.Success);
            if (csharpValid && anchor is not null)
            {
                Assert.Equal(new[] { "VIB000" }, checkedProgram.Errors.SelectMany(message =>
                    Regex.Matches(message, @"\b(?:VIB\d{3}|CS\d{4})\b").Select(match => match.Value)).Distinct());
            }
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(new[] { "VIB000" }, stage0.Select(diagnostic => diagnostic.Id).Distinct());
            Assert.Equal(new[] { "VIB000" }, analyzer.Select(diagnostic => diagnostic.Id).Distinct());
            var stage0Diagnostic = Assert.Single(stage0, diagnostic => diagnostic.Message.StartsWith(message, StringComparison.Ordinal));
            if (message == "Assembly-level attributes")
            {
                Assert.Contains("remove the attribute.", stage0Diagnostic.Message, StringComparison.Ordinal);
            }
            var analyzerDiagnostic = Assert.Single(analyzer, diagnostic => diagnostic.GetMessage().StartsWith(message, StringComparison.Ordinal));
            Assert.Equal(stage0Diagnostic.Span.Start, analyzerDiagnostic.Location.SourceSpan.Start);
            if (anchor is not null)
            {
                Assert.Equal(source.IndexOf(anchor, StringComparison.Ordinal), stage0Diagnostic.Span.Start);
                Assert.Equal(anchor.Length, stage0Diagnostic.Span.Length);
                Assert.Equal(anchor.Length, analyzerDiagnostic.Location.SourceSpan.Length);
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // A concrete indexer cannot have a semicolon accessor. Its invalid rows
    // still verify the init boundary on the shared Roslyn checker and Analyzer.
    [Theory]
    [InlineData("class-property-auto", "WriteLine(7); class S { public int V { get; init; } }", null)]
    [InlineData("class-property-arrow", "WriteLine(new S(7).V); class S { private int x; public int V { get => x; init => x = value; } public S(int value) { x = value; } }", null)]
    [InlineData("class-property-block", "WriteLine(new S(7).V); class S { private int x; public int V { get => x; init { x = value; } } public S(int value) { x = value; } }", null)]
    [InlineData("class-indexer-auto", "WriteLine(7); class S { private int x; public int this[int i] { get => x; init; } public S(int value) { x = value; } }", "CS0501")]
    [InlineData("class-indexer-arrow", "WriteLine(new S(7)[0]); class S { private int x; public int this[int i] { get => x; init => x = value; } public S(int value) { x = value; } }", null)]
    [InlineData("class-indexer-block", "WriteLine(new S(7)[0]); class S { private int x; public int this[int i] { get => x; init { x = value; } } public S(int value) { x = value; } }", null)]
    [InlineData("class-indexer-private-init", "WriteLine(new S(7)[0]); class S { private int x; public int this[int i] { get => x; private init => x = value; } public S(int value) { x = value; } }", null)]
    [InlineData("class-indexer-init-only", "WriteLine(7); class S { private int x; public int this[int i] { init { x = value; } } public S(int value) { x = value; } }", null)]
    [InlineData("struct-property-auto", "WriteLine(7); struct S { public int V { get; init; } }", null)]
    [InlineData("struct-property-arrow", "WriteLine(new S(7).V); struct S { private int x; public int V { get => x; init => x = value; } public S(int value) { x = value; } }", null)]
    [InlineData("struct-property-block", "WriteLine(new S(7).V); struct S { private int x; public int V { get => x; init { x = value; } } public S(int value) { x = value; } }", null)]
    [InlineData("struct-indexer-auto", "WriteLine(7); struct S { private int x; public int this[int i] { get => x; init; } public S(int value) { x = value; } }", "CS0501")]
    [InlineData("struct-indexer-arrow", "WriteLine(new S(7)[0]); struct S { private int x; public int this[int i] { get => x; init => x = value; } public S(int value) { x = value; } }", null)]
    [InlineData("struct-indexer-block", "WriteLine(new S(7)[0]); struct S { private int x; public int this[int i] { get => x; init { x = value; } } public S(int value) { x = value; } }", null)]
    [InlineData("struct-indexer-private-init", "WriteLine(new S(7)[0]); struct S { private int x; public int this[int i] { get => x; private init => x = value; } public S(int value) { x = value; } }", null)]
    [InlineData("struct-indexer-init-only", "WriteLine(7); struct S { private int x; public int this[int i] { init { x = value; } } public S(int value) { x = value; } }", null)]
    [InlineData("class-property-auto-ctor-use", "WriteLine(new S(7).V); class S { public int V { get; init; } public S(int value) { V = value; } }", null)]
    [InlineData("struct-property-auto-ctor-use", "WriteLine(new S(7).V); struct S { public int V { get; init; } public S(int value) { V = value; } }", null)]
    public Task ExplicitInitIsRejectedAtTheAccessor(string shape, string source, string? csharpError)
    {
        _ = shape;
        return CheckUnsupportedSyntaxAsync(source, "'init' accessors are not supported", csharpValid: csharpError is null,
            expectedCSharpError: csharpError, anchor: "init");
    }

    [Fact]
    public Task GlobalQualificationPreservesNativeOutput() => Round12RuleWitness.AcceptAsync(
        "WriteLine(global::System.Int32.MaxValue);", "2147483647\n");


    // Twenty SDK-valid rows from the 120-valid-row evidence matrix: all receivers,
    // all eight forms, and all three syntactic positions without 120 compiles here.
    [Theory]
    [InlineData("Vec<int> value = [7, 8];\nif (value is []) WriteLine(11); else WriteLine(13);")]
    [InlineData("Vec<int> value = [7, 8];\nswitch (value) { case [7]: WriteLine(11); break; default: WriteLine(13); break; }")]
    [InlineData("Vec<int> value = [7, 8];\nWriteLine(value switch { [7, 8] => 11, _ => 13 });")]
    [InlineData("Vec<int> value = [7, 8];\nif (value is [var a, var b]) WriteLine(11); else WriteLine(13);")]
    [InlineData("Span<int> value = [7, 8];\nswitch (value) { case [7, ..]: WriteLine(11); break; default: WriteLine(13); break; }")]
    [InlineData("Span<int> value = [7, 8];\nWriteLine(value switch { [.., 8] => 11, _ => 13 });")]
    [InlineData("Span<int> value = [7, 8];\nif (value is [7, .. var rest]) WriteLine(11); else WriteLine(13);")]
    [InlineData("Span<int> value = [7, 8];\nswitch (value) { case [_, _]: WriteLine(11); break; default: WriteLine(13); break; }")]
    [InlineData("ReadOnlySpan<int> value = [7, 8];\nWriteLine(value switch { [] => 11, _ => 13 });")]
    [InlineData("ReadOnlySpan<int> value = [7, 8];\nif (value is [7, 8]) WriteLine(11); else WriteLine(13);")]
    [InlineData("ReadOnlySpan<int> value = [7, 8];\nswitch (value) { case [7, ..]: WriteLine(11); break; default: WriteLine(13); break; }")]
    [InlineData("ReadOnlySpan<int> value = [7, 8];\nWriteLine(value switch { [7, .. var rest] => 11, _ => 13 });")]
    [InlineData("ReadOnlySpan<byte> value = [7, 8];\nif (value is [7]) WriteLine(11); else WriteLine(13);")]
    [InlineData("ReadOnlySpan<byte> value = [7, 8];\nWriteLine(value switch { [var a, var b] => 11, _ => 13 });")]
    [InlineData("ReadOnlySpan<byte> value = [7, 8];\nswitch (value) { case [.., 8]: WriteLine(11); break; default: WriteLine(13); break; }")]
    [InlineData("ReadOnlySpan<byte> value = [7, 8];\nif (value is [_, _]) WriteLine(11); else WriteLine(13);")]
    [InlineData("string value = \"\\a\\b\";\nswitch (value) { case ['\\a', '\\b']: WriteLine(11); break; default: WriteLine(13); break; }")]
    [InlineData("string value = \"\\a\\b\";\nWriteLine(value switch { [var a, var b] => 11, _ => 13 });")]
    [InlineData("string value = \"\\a\\b\";\nif (value is [.., '\\b']) WriteLine(11); else WriteLine(13);")]
    [InlineData("string value = \"\\a\\b\";\nswitch (value) { case ['\\a', .. var rest]: WriteLine(11); break; default: WriteLine(13); break; }")]
    [InlineData("Span<int> s = [1]; if (s is [1]) WriteLine(1);")]
    [InlineData("Span<int> a = [2]; if (a is [2]) WriteLine(2); if (a is [3]) WriteLine(3);")]
    [InlineData("ReadOnlySpan<int> s = [2, 3]; if (s is [.. [2, 3]]) WriteLine(2);")]
    [InlineData("Span<int> s = [2, 3]; int n = 2; if (n > 1 && s is not ([2, 3])) WriteLine(n);")]
    [InlineData("WriteLine(Probe()); static int Probe() { ReadOnlySpan<int> s = [1, 2]; return s switch { ([.., 2]) => 8, _ => 9 }; }")]
    [InlineData("WriteLine(K.Probe()); static class K { public static int Probe() { ReadOnlySpan<int> s = [1, 2]; return (s is [1, 2]) ? 8 : 9; } }")]
    [InlineData("using R x = new R(); ReadOnlySpan<int> s = [2]; if (s is [2]) WriteLine(2); sealed class R : IDisposable { public void Dispose() {} }")]
    [InlineData("var xs = new Items(); if (xs is [var item]) WriteLine(item.N); sealed class Items { public int Length => 1; public C this[int i] => new C(i); } sealed class C(int n) { public int N = n; }")]
    public Task ListPatternsRejectEveryCheckedPosition(string source) => CheckExactRoundTwoDiagnosticsAsync(source, root =>
    {
        var nodes = root.DescendantNodes().ToArray();
        var listPattern = ("List patterns are not supported in Vibranium v0.2; compare the length and index the elements.",
            nodes.OfType<ListPatternSyntax>().First().OpenBracketToken.Span);
        // String switches are independently banned, including those with no list pattern.
        if (source.StartsWith("string value", StringComparison.Ordinal) &&
            nodes.SingleOrDefault(node => node is SwitchStatementSyntax or SwitchExpressionSyntax) is { } stringSwitch)
        {
            var scrutinee = stringSwitch is SwitchStatementSyntax statement
                ? statement.Expression : ((SwitchExpressionSyntax)stringSwitch).GoverningExpression;
            return [("Switch on 'string' is banned in Vibranium; match UTF-8 bytes via '.AsUtf8()' instead.", scrutinee.Span), listPattern];
        }
        return [listPattern];
    });

    [Theory]
    [InlineData("public static bool F(int[] items) => items is [1];")]
    [InlineData("public static bool F(int[] items) => items switch { [1] => true, _ => false };")]
    [InlineData("public static bool F(int[] items) { switch (items) { case [1]: return true; default: return false; } }")]
    public Task ListPatternOnArrayRetainsTheIndependentArrayTypeDiagnostic(string method) => CheckExactRoundTwoDiagnosticsAsync(
        "WriteLine(2); static class K { " + method + " }", root =>
        [("Managed array types 'T[]' are not supported in Vibranium; use Vec<T>, Span<T>, or ReadOnlySpan<T>.",
            root.DescendantNodes().OfType<ArrayTypeSyntax>().Single().Span),
         ("List patterns are not supported in Vibranium v0.2; compare the length and index the elements.",
            root.DescendantNodes().OfType<ListPatternSyntax>().Single().OpenBracketToken.Span)]);

    [Theory]
    [InlineData("label: WriteLine(2);")]
    [InlineData("#pragma warning disable CS0164\nlabel: WriteLine(2);")]
    [InlineData("#pragma warning disable CS0164\nstatic class K { public static void Main() { label: WriteLine(2); } }")]
    [InlineData("F(); static void F() { label: WriteLine(2); }")]
    [InlineData("{ label: WriteLine(2); }")]
    [InlineData("int n = 1; switch (n) { case 1: label: WriteLine(2); break; }")]
    public Task LabeledStatementsRejectAtTheLabelToken(string source) => CheckExactRoundTwoDiagnosticsAsync(source, root =>
        [("Labeled statements are not supported in Vibranium v0.2 ('goto' is not supported).",
            root.DescendantNodes().OfType<LabeledStatementSyntax>().Single().Identifier.Span)]);

    [Fact]
    public Task LabelAndGotoBothRemainRejected() => CheckExactRoundTwoDiagnosticsAsync("goto label; label: WriteLine(2);", root =>
        [("Unstructured 'goto' statements are banned in Vibranium.", root.DescendantNodes().OfType<GotoStatementSyntax>().Single().Span),
         ("Labeled statements are not supported in Vibranium v0.2 ('goto' is not supported).",
             root.DescendantNodes().OfType<LabeledStatementSyntax>().Single().Identifier.Span)]);

    [Theory]
    [InlineData("#pragma warning disable CS0657, CS0658\nWriteLine(3); [field: Unknown] sealed class C {}", "Unknown")]
    [InlineData("WriteLine(3); [field: Unknown] sealed class C {}", "Unknown")]
    [InlineData("WriteLine(3); [property: Unknown] sealed class C {}", "Unknown")]
    [InlineData("WriteLine(3); [type: Unknown] sealed class C {}", "Unknown", "CS0246")]
    [InlineData("WriteLine(F()); [type: Unknown] static int F() => 7;", "Unknown")]
    [InlineData("WriteLine(3); [event: Unknown] sealed class C {}", "Unknown")]
    [InlineData("WriteLine(3); [return: Unknown] sealed class C {}", "Unknown")]
    [InlineData("WriteLine(3); [madeup: Unknown] sealed class C {}", "Unknown")]
    [InlineData("WriteLine(3); static class C { [field: Unknown] public static int F() => 7; }", "Unknown")]
    [InlineData("WriteLine(F()); [field: global::Vibranium.MutAttribute] static int F() => 7;", "global::Vibranium.MutAttribute")]
    [InlineData("using M = Vibranium.MutAttribute; WriteLine(3); static class C { [property: M] public static int F() => 7; }", "M")]
    [InlineData("WriteLine(3); [return: LifetimeOf(\"x\")] sealed class C {}", "LifetimeOf")]
    [InlineData("WriteLine(3); [property: System.Diagnostics.CodeAnalysis.UnscopedRef] struct C {}", "System.Diagnostics.CodeAnalysis.UnscopedRef")]
    [InlineData("WriteLine(3); [Unknown] sealed class C {}", "Unknown", "CS0246")]
    [InlineData("using MissingAlias = NoSuchAttribute; WriteLine(3); [MissingAlias] sealed class C {}", "MissingAlias", "CS0246")]
    [InlineData("using Mut = Vibranium.MutAttribute; using MutAttribute = System.ObsoleteAttribute; WriteLine(F()); [Mut] static int F() => 7;", "Mut", "CS1614")]
    [InlineData("WriteLine(3); [System.String] sealed class C {}", "System.String", "CS0616")]
    public Task InvalidOrUnresolvedAttributeTargetsFailClosed(string source, string attributeName, string? csharpError = null) =>
        CheckExactRoundTwoDiagnosticsAsync(source, root =>
        {
            var attribute = Assert.Single(root.DescendantNodes().OfType<AttributeSyntax>());
            Assert.Equal(attributeName, attribute.Name.ToString());
            const string lifetime = "'[LifetimeOf]' is only supported as '[return: LifetimeOf(...)]' on methods in Vibranium.";
            string message = attributeName switch
            {
                "LifetimeOf" => lifetime,
                "global::Vibranium.MutAttribute" or "M" => "Attribute 'Vibranium.MutAttribute' is not supported at this location in Vibranium.",
                "System.Diagnostics.CodeAnalysis.UnscopedRef" => "Attribute 'System.Diagnostics.CodeAnalysis.UnscopedRefAttribute' is not supported at this location in Vibranium.",
                _ => $"Attribute '{attributeName}' does not resolve to a supported attribute at this location in Vibranium.",
            };
            return [(message, attribute.Span)];
        }, csharpError);

    [Theory]
    [InlineData("[assembly: System.Reflection.AssemblyCompany(\"test\")] WriteLine(3);", "Demo.AssemblyInfo.cs")]
    [InlineData("[assembly: global::System.Runtime.Versioning.TargetFramework(\".NETCoreApp,Version=v11.0\")] WriteLine(3);", ".NETCoreApp,Version=v11.0.AssemblyAttributes.cs")]
    [InlineData("using Framework = System.Runtime.Versioning.TargetFrameworkAttribute; [assembly: Framework(\".NETCoreApp,Version=v11.0\")] WriteLine(3);", "Program.cs")]
    public Task AuthoredAssemblyMetadataHasNoSdkPropertyAdvice(string source, string fileName) =>
        CheckExactRoundTwoDiagnosticsAsync(source, root =>
            [("Assembly-level attributes are not supported in Vibranium v0.2; remove the attribute.",
                root.DescendantNodes().OfType<AttributeSyntax>().Single().Span)], fileName: fileName);

    [Theory]
    [InlineData("field", "WriteLine(new C(7).X); [{target}: Mut] sealed class C(int x) { public int X = x; }", "Vibranium.MutAttribute")]
    [InlineData("param", "using System.Diagnostics.CodeAnalysis; WriteLine(new S(7).X); [{target}: UnscopedRef] struct S(int x) { public int X = x; }", "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute")]
    [InlineData("return", "WriteLine(new C(7).X); [{target}: LifetimeOf(\"x\")] sealed class C(int x) { public int X = x; }", "Vibranium.LifetimeOfAttribute")]
    [InlineData("property", "F(); WriteLine(7); [{target}: Mut] static void F() { }", "Vibranium.MutAttribute")]
    [InlineData("event", "F(); [{target}: Mut] static void F() { WriteLine(7); }", "Vibranium.MutAttribute")]
    [InlineData("type", "F(); [{target}: Mut] static void F() { WriteLine(7); }", "Vibranium.MutAttribute")]
    [InlineData("typevar", "F(); [{target}: Mut] static void F() { WriteLine(7); }", "Vibranium.MutAttribute")]
    public async Task PlainAndVerbatimTargetsMustAttachToAnAllowedDeclaration(string target, string form, string type)
    {
        string message = type == "Vibranium.LifetimeOfAttribute"
            ? "'[LifetimeOf]' is only supported as '[return: LifetimeOf(...)]' on methods in Vibranium."
            : $"Attribute '{type}' is not supported at this location in Vibranium.";
        foreach (string spelling in new[] { target, "@" + target })
        {
            await CheckExactRoundTwoDiagnosticsAsync(form.Replace("{target}", spelling, StringComparison.Ordinal), root =>
                [(message, root.DescendantNodes().OfType<AttributeSyntax>().Single().Span)]);
        }
    }

    [Theory]
    [InlineData("return", "Span<int> s = [4]; WriteLine(K.First(s, s)[0]); static class K { [{target}: LifetimeOf(\"a\")] public static ReadOnlySpan<int> First(ReadOnlySpan<int> a, ReadOnlySpan<int> b) => a; }")]
    [InlineData("param", "using System.Diagnostics.CodeAnalysis; Init(out var x); WriteLine(x); static void Init([{target}: UnscopedRef] out int p) { p = 4; }")]
    [InlineData("method", "using M = Vibranium.MutAttribute; var c = new C(1); c.Add(3); WriteLine(c.Value); sealed class C(int n) { public int Value = n; [{target}: M] public void Add(int d) { Value += d; } }")]
    [InlineData("property", "using System.Diagnostics.CodeAnalysis; S s = new S(4); WriteLine(s.ValueRef); struct S(int n) { public int Value = n; [{target}: UnscopedRef] public ref int ValueRef => ref Value; }")]
    public async Task PlainAndVerbatimTargetsAcceptTheAttachedAttribute(string target, string form)
    {
        foreach (string spelling in new[] { target, "@" + target })
        {
            await CheckExactRoundTwoDiagnosticsAsync(form.Replace("{target}", spelling, StringComparison.Ordinal), _ => []);
        }
    }

    [Theory]
    [InlineData("assembly", "System.Reflection.AssemblyCompany(\"test\")", "Assembly-level attributes are not supported in Vibranium v0.2; remove the attribute.")]
    [InlineData("module", "System.Runtime.InteropServices.DefaultCharSet(System.Runtime.InteropServices.CharSet.Unicode)", "Module-level attributes are not supported in Vibranium v0.2; remove the attribute.")]
    public async Task PlainAndVerbatimAssemblyAndModuleTargetsRemainBanned(string target, string attribute, string message)
    {
        foreach (string spelling in new[] { target, "@" + target })
        {
            await CheckExactRoundTwoDiagnosticsAsync($"[{spelling}: {attribute}] WriteLine(7);", root =>
                [(message, root.DescendantNodes().OfType<AttributeSyntax>().Single().Span)]);
        }
    }

    [Theory]
    [InlineData("foreach (scoped ReadOnlySpan<byte> line in \"ab\\ncde\".Lines()) { WriteLine(line.Length); }")]
    [InlineData("Span<int> s = [10, 20]; foreach (scoped ref int x in s) { x += 1; } WriteLine(s[0]);")]
    [InlineData("ReadOnlySpan<int> s = [10, 20]; foreach (scoped ref readonly int x in s) { WriteLine(x); }")]
    [InlineData("F(); static void F() { foreach (scoped var line in \"ab\\ncde\".Lines()) WriteLine(line.Length); }")]
    public Task ScopedForeachIterationVariablesAreRejected(string source) => CheckExactRoundTwoDiagnosticsAsync(source, root =>
        [("'scoped' iteration variables are not supported in Vibranium v0.2; omit 'scoped' from the 'foreach' variable.",
            root.DescendantNodes().OfType<ScopedTypeSyntax>().Single().ScopedKeyword.Span)]);

    [Theory]
    [InlineData("foreach (ReadOnlySpan<byte> line in \"ab\\ncde\".Lines()) { WriteLine(line.Length); }")]
    [InlineData("Span<int> s = [10, 20]; foreach (ref int x in s) { x += 1; } WriteLine(s[0]);")]
    [InlineData("ReadOnlySpan<int> s = [10, 20]; foreach (ref readonly int x in s) { WriteLine(x); }")]
    public Task OrdinaryForeachIterationVariablesRemainSupported(string source) => CheckExactRoundTwoDiagnosticsAsync(source, _ => []);

    [Fact]
    public Task ScopedForeachStillRejectedBySelf() => Round12RuleWitness.RejectAsync(
        "foreach (scoped ReadOnlySpan<byte> line in \"ab\\ncde\".Lines()) { WriteLine(line.Length); }",
        "VIB000", banned: true, exact: true);

    [Fact]
    public Task OrdinaryForeachRemainsSupportedBySelf() => Round12RuleWitness.AcceptAsync(
        "foreach (ReadOnlySpan<byte> line in \"ab\\ncde\".Lines()) { WriteLine(line.Length); }", "2\n3\n");

    private static async Task CheckExactRoundTwoDiagnosticsAsync(string source,
        Func<SyntaxNode, (string Message, TextSpan Span)[]> expectedDiagnostics, string? csharpError = null, string fileName = "Program.cs")
    {
        string temp = Path.Combine(Path.GetTempPath(), $"vibranium-subset-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string path = Path.Combine(temp, fileName);
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SubsetGate", includeStd: true);
            var csharpErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (csharpError is null) Assert.Empty(csharpErrors);
            else Assert.Contains(csharpErrors, diagnostic => diagnostic.Id == csharpError);

            var expected = expectedDiagnostics(compilation.SyntaxTrees.Single(tree => tree.FilePath == path).GetRoot())
                .Select(item => (Id: "VIB000", item.Message, item.Span.Start, item.Span.Length)).ToArray();
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            Assert.Equal(expected, stage0.OrderBy(diagnostic => diagnostic.Span.Start)
                .Select(diagnostic => (diagnostic.Id, diagnostic.Message, diagnostic.Span.Start, diagnostic.Span.Length)));
            Assert.Equal(expected, analyzer.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
                .Select(diagnostic => (diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)));
            if (csharpError is null)
            {
                var checkedProgram = VibraniumCompilerDriver.CheckAndEmitIr(compilation);
                Assert.Equal(expected.Length == 0, checkedProgram.Success);
                Assert.Equal(expected.Select(item => item.Id), checkedProgram.Errors.SelectMany(message =>
                    Regex.Matches(message, @"\b(?:VIB\d{3}|CS\d{4})\b").Select(match => match.Value)));
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public Task ListPatternStillRejectedBySelf() => Round12RuleWitness.RejectAsync(
        "ReadOnlySpan<int> span = [7, 8]; if (span is [7, ..]) WriteLine(7);", "VIB000", banned: true, exact: true);

    [Fact]
    public Task ExplicitLengthAndIndexPreserveNativeOutput() => Round12RuleWitness.AcceptAsync(
        "ReadOnlySpan<int> span = [7, 8]; if (span.Length == 2 && span[0] == 7) WriteLine(7);", "7\n");

    // Minimal positive controls from the pre14 syntax inventory (with verified
    // .NET/native agreement before and after the gate).
    [Theory]
    [InlineData("int n = -1; WriteLine(n >>> 1);", "2147483647\n")]
    [InlineData("int n = 7; ; WriteLine(n);", "7\n")]
    [InlineData("int n = 7; scoped ref int r = ref n; r += 2; WriteLine(n);", "9\n")]
    [InlineData("WriteLine(\"\"\"word\"\"\"u8.Length);", "4\n")]
    public Task EvidenceBackedSyntaxRemainsSupported(string source, string expected) =>
        Round12RuleWitness.AcceptAsync(source, expected);

}
