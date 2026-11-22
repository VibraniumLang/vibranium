# Vibranium v0.2 Language & Compiler Specification

**Vibranium** is a strict, ownership- and borrow-checked subset of **base C# 15 (`.NET 11`)** with Rust-style compile-time memory safety and dual execution:
- **Development / Debug / Test (`dotnet build`, `dotnet run`, `dotnet test`):** Runs directly on Roslyn `5.12.0` + `.NET 11` with zero user-source or IL rewriting, full C# IDE support (Rider, Visual Studio, VS Code C# Dev Kit), step-through CLR debugging, and live ownership diagnostics via `Vibranium.Analyzer`.
- **Production / Native Release (`vib build`):** Lowers the exact same verified program through Vibranium MIR to standalone, zero-GC, zero-CLR **LLVM 18** native machine code (`clang-18 -O3`) with deterministic memory reclamation (`free`), alias-free vectorization (`noalias`), and **zero authored C runtime/shim files in the Vibranium compiler or runtime** (note: running `lake build` in `formal/` generates Lean build artifacts under `formal/.lake/build/ir/`, which are excluded from the compiler/runtime) (`vib` emits OS/libc declarations and runtime helpers directly as LLVM 18 IR in `vib_rt.ll` and `vib_std.ll`; `clang-18` links only the platform C library/CRT).

### Scope & Trust Boundary
- **Scope of Guarantees:** Vibranium's compile-time memory-safety, deterministic reclamation, and `.NET 11`-vs-LLVM 18 observable execution parity guarantees apply strictly to verified programs within the closed Vibranium v0.2 subset on the supported Linux `x86_64` native target, subject to best-effort exclusions for host out-of-memory and call-stack exhaustion. AddressSanitizer and LeakSanitizer test runs serve as empirical validation evidence alongside `formal/`'s mechanized **abstract-core metatheory** in Lean 4 (which proves ownership, exact-once linear disposal, and dual-mode bisimulation over an idealized core calculus rather than Roslyn lowering or LLVM emission).
- **Fail-Closed Trust Boundary:** Suppressing diagnostics (`#pragma warning disable`), building on `.NET` with `Vibranium.Analyzer` disabled, or mutating Vibranium objects from unverified external C# assemblies places CLR execution outside Vibranium's safety boundary. **`vib` always independently runs full frontend + MIR ownership/borrow verification and unconditionally fails closed** regardless of `#pragma` suppressions, file/directory naming, or MSBuild analyzer settings.

---

## 1. Core Design Principles & Closed v0.2 Subset

1. **Pure Base C# 15 & SDK Parity:** Every Vibranium source file is 100% valid C# 15. `Vibranium.App.props` injects implicit `System`, `Vibranium`, and `static Vibranium.Prelude` imports (plus `Vibranium.Std` and `static Vibranium.Std.ConsoleIo` when `Vibranium.Std` is referenced), enables `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` as build policy (`dotnet build` under this profile also refuses a program with any other warning, which does not make the program C#-invalid; see §4 "C# validity"), pins `<CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>` and `<InvariantGlobalization>true</InvariantGlobalization>`, promotes `CS0183`, `CS0184`, `CS8509`, `CS8519`, `CS8520`, `CS8524`, `CS8778`, `CS8793`, `CS8794`, `CS8846`, `CS9191`, `CS9192`, and `CS9193` to errors (non-exhaustive `switch` expressions are likewise rejected by `vib` regardless of `#pragma`/MSBuild settings), and links `Vibranium.Internal.VibraniumAppBootstrap` (`[ModuleInitializer]` calling `VibraniumRuntime.Initialize()`) into `Exe` builds (`skipped` as a no-op by `Vibranium.Frontend.Roslyn` / `vib`).
2. **Null-Free by Construction:** `null` literals, **nullable type annotations `T?`** (`Node?`, `int?`; conditional `c ? a : b` is permitted), and **null-forgiving postfix `x!`** (logical negation `!b` is permitted) are banned (`VIB008`). Absence and recoverable errors use zero-boxing C# 15 `[Union]` structs: **`Option<T>`** (`Some(x)` / `None`) and **`Result<T, E>`** (`Ok(x)` / `Err(e)`).
3. **Affine Memory vs. Exact-Once Linear Resources:**
   - **Memory-only owners** (`class` instances, `Vec<T>`, structs holding `!Copy` fields) are **affine**: `var b = a;` moves ownership cleanly. On `.NET`, unreachable memory is collected by GC; on LLVM, `vib` emits deterministic drop glue (`free`) at scope exit and overwrite.
   - **Side-effecting resources** (`class R : IDisposable`, plus L1 fallible wrappers `Option<R>` and `Result<R, E>` with `Copy` `E`) are **linear** with **exact-once disposal** (`VIB006`): every normal control-flow path must dispose or move them exactly once, executing `Dispose()` in identical deterministic order on `.NET` and LLVM.
4. **Shared Roslyn-Free Core & Self-Hosting:** `Vibranium.Analyzer` (`netstandard2.0` IDE/MSBuild analyzer) and `vib` (Stage-0 native compiler) invoke the exact same Roslyn-free `Vibranium.Compiler.Core`, `Vibranium.Analysis`, and `Vibranium.Backend.Llvm` pipeline, while `src/vib-self` implements the self-hosted pure-Vibranium compiler verified to fixed-point Triple-Identity (`managed.ll == stage1.ll == stage2.ll == repro.ll`).
5. **`.NET` BCL Ergonomics & Zero-Ceremony Rule:** Vibranium APIs and compiler lowerings follow standard `.NET 11` BCL names (`Vec<T>.Add`, `Span<byte>`/`ReadOnlySpan<byte>` `.SequenceEqual`/`.StartsWith`/`.EndsWith`/`.IndexOf`/`.LastIndexOf`/`.Contains`/`.Trim*`, `int.TryParse`/`long.TryParse`, `File.Exists`/`ReadAllText`/`WriteAllText`/`AppendAllText`, `ReadLine`/`WriteLine`, `this[int]`, `^k`, `a..b`, `foreach`) and pair `ReadOnlySpan<byte>` APIs with plain `string` overloads (`text.AsUtf8()`) so idiomatic C# compiles without `"..."u8` ceremony while preserving zero-GC native execution and strict borrow provenance.

### Closed v0.2 C# Subset (`VIB000`)
Vibranium v0.2 fails closed: any C# feature outside the verified subset is rejected with **`VIB000`**, including:
- Ref reassignment (`r = ref value`) targets must be ref locals (including `ref readonly` locals), not `ref`/`in`/`out` parameters; ordinary writes through mutable ref parameters and local ref carriers remain supported.
- Pinned `.NET SDK 11.0.100` compatibility: a simple assignment whose direct left-hand side is a ref-assignment expression cannot have its result consumed (for example, `int value = ((r = ref a) = 2)`); this SDK emits invalid managed IL for that shape. Discarded expression statements, genuine discard-assignment chains, `for` initializers/iterators, and void expression bodies remain supported, as do compound assignments and assignments to projected fields.
- Class inheritance (`: BaseClass`), `virtual`, `abstract`, `override`, and arbitrary interfaces (except `class R : IDisposable`; `struct S : IDisposable` is banned by `VIB000`/`VIB006`), as well as interface-typed variables, parameters, casts, or boxing.
- Object and collection initializer syntax on user types (`new S { A = 1 }`); construct values through constructors or field assignment instead.
- `object`, `dynamic`, and any boxing conversion (including `object` / `IUnion` / reference-widening patterns on `[Union]` types that lower through `.Value`).
- C# 15 shorthand `union` declarations and `closed` hierarchies, user-defined `[Union]` types, user-defined `ref struct` types, user `where T : allows ref struct` constraints, and `ref` locals bound to borrowed views (`Span<T>`, `ReadOnlySpan<T>`, `OptionRef<T>`, `OptionMut<T>`).
- User code may apply only [Mut], [return: LifetimeOf(...)], and [UnscopedRef], subject to their target restrictions. Other attributes and all assembly: / module: attributes are rejected with VIB000 outside the trusted sysroot and embedded runtime sources. This includes user applications of `[StructLayout(LayoutKind.Explicit)]`, `[FieldOffset]`, `[ExtendedLayout]`, and `[InlineArray]`. The catalog restriction also rejects `[Native]` outside the locked `Vibranium.Std.Sys` catalog.
- Direct calls to `[Obsolete(..., error: true)]` internal `[Union]` protocol members (`Value`, `HasValue`, `TryGetValue`, and `OptionRef`/`OptionMut` case constructors) or direct `.Deconstruct(...)` calls (`VIB006` on linear union wrappers, `VIB000` otherwise).
- Tuples: tuple literals, tuple types, and deconstruction declarations and assignments (by-value property getters returning `!Copy` fields are rejected separately, as illegal field moves under `VIB005`).
- `unsafe`, pointers, function pointers, `fixed`, and user `stackalloc`.
- `async`/`await`, iterator `yield`, and multi-threading APIs.
- Delegates, lambdas, closures, events, and non-`static` (capturing) local functions (`static` local functions and static/instance methods are supported).
- Exceptions (`try`, `catch`, `finally`, `throw`), finalizers (`~C()`), static constructors (`static C()`), and all `static` fields or `static` auto-properties other than `const` (including `static readonly`).
- Reflection, raw `T[]` arrays in user code (`Vec<T>`, `Span<T>`, and `ReadOnlySpan<T>` replace raw arrays), unstructured `goto`, `checked(...)` blocks/expressions, explicit `.GetEnumerator()` calls or enumerator locals/types outside `foreach`, and any unwhitelisted external BCL or P/Invoke call.
- Local functions declared directly in a `switch` section. Declare them inside a block (`case 1: { static int F() => 1; ... }`) or outside the `switch`. Diagnostic: "Local functions must be declared inside a block; declaring one directly in a switch section is not supported in Vibranium v0.2."
- Field-by-field initialization of a struct with owned fields: assigning to, or passing as `out`, a field of a struct that is not yet fully initialized when the struct owns memory directly or through nested struct fields (`Vec<T>`, `StringBuf`, class instances, `Option`/`Result` of those). Initialize such a struct in one assignment. Plain-value structs may still be initialized field by field, and fields of a fully initialized struct may be overwritten. Diagnostic: "A struct with owned fields must be initialized in one assignment; initializing it field by field (including through out) is not supported in Vibranium v0.2."
- An argument for a `ref readonly` parameter written without `in` or `ref` (`CS9192`, promoted to an error). Pass a variable as `in x` or `ref x`; a non-variable argument is `CS9193`.
- Patterns other than `var` and discard on a value whose type is a generic type parameter (`F<T>(T x) => x is 0`, `x is int`, `x is Box b`, and `switch` arms of those kinds). C# gives such patterns an implicit runtime type test that v0.2's monomorphized lowering does not model; test concrete types instead. Diagnostic: "Patterns other than var and discard on a value of generic type parameter type are not supported in Vibranium v0.2."
- Conditional compilation directives (`#if`, `#elif`, `#else`, `#endif`, `#define`, `#undef`); `.NET` and native builds must compile the same code. `#pragma`, `#region` and `#nullable` keep their meaning. Diagnostic: "Conditional compilation directives (#if, #elif, #else, #endif, #define, #undef) are not supported in Vibranium v0.2; .NET and native builds must compile the same code."
- File-local types (`file class`, `file struct`). Diagnostic: "File-local types (file class, file struct) are not supported in Vibranium v0.2; declare the type without the file modifier."
- `partial` types and members, except the locked `Vibranium.Std.Sys` `[Native]` catalog. Diagnostic: "Partial types and members are not supported in Vibranium v0.2."
- Types nested inside a generic type (types nested in non-generic types remain supported). Diagnostic: "Types nested inside a generic type are not supported in Vibranium v0.2; declare the type at namespace level."
- Constructor chaining (`: this(...)`). Diagnostic: "Constructor chaining (: this(...)) is not supported in Vibranium v0.2; initialize every field in each constructor or use a static factory method."
- A `ref readonly` parameter in any method, local function, constructor, operator or indexer. `in` parameters, `ref readonly` locals and `ref readonly` returns stay supported. Diagnostic: "ref readonly parameters are not supported in Vibranium v0.2; use an in parameter."
- Declaring any symbol named `_`: local, parameter, field, property, method, type, type parameter, label, foreach variable, or pattern/out/deconstruction designation where C# binds a real variable named `_`. Namespace `_`, using alias `_`, query range variables, and tuple element names `_` are covered too. Discards stay: `_ = e`, `out _`, `out var _`, `_` patterns, `case _`, and every place C# itself treats `_` as a discard. Diagnostic: "Declaring an identifier named _ is not supported in Vibranium v0.2; use _ only as a discard."
- An optional parameter: a parameter with a default value (`int x = 0`), or with `[Optional]` or `[DefaultParameterValue]`. Diagnostic: "Optional parameters are not supported in Vibranium v0.2; declare an overload instead."
- A named argument out of its parameter's position, meaning its position in the argument list differs from its parameter's index. Named arguments in their own position stay. Diagnostic: "Named arguments out of parameter order are not supported in Vibranium v0.2; pass arguments in parameter order."
- Any assignment whose target is `this` (simple, compound, deconstruction, and prefix/postfix `++`/`--` on `this`), and `ref this` passed or bound as a writable ref (`ref this`, `out this`). Reading `this` and field access through it stay. Diagnostic: "Assigning to this is not supported in Vibranium v0.2; assign the fields individually."
- A conditional ref expression, `c ? ref a : ref b`, in any position. Diagnostic: "Conditional ref expressions (c ? ref a : ref b) are not supported in Vibranium v0.2; use an if statement."
- A collection expression that is an element or spread operand of another collection expression, or a branch of `?:`, or an arm value of a switch expression. Diagnostic: "Collection expressions nested in another collection expression or used as a conditional or switch-expression branch are not supported in Vibranium v0.2; assign the collection to a local first."
- The native-sized integer types `nint` and `nuint`, including `System.IntPtr` and `System.UIntPtr` and their aliases, in any type position, cast, `sizeof`, generic argument or member access (`nint.MaxValue`). Diagnostic: "The native-sized integer types nint and nuint are not supported in Vibranium v0.2; use long or ulong."
- A statement directly inside a switch section (not inside a nested block) that declares a local whose scope is the switch block: a local declaration, a local function (which also reports the local-function cut above), or any out variable, pattern designation or deconstruction designation that C# scopes to the switch block, including those in an `if` or expression-statement condition, because they leak into the enclosing statement list. Pattern variables in `case` labels stay supported. Diagnostic: "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }."
- A condition that is a C# constant expression (Roslyn `ConstantValue` present, including const locals/fields, `!true`, `1 == 1`): the condition of `if`, `while`, `do`, `for` and `?:`; a `when` clause; or the governing expression of a switch statement or switch expression. Exempt: a literal `true` as the condition of `while`, `do ... while` or `for`, and `for (;;)`. Constant operands inside a non-constant condition stay. Diagnostic: "Constant conditions, guards and switch selectors are not supported in Vibranium v0.2; only while (true), do ... while (true) and for (;;) may use a constant condition."
- Relational patterns (`< e`, `<= e`, `> e`, `>= e`) and the `and` pattern combinator, anywhere a pattern appears (`is`, switch statement labels, switch expression arms, nested and property patterns). `or`, `not`, constant, type, declaration, `var`, discard, property and positional patterns stay. Diagnostic: "Relational patterns and and-patterns are not supported in Vibranium v0.2; use a when guard or an if condition."
- A base constructor initializer `: base(...)`, with or without arguments, on any constructor. Constructor chaining with `: this(...)` is covered by its own cut above. Diagnostic: "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance."
- List patterns and slice patterns (`[7, 8]`, `[var a, ..]`, `[.., var rest]`), anywhere a pattern appears (`is`, switch statement labels, switch expression arms, nested patterns), on any receiver. Diagnostic: "List patterns are not supported in Vibranium v0.2; compare the length and index the elements."
- Each cut above reports one `VIB000` per source file, at its first occurrence.

