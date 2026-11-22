using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>One row of the v0.2 feature catalog.</summary>
/// <param name="Tag">Generator tag (computed from the AST, see Ast.cs).</param>
/// <param name="Kind">supported | near-miss | banned | cut | vib-invalid (C#-valid shape that SPEC rejects; the
/// frontends must agree on the IDs, and acceptance by every frontend is counted per tag as a lead to vet) |
/// vib-reject (activation profile: C#-valid shape that SPEC rejects with the IDs in <c>Expected</c>; acceptance by
/// every frontend is a <c>spec-accepted</c> disagreement and a missing expected ID a <c>spec-wrong-id</c>).</param>
/// <param name="Spec">SPEC.md section(s).</param>
/// <param name="Expected">For banned/cut/vib-reject rows: the documented diagnostic(s), any of which satisfies the row.</param>
/// <param name="Anchor">Generator production that targets the row.</param>
internal sealed record Feature(string Tag, string Kind, string Area, string Description, string Spec, string[] Expected, string Anchor);

/// <summary>The explicit v0.2 scope definition the generator draws from (docs/CONFORMANCE.md is derived from it).</summary>
internal static class Features
{
    private static Feature S(string tag, string area, string desc, string spec, string anchor) => new(tag, "supported", area, desc, spec, [], anchor);
    private static Feature N(string tag, string area, string desc, string spec, string anchor) => new(tag, "near-miss", area, desc, spec, [], anchor);
    private static Feature B(string tag, string area, string desc, string spec, params string[] expected) => new(tag, "banned", area, desc, spec, expected, "Banned");
    private static Feature C(string tag, string area, string desc, string anchor) => new(tag, "cut", area, desc, "user-approved v0.2 cut (not yet in SPEC)", ["VIB000"], anchor);

    // extended profile (--profile extended): supported rows (X) and C#-valid, Vibranium-invalid rows (V, expected ID in the text)
    private static Feature X(string tag, string area, string desc, string spec, string anchor) => new(tag, "supported", area, desc, spec, [], anchor + " (extended)");
    private static Feature V(string tag, string area, string desc, string spec, string expect, string anchor) => new(tag, "vib-invalid", area, desc + " — expected " + expect, spec, [], anchor + " (extended)");

    // activation profile (--profile activation): supported rows (A) and C#-valid rows that SPEC rejects with a known ID (R)
    private static Feature A(string tag, string area, string desc, string spec, string anchor) => new(tag, "supported", area, desc, spec, [], anchor + " (activation)");
    private static Feature R(string tag, string area, string desc, string spec, string expect, string anchor) => new(tag, "vib-reject", area, desc, spec, [expect], anchor + " (activation)");
    /// <summary>A shape the generator produced before the C10–C20 cuts; kept so old corpus tags stay catalogued.</summary>
    private static Feature Retired(string tag, string area, string desc, string cut, string note) => new(tag, "retired", area, desc + " — retired: cut " + cut + "; " + note, "user-approved v0.2 cut " + cut, [], "none (retired)");
    /// <summary>Soundness-profile scenario coordinate: no expectation (any accepted program must run sanitizer-clean).</summary>
    private static Feature SN(string tag, string desc) => new(tag, "scenario", "soundness", desc, "§3 ownership and borrows (expectation-free)", [], "Snd* (soundness)");
    private static Feature R12(string tag, string desc) => new(tag, "cut", "cut", desc, "user-approved v0.2 cut (2026-11-17)", ["VIB000"], MatrixOnly + "; tagged by the C10–C21 scan");
    private static Feature CA(string tag, string desc, string anchor) => new(tag, "cut", "cut", desc, "decided v0.2 cut (SPEC/cut list pending)", ["VIB000"], anchor + " (activation)");

    /// <summary>Anchor text of rows that only the matrix probes cover (the generator has no production for them).</summary>
    public const string MatrixOnly = "matrix probes only";