- The constructs below are outside the subset too. They are not cuts: the one-per-file rule above does not apply to them, and the compilers' messages for them may differ, because §4 compares diagnostic IDs only.
- The null-conditional operators `?.` and `?[]` and the null-coalescing operators `??` and `??=`, rejected with `VIB008` rather than `VIB000` because no Vibranium value is null. Use `Option<T>` (`IsSome`, `UnwrapOr`) for absence.
- `with` expressions (`p with { X = 2 }`, including `p with { }`). Copy the value and assign its fields.
- `sizeof(...)` expressions. Write the size as a literal.
- `nameof(...)` expressions. Write the name as a string literal.
- `params` parameters of any type (`params int[]`, `params ReadOnlySpan<T>`). Declare a `ReadOnlySpan<T>` parameter and pass a collection expression (`F([1, 2, 3])`).
- The `decimal` type and `m`-suffixed literals. Use a scaled `long` or `double`.
- C# 14 `extension(...)` blocks. Classic extension methods (a `this` parameter in a `static class`) stay supported.
- `string` operators (`==`, `!=`, `+`), constant `string` patterns (`s is "a"`) and `switch` on `string`. Compare UTF-8 bytes instead (`s.AsUtf8().SequenceEqual("a".AsUtf8())`).
- User-defined operators and conversions: static unary, binary, equality and comparison operators (`operator +`, `operator ==`) and `implicit`/`explicit` conversion operators. The C# 14 instance compound-assignment operator `void operator +=(T)` stays supported.
- Spread elements `..e` in collection expressions. Add the elements one by one (`Vec<T>.Add`).
- Explicit interface implementations (`void IDisposable.Dispose()`). Declare `public void Dispose()` on the linear type.
- `goto case` and `goto default`, which count as unstructured `goto`.
- Instance auto-properties (`{ get; set; }`, `{ get; }`) and `init` accessors. Declare a field, or a property whose accessors have bodies over a field (`[Mut] set` on a class, §3.1).
- A `using` statement on an existing variable or expression (`using (r) { ... }`). Declare the resource in the statement: `using (var r = ...) { ... }` or `using var r = ...;`.
- `checked` is the only banned overflow context: `unchecked(...)` expressions and `unchecked { ... }` blocks stay supported and change nothing, because integer arithmetic already wraps (§5.3).
- A `foreach` iteration variable marked `scoped`. Omit `scoped` from the iteration variable.
- **Entry Point:** Top-level statements (that do not reference implicit `string[] args`) or parameterless `static void Main()` / `static int Main()`; CLI arguments are read safely via `Vibranium.Std.Env.Args()`.

| Kind | Minimal example | VIB000 message |
|---|---|---|
| CLR varargs and typed references | `F(__arglist(7))`, `__makeref(n)`, `__reftype(r)`, `__refvalue(r, int)` | `'<keyword>' is not supported in Vibranium.` |
| User alias-qualified names (also in attributes) | `Mine::C.Get()`, `[Mine::Mut]` (`global::` stays supported) | `User alias-qualified names are not supported in Vibranium v0.2; use a qualified name or 'global::'.` |
| Extern alias | `extern alias X;` | `'extern alias' directives are not supported in Vibranium v0.2.` |
| Volatile field | `private volatile int n;` | `'volatile' fields are not supported in Vibranium v0.2; use an ordinary field.` |
| Required member | `public required int X;` | `'required' members are not supported in Vibranium v0.2; initialize members in a constructor.` |
| `as` expression | `x as C` | `'as' is not supported in Vibranium v0.2; use a type pattern ('x is C c').` |
| `notnull` constraint | `where T : notnull` | `The 'notnull' constraint is not supported in Vibranium v0.2; Vibranium types are never null, so omit it.` |
| User assembly attribute | `[assembly: System.Reflection.AssemblyVersion("1.0")]` | `Assembly-level attributes are not supported in Vibranium v0.2; remove the attribute.` |
| User module attribute | `[module: System.Runtime.InteropServices.DefaultCharSet(System.Runtime.InteropServices.CharSet.Unicode)]` | `Module-level attributes are not supported in Vibranium v0.2; remove the attribute.` |
| SDK assembly info | `[assembly: System.Reflection.AssemblyTitle("Demo")]` in generated AssemblyInfo | `Assembly-level attributes are not supported in Vibranium v0.2; set <GenerateAssemblyInfo>false</GenerateAssemblyInfo>.` |
| SDK target framework attribute | `[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v11.0")]` in the generated target-framework file | `Assembly-level attributes are not supported in Vibranium v0.2; set <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>.` |
| Unlisted attribute | `[System.Runtime.CompilerServices.ModuleInitializer]` on a user method | `Attribute '<bound type>' is not supported at this location in Vibranium.` |
| Explicit `init` accessor | `int X { get => x; init { x = value; } }` | `'init' accessors are not supported in Vibranium v0.2; use an explicit backing field and accessor bodies.` |
| List or slice pattern | `span is [7, ..]` | `List patterns are not supported in Vibranium v0.2; compare the length and index the elements.` |
| Standalone label | `label: WriteLine(2);` | `Labeled statements are not supported in Vibranium v0.2 ('goto' is not supported).` |
| Scoped `foreach` iteration variable | `foreach (scoped ReadOnlySpan<byte> line in "text".Lines()) { ... }` | `'scoped' iteration variables are not supported in Vibranium v0.2; omit 'scoped' from the 'foreach' variable.` |
| Named positional element in a union-case pattern | `Some<int>(value: 7)` | `Named positional elements in union-case patterns are not supported in Vibranium v0.2; drop the element name.` |

---

## 2. Type System, Generics, UTF-8 / `string` & Zero-Safety

| Kind | Base C# 15 Syntax in Vibranium | Ownership & Native Lowering (`64-bit`) |
|---|---|---|
| **`Copy` Value Types** | `sbyte`, `byte` (`i8`), `short`, `ushort`, `char` (`i16`), `int`, `uint` (`i32`), `long`, `ulong` (`i64`), `bool`, `float`, `double`, user `enum` (`int` / `i32` underlying), `string`, `Unit`, `NoneType`, `ReadOnlySpan<T>`, `OptionRef<T>`, and non-`ref` `struct`s whose fields are all `Copy` | Bitwise copy on `=`, argument pass, and return; zero drop glue. |
| **Composite `Copy` Unions/Wrappers** | `Option<T>`, `Some<T>`, `Ok<T>`, `Err<E>`, `Result<T, E>` | `Option<T>`, `Some<T>`, `Ok<T>`, `Err<E>` are `Copy` **iff** their payload type is `Copy`. `Result<T, E>` is `Copy` **iff both** `T` and `E` are `Copy`. |
| **UTF-8 Byte-Span Literals (`u8`)** | `"hello\n"u8` (`ReadOnlySpan<byte>`) | Static immutable UTF-8 byte slice (`{ ptr, i32 byte_len }` with static lifetime provenance, including non-null empty `""u8`; safe to return directly from functions). |
| **UTF-8 String Slice** | `string` (`"hello\n"`) | `Copy`, **non-zero-safe** immutable UTF-8 literal slice (`{ ptr, i64 byte_len }` in static storage; no drop glue). |
| **User `enum` Types** | `enum Mode { Read = 0, Write = 1 }` (`int` underlying; non-`int` underlying rejected by `VIB000`) | `Copy`, **zero-safe** 32-bit signed integer scalar (`i32`), supporting casts to/from integers and `switch` matching. |
| **Owned Heap Object** | Plain `class Node` (`sealed` optional) | `!Copy` (affine) single-owner non-null heap allocation (`malloc` + deterministic recursive/iterative drop + `free`). |
| **Owned Buffer / Struct** | `Vec<T> where T : unmanaged` (`Vec<int> v = [1, 2, 3];`), `struct` with `!Copy` fields (`StringBuf`, `IntMap<V>`, `StrMap<V>`) | `!Copy` (affine) inline value owning heap storage; `where T : unmanaged` implies Vibranium `Copy` (since `struct : IDisposable` is banned). |
| **Linear Resource & L1 Unions** | `class File : IDisposable`, `Option<R>`, `Result<R, E>` (`E` is `Copy`) | `!Copy` linear owner; exact-once disposal via `using var` or consumption/move by value (`VIB006`). |
| **Shared Borrowed Views** | `in T`, `ReadOnlySpan<T>` (`T` is `Copy`), `OptionRef<T>` | Stack-bound shared borrows (`ReadOnlySpan<T>` and `OptionRef<T>` are `Copy`). |
| **Exclusive Borrowed Views** | `ref T`, `out T`, `Span<T>` (`T` is `Copy`), `OptionMut<T> where T : class` | Stack-bound exclusive borrows; **`Span<T>` and `OptionMut<T>` are `!Copy`** (move on `=`, with implicit reborrowing — see §3). |