    public static readonly Feature[] All =
    [
        // locals, discards, out arguments
        S("local.decl", "locals", "typed local declaration with initializer", "§1", "*"),
        S("local.var", "locals", "`var` local", "§1", "*"),
        S("local.uninit", "locals", "local declared without initializer, assigned later (definite assignment)", "§3.7(2)", "SwitchStmt/Panic/StructInit"),
        Retired("local.underscore", "locals", "a real local named `_`", "C11", "the generator declares no symbol named `_`; the discard forms stay (`assign.underscore`, `out.underscore`, `out.var-discard`, `out.typed-discard`, `pattern.type-discard`, `pattern.discard`)"),
        Retired("local.underscore-read", "locals", "reading a variable named `_`", "C11", "the generator declares no symbol named `_`; the discard forms stay (`assign.underscore`, `out.underscore`, `out.var-discard`, `out.typed-discard`, `pattern.type-discard`, `pattern.discard`)"),
        Retired("param.underscore", "locals", "a parameter named `_`", "C11", "the generator declares no symbol named `_`; the discard forms stay (`assign.underscore`, `out.underscore`, `out.var-discard`, `out.typed-discard`, `pattern.type-discard`, `pattern.discard`)"),
        Retired("field.underscore", "locals", "a field named `_`", "C11", "the generator declares no symbol named `_`; the discard forms stay (`assign.underscore`, `out.underscore`, `out.var-discard`, `out.typed-discard`, `pattern.type-discard`, `pattern.discard`)"),
        S("assign.underscore", "locals", "`_ = e` discard", "§1", "OutArg/Underscore"),
        S("local.ref", "locals", "ref / ref readonly local", "§3, §1 ref reassignment rule", "RefParams"),
        S("out.var", "out", "`out var x`", "§1, §3.5 guards", "OutArg"),
        S("out.typed", "out", "`out int x`", "§1", "OutArg"),
        S("out.underscore", "out", "`out _` discard", "§3.7", "OutArg/Underscore"),
        S("out.var-discard", "out", "`out var _`", "§1", "OutArg"),
        S("out.typed-discard", "out", "`out int _`", "§1", "OutArg"),
        S("out.existing-local", "out", "`out x` to an existing local", "§1", "OutArg"),
        S("out.field", "out", "`out s.A` to a field of a fully initialized plain struct", "§3.1, §3.7", "OutArg"),
        S("out.other", "out", "other out targets", "§1", "OutArg"),
        S("bcl.tryparse", "out", "`int.TryParse`/`long.TryParse` BCL alias", "§3.5 BCL aliases", "OutArg"),
        // constants and enums
        S("const.local", "const", "local `const` (top-level, function, used from static local functions)", "§1 static fields rule", "Const"),
        S("const.class", "const", "class `const` field used by simple name (incl. inside static local functions)", "§1", "Const"),
        S("type.enum", "const", "user enum with int underlying type", "§2", "Const/Collection"),
        S("pattern.named-const", "const", "named constant (const/enum member) as a case pattern", "§3.4", "Const"),
        S("expr.qualified-member", "const", "qualified `E.A` / `C.K` access", "§2", "*"),
        // switch
        S("switch.stmt", "switch", "switch statement", "§3.4, §3.7(1)", "SwitchStmt"),
        S("switch.expr", "switch", "switch expression", "§3.4, CS8509 promoted", "SwitchExpr"),
        S("switch.sel.int", "switch", "int/long selector", "§3.4", "SwitchStmt"),
        S("switch.sel.bool", "switch", "bool selector (true/false cases)", "§3.7", "SwitchStmt"),
        S("switch.sel.unsigned", "switch", "unsigned selector", "§3.7(1)", "SwitchStmt"),
        S("switch.sel.char", "switch", "char selector", "§3.4", "SwitchStmt"),
        S("switch.sel.enum", "switch", "enum selector", "§2, §3.4", "SwitchStmt/SwitchExpr"),
        S("switch.sel.option", "switch", "Option<int> selector with Some/NoneType patterns", "§3.4, §3.7", "SwitchStmt/OptionResult"),
        S("switch.sel.result", "switch", "Result<int,int> selector with Ok/Err patterns", "§3.4, §3.7", "SwitchStmt/OptionResult"),
        S("switch.sel.float", "switch", "float/double selector (never exhaustive: NaN)", "§5.3", "SwitchStmt/SwitchExpr"),
        S("switch.sel-unparenthesized", "switch", "operator expression before `switch` without parentheses (`a % b switch { … }` parses as `a % (b switch { … })`; `-x switch` as `(-x) switch`)", "§1 (C# 15 grammar subset)", "SwitchExpr"),
        S("switch.default", "switch", "`default:` section", "§3.7", "SwitchStmt"),
        S("switch.no-default", "switch", "switch statement without default (exhaustive or not)", "§3.7(1)(2)", "SwitchStmt"),
        S("switch.guard", "switch", "non-constant `when` guard", "§3.7(2)", "SwitchStmt"),
        Retired("switch.guard-const", "switch", "`when` guard that is a constant expression of literals (`when 0.0 >= 0.0`); C# folds it like `when true`/`when false`", "C19", "guards are non-constant"),
        Retired("switch.guard-true", "switch", "`when true` guard", "C19", "guards are non-constant"),
        Retired("switch.guard-false", "switch", "`when false` guard", "C19", "guards are non-constant"),
        S("switch.nested", "switch", "switch nested in a switch section", "§3.7", "SwitchStmt"),
        Retired("switch.section-local", "switch", "declaration directly in a switch section (switch-block declaration space)", "C18", "a section that declares a local is wrapped in a block (Decut)"),
        Retired("switch.cross-section-var", "switch", "variable declared directly in one section (local or `out var`), assigned and read in a later section", "C18", "a section that declares a local is wrapped in a block (Decut)"),
        Retired("switch.cross-section-dup", "switch", "the same name declared directly in two sections of one switch block (C# CS0128)", "C18", "a section that declares a local is wrapped in a block (Decut)"),
        S("switch.expr-no-discard", "switch", "switch expression without discard arm", "CS8509 promoted, §1", "SwitchExpr"),
        N("switch.section-no-terminator", "switch", "section end reachable (fallthrough)", "§3.7(1) VIB000 / CS0163", "Mutator drop-terminator"),
        // patterns
        S("pattern.const", "patterns", "constant pattern", "§3.4", "SwitchStmt"),
        Retired("pattern.relational", "patterns", "relational pattern", "C20", "case labels use constant, `or` and `not` patterns; relational tests move to `when` guards or `&&` conditions"),
        Retired("pattern.and", "patterns", "`and` pattern combinator", "C20", "case labels use constant, `or` and `not` patterns"),
        S("pattern.combinator", "patterns", "`or`/`not` patterns (`and` is cut C20)", "§3.4", "SwitchStmt/PatternVar"),
        S("pattern.decl", "patterns", "declaration pattern `int n`", "§3.4", "SwitchStmt/PatternVar"),
        S("pattern.var", "patterns", "var pattern", "§3.4", "SwitchStmt/PatternVar"),
        S("pattern.discard", "patterns", "discard pattern `_`", "§3.4", "SwitchExpr"),
        S("pattern.type", "patterns", "type pattern", "§3.4", "SwitchStmt/SwitchExpr"),
        S("pattern.type-discard", "patterns", "`int _` designation", "§3.4", "SwitchStmt/SwitchExpr"),
        S("pattern.union", "patterns", "Some<T>/NoneType/Ok<T>/Err<E> union patterns", "§3.4", "SwitchStmt/OptionResult"),
        S("pattern.is", "patterns", "`e is pattern` expression (pattern variables)", "§3.4", "PatternVar"),
        // functions
        S("localfunc.static", "functions", "static local function (forward call, recursion)", "§1", "LocalFunc"),
        S("localfunc.nested", "functions", "static local function nested in another function body", "§1", "LocalFunc/Const"),
        S("localfunc.in-class-method", "functions", "static local function inside a class method", "§1", "Const/LocalFunc"),
        S("method.static", "functions", "static method of a class", "§1", "*"),
        S("method.instance", "functions", "instance method ([Mut] or shared receiver)", "§3.1", "ClassInstance"),
        S("func.expr-body", "functions", "expression-bodied function", "§1", "*"),
        S("func.returns-span", "functions", "function returning ReadOnlySpan<T>", "§3.3 lifetimes, §3.7 source contexts", "Collection"),
        // parameters and arguments
        S("param.ref", "params", "`ref` parameter", "§3, §3.3", "RefParams"),
        S("param.in", "params", "`in` parameter", "§3", "RefParams/Const"),
        Retired("param.ref-readonly", "params", "`ref readonly` parameter (CS9193 promoted)", "C10", "`in` parameters instead"),
        S("param.out", "params", "`out` parameter", "§3", "RefParams/OutArg"),
        S("param.scoped", "params", "`scoped` span parameter", "§3.3", "Collection"),
        S("arg.ref", "params", "`ref` argument", "§3.3", "RefParams"),
        S("arg.in", "params", "explicit `in` argument", "§3", "RefParams/Const"),
        S("arg.out", "params", "`out` argument", "§3", "OutArg/RefParams"),
        N("arg.rvalue-to-ref-param", "params", "literal/rvalue passed with an explicit `in` (CS8156)", "CS9193 / CS8156", "RefParams/Const"),
        S("expr.conditional-arg", "params", "conditional expression as an argument", "§1", "CondArg"),
        // structs and classes
        S("type.struct", "types", "user struct", "§2", "StructInit"),
        S("struct.local", "types", "struct local", "§2", "StructInit"),
        S("struct.fieldwise-init", "types", "plain-value struct initialized field by field", "§3.7(2)", "StructInit"),
        S("struct.out-fieldwise", "types", "plain-value struct out parameter initialized field by field", "§3.7(2)", "StructInit"),
        S("struct.owned-field", "types", "struct with an owned field (Vec<int> or class)", "§2", "StructInit"),
        S("struct.new-default", "types", "`new S()` of a struct", "§2 zero-safety", "StructInit"),
        S("struct.ctor", "types", "struct primary constructor `new SO(a, b, owned)` (the fully initialized form of an owned struct)", "§2", "StructInit/RefParams/OutArg"),
        S("type.class", "types", "sealed class (primary ctor, fields)", "§2", "ClassInstance/StructInit"),
        S("class.primary-ctor", "types", "primary constructor", "§2 rule 3", "ClassInstance"),
        S("type.field", "types", "instance field", "§2", "*"),
        S("type.resource", "types", "`class R : IDisposable`", "§2 linear policy", "Using"),
        S("using.var", "resources", "`using var r = ...` (LIFO disposal)", "§2 exact-once disposal", "Using"),
        S("using.block", "resources", "`using (var r = ...) { }`", "§2", "Using"),
        // collections
        S("coll.rospan", "collections", "ReadOnlySpan<T> collection expression", "§2, §3.2", "Collection"),
        S("coll.span", "collections", "Span<T> collection expression", "§3.2", "Collection/Loop"),
        S("coll.vec", "collections", "Vec<int> collection expression", "§3.5", "Collection/Loop"),
        S("coll.empty", "collections", "empty collection `[]`", "§3.5", "Collection"),
        S("coll.local-init", "collections", "collection expression as a local initializer", "§2", "Collection"),
        S("coll.arg", "collections", "collection expression as an argument", "§2", "Collection"),
        S("coll.conditional-locals", "collections", "a conditional choosing between two collection locals (the C16-compliant form: collections assigned to locals first)", "§2, §3.7", "Collection"),
        Retired("coll.conditional", "collections", "conditional between collection expressions", "C16", "a conditional chooses between collection locals instead"),
        Retired("coll.conditional-unparenthesized", "collections", "`c ? [a] : [b]` without parentheses", "C16", "a conditional chooses between collection locals instead"),
        S("coll.returned", "collections", "collection expression returned as ReadOnlySpan<T> after an unconditional Panic (runtime-unreachable, accepted; a reachable return is `banned.collection-return-span`)", "§3.3, §3.7", "Collection"),
        S("coll.elem-char", "collections", "char elements", "§2", "Collection"),
        S("coll.elem-byte", "collections", "byte elements", "§2", "Collection"),
        S("coll.elem-long", "collections", "long elements", "§2", "Collection"),
        S("coll.elem-enum", "collections", "enum elements", "§2", "Collection"),
        N("coll.foreach-literal", "collections", "foreach directly over a bare collection literal (no natural type in C#: every frontend must reject)", "C# CS9176", "Loop"),
        S("expr.index", "collections", "indexing (`s[i]`, `s[^1]`)", "§3.2", "Collection"),
        S("assign.index", "collections", "element write `v[i] = x`", "§3.2", "Collection"),
        // loops
        S("loop.for", "loops", "for loop", "§1", "Loop"),
        S("loop.while", "loops", "while loop", "§1", "Loop/PatternVar"),
        S("loop.do", "loops", "do-while loop", "§1", "Loop"),
        S("loop.foreach", "loops", "foreach (value)", "§3.2(4)", "Loop/Collection"),
        S("loop.foreach-ref", "loops", "foreach (ref var x in v)", "§3.2(4)", "Loop"),
        S("loop.foreach-vec", "loops", "foreach over Vec<int>", "§3.2(4)", "Loop"),
        S("loop.continue", "loops", "continue (incl. from switch sections)", "§1", "Loop/SwitchStmt"),
        // Option / Result / Panic
        S("union.ctor", "unions", "Some/None/Ok/Err", "§3.5", "OptionResult"),
        S("union.guard", "unions", "`if ([!]x.IsNone(out var v))` / `IsErr(out var ok, out var err)`", "§3.5 guards", "OptionResult"),
        S("union.consume", "unions", "Unwrap/Expect/UnwrapOr/UnwrapOrDefault/IsSome", "§3.5", "OptionResult"),
        S("panic.call", "panic", "`VibraniumRuntime.Panic` (source continuation, runtime divergence, exit 101)", "§3.7(1)(3), §5.4", "Panic"),
        // arithmetic and expressions
        S("arith.int", "arith", "int arithmetic (wrapping)", "§5.3", "Arith/*"),
        S("arith.long", "arith", "long arithmetic", "§5.3", "Arith"),
        S("arith.unsigned", "arith", "unsigned arithmetic", "§5.3", "Arith"),
        S("arith.float", "arith", "float arithmetic", "§5.3 IEEE", "Arith"),
        S("arith.double", "arith", "double arithmetic", "§5.3 IEEE", "Arith"),
        S("arith.div-int", "arith", "int division (zero / MIN/-1 panics)", "§5.3, §5.4", "Arith"),
        S("arith.div-long", "arith", "long division", "§5.3", "Arith"),
        S("arith.div-unsigned", "arith", "unsigned division", "§5.3", "Arith"),
        S("arith.div-float", "arith", "float division", "§5.3", "Arith"),
        S("arith.div-double", "arith", "double division", "§5.3", "Arith"),
        S("arith.rem-int", "arith", "int `%`", "§5.3", "Arith"),
        S("arith.rem-long", "arith", "long `%`", "§5.3", "Arith"),
        S("arith.rem-unsigned", "arith", "unsigned `%`", "§5.3", "Arith"),
        S("arith.rem-float", "arith", "float `%` (libc-only link contract)", "§5.3, README libc-only", "Arith"),
        S("arith.rem-double", "arith", "double `%` (libc-only link contract)", "§5.3, README libc-only", "Arith"),
        S("arith.shift", "arith", "shifts (count masking)", "§5.3", "Arith"),
        S("arith.bitwise", "arith", "& | ^", "§5.3", "*"),
        S("arith.float-to-int-cast", "arith", "saturating float->int cast", "§5.3", "Arith"),
        S("expr.cast", "arith", "numeric/enum casts", "§5.3", "*"),
        S("expr.compare", "expr", "comparisons", "§1", "*"),
        S("expr.logical", "expr", "&& / || short-circuit", "§5.2 drop flags", "*"),
        S("expr.conditional", "expr", "conditional `c ? a : b`", "§1", "*"),
        S("expr.u8-literal", "expr", "\"...\"u8 literal", "§2", "Collection"),
        S("assign.simple", "expr", "simple assignment", "§5.2", "*"),
        S("assign.compound", "expr", "compound assignment", "§5.2", "*"),
        S("assign.field", "expr", "field assignment", "§3.1", "StructInit/ClassInstance"),
        S("stmt.if", "statements", "if", "§1", "*"),
        S("stmt.if-else", "statements", "if/else", "§1", "*"),
        S("stmt.nested-block", "statements", "nested block scopes", "§3.7", "*"),
        // near-miss shapes (C#-invalid or subset-invalid on purpose)
        N("mut.embedded-decl", "near-miss", "declaration as an embedded statement (`if (c) int x = 1;`)", "C# CS1023", "Mutator embed-decl"),
        // extended profile: Span slicing, ranges and Index
        X("span.from-coll", "span", "`Span<int> s = [..]` from a collection expression", "§3.7 storage contract", "SpanSlice"),
        X("span.from-vec", "span", "`Span<int>` borrowed from a `Vec<int>` (`AsSpan()`, `Slice`, `v[..]`)", "§3.5 Vec table", "SpanSlice"),
        X("span.vec-range", "span", "`v[a..b]` on a `Vec<T>` (implicit Range pattern over `Count` + `Slice`)", "§3.5 Vec table", "SpanSlice"),
        X("span.vec-slice-call", "span", "`v.AsSpan()` / `v.Slice(start, length)`", "§3.5 Vec table", "SpanSlice"),
        X("span.vec-after-borrow", "span", "the owning `Vec` read after its span borrow ended (NLL)", "§3.2, §3.4", "SpanSlice"),
        X("span.range", "span", "`s[a..b]`, `s[a..]` slicing", "§3.5 Span table", "SpanSlice"),
        X("span.range-from-end", "span", "`s[..^k]`, `s[a..^k]` ranges from the end", "§3.5 Span table", "SpanSlice"),
        X("span.index-from-end", "span", "`s[^k]` and `r[^1]` reads", "§3.2(2)", "SpanSlice"),
        X("span.slice-local", "span", "a `Span<int>` slice local kept live across nested code", "§3.2", "SpanSlice"),
        X("span.slice-call", "span", "`s.Slice(a)` / `s.Slice(a, n)`", "§3.5 Span table", "SpanSlice"),
        X("span.to-rospan", "span", "implicit `Span<int>` → `ReadOnlySpan<int>` shared reborrow", "§3.2(1)", "SpanSlice"),
        X("span.write-through-slice", "span", "write through a slice: `t[0] = e`", "§3.2(2)", "SpanSlice"),
        X("span.write", "span", "`s[i] = e` / `s[i] += e`", "§3.2(2)", "SpanSlice"),
        X("span.read", "span", "`s[i]` value read", "§3.2(2)", "SpanSlice"),
        X("span.length", "span", "`s.Length` / `s.IsEmpty`", "§3.5 Span table", "SpanSlice"),
        X("span.foreach", "span", "`foreach (var e in slice)`", "§3.2 foreach (decided N8 rule)", "SpanSlice"),
        X("span.foreach-ref", "span", "`foreach (ref var e in s) e += k`", "§3.2 foreach", "SpanSlice"),
        X("span.arg-rospan", "span", "a span or slice passed to a `ReadOnlySpan<int>` parameter", "§3.2(1)", "SpanSlice"),
        X("span.arg-span", "span", "a span or slice passed to a `Span<int>` parameter, then the span is reused", "§3.2(1) reborrow", "SpanSlice"),
        X("span.write-after-shared", "span", "write to a span after its `ReadOnlySpan` copy is dead", "§3.2, NLL", "SpanSlice"),
        X("span.oob-panic", "span", "out-of-range slice: panic `slice out of bounds`, exit 101 everywhere", "§3.5 panics", "SpanSlice"),
        X("span.index-oob-panic", "span", "out-of-range index write: panic, exit 101 everywhere", "§3.2(2)", "SpanSlice"),
        V("span.two-slices-one-call", "span", "`Pair(s[..k], s[k..])`: two `Span<int>` arguments sliced from one span in one call", "§3.3 expanded exclusive set (root provenance)", "VIB002", "SpanSlice"),
        V("span.nm-overlap", "span", "`Over(s, s)` / `Over(s[0..2], s[1..3])` with two `Span<int>` parameters", "§3.3", "VIB002", "SpanSlice"),
        V("span.nm-parent-while-live", "span", "write to the parent span while a slice of it is live", "§3.2", "VIB003", "SpanSlice"),
        V("span.nm-moved", "span", "`var m = s;` moves a `Span<T>` (`!Copy`), then `s` is used", "§3.2(1)", "VIB001", "SpanSlice"),
        V("span.nm-vec-grow", "span", "`v.Add(1)` while a span borrowed from `v` is live", "§3.2, §3.5", "VIB003", "SpanSlice"),
        // extended profile: Option slots through OptionRef / OptionMut
        X("optslot.local", "option slots", "`Option<Cell>` local holding a class", "§2", "OptionSlot"),
        X("optslot.take", "option slots", "`o.Take()`", "§3.5 Option table", "OptionSlot"),
        X("optslot.replace", "option slots", "`o.Replace(Some(..))`", "§3.5 Option table", "OptionSlot"),
        X("optslot.issome", "option slots", "`o.IsSome`", "§3.5 Option table", "OptionSlot"),
        X("optref.asref-pattern", "option slots", "`o.AsRef() is Some<Cell>(var c)` shared read", "§3.1, §3.5", "OptionSlot"),
        X("optref.param", "option slots", "`OptionRef<Cell>` parameter", "§3.5", "OptionSlot"),
        X("optref.chain", "option slots", "nested `c.Next.AsRef()` pattern", "§3.5", "OptionSlot"),
        X("optref.switch", "option slots", "`o.AsRef() switch { Some<Cell>(var c) => .., _ => .. }`", "§3.5", "OptionSlot"),
        X("optmut.local", "option slots", "`OptionMut<Cell> m = o.AsMut();`", "§3.5", "OptionSlot"),
        X("optmut.set", "option slots", "`m.Set(Some(..))`", "§3.5", "OptionSlot"),
        X("optmut.replace", "option slots", "`m.Replace(..)`", "§3.5", "OptionSlot"),
        X("optmut.take", "option slots", "`m.Take()`", "§3.5", "OptionSlot"),
        X("optmut.pattern-write", "option slots", "`o.AsMut() is Some<Cell>(var c)` then `c.V += k`", "§3.1, §3.5", "OptionSlot"),
        X("optmut.param", "option slots", "`OptionMut<Cell>` parameter written through", "§3.5", "OptionSlot"),
        X("optmut.chain-set", "option slots", "`c.Next = Some(..)` through an `AsMut()` binding", "§3.5", "OptionSlot"),
        V("optmut.nm-alias", "option slots", "`o` read while an `OptionMut` of it is live", "§3.2", "VIB003", "OptionSlot"),
        V("optref.nm-write", "option slots", "field write through an `AsRef()` (shared) binding", "§3.1 shared pattern binding", "VIB004", "OptionSlot"),
        V("optslot.nm-place-write", "option slots", "field write through a plain `o is Some<Cell>(var c)` place binding (shared unless `.AsMut()`)", "§3.1", "VIB004", "OptionSlot"),
        V("optmut.nm-move", "option slots", "an `AsMut()` binding passed by value (move out of a borrow)", "§3", "a rejection (ID not fixed by SPEC)", "OptionSlot"),
        V("optmut.nm-overlap", "option slots", "`Two(o.AsMut(), o.AsMut())`", "§3.3", "VIB002", "OptionSlot"),
        // extended profile: [Mut] receivers
        X("mut.local", "[Mut] receivers", "class with `[Mut]` methods held in a local", "§3.1 receiver conventions", "MutReceiver"),
        X("mut.local-call", "[Mut] receivers", "`a.Add(e)` on a `[Mut]` method", "§3.1", "MutReceiver"),
        X("mut.two-phase-read", "[Mut] receivers", "`a.Add(a.Total)` two-phase receiver borrow", "§3.2 two-phase", "MutReceiver"),
        X("mut.calls-mut", "[Mut] receivers", "a `[Mut]` method calling another `[Mut]` method on `this`", "§3.1", "MutReceiver"),
        X("mut.drain", "[Mut] receivers", "`[Mut]` method clearing a `Vec` field", "§3.1", "MutReceiver"),
        X("mut.shared-call", "[Mut] receivers", "unmarked (shared `this`) method iterating a `Vec` field", "§3.1", "MutReceiver"),
        X("mut.ref-param", "[Mut] receivers", "`[Mut]` call through a `ref` parameter", "§3.1", "MutReceiver"),
        X("mut.in-param-shared", "[Mut] receivers", "shared calls through an `in` parameter", "§3.1", "MutReceiver"),
        X("mut.field-foreach", "[Mut] receivers", "`foreach (var e in a.Items)`", "§3.2 foreach", "MutReceiver"),
        X("mut.struct-local", "[Mut] receivers", "struct local with mutating and `readonly` methods", "§3.1", "MutReceiver"),
        X("mut.struct-method", "[Mut] receivers", "non-`readonly` struct method (exclusive `this`)", "§3.1", "MutReceiver"),
        X("mut.struct-readonly-method", "[Mut] receivers", "`readonly` struct method (shared `this`)", "§3.1", "MutReceiver"),
        X("mut.nested-field", "[Mut] receivers", "`[Mut]` method forwarding to a `[Mut]` method of a class field", "§3.1", "MutReceiver"),
        V("mut.nm-from-shared", "[Mut] receivers", "unmarked method calling a `[Mut]` method on `this`", "§3.1 `[Mut]` body verification", "VIB004", "MutReceiver"),
        V("mut.nm-in-param", "[Mut] receivers", "`[Mut]` call through an `in` parameter", "§3.1", "VIB004", "MutReceiver"),
        V("mut.nm-two-phase", "[Mut] receivers", "`a.Add(a.Drain())`: exclusive argument under a reserved exclusive receiver", "§3.2 two-phase", "VIB002 or VIB003", "MutReceiver"),
        V("mut.nm-borrow-live", "[Mut] receivers", "`a.Add(1)` while a span of `a.Items` is live", "§3.2", "VIB003", "MutReceiver"),
        V("mut.nm-foreach-mutate", "[Mut] receivers", "`foreach (var g in a.Items) a.Add(g)`", "§3.2 foreach", "VIB003", "MutReceiver"),
        V("mut.nm-moved", "[Mut] receivers", "`var h = a;` moves the class, then `a.Add(1)`", "§3.4", "VIB001", "MutReceiver"),
        // extended profile: foreach over UTF-8 views
        X("utf8.string-lines", "UTF-8 views", "`foreach (var l in \"..\".Lines())`", "§2 closed foreach views", "Utf8View"),
        X("utf8.span-local", "UTF-8 views", "`ReadOnlySpan<byte> u = \"..\"u8;`", "§2", "Utf8View"),
        X("utf8.span-lines", "UTF-8 views", "`foreach (var l in u.Lines())`", "§2", "Utf8View"),
        X("utf8.stringbuf", "UTF-8 views", "`StringBuf` built with `Append`/`AppendLine`", "§3.6 Std T1", "Utf8View"),
        X("utf8.buf-lines", "UTF-8 views", "`foreach (var l in sb.Lines())`", "§2", "Utf8View"),
        X("utf8.vec-bytes", "UTF-8 views", "`Vec<byte>` built from byte literals", "§2", "Utf8View"),
        X("utf8.vec-lines", "UTF-8 views", "`foreach (var l in v.AsReadOnlySpan().Lines())`", "§2", "Utf8View"),
        X("utf8.foreach", "UTF-8 views", "line loop body (with nested code in the body)", "§2", "Utf8View"),
        X("utf8.line-ops", "UTF-8 views", "`StartsWith`, `IndexOf`, `Trim`, `SequenceEqual` on a line", "§3.6 ByteSpanExtensions", "Utf8View"),
        X("utf8.line-tryparse", "UTF-8 views", "`int.TryParse(line.Trim(), out var v)`", "§3.5 BCL aliases", "Utf8View"),
        X("utf8.slice-escapes-loop", "UTF-8 views", "a line saved into a `scoped` local and used after the loop (owner unmutated)", "§2", "Utf8View"),
        V("utf8.nm-mutate-owner", "UTF-8 views", "`sb.Append(..)` inside `foreach (var l in sb.Lines())`", "§2", "VIB003", "Utf8View"),
        V("utf8.nm-slice-after-clear", "UTF-8 views", "`sb.Clear()` while a slice of `sb` is live", "§2, §3.2", "VIB003", "Utf8View"),
        V("utf8.nm-view-local", "UTF-8 views", "`var lines = text.Lines();` (view in a local)", "§2 closed foreach views", "VIB000", "Utf8View"),
        // extended profile: Std text and maps
        X("std.intmap", "Std", "`IntMap<int>` `Set`/`Get`/`Contains`/`Count`", "§3.6 Std T1", "StdText"),
        X("std.intmap-rehash", "Std", "`IntMap` growth past its capacity", "§3.6 Std T1", "StdText"),
        X("std.intmap-remove", "Std", "`IntMap.Remove`", "§3.6 Std T1", "StdText"),
        X("std.strmap", "Std", "`StrMap<int>` with `string` and `u8` keys", "§3.6 Std T1", "StdText"),
        X("std.stringbuf", "Std", "`StringBuf` `Append`/`AppendLine`/`AsUtf8`/`Length`", "§3.6 Std T1", "StdText"),
        X("std.stringbuf-int", "Std", "`StringBuf.Append(int)` incl. `int.MinValue`", "§3.6 Std T1", "StdText"),
        X("std.stringbuf-long", "Std", "`StringBuf.AppendLong` incl. `long.MinValue`", "§3.6 Std T1", "StdText"),
        X("std.stringbuf-bool", "Std", "`StringBuf.Append(bool)`", "§3.6 Std T1", "StdText"),
        X("std.stringbuf-hex", "Std", "`StringBuf.AppendHex(bits, minDigits)`", "§3.6 Std T1", "StdText"),
        X("std.stringbuf-rune", "Std", "`StringBuf.AppendRune` across UTF-8 length boundaries", "§3.6 Std T1", "StdText"),
        X("std.stringbuf-rune-panic", "Std", "`AppendRune` of a surrogate or out-of-range scalar: panic, exit 101", "§3.6 Std T1", "StdText"),
        X("std.stringbuf-op", "Std", "`sb += \"..\"` (`[Mut] void operator +=`)", "§3.1", "StdText"),
        X("std.stringbuf-truncate", "Std", "`sb.Truncate(sb.Length - 1)` two-phase", "§3.6 Std T1", "StdText"),
        X("std.bytes", "Std", "`ReadOnlySpan<byte>` text local", "§3.6 ByteSpanExtensions", "StdText"),
        X("std.bytes-trim", "Std", "`Trim`/`TrimStart`/`TrimEnd`", "§3.6 ByteSpanExtensions", "StdText"),
        X("std.bytes-indexof", "Std", "`IndexOf`/`LastIndexOf` of a byte", "§3.6 ByteSpanExtensions", "StdText"),
        X("std.bytes-prefix", "Std", "`StartsWith(string)`", "§3.6 ByteSpanExtensions", "StdText"),
        X("std.bytes-contains", "Std", "`Contains(string)`", "§3.6 ByteSpanExtensions", "StdText"),
        X("std.tryparse", "Std", "`int`/`long.TryParse` over `string` and `u8` inputs incl. edge values", "§3.5 BCL aliases", "StdText"),
        V("std.nm-map-moved", "Std", "`var m2 = m;` moves an `IntMap`, then `m.Set(..)`", "§3.4", "VIB001", "StdText"),
        V("std.nm-map-key-borrow", "Std", "`m.Set(..)` while a `KeyAt` slice of `m` is live", "§3.2", "VIB003", "StdText"),
        V("std.nm-buf-borrow", "Std", "`sb.Append(..)` while `sb.AsUtf8()` is live", "§3.2", "VIB003", "StdText"),
        // activation profile: borrow + move of related places in one call or assignment (call-activation model)
        A("ca.owner-vec", "call-activation", "owner is a `Vec<int>` local", "§3.2, §3.4", "CaCall/CaReceiver/CaAssign"),
        A("ca.owner-class", "call-activation", "owner is a sealed class instance", "§3.2, §3.4", "CaCall/CaReceiver"),
        A("ca.owner-holder", "call-activation", "owner is a class holding a class field `N` and a `Vec<int>` field `Items` (projections)", "§3.2, §3.4", "CaCall/CaReceiver/CaAssign"),
        A("ca.owner-option", "call-activation", "owner is an `Option<T>` of a class", "§3.2, §3.4", "CaCall"),
        A("ca.owner-linear", "call-activation", "owner is an `IDisposable` class (linear)", "§2, §3.4", "CaCall"),
        A("ca.owner-struct", "call-activation", "owner is a readonly struct holding a `Vec<int>`", "§3.2, §3.4", "CaReceiver"),
        A("ca.rel-same", "call-activation", "the loaned and the moved place are the same local", "§3.2", "CaCall/CaReceiver/CaAssign"),
        A("ca.rel-ext", "call-activation", "the loaned place extends the moved place (`x.N` loaned, `x` moved)", "§3.2", "CaCall/CaReceiver/CaAssign"),
        A("ca.rel-prefix", "call-activation", "the moved place extends the loaned place (`x` loaned, `x.N` moved)", "§3.2", "CaCall/CaAssign"),
        A("ca.rel-unrelated", "call-activation", "loaned and moved places are distinct owners", "§3.2", "CaCall/CaReceiver/CaAssign"),
        A("ca.loan-in", "call-activation", "`in` argument", "§3.2", "CaCall"),
        A("ca.loan-in-implicit", "call-activation", "an lvalue passed to an `in` parameter without a modifier (by reference, like `in`; replaces the `ref readonly` parameter forms cut by C10)", "§3.2", "CaCall"),
        A("ca.named-in-position", "call-activation", "the call names its arguments in parameter order (`Eat(a: in x, b: x)`; out-of-order names are cut C13)", "§3.2", "CaCall"),
        Retired("ca.loan-ref-readonly", "call-activation", "`ref readonly` parameter (`in` or `ref` argument)", "C10", "`in` parameters, with an explicit `in` or an implicit by-reference argument (`ca.loan-in-implicit`)"),
        A("ca.loan-ref", "call-activation", "`ref` argument", "§3.2", "CaCall"),
        A("ca.loan-out", "call-activation", "`out` argument", "§3.2", "CaCall"),
        A("ca.loan-view", "call-activation", "view argument: `AsSpan()`, `AsReadOnlySpan()`, a range slice, `AsRef()`/`AsMut()` of an Option", "§3.2, §3.3", "CaCall"),
        A("ca.loan-ref-proj", "call-activation", "`in`/`ref` argument of a field projection (`x.N`, `x.Items`)", "§3.2", "CaCall"),
        A("ca.loan-shared-recv", "call-activation", "shared receiver of an instance method or indexer", "§3.2 two-phase receiver", "CaReceiver"),
        A("ca.loan-mut-recv", "call-activation", "`[Mut]` receiver (method or `[Mut] operator +=`)", "§3.2 two-phase receiver", "CaReceiver"),
        A("ca.loan-this-in", "call-activation", "`this in` extension receiver", "§3.2", "CaReceiver"),
        A("ca.loan-this-ref", "call-activation", "`this ref` extension receiver", "§3.2", "CaReceiver"),
        A("ca.loan-target", "call-activation", "the assignment target is the loan (indexer set, compound index, ref local, field)", "§3.2", "CaAssign"),
        A("ca.move-bare", "call-activation", "move by passing the owner by value", "§3.4", "CaCall/CaReceiver/CaAssign"),
        A("ca.move-explicit", "call-activation", "move through `Move(x)`", "§3.4", "CaCall/CaReceiver/CaAssign"),
        A("ca.move-consume", "call-activation", "move through a consuming call (`o.Unwrap()`)", "§3.4", "CaCall"),
        A("ca.form-local", "call-activation", "the call is to a static local function", "§3.2", "CaCall"),
        A("ca.form-static", "call-activation", "the call is to a static method", "§3.2", "CaCall"),
        A("ca.form-instance", "call-activation", "the call is to an instance method", "§3.2", "CaCall/CaReceiver"),
        A("ca.form-ctor", "call-activation", "the call is a constructor (`new W(..)`)", "§3.2", "CaCall"),
        A("ca.form-extension", "call-activation", "the call is an extension method with a `this in`/`this ref` receiver", "§3.2", "CaReceiver"),
        A("ca.form-indexer-get", "call-activation", "indexer read whose index expression moves the receiver (`x[Idx(x)]`)", "§3.2", "CaReceiver"),
        A("ca.form-indexer-set", "call-activation", "indexer write whose value moves the target owner (`x[0] = Len(x)`)", "§3.2", "CaAssign"),
        A("ca.form-compound-index", "call-activation", "compound indexer assignment (`x[0] += Len(x)`)", "§3.2", "CaAssign"),
        A("ca.form-operator", "call-activation", "`[Mut] operator +=` whose operand moves the receiver", "§3.2", "CaReceiver"),
        A("ca.form-assign-ref", "call-activation", "assignment through a `ref` local (`ref var t = ref x; t = Move(..)`)", "§3.2", "CaAssign"),
        A("ca.form-assign-field", "call-activation", "field assignment whose value moves the owner (`x.N = Take(x)`, `x.N.V = PeekH(x)`)", "§3.2", "CaAssign"),
        A("ca.control-unrelated", "call-activation", "control: the same shapes on distinct owners (accepted; executed)", "§3.2", "CaCall/CaReceiver/CaAssign"),
        A("ca.control-shared-read-reserved", "call-activation", "control: shared reads of the target owner in the value (`x[0] = x.Count + x[1]`)", "§3.2 two-phase", "CaAssign"),
        A("ca.control-own-slot", "call-activation", "control: `x = Move(x)` (own slot, legal)", "§3.4", "CaAssign"),
        R("ca.borrow-then-move", "call-activation", "an earlier argument, receiver or assignment target borrows a place that a later operand of the same call or assignment moves", "§3.2 call activation (model; SPEC text pending)", "VIB003", "CaCall/CaReceiver/CaAssign"),
        R("ca.move-then-use", "call-activation", "an earlier argument moves a place that a later argument borrows", "§3.4 use after move", "VIB001", "CaCall"),
        // activation profile: views returned through parameters
        A("view.lifetimeof", "views", "`[return: LifetimeOf(\"x\")]` that returns a view of `x` (or a slice of it)", "§3.3 LifetimeOf", "ViewReturn"),
        A("view.lifetimeof-two", "views", "`[return: LifetimeOf(\"x\", \"y\")]`", "§3.3 LifetimeOf", "ViewReturn"),
        A("view.call-site-other-mutated", "views", "the call site mutates the input not named by `LifetimeOf` while the result lives", "§3.3", "ViewReturn"),
        A("view.elision-this", "views", "instance method returning a view of `this` with a view parameter (elision ties the result to `this`)", "§3.3 elision", "ViewReturn"),
        A("view.elision-single", "views", "a single borrowed input: elision ties the result to it", "§3.3 elision", "ViewReturn"),
        A("view.reborrow-parent-after", "views", "`First(s)` returns a reborrow of span `s`; `s` is used after the reborrow's last use", "§3.2 reborrow", "ViewReturn"),
        R("view.lifetimeof-violated", "views", "`[return: LifetimeOf(\"x\")]` with a body that may return `y`", "§3.3 LifetimeOf", "VIB007", "ViewReturn"),
        R("view.elision-this-violated", "views", "instance method whose elided result is tied to `this` returns the view parameter", "§3.3 elision", "VIB007", "ViewReturn"),
        R("view.elision-ambiguous", "views", "two borrowed inputs (views or `OptionRef`) and a returned view without `LifetimeOf`", "§3.3 elision", "VIB007", "ViewReturn"),
        R("view.use-after-mutate", "views", "an input named by `LifetimeOf` (or the single elided input) is mutated while the result lives", "§3.2", "VIB003", "ViewReturn"),
        R("view.reborrow-parent-while-live", "views", "write through span `s` while `First(s)` is still live", "§3.2 reborrow", "VIB003", "ViewReturn"),
        A("view.param-reslice-return", "views", "a by-value `ReadOnlySpan<int>` parameter re-sliced from itself (`x = x[1..]`) and returned (control for the two rows below)", "§3.3 elision", "ViewReturn"),
        R("view.param-stack-escape", "views", "a by-value `ReadOnlySpan<int>` parameter assigned a collection expression (`x = [1, 2]`, on one branch, `x = []`, or returned as `x[1..]`) and returned: the storage is owned by the parameter's scope and the view escapes the method (C# accepts constant elements)", "§3.7 collection storage", "VIB007", "ViewReturn"),
        R("view.param-stack-scope-exit", "views", "the same shape: the parameter's scope ends while the returned view borrows its collection storage", "§3.7 collection storage", "VIB003", "ViewReturn"),
        // activation profile (release-7.1): moved views, loop-reassigned views and assignment values (the assignment-value cascade)
        A("view.move-use-dest", "views", "a `!Copy` view moved by a plain move (`Span<int> t = s;`); only the destination is used, then the owner (control)", "§3.2 item 1 (`Span<T>` is `!Copy`)", "ViewMove"),
        A("view.move-owner-between", "views", "with `view.move-use-source`: the owner is read or written after the destination's last use and before the source's use", "§3.2 item 1", "ViewMove"),
        A("view.move-optionmut", "views", "the moved view (or assignment-value destination) is an `OptionMut<T>` instead of a `Span<int>`", "§3.2 item 1", "ViewMove"),
        R("view.move-use-source", "views", "a `!Copy` view (`Span<int>`, `OptionMut<T>`) moved by a plain move, then the source used (before or after the destination): exactly VIB001; the moved source keeps no borrow (decision A+)", "§3.2 item 1", "VIB001", "ViewMove"),
        A("view.move-reassign-source", "views", "a `Span<int>` moved by a plain move, the destination used, the source reassigned from another owner, then the first owner and the source used (control: the reassignment ends the source's old borrows)", "§3.2 item 1", "ViewMove"),
        R("view.move-dest-live-owner-used", "views", "a `!Copy` view moved by a plain move; the owner is read while the destination is still live (a real conflict)", "§3.2", "VIB003", "ViewMove"),
        new("view.move-conditional-use-source", "vib-reject", "views", "a `!Copy` view moved on one branch only, then the owner read and the source used: VIB001 and VIB003 (the oracle checks that one of them is reported)", "§3.2 item 1", ["VIB001", "VIB003"], "ViewMove (activation)"),
        A("view.loop-reassign-move", "views", "a `Span<int>` declared before a loop from another owner and reassigned in it by a plain move (`t = s;`), used after the assignment (and maybe after the loop): accepted; the overwritten value's borrows end", "§3.2 item 1 (decision A+)", "ViewMove"),
        A("view.loop-reassign-call", "views", "the same with a call result (`t = Id(s);`)", "§3.2 item 1 (decision A+)", "ViewMove"),
        A("view.loop-assign-value-receiver", "views", "in a loop, the value of an assignment to a view declared before it used as a receiver (`n += (s = b.AsSpan()).Length`, `[0]`, `[1..].Length`; maybe reassigned and used again): accepted", "§3.2 item 1 (decision A+)", "ViewMove"),
        A("view.loop-assign-value-argument", "views", "in a loop, the value of an assignment to a view used as an argument (`n += Len(s = b.AsSpan())`): accepted", "§3.2 item 1 (decision A+)", "ViewMove"),
        R("view.loop-old-value-used-top", "views", "a loop-reassigned view's old value used at the top of the next iteration while its owner is written (a real conflict)", "§3.2", "VIB003", "ViewMove"),
        R("view.loop-moved-source-used", "views", "a view moved by an assignment value used at the top of the next iteration (a real use after move)", "§3.2 item 1", "VIB001", "ViewMove"),
        A("view.rebind-old-owner-mutated", "views", "a live view rebound to another owner (`t = b.AsSpan()`, `t = s`, `t = Id(..)`, also `ReadOnlySpan<int>`), then its old owner mutated (`a.Add(7)`) while the view is still used: accepted, the rebinding ends the old borrows", "§3.2 item 1", "ViewMove"),
        A("view.loop-rebind-old-owner-mutated", "views", "the same in a loop: the view is used at the top of each iteration, rebound, then its old owner mutated: accepted", "§3.2 item 1 (decision A+)", "ViewMove"),
        R("view.rebind-old-derived-live", "views", "a view derived from the old value (`u = t[..1]`) is still live when the old owner is mutated after the rebinding (a real conflict)", "§3.2", "VIB003", "ViewMove"),
        R("view.assign-value-then-use", "views", "the value of an assignment to a `!Copy` view used as a receiver, an argument or a chain (`(s = b.AsSpan()).Length`, `[0]`, `[1..].Length`, `Len(s = ..)`, `t = s = ..`, `(m = o2.AsMut()).IsSome`), then the view used again: exactly VIB001", "§3.2 item 1", "VIB001", "ViewMove"),
        A("view.assign-value-no-later-use", "views", "the same assignment values without a later use of the view (control)", "§3.2 item 1", "ViewMove"),
        A("view.assign-value-copy-view", "views", "the same with a `ReadOnlySpan<int>` destination, which is `Copy`, then the view used again (control)", "§3.2 item 1", "ViewMove"),
        A("view.assign-value-chain", "views", "with an assignment-value row: the chain form `Span<int> t = s = b.AsSpan();`", "§3.2 item 1", "ViewMove"),
        // every profile (release-7.1): switches over Option/Result with whole-union labels (the self switch-label class)
        S("uswitch.stmt", "union-switch", "a `switch` statement over `Option<T>` or `Result<T, int>` in a helper `int UsN(int p)` that is called on the None/Err path, on a payload path whose guards fail and on one whose guards hold", "§3.4", "UnionSwitch"),
        S("uswitch.expr", "union-switch", "the same arms as a switch expression (`n += s switch { .. };`)", "§3.4", "UnionSwitch"),
        S("uswitch.option", "union-switch", "the scrutinee is an `Option<T>` (cases `Some<T>`, `NoneType`)", "§3.4", "UnionSwitch"),
        S("uswitch.result", "union-switch", "the scrutinee is a `Result<T, int>` (cases `Ok<T>`, `Err<int>`)", "§3.4", "UnionSwitch"),
        S("uswitch.payload-copy", "union-switch", "`Copy` payload (`int`)", "§3.4", "UnionSwitch"),
        S("uswitch.payload-owner", "union-switch", "owner payload `Vec<int>` (bound as a shared borrow from a place, owned from a call)", "§3.4", "UnionSwitch"),
        S("uswitch.payload-class", "union-switch", "owner payload: a class instance", "§3.4", "UnionSwitch"),
        S("uswitch.payload-linear", "union-switch", "linear payload (an `IDisposable` class, consumed by `using var` in a helper): union cases only, one arm per case and no guard, from a call or `Move(o)`", "§2 linear policy, §3.4", "UnionSwitch"),
        S("uswitch.place", "union-switch", "the scrutinee is an owned local (borrowed in place, then used after the switch)", "§3.4 place scrutinees", "UnionSwitch"),
        S("uswitch.rvalue", "union-switch", "the scrutinee is a call (consumed)", "§3.4 rvalue scrutinees", "UnionSwitch"),
        S("uswitch.move-place", "union-switch", "the scrutinee is `Move(o)` of a linear local", "§3.4", "UnionSwitch"),
        S("uswitch.not-case", "union-switch", "a `not` label of a case type (`not NoneType`, `not Some<T>`, `not Err<int>`, `not Ok<T>`), guarded or not", "§3.4", "UnionSwitch"),
        S("uswitch.or-cases", "union-switch", "an `or` of the case types with a guard (`Some<T> or NoneType when ..`)", "§3.4", "UnionSwitch"),
        S("uswitch.var", "union-switch", "a guarded `var w` designation of the whole union (`Copy` payloads)", "§3.4", "UnionSwitch"),
        S("uswitch.var-final", "union-switch", "a final `var w` catch-all after a guarded payload arm (`Copy` payloads, or an owner from a call)", "§3.4", "UnionSwitch"),
        S("uswitch.type-designation", "union-switch", "a case type with a designation and a guard (`Some<int> s when ..`, `Ok<int> s`)", "§3.4", "UnionSwitch"),
        S("uswitch.guard", "union-switch", "the first whole-union label has a `when` guard", "§3.4", "UnionSwitch"),
        S("uswitch.two-fallbacks", "union-switch", "two guarded whole-union labels before the union cases", "§3.4", "UnionSwitch"),
        S("uswitch.case-guard", "union-switch", "a guarded payload arm before the unguarded one", "§3.4", "UnionSwitch"),
        S("uswitch.exhaustive", "union-switch", "no default: the labels cover both cases (e.g. `case not Some<T>:` then `case Some<T>(var v):`)", "§3.4", "UnionSwitch"),
        S("uswitch.default", "union-switch", "a `default:` section or `_` arm takes the rest", "§3.4", "UnionSwitch"),
        S("uswitch.loop", "union-switch", "the switch statement is in a loop and a whole-union section ends with `continue`", "§3.4", "UnionSwitch"),
        S("uswitch.nonunion-label", "union-switch", "at least one label is not a union case (`Some/None/Ok/Err(..)`), a discard or a designation-free `var`/type pattern", "§3.4", "UnionSwitch"),
        S("uswitch.stmt-nonunion-label", "union-switch", "a switch statement with such a label (root N19: self dropped these labels)", "§3.4", "UnionSwitch"),
        S("uswitch.expr-whole-union-arm", "union-switch", "a switch expression with such an arm", "§3.4", "UnionSwitch"),
        // activation profile: conditional temporaries
        A("cond.temp-arm-arg", "temporaries", "a temporary `Vec` viewed in one arm of a conditional passed as an argument", "§3.7 temporaries", "CondTemp"),
        A("cond.temp-both-arms", "temporaries", "temporaries viewed in both arms of a conditional argument", "§3.7 temporaries", "CondTemp"),
        R("cond.temp-escapes-statement", "temporaries", "a conditional whose arm views a temporary is stored in a local", "§3.7 temporaries", "VIB007", "CondTemp"),
        // activation profile: constant float conversions to native integers
        Retired("arith.float-nint-const", "arith", "`unchecked((nint)c)` / `(nuint)` of an in-range float constant", "C17", "the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`)"),
        Retired("arith.float-nint-const-oob", "arith", "`unchecked((nint)c)` / `(nuint)` of an out-of-range float constant (saturating)", "C17", "the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`)"),
        Retired("arith.float-nint-const-special", "arith", "`unchecked((nint)c)` / `(nuint)` of an infinity or NaN constant", "C17", "the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`)"),
        Retired("arith.float-nint-runtime", "arith", "the same conversions of a runtime `double`/`float` local", "C17", "the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`)"),
        Retired("arith.nint-bounds", "arith", "`nint.MinValue`/`MaxValue`, `nuint.MinValue`/`MaxValue`", "C17", "the FloatNint anchor converts to `long`/`ulong`/`int`/`uint` (`arith.float-int-*`)"),
        A("arith.float-int-const", "arith", "`unchecked((long)c)` / `(ulong)`, `(int)`, `(uint)` of an in-range float constant (replaces the `nint`/`nuint` forms cut by C17)", "§5 numeric lowering", "FloatNint"),
        A("arith.float-int-const-oob", "arith", "the same conversions of an out-of-range float constant (saturating)", "§5 numeric lowering", "FloatNint"),
        A("arith.float-int-const-special", "arith", "the same conversions of an infinity or NaN constant", "§5 numeric lowering", "FloatNint"),
        A("arith.float-int-runtime", "arith", "the same conversions of a runtime `double`/`float` local", "§5 numeric lowering", "FloatNint"),
        A("arith.int-bounds", "arith", "`long.MinValue`/`MaxValue`, `ulong.MaxValue`, `int.MinValue`/`MaxValue`, `uint.MaxValue`", "§5 numeric lowering", "FloatNint"),
        // activation profile: Std, generated code, declarations
        A("std.append-nonseekable", "Std", "`File.AppendAllText` / `File.OpenAppend(..).WriteAll` to `/dev/stdout` or `/dev/stderr` (a pipe in the harness)", "§3.6 File", "StdAppend"),
        A("gen.auto-generated-header", "generated", "`// <auto-generated/>` first line: the Analyzer must still report", "§6 analyzer parity", "*"),
        A("gen.file-name-g-cs", "generated", "the program is compiled as `Program.g.cs`: the Analyzer must still report", "§6 analyzer parity", "*"),
        A("pp.region", "declarations", "control for C5: `#region`/`#endregion` stay valid", "decided C5 scope", "DeclCuts"),
        A("type.nested-nongeneric", "declarations", "control for C8: a class nested in a non-generic class", "decided C8 scope", "DeclCuts"),
        A("type.multi-ctor", "declarations", "control for C9: two constructors without chaining", "decided C9 scope", "DeclCuts"),
        R("decl.this-call-after-init", "declarations", "a constructor calls an instance method on `this` after every field is assigned (decided N15: in v0.2 a constructor may not call an instance method on `this`, read an instance property through `this` or let `this` escape, whatever is assigned)", "§2 zero-safety (N15: SPEC aligned to the implementations)", "VIB008", "DeclCuts"),
        A("decl.ctor-static-call", "declarations", "control for N15: a constructor assigns its fields and calls a static method with its parameter", "§2 zero-safety", "DeclCuts"),
        R("decl.this-call-before-init", "declarations", "a constructor calls an instance method on `this` before a non-zero-safe field is initialized", "§2 zero-safety", "VIB008", "DeclCuts"),
        // decided N13 rule (matrix probes; the generator reaches it only by chance)
        new("flow.unreachable-section", "supported", "flow", "borrow/move checks follow Roslyn reachability: a `default:` section that Roslyn proves unreachable (earlier value cases cover the input, CS0162) is not flow-checked, code after `return` and code after an unconditional Panic (`when false` and `if (false)` are cut C19 since 2026-11-17); a use after move there is accepted, the same body in a reachable `default:` is VIB001", "decided N13 rule (SPEC text pending)", [], MatrixOnly),
        // banned by design (SPEC §1 closed subset)
        B("banned.null", "banned", "`null` literal", "§1(2), §4 VIB008", "VIB008"),
        B("banned.nullable", "banned", "nullable annotation `T?`", "§1(2), §4 VIB008", "VIB008"),
        B("banned.null-forgiving", "banned", "null-forgiving `x!`", "§1(2), §4 VIB008", "VIB008"),
        B("banned.goto", "banned", "`goto`/labels", "§1 closed subset (unstructured goto)", "VIB000"),
        B("banned.string-concat", "banned", "string `+`", "§2 string restrictions", "VIB000"),
        B("banned.string-equality", "banned", "string `==`", "§2 string restrictions", "VIB000"),
        B("banned.string-length", "banned", "`string.Length`", "§2 string restrictions", "VIB000"),
        B("banned.interpolation", "banned", "`$\"...\"` interpolation", "§2 string restrictions, §7(1)", "VIB000"),
        B("banned.struct-idisposable", "banned", "`struct S : IDisposable`", "§1, §2 linear policy", "VIB000", "VIB006"),
        B("banned.manual-dispose", "banned", "manual `.Dispose()` call", "§2 exact-once disposal (1)", "VIB006"),
        B("banned.linear-field", "banned", "IDisposable resource stored in a field", "§2, §4 VIB006", "VIB006"),
        B("banned.try", "banned", "try/finally", "§1 exceptions", "VIB000"),
        B("banned.throw", "banned", "throw", "§1 exceptions", "VIB000"),
        B("banned.lambda", "banned", "lambda / delegate", "§1 delegates, lambdas", "VIB000"),
        B("banned.nonstatic-localfunc", "banned", "non-static local function", "§1", "VIB000"),
        B("banned.static-field", "banned", "static (non-const) field", "§1", "VIB000"),
        B("banned.array", "banned", "raw `T[]` array", "§1", "VIB000"),
        B("banned.object", "banned", "`object` / boxing", "§1", "VIB000"),
        B("banned.checked", "banned", "`checked(...)`", "§1", "VIB000"),
        B("banned.inheritance", "banned", "class inheritance", "§1", "VIB000"),
        B("banned.object-initializer", "banned", "object/collection member initializer `new S { A = 1 }` (listed in SPEC from candidate-freeze-3; freeze-2 frontends already fail closed)", "§1 closed subset", "VIB000"),
        B("banned.collection-return-span", "banned", "collection expression returned as `ReadOnlySpan<T>`/`Span<T>` on a reachable path (`return [];`, `return [1, 2];`, `=> [1]`); a collection rvalue has its destination's storage and there is no static-data promotion", "§3.7 storage contract (decided; SPEC text pending)", "VIB007"),
        B("banned.default-nonzerosafe", "banned", "`default` of a non-zero-safe type (Vec<T>)", "§2 zero-safety (1)", "VIB008"),
        // soundness profile (--profile soundness): scenario coordinates, no expectation; an accepted program must run
        // ASan/LSan-clean at O0 and O3 with the managed output
        SN("snd.producer-span", "loan producer: `Span<int> s = v.AsSpan()`"),
        SN("snd.producer-ros", "loan producer: `ReadOnlySpan<int> s = v.AsReadOnlySpan()`"),
        SN("snd.producer-slice-range", "loan producer: `v.AsSpan()[0..2]`"),
        SN("snd.producer-slice-method", "loan producer: `v.Slice(0, 2)`"),
        SN("snd.producer-slice-ro", "loan producer: `v.ReadOnlySlice(1, 2)`"),
        SN("snd.producer-vec-range", "loan producer: `v[0..2]` (Vec Range pattern)"),
        SN("snd.producer-view-helper", "loan producer: a view returned by a helper (`Head(v.AsReadOnlySpan())`, elided lifetime)"),
        SN("snd.producer-ref-elem", "loan producer: `ref int s = ref v[0]`"),
        SN("snd.producer-ref-field", "loan producer: `ref int s = ref h.K` (class field)"),
        SN("snd.producer-optref", "loan producer: `var s = o.AsRef()`"),
        SN("snd.producer-optmut", "loan producer: `var s = o.AsMut()`"),
        SN("snd.producer-recv-view", "loan producer: a view returned by a receiver method (`h.View()`)"),
        SN("snd.inval-move", "invalidator: move of the owner (`var w = v;`)"),
        SN("snd.inval-sink", "invalidator: the owner passed to a consuming parameter"),
        SN("snd.inval-sink-explicit", "invalidator: `Sink(Move(v))`"),
        SN("snd.inval-growth", "invalidator: `v.Add(p)` (may reallocate)"),
        SN("snd.inval-clear", "invalidator: `v.Clear()`"),
        SN("snd.inval-resize", "invalidator: `v.Resize(8, p)` (reallocates)"),
        SN("snd.inval-reassign", "invalidator: the owner reassigned (`v = [p]`, `h = new …`, `o = None`)"),
        SN("snd.inval-scope-exit", "invalidator: the owner's block ends while the view (declared outside) lives"),
        SN("snd.inval-mut-call", "invalidator: a `[Mut]` method on the owner (`h.Push(p)`)"),
        SN("snd.inval-field-move", "invalidator: a field moved out of the owner (`var it = h.Items`)"),
        SN("snd.inval-items-growth", "invalidator: growth through a field (`h.Items.Add(p)`)"),
        SN("snd.inval-take", "invalidator: `o.Take()`"),
        SN("snd.inval-replace", "invalidator: `o.Replace(Some(..))`"),
        SN("snd.place-after", "placement: use after the invalidation (straight line)"),
        SN("snd.place-before", "placement: use before the invalidation (control: the loan has ended)"),
        SN("snd.place-reacquire", "placement: invalidate, re-acquire the loan, use"),
        SN("snd.place-branch", "placement: invalidation in one branch, use after the `if`"),
        SN("snd.place-branch-disjoint", "placement: use and invalidation in disjoint branches (control)"),
        SN("snd.place-loop", "placement: use, then invalidation in the first iteration (loop-carried)"),
        SN("snd.place-loop-reacquire", "placement: acquire, use and invalidate in every iteration"),
        SN("snd.place-switch", "placement: invalidation in a switch section, use after the switch"),
        SN("snd.place-after-loop", "placement: invalidation in a loop, use after it"),
        SN("snd.place-nested-use-first", "placement: use and invalidation as arguments of one call, the use first"),
        SN("snd.place-nested-use-last", "placement: use and invalidation as arguments of one call, the invalidation first"),
        SN("snd.place-scope-exit", "placement: view declared outside the owner's block, used after it"),
        SN("snd.use-read", "the later use reads through the loan"),
        SN("snd.use-write", "the later use writes through the loan"),
        // static expectations of the soundness scenarios (release-7): the expectation oracle checks them with or without execution
        new("snd.expect-conflict", "vib-reject", "soundness", "a later use keeps the loan live across a conflicting invalidation of its owner (placements after, branch, loop, switch, after-loop, nested-use-last, scope-exit)", "§3.2 borrows", ["VIB003"], "Snd* (soundness)"),
        new("snd.expect-use-after-move", "vib-reject", "soundness", "the loan is re-acquired from an owner that an earlier statement moved (placements reacquire, loop-reacquire with a moving invalidator)", "§3.1 moves", ["VIB001"], "Snd* (soundness)"),
        new("snd.expect-field-move", "vib-reject", "soundness", "a `Vec` field is moved out of a class (`var it = h.Items;`), whatever the placement", "§3.1 moves", ["VIB005"], "Snd* (soundness)"),
        // user-approved cuts
        C("cut.localfunc-in-switch-section", "cut", "local function declared directly in a switch section (inside a braced block stays valid)", "SwitchStmt/Const"),
        C("cut.struct-piecewise-owned", "cut", "piecewise init (field assignment or out) of a not-yet-fully-initialized struct with owned/drop fields, directly or nested", "StructInit/RefParams"),
        new("cut.generic-pattern", "cut", "cut", "any pattern other than `var` and discard on a value of open type-parameter type: constant, relational, type and declaration patterns (`x is 0`, `x is > 0 or <= 0`, `x is int`, `x is Box b`, `x is int and > 0`) and switch arms or case labels of those kinds; one VIB000 at the governing expression (`x is var v` and `_` stay valid)", "decided v0.2 cut C4 (SPEC/cut list pending)", ["VIB000"], MatrixOnly),
        CA("cut.preprocessor", "conditional compilation directives (`#if`, `#elif`, `#else`, `#endif`, `#define`, `#undef`); one VIB000 per file at the first directive keyword (`#region`, `#pragma`, `#nullable` stay valid)", "DeclCuts"),
        CA("cut.file-type", "file-local types (`file class`, `file struct`); VIB000 at `file`", "DeclCuts"),
        CA("cut.partial", "partial types and members (single-part `partial` classes included; the Std Sys catalog is exempt); VIB000 at `partial`", "DeclCuts"),
        CA("cut.nested-in-generic", "a type nested in a generic type (`Outer<T>.Inner`); VIB000 at the nested type's identifier (non-generic nesting stays valid)", "DeclCuts"),
        CA("cut.ctor-chaining", "constructor chaining `: this(...)`; VIB000 at `this` (`: base(...)` is C21)", "DeclCuts"),
        // C10–C20 (user-approved 2026-11-17) and C21 (decided 2026-11-18): the generator never produces them; the tool's own Roslyn scan (CutScan.cs)
        // tags any program that contains one, and the matrix has probes, controls and C11 discard near-misses
        R12("cut.ref-readonly-param", "C10: a `ref readonly` parameter (method, local function, constructor, operator, indexer); anchor `ref` (`in` parameters and `ref readonly` locals/returns stay)"),
        R12("cut.underscore-decl", "C11: declaring any symbol named `_` (local, parameter, field, property, method, type, type parameter, label, foreach variable, namespace, using alias, query range variable, tuple element name); anchor `_` (discards stay: `_ = e`, `out _`, `out var _`, `_` patterns)"),
        R12("cut.optional-param", "C12: a parameter with a default value or `[Optional]`/`[DefaultParameterValue]`; anchor the parameter's first token"),
        R12("cut.named-arg-order", "C13: a named argument whose position differs from its parameter's index; anchor the argument name (named arguments in position stay)"),
        R12("cut.this-assign", "C14: assignment to `this` (simple, compound, deconstruction, prefix/postfix `++`/`--`) and `ref this`/`out this` as a writable ref; anchor `this`"),
        R12("cut.conditional-ref", "C15: a conditional ref expression `c ? ref a : ref b`; anchor `?`"),
        R12("cut.nested-collection", "C16: a collection expression as an element or spread operand of another collection expression, a `?:` branch or a switch-expression arm value; anchor the inner `[`"),
        R12("cut.native-int", "C17: `nint`/`nuint` (and `IntPtr`/`UIntPtr`) in any type position, cast, `sizeof`, generic argument or member access; anchor the type token"),
        R12("cut.switch-section-decl", "C18: a statement directly in a switch section that declares a switch-block-scoped local (local declaration, or an out/pattern/deconstruction variable in an expression statement or `if` condition); anchor the statement's first token (a local function reports both C1 and C18)"),
        R12("cut.constant-condition", "C19: a C# constant condition of `if`/`while`/`do`/`for`/`?:`, a constant `when` guard or a constant switch selector (literal `true` loop conditions and `for (;;)` stay); anchor the condition's first token"),
        R12("cut.relational-and-pattern", "C20: a relational pattern (`< e`, `<= e`, `> e`, `>= e`) or an `and` pattern anywhere; anchor the operator or `and` (`or`, `not`, constant, type, declaration, `var`, discard, property, positional and list patterns stay)"),
        new("cut.base-ctor-call", "cut", "cut", "C21: a base constructor initializer `: base(...)`, with or without arguments, on any constructor; anchor `base` (C9 `: this(...)` is a separate cut; a constructor without an initializer stays)", "decided v0.2 cut C21 (2026-11-18)", ["VIB000"], MatrixOnly + "; tagged by the C10–C21 scan"),
    ];