### Plain `string`, `.AsUtf8()`, Closed UTF-8 `foreach` Views & `"..."u8` Semantics (`VIB000`, `VIB008`)
- **`string` Literals & `text.AsUtf8()`:** `string` is `Copy` and **non-zero-safe** (`default(string)` and `null` are rejected by `VIB008`), originating strictly from **well-formed Unicode string literals** (lone UTF-16 surrogates `\uD800`–`\uDFFF` are rejected by `VIB000`). Calling **`text.AsUtf8()`** (`StringUtf8Extensions.AsUtf8`) yields a `ReadOnlySpan<byte>` with `'static` provenance (empty loan set): on native LLVM 18 it retags `{ ptr, i64 }` to `{ ptr, i32 }` (folding constant literals directly to pooled `@.bN` constants with zero allocation and zero copy); on `.NET 11` it uses strict UTF-8 encoding cached by string reference in a `ConditionalWeakTable<string, byte[]>` (cold call allocates once; warm calls do not allocate; zero-GC is the native LLVM 18 guarantee). Every `Vibranium.Core` and `Vibranium.Std` UTF-8 text/path/key API provides plain `string` overloads alongside `ReadOnlySpan<byte>`.
- **`"..."u8` UTF-8 Byte Literals:** Evaluate directly to `ReadOnlySpan<byte>` backed by deduplicated static global constants (`{ ptr, i32 byte_len }` with `'static` provenance, including non-null empty `""u8`).
- **`string` Restrictions Unchanged:** `string.Length`, character indexing `s[i]`, `==`/`!=`, concatenation `+`, and `$"..."` remain `VIB000` so CLR UTF-16 code units and native UTF-8 bytes never diverge (byte length is `s.AsUtf8().Length`).
- **Closed Core `foreach` Views (`Utf8Lines` & `Utf8ArgEnumerator`):** Shared-view `ref struct` carriers (`{ ptr, i32 }`, `IsCopy = true`, `IsViewLike = true`) permitted **solely** as method return values (`span.Lines()`, `StringBuf.Lines()`, `ArgList.GetEnumerator()`) and direct `foreach` collections (`VIB000` on local variables, parameters, fields, or explicit `.GetEnumerator()`/`.MoveNext()`/`.Current`; `foreach (ref var ...)` over these views is C#-invalid, `CS1510`, and every compiler rejects it). `Lines()` splits on `\r\n`, `\n`, or standalone `\r` (`""` yields 0 lines; `"a\n"` and `"a\r\n"` yield `["a"]`; `"\n"` yields `[""]`; interior empty lines preserved) via `@__vib_utf8_scan` in `vib_rt.ll`. Each yielded `ReadOnlySpan<byte>` inherits **only** the source owner's shared loan (mutating/moving the owner inside the loop is `VIB003`/`VIB001`, while a slice saved out of the loop survives loop exit so long as the owner remains live and unmutated).
- Native output writes explicit byte lengths (`write(fd, ptr, byte_len)`), never NUL-terminated `%s` / `strlen` (embedded `\0` bytes are preserved identically on `.NET` and LLVM).

### Zero-Safety & Constructor Initialization (`VIB008`)
- **Zero-safe types:** Primitives (`sbyte`, `byte`, `short`, `ushort`, `char`, `int`, `uint`, `long`, `ulong`, `bool`, `float`, `double`), user `enum` (`int` underlying), `Unit`, `NoneType`, `Option<T>` (zeroed storage represents `None`), `Span<T>`, `ReadOnlySpan<T>` (zeroed storage is an empty `{ null, 0 }` span), and non-`ref` user `struct`s whose fields are all zero-safe.
- **Non-zero-safe types:** All `class` types, `string`, `Vec<T>`, `Result<T, E>`, `Some<T>`, `Ok<T>`, `Err<E>`, `OptionRef<T>`, `OptionMut<T>`, all generic type parameters `T` (including `where T : unmanaged`), and structs containing any non-zero-safe field.
- **Initialization Rules (`VIB008`):**
  1. For any non-zero-safe type, `default` / `default(T)`, implicit zeroing `new S()` / `new T()`, `new T[n]`, and unassigned fields are compile-time errors (`VIB008`). *(Note: `Vibranium.Core` provides an explicit `public Vec()` constructor initializing `_buffer = []` so `new Vec<T>()` never produces a `null` `_buffer` on CLR, while `default(Vec<T>)` and unassigned `Vec<T>` fields are strictly rejected by `VIB008`; `Vec<T> v = [];` or `new Vec<T>(capacity)` is preferred.)*
  2. In every `class` and `struct` constructor, **all non-zero-safe fields must be definitely assigned before normal constructor exit** (`VIB008`). Throughout a constructor, even after every field is assigned, evaluating `this` as a value (passing, storing or aliasing it) or using explicit or implicit `this` as the receiver of an instance method, property or indexer is also `VIB008`; direct field reads and writes (subject to definite assignment) and calls on already-initialized fields or locals remain allowed.
  3. **Primary Constructors & Captured `<param>P` Fields (`VIB001`, `VIB004`, `VIB005`, `VIB006`, `VIB008`):** Both explicit-field primary constructors (`class Node(int v, Option<Node> n) { public int Value = v; public Option<Node> Next = n; }`) and captured primary-constructor parameters referenced inside instance member bodies (`class Counter(int start) { public int Next() => start + 1; }`, `readonly struct Scale(int factor) { public int Apply(int x) => x * factor; }`, `class Holder(Vec<int> items)`) are supported. Captured parameters lower to real MIR instance fields (`<param>P`) participating in `Copy`, `ZeroSafe` (`VIB008`), linear `IDisposable` prohibition (`VIB006`), move-out-of-`this` rejection (`VIB005`), double-move detection across field initializers + capture (`VIB001`), immutable parameter binding (`VIB004` on direct reassignment/`ref`/`out`, and `[Mut]`/`readonly` receiver enforcement on interior mutations), and deterministic LLVM 18 drop glue.
  4. **Compile-Time Modern C# 15 Code Style:** `.editorconfig` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` enforce file-scoped namespaces (`IDE0161`), primary constructors (`IDE0290`), top-level statements (`IDE0211`), and simple `using var` declarations (`IDE0063`) as build errors.

### Linear `IDisposable` & L1 Fallible Union Policy (`VIB006`, `VIB001`, `VIB000`)
- Only `class` types may implement `IDisposable` (`struct : IDisposable` is rejected by `VIB000`/`VIB006`).
- **L1 Fallible Linear Wrappers (`Option<R>` & `Result<R, E>`):**
  - A linear type `R` (a `class : IDisposable` or nested L1 linear union) may instantiate **`Option<R>`, `Some<R>`, `Ok<R>`, and `Result<R, E>`** (plus factory/move intrinsics `Prelude.Some<R>`, `Prelude.Ok<R>`, and `Prelude.Move<R>`), subject to:
    1. In `Result<R, E>`, **`E` must be `Copy`** (and non-linear; a linear resource may never appear as the error type `E`) (`VIB006`).
    2. Instantiating any other generic type (`Vec<R>`, user `Box<R>`) or any user generic method with a linear type argument (explicitly or via type inference) is rejected (`VIB006`).
  - `Option<R>` and `Result<R, E>` are themselves **linear** (`!Copy`) owners:
    - They must be consumed on every normal control-flow path via `if ([!]opt.IsNone(out var r))`, `if ([!]res.IsErr(out var r, out var err))`, `.Unwrap()`, consuming `RvalueScrutinee` pattern match (`Move(opt)`, `Open(...) switch { ... }`), passing by value, or returning (`VIB006` if unconsumed, discarded, or overwritten while live).
    - **Banned Operations on Linear Unions (`VIB006`):** Storing in fields or arrays/collections, `.UnwrapOr(...)`, `OptionExtensions.{AsRef, AsMut, Take, Replace}`, `.UnwrapMut()`, `OptionMut.*`, in-place borrow pattern matching (`PlaceScrutinee` such as `if (opt is Some<R>(var r))` without `Move(opt)`), nested subpatterns (`Some<R>({ ... })` / `Ok<R>({ ... })`), and direct `.Deconstruct(...)` calls. A `switch` statement or expression that consumes a linear union rvalue (`Open(...) switch { ... }`, `switch (Move(opt))`) may test each variant in at most one arm, with no `when` guard on a variant arm and no `not` or `or` pattern (`VIB006` at the arm); `_`, `default`, `var x` and a guarded `_` arm stay allowed, so write one arm per variant and branch inside it (`case Some<R>(var r): if (cond) { ... } else { ... }`).
- **Exact-Once Disposal:**
  1. Manual `.Dispose()` calls and `ref`/`out` aliasing of linear `IDisposable` or L1 linear union types (`in R` shared borrows of a live `IDisposable` are allowed) are banned (`VIB006`).
  2. `using var r = ...;` (or `using (var r = ...)`) on `class R : IDisposable` binds `r` as non-movable (`VIB006`) and disposes `r` deterministically in reverse lexical (`LIFO`) declaration order on every normal scope exit.
  3. Non-`using` linear locals and parameters must be consumed on every normal control-flow path by **move** (`using var _ = r;`, passing by value to a call, or returning).

### Generics & Monomorphization (`S11` — `VIB000`, `VIB005`, `VIB006`, `VIB008`)
- **Supported Constraints:** `where T : class`, `where T : struct`, and `where T : unmanaged` on user generic declarations (`Prelude.Move<T>` is the sole intrinsic carrying `allows ref struct`). Interface constraints, base-class constraints, `new()` constraints, and user `allows ref struct` constraints are rejected (`VIB000`).
- **Definition-Site Ownership:** A generic type parameter `T` is checked at the generic definition as **affine (`!Copy`), non-zero-safe, and non-linear** unless constrained with `where T : unmanaged`. Adding `where T : unmanaged` grants **`Copy`**, while keeping `T` **non-zero-safe** (so `default`, `default(T)`, and `new T()` report **`VIB008`**, and `static T Dup<T>(in T x) => x;` without `where T : unmanaged` is rejected by `VIB005`).
- **No Linear User Generic Instantiation:** Instantiating any user generic type or user generic method with a linear `IDisposable` or L1 linear union type argument is rejected (`VIB006`).
- **Monomorphization:** `vib` monomorphizes all reachable generic types and methods into concrete specializations and rejects polymorphic recursion (`F<T>` transitively instantiating `F<Wrap<T>>`) under `VIB000`.

---

## 3. Borrowing, Mutability, Pattern Matching, Core Intrinsics & `Vibranium.Std`

| Rust | Vibranium (100% Valid Base C# 15) |
|---|---|
| `let b = a;` (move) | `var b = a;` (move) |
| `Box::new(Node { val: 1, next: None })` | `new Node(1, None)` |
| `vec![1, 2, 3]`, `&mut v[1..3]` | `Vec<int> v = [1, 2, 3];`, `Span<int> s = v[1..3];` |
| `b"HTTP/1.1 200 OK\r\n"` | `"HTTP/1.1 200 OK\r\n"u8` (`ReadOnlySpan<byte>`) |
| `fn f(x: &T, y: &mut T)` | `static void F(in T x, ref T y)` |
| `let r = &mut v[0];` | `ref var r = ref v[0];` |
| `for x in &mut v { *x += 1; }` | `foreach (ref var x in v) x += 1;` |
| `&self` / `&mut self` (`class` vs `struct`) | `class`: unmarked / `[Mut]`; `struct`: `readonly` / unmarked |
| `match opt { Some(x) => x, None => 0 }` | `opt switch { Some<int>(var x) => x, NoneType => 0 }` |
| `let mut cur = &head; while let Some(n) = cur { cur = &n.next; }` | `for (var cur = head.AsRef(); cur is Some<Node>(var n); cur = n.Next.AsRef())` |
| `while let Some(n) = cur { cur = &mut n.next; }` | `while (cur is Some<Node>(var n)) cur = n.Next.AsMut();` |
| `*cur = Some(x);`, `n.next.take()` | `cur.Set(Some(x));`, `n.Next.Take()` |
| `let f = File::open(p)?;` | `if (File.OpenRead(p).IsErr(out var f, out var err)) return err;` |
| `Drop` on OS resource | `using var f = File.Create(p).Unwrap();` (or move by value) |

### 3.1 Receiver Mutability, Defensive-Copy Ban & Body Verification (`VIB004`)
- **Receiver Conventions:** Unmarked `class` methods, getters, and setters borrow `this` as shared (`&self`); `[Mut]` `class` methods, `[Mut] set` accessors, `[Mut] void operator +=(T)`, and `set` accessors of `[Mut]`-annotated properties/indexers borrow `this` exclusively (`&mut self`) (`[Mut]` on a property or indexer propagates exclusively to `set` while keeping `get` shared unless `[Mut] get` is placed on the getter itself). `readonly` `struct` methods/getters borrow `this` as shared; non-`readonly` `struct` methods, `set` accessors, and instance `void operator +=(T)` borrow `this` exclusively. Constructors initialize `this` mutably.
- **Defensive-Copy Ban:** Calling any non-`readonly` struct method, property accessor, `Vec.Slice`, `Vec.AsSpan`, `+=`, or write/`ref` indexer on a shared place (`in` parameter, `readonly` field, unmarked `class` `this` field, or shared pattern binding) is rejected with **`VIB004`** (preventing silent C# struct defensive-copy lost writes), except for intrinsic `Vec<T>` value-read indexing and value-`foreach` below.
- **`[Mut]` & `readonly` Body Verification:** The compiler verifies that bodies of unmarked `class` members and `readonly` `struct` members uphold **deep transitive immutability** on `this` (`VIB004`), catching heap mutations through nested `class` fields inside `readonly struct` methods that stock `csc` allows.

### 3.2 `Span<T>` `!Copy` Reborrowing, Indexing, Two-Phase Borrows & `foreach` (`VIB000`, `VIB002`, `VIB003`, `VIB004`)
1. **`Span<T>` vs `ReadOnlySpan<T>`:** `ReadOnlySpan<T>` is `Copy`. **`Span<T>` is `!Copy`** (`var s2 = s1;` moves `s1`), enforcing Aliasing XOR Mutability and sound LLVM 18 `noalias` vectorization. When `Span<T> s` is passed as a `Span<T>` or `ReadOnlySpan<T>` call argument, sliced (`s[a..b]`, `s.Slice(...)`), indexed (`s[i]`), or iterated via `foreach`, Vibranium **implicitly reborrows** `s` instead of moving it: parent `s` is frozen while the derived borrow is live (`VIB003`), and passing overlapping exclusive borrows in one call (`Foo(s, s)`, `Foo(s[0..2], s[1..3])`) is rejected by **`VIB002`**. Explicit `Move(s)` moves `s` without reborrowing. The value of an assignment expression is a copy or move of its destination, so using that value (`(d = e).Length`, `F(d = e)`, `a = d = e`) moves a `!Copy` destination `d`, `Span<T>` included, and a later use of `d` is `VIB001` (`int n = (s = []).Length; WriteLine(s.Length);`); a `Copy` destination such as a `ReadOnlySpan<T>` stays usable. `Span<T>` and `ReadOnlySpan<T>` elements must be `Copy`: any declaration or expression of a span type with a `!Copy` element type (including `Vec<T>.AsSpan()` of a `!Copy` `T`) is `VIB000` ("Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2.").
2. **Context-Sensitive Indexing:** `v[i]` and `span[i]` (plus `v[^k]` and `span[^k]`) in **value-read position** require only a **shared** borrow (valid on `in Vec<T>` and `ReadOnlySpan<T>`). Direct writes (`v[i] = x`), compound assignments (`v[i] += x`), and `ref`/`out` taking (`ref var r = ref v[i];`, `F(ref v[i])`, `F(out v[i])`) require an **exclusive** borrow (`VIB004` on shared places). Range slicing `v[a..b]` on `Vec<T>` lowers to `v.Slice(...)` (exclusive -> `Span<T>`); shared places slice via `v.ReadOnlySlice(start, len)` or `v.AsReadOnlySpan()[a..b]`. **Generic C# Implicit `Index` (`^k`) & `Range` (`a..b`, `..`):** On any struct or class exposing `int Length` or `int Count`, `x[^k]` lowers to `x[len - k]` (via `this[int]`) and `x[a..b]` lowers to `x.Slice(start, end - start)` (via `Slice(int, int)`), evaluating `x` once and reading `Length`/`Count` at most once (e.g., `StringBuf[^1]`, `StringBuf[..]`, `ArgList[^1]`); standalone `System.Index` / `System.Range` variables remain `VIB000`.
3. **Two-Phase Borrows & Left-to-Right Receiver Evaluation:** Assignment LHS projections (`v[i] = rhs`, `h.X = rhs`) and instance call receivers / `in` / `ref` arguments (`v.Push(v.Count)`, `a.Add(a.Read())`) evaluate their receiver/target place **strictly before** arguments/RHS in C# left-to-right order. Mutable receivers and `ref` arguments are **reserved** before argument evaluation and **activated** at the store or invocation. Between reservation and activation, **shared reads** of the same root (`v[i] = v[i - 1] + fib`, `v.Push(v.Count)`) are allowed, whereas any intervening mutation, `[Mut]` call, `ref`/`out` borrow, or move of the receiver or any prefix/base (`v.Push(v.Pop().Unwrap())`, `h.V.Push(Swap(ref h))`, `o.I.Add(SwapO(ref o))`, `a.Add(a.Drain())`, `h.Read(Swap(ref h))`) is rejected by **`VIB003`**.
4. **`foreach` Loans:** Explicit `.GetEnumerator()` calls and enumerator locals/types outside `foreach` are banned (`VIB000`). `foreach (var x in v)` holds a **shared** loan on `v` across the entire loop body (valid on `in Vec<T>`); `foreach (ref var x in v)` holds an **exclusive** loan on `v` across the entire loop body (`VIB004` on shared places; mutating or moving `v` such as `v.Push(...)` inside the loop is `VIB003`). The same holds for `foreach (var x in s)` over a `Span<T>`: it is a shared reborrow of `s`, so reads such as `s.Length` and `s[i]` in the body are valid, while writes, exclusive calls such as `Mutate(s)`, and moves of `s` are `VIB003`; `foreach (ref var x in s)` is an exclusive reborrow. The hidden enumeration view's loan remains live throughout the foreach body, including paths ending in `break`, `return`, or divergence; leaving the enumeration scope ends the loan, so uses after the loop retain ordinary NLL behavior.

### 3.3 Overlapping Call Borrows, Slot-Cursors & Lifetimes (`VIB002`, `VIB003`, `VIB007`)
- **Call Activation (`VIB003`):** A call's operands (receiver or assignment target place first, then arguments in source order) are evaluated left to right, and every loan an operand creates (`in`, `ref readonly`, `ref` and `out` arguments, shared and `[Mut]` receivers, ref projections, and by-value view arguments such as `v.AsReadOnlySpan()` or `o.AsMut()`) stays live at least until the invocation or store completes. Moving a place that equals, prefixes or extends a place under such a loan while evaluating a later operand is `VIB003`, reported once at the moving operand (`Eat(v.AsReadOnlySpan(), v)`, `Eat(ref n, n)`, `b.Absorb(b)`, `r = Move(v)` while `r` refers to `v`). An assignment to a plain local or parameter creates no loan, so `v = F(v)` stays valid. A target that needs a place evaluated before the right-hand side (a ref local or parameter, a projection rooted in a reference or heap object, an indexer, or a dereference of a returned ref) creates a reserved exclusive loan on that base, so moving the base or any prefix or extension of it on the right-hand side is `VIB003` (`h.N = Move(h)` with `h` a class, `r = Move(v)` with `ref var r = ref v`); an assignment to a field of a plain struct local (`s.F = Move(s)`) is not affected by this rule. A view returned from a call on a span `s` is a reborrow of `s`, not a fresh borrow of its owner.
- **Expanded Exclusive Set (`VIB002`):** Within a single call or construction, two receiver/argument borrows with overlapping root provenance conflict under **`VIB002`** whenever **at least one** belongs to the exclusive set: **`ref` parameters, `out` parameters, `[Mut]` class receivers, non-`readonly` struct receivers, `Span<T>` arguments, and `OptionMut<T>` arguments** (rejecting `F(out x, out x)`, `F(ref x, out x)`, `F(out x, in x)`, `F(s, s)`, etc.). Multiple shared borrows (`in`, `ReadOnlySpan<T>`, `OptionRef<T>`, unmarked class receiver, `readonly` struct receiver) may alias freely.
- **Slot-Cursor Field & Freezing Rules:**
  - `.AsMut()`, `.Take()`, and `.Replace(...)` are `this ref Option<T>` extensions and therefore require mutable **fields** (`public Option<Node> Next;`), not C# properties (`CS0206`). `.AsRef()` is a `this in Option<T>` extension whose receiver must be a storage location, not a temporary: `Make().AsRef()` is rejected (`VIB000`), while `o.AsRef()` on a local `o` stays valid.
  - `OptionMut<T>.AsRef()` creates a shared `OptionRef<T>` reborrow that **freezes** the parent `OptionMut<T>` against mutation (`Set`, `Replace`, `Take`, mutable match) and move while that `OptionRef<T>` or any derived binding remains live (`VIB003`).
  - Polonius-style per-point loan provenance tracks cursor step-reborrows (`cur = n.Next.AsMut()`, `cur = n.Next.AsRef()`), preserving original root provenance plus moved cursor loan chains (accepting `Append` and mid-list `RemoveFirst` while rejecting stale `n` access under `VIB003` and local heap-field escapes under `VIB007`).
- **Lifetimes & `[LifetimeOf]` (`VIB007`, `VIB000`):**
  - Returning a borrowed reference or view (`ref T`, `ref readonly T`, `Span<T>`, `ReadOnlySpan<T>`, `OptionRef<T>`, `OptionMut<T>`) must never escape a local variable or by-value parameter, including heap-field projections of local/by-value owners (`VIB007`); `"..."u8` literals have `'static` provenance and may always be returned.
  - **Temporaries (`VIB007`):** A temporary (a call result, conversion, collection expression, `Move(...)`, or conditional or switch expression that is not stored in a variable) can be borrowed only within the expression that creates it, including the payload bindings of the patterns that expression feeds (`WriteLine(MakeVec(7).AsSpan()[1]);`, `if (Peek(MakeNode(7)) is Some<Node>(var n)) WriteLine(n.V);`). A named variable (a local, `ref` local, `foreach` iteration variable or `is var` binding) cannot receive a reference or view whose provenance includes a temporary, and a function cannot return one: both are `VIB007` (`Span<int> s = MakeVec(7).AsSpan();`, `ref readonly Vec<int> r = ref Id(MakeVec(7));`). Bind the owner to a local first (`var v = MakeVec(7); Span<int> s = v.AsSpan();`). As a consequence, a `foreach` whose iteration variable is a reference or view into a temporary (`foreach (var line in MakeBuf(t).Lines())`, `foreach (ref var x in MakeVec(7))`) is `VIB007` in v0.2, while a by-value loop over the `Copy` elements of a temporary (`foreach (var x in MakeVec(7))`) is valid.
  - **Lifetime Elision:** Unannotated borrow-returning functions tie returned provenance to borrowed `this` when the receiver is borrowed, or else to the sole borrowed input parameter (`ref`, `in`, `Span<T>`, `ReadOnlySpan<T>`, `OptionRef<T>`, `OptionMut<T>`). Functions with zero borrowed inputs (returning non-static borrows) or multiple borrowed inputs (and no borrowed `this`) require `[return: LifetimeOf("p1", ...)]` (`VIB007`). `scoped` parameters never contribute return provenance.
  - **Verification:** `VIB007` checks every return site to ensure returned loan provenance is a subset of the named borrowed parameters (`"this"` permitted) and propagates that union of loans at call sites. Applying `[LifetimeOf]` to parameters or user types is banned (`VIB000`). `ref`/`out` parameters whose type is a borrowed view (`Span<T>`, `ReadOnlySpan<T>`, `OptionRef<T>`, `OptionMut<T>`, including via `allows ref struct` generic instantiation) are banned (`VIB000`); borrowed views leave a function only through its return value.

### 3.4 Pattern Matching & `[Union]` Rules (`VIB000`, `VIB004`, `VIB005`, `VIB006`)
- **Union `.Value` Boxing Ban:** `object`, `IUnion`, or reference-widening patterns on `[Union]` types (`opt is object x`) that lower via `.Value` and heap-box are rejected (`VIB000`). `var x` binds the union struct directly without boxing; `{ } x` on a union type is not supported (`VIB000`).
- **Rvalue Scrutinees** (`Call()`, `opt.Take()`, `Move(x)`): Consume the scrutinee as a value, and drop any active `!Copy` payload not bound by the taken arm at match end (`VIB006` if a linear variant would be dropped). In a switch on an rvalue scrutinee, a `!Copy` binding made by an arm is a shared borrow of the scrutinee (reading it is allowed, moving it is `VIB005`) when a later arm, tried when this arm's pattern or `when` guard fails, tests or binds the scrutinee again (a `Some`/`None`/`Ok`/`Err` pattern, also inside `not` or `or`, or `var x`). The binding takes ownership when every later arm is `_`, `default` or a guarded `_`, even if its own arm has a `when` guard (a payload not taken because the guard failed is dropped at match end), and when its arm has no `when` guard and is immediately followed by an unguarded arm for the other variant, with no arm after that one testing or binding the scrutinee again. Linear `Option<R>` / `Result<R, E>` transfer ownership of matched payloads without nested subpatterns. On a linear `Option<R>` / `Result<R, E>`, each variant is tested by at most one arm, without a `when` guard, and `not` / `or` patterns are banned (§2, Banned Operations on Linear Unions).
- **Place Scrutinees (`Option<T>`, `Result<T, E>`) & `OptionRef<T>`:** Borrow the scrutinee as **shared** (rejected by **`VIB006`** on linear `Option<R>` / `Result<R, E>`):
  - `Copy` payloads bind as independent copies.
  - `!Copy` payloads (`class` and `!Copy` structs such as `Vec<int>`, `StringBuf`, `IntMap<V>`, `StrMap<V>`) **unconditionally bind as shared/immutable borrows**: **`VIB004`** bans variable reassignment (`x = ...`), field writes, `[Mut]` calls, non-`readonly` struct calls (`v.Push(...)`), and `ref`/`out` aliasing; **`VIB005`** bans moving `x` (preventing CLR struct-copy lost writes / native UAF on `Option<Vec<int>>`; mutable access to `class` payloads uses `opt.AsMut()`, and mutable access to struct payloads uses `ref var v = ref opt.UnwrapMut();`).
- **`OptionMut<T> where T : class` Pattern Bindings (`Some<Node>(var n)`):**
  - Allow heap field writes (`n.Val = 1`) and `[Mut]` / `ref`-field calls (`n.Add(1)`, `n.Next.AsMut()`, `n.Next.Take()`).
  - Forbid moving `n` (`VIB005`), reassigning `n = ...` (`VIB004`), **and passing or aliasing `n` itself by `ref` or `out` (`Bump(ref n)`, `Bump(out n)`, `ref var r = ref n`)** (`VIB004`).

Property patterns are not supported in Vibranium v0.2; Option and Result cases are matched with the positional form.

### 3.5 Fail-Closed `Vibranium.Core` & `Span` Intrinsic Contract (`VIB000`, `VIB001`, `VIB005`, `VIB006`)
**Fail-Closed Rule:** Every `Vibranium.Core` or BCL member **not** listed in the table below (e.g., `Span.CopyTo`, `Fill`, `Clear`, `ToArray`, `Object.ToString`/`Equals`/`GetHashCode`, `String.*`) is rejected by **`VIB000`**, ensuring no unmodeled `readonly` method silently defaults to shared-borrow and double-frees `!Copy` payloads.

| Type / Module | Whitelisted Member / Syntax | Receiver / Arg Mode & Loan Provenance | Canonical Panic (if any) |
|---|---|---|---|
| `Prelude` | `Some(T)`, `Ok()`, `Ok(T)`, `Err(E)`, `None`, `Move(T)` (`allows ref struct`) | Consumes `T`/`E` (`Copy` copies; `Ok()` produces `Ok<Unit>`); `None` and `Unit` are zero-safe `Copy`. | — |
| `Some<T>`, `Ok<T>`, `Err<E>` | Constructors, `.Value`, `.Error` | Direct `.Value` / `.Error` **consumes** receiver (`VIB005` if non-movable `!Copy`; direct `.Deconstruct` is `VIB006` on linear, `VIB000` otherwise). | — |
| `Option<T>` | `Option(Some<T>)`, `Option(NoneType)` (implicit/explicit) | Consumes wrapper payload. | — |
| `Option<T>` | `.IsSome`, `.IsEmpty` | `shared` borrow of receiver. | — |
| `Option<T>` | `.IsNone(out var val)` | **Consumes** receiver; valid **only** in `if ([!]opt.IsNone(out var val))` guard (`VIB000` elsewhere). | — |
| `Option<T>` | `.Unwrap()` | **Consumes** receiver (`Copy` copies; `!Copy`/linear moves local or `VIB005` on place). | `"called Option.Unwrap() on None"` |
| `Option<T>` | `.UnwrapOr(T fallback)` | **Consumes** both receiver and eager `fallback`; drops unselected `!Copy` `fallback` on `Some` (`VIB006` if `T` is linear). | — |
| `Option<T>` | `.UnwrapMut()` | `exclusive` borrow of receiver -> returns `ref T` with receiver loan provenance (`VIB006` if `T` is linear). | `"called Option.UnwrapMut() on None"` |
| `OptionExtensions` | `slot.AsRef()` | `shared` borrow of `Option<T>` place -> `OptionRef<T>` tied to `slot` (`VIB006` if `T` is linear). | — |
| `OptionExtensions` | `slot.AsMut()` (`T : class`) | `exclusive` borrow of mutable `Option<T>` field/local -> `OptionMut<T>` tied to `slot` (`VIB006` if `T` is linear). | — |
| `OptionExtensions` | `slot.Take()` | `exclusive` borrow of mutable `Option<T>` field/local -> returns owned `Option<T>`, writes `None` (no drop; `VIB006` if `T` is linear). | — |
| `OptionExtensions` | `slot.Replace(Option<T> v)` | `exclusive` two-phase borrow of `slot`, consumes `v` -> returns prior owned `Option<T>` (no drop; `VIB006` if `T` is linear). | — |
| `OptionRef<T>` | `.IsSome`, `.IsEmpty`, pattern `Some<T>` / `NoneType` | `shared` borrow; `!Copy` pattern binding is shared borrow with `OptionRef` provenance. | — |
| `OptionMut<T>` (`T : class`) | `.IsSome`, `.IsEmpty`, `.AsRef()` | `shared` borrow; `.AsRef()` freezes parent `OptionMut<T>` (`VIB003`) while `OptionRef<T>`/binding is live. | — |
| `OptionMut<T>` (`T : class`) | `.Set(Option<T> v)`, `.Replace(Option<T> v)`, `.Take()`, pattern `Some<T>` / `NoneType` | `exclusive` on `OptionMut<T>`; `Set` drops overwritten `Some`; `Replace`/`Take` return prior `Option<T>` un-dropped (`VIB006` if `T` is linear). | — |
| `Result<T, E>` | `Result(Ok<T>)`, `Result(Err<E>)` (implicit/explicit), `.IsOk` | Constructors consume wrapper (`VIB006` if `T` is linear and `E` is `!Copy`, or if `E` is linear); `.IsOk` takes `shared` borrow. | — |
| `Result<T, E>` | `.IsErr(out var ok, out var err)` | **Consumes** receiver; valid **only** in `if ([!]res.IsErr(out var ok, out var err))` guard (`VIB000` elsewhere). | — |
| `Result<T, E>` | `.Unwrap()` | **Consumes** receiver (`Copy` copies; `!Copy`/linear moves local or `VIB005` on place). | `"called Result.Unwrap() on Err"` |
| `Result<T, E>` | `.UnwrapOr(T fallback)` | **Consumes** receiver + eager `fallback`; drops `!Copy` `fallback` on `Ok`, or drops `!Copy` `E` on `Err` (`VIB006` if `T` is linear). | — |
| `Vec<T>` (`T : unmanaged`) | `new Vec<T>()`, `new Vec<T>(int cap)`, `[e1, ...]` (`VecBuilder.Create`) | Returns owned `Vec<T>` (`[]` and `new Vec<T>()` allocate 0 bytes). | `"Vec capacity cannot be negative"` / `"Vec capacity overflow"` / `"out of memory"` |
| `Vec<T>` | `.Count`, `.Capacity`, `.AsReadOnlySpan()`, `.ReadOnlySlice(int, int)` | `shared` borrow (`AsReadOnlySpan`/`ReadOnlySlice` return `ReadOnlySpan<T>` tied to `Vec` loan). | `"slice out of bounds"` (`ReadOnlySlice`) |
| `Vec<T>` | `.Push(T)`, `+=`, `.Pop()`, `.Clear()` | `exclusive` borrow (`Push`/`+=` two-phase reservable; `Clear` resets `Count` to `0`). | `"Vec capacity overflow"` / `"out of memory"` (`Push`/`+=`) |
| `Vec<T>` | `.Truncate(int length)` | `exclusive` borrow (two-phase reservable); sets `Count = length` when `0 <= length <= Count`. | `"truncate out of bounds"` |
| `Vec<T>` | `.Reserve(int additional)`, `.Extend(ReadOnlySpan<T> items)` | `exclusive` borrow (two-phase reservable; `Extend` rejects `items` borrowing the same `Vec` under `VIB003`). | `"Vec capacity overflow"` / `"out of memory"` |
| `Vec<T>` | `.Resize(int length, T fill)` | `exclusive` borrow (two-phase reservable); truncates or reserves and fills with `fill`. | `"Vec length cannot be negative"` / `"Vec capacity overflow"` / `"out of memory"` |
| `Vec<T>` | `v[int i]`, `v[Index]` | `shared` on value-read; `exclusive` (two-phase reservable on `=`/`+=`) on write/`+=`/`ref`/`out`. | `"index out of bounds"` |
| `Vec<T>` | `.Slice(int, int)`, `v[Range]`, `.AsSpan()` | `exclusive` borrow -> `Span<T>` tied to `Vec` loan. | `"slice out of bounds"` (`Slice` / `Range`) |
| `Vec<T>` | `foreach (var x in v)` / `foreach (ref var x in v)` | `shared` (`var`) or `exclusive` (`ref var`) loan held across entire loop body. | — |
| `Span<T>` / `ReadOnlySpan<T>` (`T` is `Copy`) | `.Length`, `.IsEmpty`, `s[int]`, `s[Index]`, `.Slice(int)`, `.Slice(int, int)`, `s[Range]`, implicit `Span<T> -> ReadOnlySpan<T>`, `foreach` | `Span<T>` implicitly reborrows (`shared` on read/`ReadOnlySpan` conversion/`var` loop; `exclusive` on write/`+=`/`ref`/`Span` slice/`ref var` loop), freezing parent `Span<T>` (`VIB003`). | `"index out of bounds"` / `"slice out of bounds"` |
| `Option<T>` / `Result<T, E>` | `.Expect(string message)` | **Consumes** receiver (including L1 linear `Option<R>` / `Result<R, E>`); panics with exact `<message>` on `None`/`Err`. | `<message>` |
| `Option<T>` / `Result<T, E>` | `.UnwrapOrDefault()` | **Consumes** non-linear receiver (`VIB006` if linear); requires `IsZeroSafe(T)` (`VIB008` otherwise); returns payload or zeroed `default(T)`. | — |
| `Vec<T>` | `.Add(T item)` | Exact BCL alias for `.Push(T)` (two-phase reservable `exclusive` borrow). | `"Vec capacity overflow"` / `"out of memory"` |
| `StringUtf8Extensions` | `text.AsUtf8()`, `text.Lines()` | Converts `string` to `'static` `ReadOnlySpan<byte>` (`{ ptr, i32 }`) or `'static` `Utf8Lines` `foreach` view. | — |
| `Utf8SpanExtensions` | `span.Lines()` (`ReadOnlySpan<byte>` or `Span<byte>`) | `shared` borrow (reborrows `Span<byte>` as shared) -> `Utf8Lines` view tied to `span` owner. | — |
| `Stdio` | `Stdio.WriteOut(ReadOnlySpan<byte> \| string)`, `Stdio.WriteErr(ReadOnlySpan<byte> \| string)` | `shared` byte-slice / `string` read; writes exact raw UTF-8 bytes to `stdout` (`fd 1`) or `stderr` (`fd 2`). | — |
| `Prelude` / `Io` | `Write(ReadOnlySpan<byte>)`, `WriteLine(ReadOnlySpan<byte>)` | `shared` byte-slice read; writes raw UTF-8 bytes to `stdout` (plus `'\n'` for `WriteLine`). | — |
| `Prelude` / `Io` / `System.Console` | `Write(...)`, `WriteLine(...)`, `WriteLine()` (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `bool`, `string`) | `shared`/`Copy` value read; invariant ASCII decimal for signed/unsigned integers, `"True"`/`"False"` for `bool`, UTF-8 `string`, `'\n'`. | — |
| `BCL Aliases (with Std)` | `MemoryExtensions.{SequenceEqual, StartsWith, EndsWith, IndexOf, LastIndexOf, Contains}<byte>`, `int.TryParse` / `long.TryParse(ReadOnlySpan<byte> \| string, out ...)` | Aliased when `Vibranium.Std` is referenced (`VIB000` under `--no-std`) to `ByteSpanExtensions.*` and `Bytes.TryParseInt32`/`TryParseInt64`. | — |
| `VibraniumRuntime` | `Panic(string message)` | Reads `string`; diverges (`[DoesNotReturn]`), flushes `stdout`, writes UTF-8 panic to `stderr`, exits `101`. | `<message>` |

#### Consuming & Branch-Sensitive Guard Rules
1. **Consuming Intrinsics:** On `Copy` receivers, consuming intrinsics copy freely (including through `in` or fields). On `!Copy` receivers, they require an owned movable value: owned locals are marked **moved** (`VIB001` on reuse; outer scope drop suppressed), while fields, indexed elements, and borrowed places are rejected with **`VIB005`** (use `.Take()` first on non-linear mutable `Option<T>` places).
2. **`if ([!]...)` Guard Intrinsics (`IsErr` / `IsNone`):** Must appear directly as an `if (guard)` or `if (!guard)` condition with fresh inline `out var` or `out _` declarations (`VIB000` otherwise):
   - For `res.IsErr(out var ok, out var err)`: `err` is initialized **only** when `IsErr` is `true`, and `ok` is initialized **only** when `IsErr` is `false` (`VIB001` on reading the inactive binding; LLVM 18 drops **only** the active variant if unmoved at branch exit, and `VIB006` enforces exact-once consumption if `ok` is linear).
   - If one branch diverges (`return`, `break`, `continue`, `Panic`), the non-diverging branch's active binding remains initialized after the `if`; if neither branch diverges, neither binding is initialized after the `if` (`VIB001` on use, and any unmoved active `!Copy` payload is dropped before merging).
   - `opt.IsNone(out var val)` obeys the identical rule (`val` initialized **only** when `IsNone` is `false`).

### 3.6 Zero-C Standard Library (`Vibranium.Std`) & Hosted-ABI `Sys` Catalog v2 (`VIB000`)
- **Two-Tier Architecture:**
  - **T1 Pure-Vibranium Modules (`src/Vibranium.Std/`):** Verified by the full `SyntaxAndSymbolGatekeeper` and `BorrowAndMoveAnalyzer` with zero special exemptions, pairing `ReadOnlySpan<byte>` APIs with plain `string` overloads:
    - **Text & Bytes:** `Utf8` (strict RFC 3629 validation), `ByteSpanExtensions` (`SequenceEqual`, `StartsWith`, `EndsWith`, `IndexOf`, `LastIndexOf`, `Contains`, ASCII `Trim`/`TrimStart`/`TrimEnd`), `Bytes` (comparison, prefix/suffix, search, `ParseInt`/`ParseLong`, invariant BCL-exact `TryParseInt32`/`TryParseInt64` accepting `ws* [+-]? [0-9]+ ws* '\0'*`), `StringBuf` (`new StringBuf()`, `this[int]`, `Slice(int, int)`, `Lines()`, `+=` for `ReadOnlySpan<byte> | string`, `Append`/`TryAppend`/`AppendLine` for `ReadOnlySpan<byte>`, `string`, `int`, `long`, `bool`), `PathText`.
    - **Console I/O (`ConsoleIo`, imported via `using static`):** `Write(in StringBuf)`, `WriteLine(in StringBuf)`, unbuffered fd-0 byte-by-byte `ReadLine()` / `ReadLine(int maxBytes)` (default 1_048_576) -> `Result<Option<StringBuf>, IoError>` (`Ok(None)` on clean EOF, strips `\n` or `\r\n`, preserves standalone `\r`, returns `IoError(InvalidInput, IoOp.Read)` on invalid UTF-8 or immediate `IoError(FileTooLarge, IoOp.Read)` on `maxBytes` overflow), and `TryReadLine(ref StringBuf line)` / `TryReadLine(ref StringBuf line, int maxBytes)` (default 1_048_576) -> `Result<bool, IoError>`.
    - **Collections:** `IntMap<V> where V : unmanaged`, `StrMap<V> where V : unmanaged` (`Find`, `Contains`, `Get`, `Set`, `Remove` with `ReadOnlySpan<byte> | string` keys), `Interner` (`Intern(ReadOnlySpan<byte> | string)`).
    - **Filesystem & Errors:** `IoError`, `IoErrorKind`, `IoOp`, linear `File : IDisposable` (`OpenRead`, `Create`, `OpenAppend`, `Exists`, `Read`, `WriteAll(ReadOnlySpan<byte> | string)`, `SyncAll`, and one-shot `ReadAllBytes`, `ReadAllText`, `WriteAllBytes`, `AppendAllBytes`, 6-overload `WriteAllText`, and 6-overload `AppendAllText` for `ReadOnlySpan<byte> | string` paths), `Fs` (`ReadAllBytes`, `WriteAllBytes` with `ReadOnlySpan<byte> | string` paths), `PathText.Normalize(ReadOnlySpan<byte> | string, ref StringBuf)`.
    - **Environment, Process & Networking:** `ArgList` (`this[int]`, `args[^k]`, and `foreach (var arg in args)` via NUL-delimited `Utf8ArgEnumerator`), `Env` (`Args()`), `Command` (`new Command(ReadOnlySpan<byte> | string)`, `.Arg(ReadOnlySpan<byte> | string)`) & `Process` (`Run`), `Ipv4Address` (`TryParse(ReadOnlySpan<byte> | string)`), `Ipv4EndPoint`, linear `TcpListener : IDisposable` (`Bind(Ipv4EndPoint)` / `Bind(Ipv4EndPoint, int backlog)`, `Bind(ReadOnlySpan<byte> | string, int port)` / `Bind(ReadOnlySpan<byte> | string, int port, int backlog)`; default backlog 128), linear `TcpStream : IDisposable` (`Connect(ReadOnlySpan<byte> | string, int port)`, `Read`, `WriteAll`/`SendAll(ReadOnlySpan<byte> | string)`, `ShutdownWrite`).
  - **T0 Trusted Platform Leaves (`Vibranium.Std.Sys` — Hosted-ABI Catalog v2, 17 Leaves):**
    - `[Native(symbol)]` is permitted **solely** on the 17 `static partial` methods of `Vibranium.Std.Sys` in the official `Vibranium.Std` manifest (`Sys/Sys.cs`); any other use or path/name spoofing is rejected with **`VIB000`**.
    - `.NET 11` executes the managed BCL adapter (`Sys/Sys.Managed.cs`); native LLVM 18 links `vib_std.ll` against system `libc`. Return values `>= 0` denote success/counts/handles, and `< 0` encode `-(int)IoErrorKind`. `Sys.FileOpen` supports `mode == 0` (read), `mode == 1` (create/truncate), and `mode == 2` (`O_WRONLY | O_CREAT | O_APPEND | O_CLOEXEC | O_NOCTTY`); `Sys.FileExists` matches `.NET 11` `System.IO.File.Exists` on Linux (`lstat` + `S_IFLNK` `stat` fallback: returns `1` for non-directory files, FIFOs, valid symlinks-to-file, and dangling/loop symlinks, and `0` for missing paths, directories, symlinks-to-dir, NUL bytes, or permission errors).

| `Vibranium.Std.Sys` Method | Native Symbol (`vib_std.ll`) | Signature |
|---|---|---|
| `Sys.FileOpen` | `@__vib_sys_file_open` | `int (ReadOnlySpan<byte> path, int mode)` |
| `Sys.FileRead` | `@__vib_sys_file_read` | `long (int handle, Span<byte> buffer)` |
| `Sys.FileWriteAll` | `@__vib_sys_file_write_all` | `int (int handle, ReadOnlySpan<byte> data)` |
| `Sys.FileSync` | `@__vib_sys_file_sync` | `int (int handle)` |
| `Sys.FileExists` | `@__vib_sys_file_exists` | `int (ReadOnlySpan<byte> path)` |
| `Sys.StdinRead` | `@__vib_sys_stdin_read` | `long (Span<byte> buffer)` |
| `Sys.Close` | `@__vib_sys_close` | `void (int handle)` |
| `Sys.TcpListen` | `@__vib_sys_tcp_listen` | `int (int address, int port, int backlog)` |
| `Sys.TcpAccept` | `@__vib_sys_tcp_accept` | `int (int listener)` |
| `Sys.TcpConnect` | `@__vib_sys_tcp_connect` | `int (int address, int port)` |
| `Sys.SockRecv` | `@__vib_sys_sock_recv` | `long (int handle, Span<byte> buffer)` |
| `Sys.SockSendAll` | `@__vib_sys_sock_send_all` | `int (int handle, ReadOnlySpan<byte> data)` |
| `Sys.SockShutdownWrite` | `@__vib_sys_sock_shutdown_write` | `int (int handle)` |
| `Sys.SockLocalPort` | `@__vib_sys_sock_local_port` | `int (int handle)` |
| `Sys.SpawnWait` | `@__vib_sys_spawn_wait` | `int (ReadOnlySpan<byte> arena, ReadOnlySpan<int> starts)` |
| `Sys.ArgCount` | `@__vib_sys_arg_count` | `int ()` |
| `Sys.ArgCopy` | `@__vib_sys_arg_copy` | `int (int index, Span<byte> destination)` |

### 3.7 Source Validity and Runtime Control Flow

The self-hosted frontend keeps three analyses distinct; an optimized runtime edge is not automatically a C# source-validity fact:

1. **C# statement endpoint reachability:** `SourceBlockTerms` records strict source reachability for return completeness and switch-section fallthrough. It recognizes source constant expressions and proven constant-selector pattern outcomes, but does not treat an algebraically inevitable nonconstant condition as a constant expression. A call to `VibraniumRuntime.Panic` has a source continuation because its managed declaration is not a C# control-flow terminator. Reachable section endpoints without `break`, `return`, or another permitted terminating statement are invalid (`VIB000`).
2. **C# definite assignment:** `SourceValidityChecker` tracks assignment, including individual struct fields, independently of moves and loans (`VIB001`). Its graph preserves expression evaluation, pattern bindings, short-circuit condition states, and source continuations after `Panic`; exact atomic constant-pattern edges are recorded separately in `SourceAssignmentTerms`. Switch dispatch snapshots the state immediately after evaluating the selector. Each source-reachable label/arm, default, and unmatched statement exit meets that initial state, so assignments in a failed `when` guard do not initialize later labels or escape through an unmatched exit. Assignments established by a successful guard remain available in its body. For `Option`/`Result` and other `[Union]` selectors, a switch statement keeps its unmatched exit even when its labels name every variant, as in C# (`CS0161`, `CS0165`, `CS0163`), and every switch-expression arm body is checked, including a `_` arm that follows complete variant arms. Dispatch reachability stops at section/arm bodies; it must not incorporate body effects or skip binding instructions. Union variant snapshots occur after compiler-generated payload extraction, before any source guard. The strict statement-reachability graph is not substituted wholesale for the definite-assignment graph.
3. **Runtime ownership and linearity:** the ownership solver uses runtime MIR, where `Panic` diverges immediately. Its move, borrow, drop, and exact-once linear-consumption obligations apply to runtime-reachable paths. Source-only continuations and switch assignment edges do not create runtime execution or require disposal/borrow cleanup in a divergent tail. Runtime-reachable paths exclude code that C# proves unreachable (CS0162), such as a `default` section whose values earlier cases cover, a `when false` section, or an `if (false)` body, and code that Vibranium's invariants make dead: code after `Panic`, and paths reachable only through a `null` value, which valid Vibranium code cannot produce.

The source-validity pass also checks C# lexical ref-/view-safe contexts (`VIB007`), using declaration initializer contexts and source `scoped` signatures rather than substituting NLL loans or Vibranium `[LifetimeOf]` summaries. This is a source-compatibility check, not an additional runtime ownership pass. Diagnostics are emitted in their canonical category without filtering away independent ownership findings.

Span-typed collection expressions follow the C# safe-context rules. A collection expression assigned directly to a local or place (initializer, assignment, or return assignment) gets storage owned by that destination's lexical scope, so a view that outlives the destination's scope is rejected (`VIB003`, and `VIB007` when it escapes the method). Storage created anonymously inside a larger expression (an argument, a conditional arm, or a nested collection) lives for the whole method frame. Both emitters allocate this storage at function entry. v0.2 does not promote constant-element collection expressions to static data: returning one as a span (`static ReadOnlySpan<int> G() => [1, 2];`, and `[]`) is rejected with `VIB007` even where C# accepts it.

Lexical declaration identity is likewise separate from initialization: direct switch-section locals share the switch-block declaration space, as do expression variables (`out var` and pattern variables) declared in a section's statements, following C#: a duplicate name across sections is `VIB000` (`CS0128`), and a later section may assign and use the variable subject to definite assignment (`VIB001`). Reserved names/types do not assign values or execute initializers. Declaration-order checks, initializer instructions, lexical cleanup, and runtime initialization/drop flags remain effective on skipped declarations and repeated switch executions. A `using` declaration in a switch section requires an explicit enclosing block, as in C#.

These implementation invariants are validated by source-diagnostic and managed/native differential regressions. The Lean development remains an **abstract Core-MIR metatheory** over an idealized calculus; it does not mechanically verify these source analyses, either frontend's lowering, or LLVM emission.

---

## 4. Diagnostic Catalogue (`Vibranium.Analyzer` & `vib`)

| Code | Rule |
|---|---|
| `VIB000` | Unsupported C# construct or unwhitelisted `Vibranium.Core`/BCL/`[Native]` member (inheritance, arbitrary interfaces, `struct : IDisposable`, non-`int` enum underlying type, `object`/boxing, union `.Value` boxing patterns, user `[Union]`/`ref struct`, `try`/`catch`/`throw`, closures, `unsafe`, raw `T[]`, `checked`, explicit `GetEnumerator`, lone UTF-16 surrogates, `string` ops, invalid `IsErr`/`IsNone` usage, unsupported generic constraints, polymorphic recursion) |
| `VIB001` | Use of moved, consumed (`Dispose()`), or uninitialized value (including inactive `IsErr`/`IsNone` guard branch bindings) |
| `VIB002` | Overlapping borrows in a single call where at least one is exclusive (`ref`, `out`, `[Mut]` class receiver, non-`readonly` struct receiver, `Span<T>`, or `OptionMut<T>`) |
| `VIB003` | Mutation, move, exclusive borrow, or scope exit of a place while an active borrow (`ref`, `Span`, `OptionRef`/`OptionMut` reborrow, `foreach` loan, two-phase reservation, shared receiver borrow, or pattern binding) is live |
| `VIB004` | Mutation, reassignment, `ref`/`out` aliasing, `[Mut]` call, or non-`readonly` struct call through a shared borrow (`in`, `ReadOnlySpan`, unmarked `class` `this`, `readonly` `struct` `this`, `OptionRef`, `!Copy` `Option`/`Result` place pattern binding) at any depth, or `ref`/`out`/reassignment on an `OptionMut` pattern binding |
| `VIB005` | Illegal move or `!Copy` consuming call out of a field, property getter, indexed element, generic `in T` (`!Copy`), or borrowed place (use `.Take()` on mutable non-linear `Option<T>` fields/locals) |
| `VIB006` | Linear `IDisposable` / L1 linear union violation: linear resource in field/collection, invalid generic instantiation (outside `Option<R>`, `Some<R>`, `Ok<R>`, `Result<R, E>` with `Copy` `E`, and `Prelude.{Some,Ok,Move}`), banned linear union ops (`UnwrapOr`, `AsRef`/`AsMut`/`Take`/`Replace`, `UnwrapMut`, place-match, nested subpattern, `.Deconstruct`), manual `.Dispose()` call, `ref`/`out` linear aliasing, discarded linear return, overwritten live linear local, unconsumed linear local on a path, or moving a `using` binding |
| `VIB007` | Returned `ref`, `Span`, or borrowed view escapes a local or by-value owner (including heap-field projections), violates lexical ref/view escape contexts (including narrower-scope rebinding), or violates `[return: LifetimeOf(...)]` / lifetime elision |
| `VIB008` | Forbidden `null`, nullable type annotation `T?`, null-forgiving postfix `x!`, zero-initialization (`default`, `default(T)`, implicit `new S()` / `new T()`, `new T[n]`) of a non-zero-safe type (including `where T : unmanaged`), or unassigned non-zero-safe field before constructor exit / `this` escape / `this` method call |

---

**Diagnostic parity across compilers.** For every rejected C#-valid program without a subset-banned construct, `Vibranium.Analyzer`, `vib`, and the self-hosted compiler report the same set of diagnostic IDs. A program containing a construct this specification bans is rejected fail-closed: every compiler rejects it and reports that construct's own ID, while further diagnostics after that rejection may differ between compilers.

**C# validity.** Every accepted program must be both valid C# under the project's language settings and references and valid Vibranium. A genuine C# error requires rejection, even if accepting the program would not affect safety. A program is C#-invalid only if it has a C# error. The 13 diagnostics that `Vibranium.App.props` lists in `WarningsAsErrors` (§1, principle 1) are errors on every compiler, so a program that raises one of them is C#-invalid. Any other C# warning, such as an unused local (`CS0168`, `CS0219`), is not a language error: `Vibranium.Analyzer`, `vib`, and `vib-stage2` accept a program whose only C# diagnostics are such warnings, while `dotnet build` under the default `Vibranium.App.props` profile refuses it because that profile also sets `TreatWarningsAsErrors`. The official pipeline (`vib check`, `vib build`, `vib run`, `dotnet build`) validates source with Roslyn. Direct `vib-stage2` has no Roslyn prerequisite and must reject invalid C# on its own; any acceptance of invalid C# is a compiler defect. The Roslyn-based paths can report C# diagnostics or more specific Vibranium diagnostics. The self-hosted compiler need not match Roslyn's diagnostic ID or wording for invalid C#. The [invalid-program mutation oracle](MUTATION.md) measures rejection behavior.

---

## 5. MIR Drop Elaboration, LLVM 18 Lowering & Runtime Parity

### 5.1 Target ABI, Layouts & Iterative Self-Recursive Drop
- **64-Bit Target:** v0.2 supports Linux `x86_64` only. Linux `aarch64` and other native targets are planned for v0.4 and are not implemented in v0.2.
- **Heap Allocation & Drop:** `class` instances allocate via `malloc(max(1, byte_size))` (`NULL` panics with `"out of memory"`). Dropping a `class` drops its `!Copy` fields and calls `free(ptr)`.
- **Bounded-Stack Iterative Worklist Drop:** For all heap `class` graphs (single-child linked lists, multi-child trees such as `TreeNode`, and struct/union-wrapped recursive types), `vib` emits a **bounded-stack iterative worklist drop** (`[64 x { ptr, ptr }]` inline stack fast-path spilling to `malloc`/`realloc` when active frontier depth exceeds 64), reclaiming arbitrarily deep graphs in $O(1)$ C call-stack frames.
- **Spans & Loops:** Never emit LLVM `nonnull` or `dereferenceable` attributes on `Span<T>` / `ReadOnlySpan<T>` data pointers (empty spans may carry null or dangling pointers). Never emit LLVM `mustprogress` on loops (side-effect-free infinite loops are defined non-termination in C#, not UB).

### 5.2 Evaluation Order, Dynamic Drop Flags & Overwrite Drops
- **Lexical LIFO & Drop Flags:** Live `!Copy` locals drop in reverse declaration (`LIFO`) order on normal scope exits (`block` end, `return`, `break`, `continue`). Locals conditionally initialized or moved across branches or short-circuit operators (`&&`, `||`, `?:`) use `false`-initialized stack drop flags updated on initialization and move.
- **Assignment `lhs = rhs` & `out` Overwrites:**
  1. Evaluate `lhs` place operands first in C# left-to-right order (including `Vec`/`Span` index bounds check/panic before `rhs` runs).
  2. Evaluate `rhs`.
  3. Drop `lhs`'s prior `!Copy` value **iff `lhs` remains initialized/unmoved after `rhs` completes** (soundly handling `x = x;`, `x = Step(x);`, and conditional moves inside `rhs`).
  4. Store the result into `lhs` and mark `lhs` initialized.
- **Slot Writes vs Moves:** `OptionMut<T>.Set(v)` and `*ref = v` drop the overwritten value; `Take()` and `Replace(v)` move the prior value out without dropping it.

### 5.3 Arithmetic, Bitwise Shifts, IEEE-754 FP & `noalias`
- **Wrapping Integer Ops:** Default `unchecked` two's-complement wrapping for `+`, `-`, `*`, and unary `-` (`add`, `sub`, `mul` emitted **without** `nsw`/`nuw`; `byte` widens to `int` per C# numeric promotion).
- **Division & Remainder Pre-Checks:** Before LLVM `sdiv`/`udiv`/`srem`/`urem`, emit explicit branch checks so hardware `SIGFPE` / LLVM poison never executes:
  1. `divisor == 0` -> panic `"attempt to divide by zero"`.
  2. Signed `MIN / -1` **and** signed `MIN % -1` (`int.MinValue`, `long.MinValue`) -> panic `"arithmetic overflow"`.
- **Shift Masking:** Mask shift counts per C# (`count & 31` for 32-bit operands, `count & 63` for 64-bit operands).
- **IEEE-754 Floating-Point & Saturating Casts:** Strict IEEE-754 `float`/`double` semantics (no `-ffast-math` or `fast` IR flags). **Compile-time constant expressions preserve Roslyn `SemanticModel` constant-folded values** (where C# folds constant `unchecked((byte)300.0)` to `0`), whereas **runtime float-to-integer casts lower directly to `llvm.fptosi.sat.iN` / `llvm.fptoui.sat.iN` at the destination integer width** (`i8`, `i16`, `i32`, `i64`), mapping `NaN -> 0` and saturating out-of-range values to `[Min, Max]` identically to `.NET 11` runtime execution.
- **Floating-Point Remainder:** `float`/`double` `%` lowers to the internal runtime helpers `__vib_rem_f32`/`__vib_rem_f64` in `vib_rt.ll` (exact integer-significand reduction, truncating, dividend-signed result) instead of LLVM `frem`, so native binaries never need `libm` (`fmod`/`fmodf`). Production binaries depend only on `libc.so.6`.
- **LLVM `noalias`:** Emit `noalias` strictly on verified exclusive pointer parameters (`ref`, `out`, `[Mut]` `class` `this`, non-`readonly` `struct` `this`, `Span<T>`, `OptionMut<T>`, and owned `!Copy` `class`/`Vec` parameters). Never assume two shared borrows (`in`, `ReadOnlySpan<T>`, `OptionRef<T>`, unmarked `class` `this`) do not alias each other.

### 5.4 Canonical Panic Contract & Immediate Abort (`Exit 101`)
Both `.NET 11` (via `Vibranium.App.props` `VibraniumAppBootstrap` + `VibraniumRuntime`) and LLVM 18 flush `stdout`, write `"panic: " + message + "\n"` in UTF-8 to `stderr`, and terminate immediately with exit code **`101`** (`Environment.Exit(101)` / `_exit(101)`) **without unwinding or running affine drops / `IDisposable.Dispose()`** (`VibraniumRuntime.InProcessTestMode` is exclusively for in-process test hosts).

| Condition | Canonical Panic Message (`stderr`: `panic: <message>\n`) |
|---|---|
| `Vec<T>[i]`, `Span<T>[i]`, `ReadOnlySpan<T>[i]` out of bounds | `"index out of bounds"` |
| `Vec` / `Span` / `ReadOnlySpan` `.Slice(...)`, `ReadOnlySlice(...)`, or `Range` out of bounds | `"slice out of bounds"` |
| `Vec<T>.Truncate(length)` with `length < 0` or `length > Count` | `"truncate out of bounds"` |
| `Vec<T>.Resize(length, fill)` with `length < 0` | `"Vec length cannot be negative"` |
| Integer `/` or `%` by `0` | `"attempt to divide by zero"` |
| Signed integer `MIN / -1` or `MIN % -1` | `"arithmetic overflow"` |
| `Option<T>.Unwrap()` on `None` | `"called Option.Unwrap() on None"` |
| `Option<T>.Expect(msg)` on `None` | `<msg>` |
| `Option<T>.UnwrapMut()` on `None` | `"called Option.UnwrapMut() on None"` |
| `Result<T, E>.Unwrap()` on `Err` | `"called Result.Unwrap() on Err"` |
| `Result<T, E>.Expect(msg)` on `Err` | `<msg>` |
| `new Vec<T>(capacity)` with `capacity < 0` | `"Vec capacity cannot be negative"` |
| `Vec<T>` `.Push`, `+=`, `.Reserve`, `.Extend`, `.Resize`, or `new Vec<T>(cap)` exceeding `Array.MaxLength` (`0x7FFFFFC7`) or byte-size overflow | `"Vec capacity overflow"` |
| Heap allocation failure (`malloc`/`realloc` `NULL` or CLR `OutOfMemoryException`; best-effort) | `"out of memory"` |

---

## 6. Pure C# / Vibranium + Pure LLVM 18 Repository Layout (All 15 Projects)

```text
Vibranium.slnx (.NET 11.0.100 / C# 15 + LLVM 18.1.3 — 15 Projects, Zero C Source/Shim Files)
├── global.json, .editorconfig, Directory.Build.props & Vibranium.App.props
├── src/
│   ├── Vibranium.Core/                   # [1] Core types ([Union] Option/Result, OptionRef/OptionMut, Vec<T>, Unit, [Mut], [LifetimeOf], [Native], Prelude, Io, Stdio, Panic, Bootstrap, vib_rt.ll)
│   ├── Vibranium.Std/                    # [2] Zero-C standard library (17-leaf Vibranium.Std.Sys T0 Catalog v2 + Sys.Managed.cs + vib_std.ll + T1 Utf8/Bytes/ByteSpanExtensions/StringBuf/ConsoleIo/IntMap/StrMap/Fs/File/Env/Process/Net)
│   ├── Vibranium.Compiler.Core/          # [3] Roslyn-free CFG MIR & canonical diagnostics (VIB000–VIB008)
│   ├── Vibranium.Analysis/               # [4] Roslyn-free Polonius flow-sensitive move, NLL borrow, two-phase borrow, escape provenance & L1 linear solver
│   ├── Vibranium.Frontend.Roslyn/        # [5] Roslyn 5.12.0 syntax/symbol gatekeeper & ControlFlowGraph -> MIR lowerer
│   ├── Vibranium.Analyzer/               # [6] netstandard2.0 live IDE/MSBuild Roslyn DiagnosticAnalyzer
│   ├── Vibranium.Backend.Llvm/           # [7] Pure textual LLVM 18 IR emitter (iterative drop glue, unpacked Span noalias vectorization)
│   ├── vib/                              # [8] Stage-0 Roslyn native compiler CLI (`vib check`, `vib emit-ll`, `vib build`, `vib run`)
│   └── vib-self/                         # [9] Self-hosted pure-Vibranium compiler (`VibraniumSelfCompiler`: `vib-stage1` / `vib-stage2`)
├── samples/
│   ├── demo/                             # [10] End-to-end language & linear resource showcase (`Demo`)
│   ├── benchmark/vibranium/              # [11] 4-way native compute benchmark (`VibraniumBenchmark`)
│   ├── http-server/vibranium/            # [12] 4-way zero-GC HTTP/1.1 server (`VibraniumHttpServer`)
│   └── bad/                              # Negative compile-time safety examples (VIB001, VIB003, VIB004, VIB006)
├── tests/
│   └── Vibranium.Tests/                  # [13] Differential (.NET 11 vs LLVM 18 + ASan/LSan + SIMD), Vibranium.Std, L1, self-host & analyzer parity suite
├── eng/conformance/                      # [14] Differential conformance fuzzer (`Vibranium.Conformance`, see docs/CONFORMANCE.md)
├── eng/mutation/                         # [15] Invalid-program mutation oracle (`Vibranium.Mutation`, see docs/MUTATION.md)
├── eng/bootstrap/bootstrap.sh            # Deterministic Triple-Identity (`managed.ll == stage1.ll == stage2.ll == repro.ll`) verifier
└── formal/                               # Mechanized Lean 4 (`VibraniumFormal`) soundness & bisimulation proofs (zero sorry)
```

---

## 7. `v0.3` Tier-B Roadmap

The following ergonomic and standard-library extensions are scheduled for Vibranium **`v0.3`** (building on the Tier-A BCL ergonomics & plain-`string` foundation):

1. **`[InterpolatedStringHandler]` String Interpolation (`$"..."`):**
   - `StringBuf` acts as its own buffering `[InterpolatedStringHandler]` (`StringBuf s = $"...";`, with `buf.Append($"...")` binding `Append(in StringBuf)` via a temporary so `{buf.Length}` / `{buf[..]}` reads pre-append state without self-borrow conflict), while `Prelude.Write`/`WriteLine` uses a zero-size streaming handler writing chunks directly to `stdout`. Supported holes: `int`, `long`, `uint`, `ulong`, `bool`, `string`, `ReadOnlySpan<byte>`, and `in StringBuf` (no `:format` / `,alignment` clauses; `Console.WriteLine($"...")` remains `VIB000`).
2. **Fluent `ref StringBuf` Method Chaining (`buf.Append(n).Append(". ").AppendLine(line)`):**
   - Paired with `vib-self` support for chained `ref`-returned struct receivers.
3. **General User-Defined `ref struct` Views, Custom `foreach` Enumerators, `Split(byte)` Views & First-Class `Range`/`Index` Values:**
   - Multi-field view loan provenance in `BorrowAndMoveAnalyzer` and `OwnershipChecker`.
4. **Buffered `StdinReader`, `File.Delete`/`Move`, Directory Enumeration & DNS Hostname Resolution.**
5. **Span/`string` `==` Operator Sugar, `switch` on UTF-8 Spans, `char` Overloads, Unicode-Aware Trim/Case Folding & Generic `MemoryExtensions<T>` for Non-`byte` Element Types.**
6. **Extended Number Formatting/Parsing (Hex, Padding, Unsigned, Floats) & Closure-Free `Option`/`Result` Combinators.**