    /// <summary>Banned and cut rows only: a program with one of these tags must be rejected with a documented ID.</summary>
    public static readonly Dictionary<string, string[]> Expected =
        All.Where(f => f.Kind is "banned" or "cut" && f.Expected.Length > 0).ToDictionary(f => f.Tag, f => f.Expected, StringComparer.Ordinal);

    /// <summary>Declaration cuts C5–C9: syntactic, so they apply in unreachable code too.</summary>
    public static readonly HashSet<string> DeclarationCuts = ["cut.preprocessor", "cut.file-type", "cut.partial", "cut.nested-in-generic", "cut.ctor-chaining"];

    /// <summary>
    /// release-7.1: the accepted ViewMove shapes (expected-accept oracle, <c>spec-rejected</c>): a C#-valid program
    /// without vib-reject tags must not be rejected on a ViewMove helper's line by every frontend.
    /// </summary>
    public static readonly HashSet<string> ExpectedAccept =
    [
        "view.move-use-dest", "view.move-reassign-source", "view.loop-reassign-move", "view.loop-reassign-call",
        "view.loop-assign-value-receiver", "view.loop-assign-value-argument", "view.assign-value-no-later-use", "view.assign-value-copy-view",
        "view.rebind-old-owner-mutated", "view.loop-rebind-old-owner-mutated",
    ];

    /// <summary>vib-reject rows: C#-valid shapes of the activation profile that SPEC rejects with these IDs.</summary>
    public static readonly Dictionary<string, string[]> ExpectedReject =
        All.Where(f => f.Kind == "vib-reject").ToDictionary(f => f.Tag, f => f.Expected, StringComparer.Ordinal);

    /// <summary>Whether a generator row belongs to <paramref name="profile"/> (default rows also run in the others).</summary>
    public static bool InProfile(Feature f, string profile) =>
        f.Anchor.EndsWith("(activation)", StringComparison.Ordinal) ? profile == "activation"
        : f.Anchor.EndsWith("(soundness)", StringComparison.Ordinal) ? profile == "soundness"
        : f.Anchor.EndsWith("(extended)", StringComparison.Ordinal) ? profile == "extended"
        : profile is not ("activation" or "soundness") || !f.Tag.StartsWith("banned.", StringComparison.Ordinal);

    /// <summary>C#-valid, Vibranium-invalid tags of the extended profile.</summary>
    public static readonly HashSet<string> VibInvalid = [.. All.Where(f => f.Kind == "vib-invalid").Select(f => f.Tag)];

    public static readonly HashSet<string> Known = [.. All.Select(f => f.Tag)];

    /// <summary>Markdown table of the catalog (used to build docs/CONFORMANCE.md).</summary>
    public static string Markdown()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var g in All.GroupBy(f => f.Kind))
        {
            sb.Append("\n#### ").Append(g.Key).Append("\n\n| Tag | Area | Feature | SPEC | Expected | Generator |\n|---|---|---|---|---|---|\n");
            foreach (var f in g)
            {
                sb.Append($"| `{f.Tag}` | {f.Area} | {f.Description.Replace("|", "\\|", StringComparison.Ordinal)} | {f.Spec} | {(f.Kind == "vib-reject" ? "C#-valid, SPEC rejects with " + string.Join(" or ", f.Expected) : f.Expected.Length > 0 ? "reject with " + string.Join(" or ", f.Expected) : f.Kind == "near-miss" ? "C#-invalid: every frontend rejects" : f.Kind == "vib-invalid" ? "C#-valid, SPEC rejects: frontends agree on IDs" : "accept; managed = Stage-0 native = self native")} | {f.Anchor} |\n");
            }
        }
        return sb.ToString();
    }
}
