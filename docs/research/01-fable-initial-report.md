**Verdict: Vibranium is feasible, and a convincing end-to-end prototype fits in one session. I spiked the risky building blocks on this machine and they all work. The honest framing: Vibranium is a new systems language that reuses C#'s syntax, type checker, IDEs and debug runtime. It is not a way to make existing C# code fast.**

Nothing was written to the main repository during this initial feasibility spike; throwaway spikes were run in an isolated scratch directory.

---

## 1. Feasibility and prior art

### Why it is worth doing
- **The inner loop is C#'s.** Incremental Roslyn builds, the .NET debugger and test runners need nothing new, because an Vibranium project is literally a C# project plus an analyzer and a small library. LLVM cost is paid only for release builds.
- **The checker runs inside the compiler the IDE already hosts.** Verified: a netstandard2.0 analyzer produced `error VIB001` through plain `dotnet build`, and `#pragma warning disable` could not suppress it.
- **The managed build is a free oracle.** If the borrow checker has a bug, the CLR run is still memory-safe, so "run both and diff" is a cheap, strong test.
- **Versus NativeAOT:** no GC and no runtime. A trivial hand-written IR program linked here to a 16 KB binary depending only on libc; NativeAOT hello-world is around a megabyte (from memory, not measured).
- **Gradual adoption:** an Vibranium library is also an ordinary .NET assembly, usable from any managed app.

### What it costs (candid)
- **Existing C# does not qualify.** `string` manipulation, `List<T>`, LINQ, async, delegates and closures, exceptions, and object graphs with back-pointers are all outside the subset at first. Vibranium code cannot call ordinary .NET libraries, because they need the GC. The standard library starts from zero.
- **Two implementations of one semantics must agree.** This is the main technical risk.
- **Borrow-checker soundness is on us,** without Rust's decade of hardening.
- **C# has no syntax for some things Rust has:** no `?` operator, no sum types, no mutability marker on class methods.

### Lessons from prior art
| Project | Relevant fact | Lesson for Vibranium |
|---|---|---|
| **Unity Burst / HPC#** | A C# subset compiled through LLVM with `[NoAlias]`, no GC; shipped at scale. Safety is runtime checks in editor builds, removed in release. Only unmanaged structs. | Closest precedent; proves the backend half. Vibranium's addition is static checking and owned heap types. |
| **Midori (M#)** | C# plus permissions (`isolated`, `immutable`, `readonly`), AOT compiled, still GC'd. Error model: fail-fast for bugs, checked errors for recoverable failures. `Span<T>` and ref returns descend from it. | Forking the language created an island; only pieces upstreamed into C# survived. Stay a strict subset. Adopt its error model. |
| **Project Verona** | Ownership of regions (whole object graphs), not single objects. | Single-owner trees make graph-shaped OO code painful. Plan an arena/region type later. |
| **Hylo, Swift** | Hylo's four parameter conventions (`let`, `inout`, `sink`, `set`) with no first-class references and no lifetime annotations. Swift's exclusivity rule and non-copyable types. | C#'s `in`, `ref`, by-value and `out` are exactly Hylo's four. Parameter-only borrows need no lifetime analysis: a sound first layer. |
| **C# 11–13** | `ref` fields, `scoped`, `[UnscopedRef]`, `ref struct` interfaces, `allows ref struct`, `ref readonly` parameters, inline arrays. C# chose `scoped` over named lifetimes. | The borrowing syntax exists. The analysis does not (section 2). |
| **SPARK, Austral** | SPARK added ownership checking to an Ada subset via a separate analyzer. Austral uses linear types with explicit destruction. | Precedents for the "subset plus checker" shape and for the resource rule below. |

---

## 2. The hardest trap: CLR and LLVM must behave identically

### The one design rule
**Vibranium never changes what a C# program means; it only rejects programs.** No rewriting, no IL weaving, no hidden bookkeeping in debug builds. The checker accepts only programs where C#'s behaviour (copy structs, alias classes, collect garbage eventually) cannot be told apart from move-and-free. Two obligations do the work:

1. **After a non-copyable value is assigned or passed by value, the source is never read again.** Then a C# struct copy, a C# reference alias and a native move are indistinguishable, and the CLR does nothing special.
2. **Nothing observable happens implicitly when a value dies.**
   - A *memory-only type* has no `Dispose` anywhere inside it. Its death is invisible: the CLR lets the GC take it, native code calls `free`. No syntax needed. This covers most types (vectors, trees, maps).
   - A *resource type* implements `IDisposable` or contains one. It must be declared with `using` or be moved or disposed on every path. Both runtimes then execute the same explicit C# statement.

Representation follows directly:
- **Owned heap value:** an instance of a `sealed class`, with exactly one owner. Optional is `T?`.
- **Move-only value:** any struct with an owned field (class reference or array) or with `Dispose`.
- **Copyable:** primitives and `unmanaged` structs without `Dispose`.
- No `Box<T>` wrappers and no runtime "moved" flag.

Alternatives I rejected:
- Handle structs with runtime flags: `using` on everything, `.Value` hops.
- Rewriting debug builds to insert drops: what you debug is no longer what you wrote, and Hot Reload is at risk.
- Finalizers: nondeterministic.
- Raw unmanaged pointers, as Burst does: needs `unsafe`.

### Divergence vectors
| Vector | Plain C# on the CLR | Vibranium rule that makes native identical |
|---|---|---|
| Move vs copy vs alias | Copies structs, aliases classes | Use after move is an error |
| Hidden struct copies | Calling a mutating method through `in` silently mutates a copy (verified) | Rejected |
| Shared borrow of a class | `in Node n` still allows `n.Value = 1` | `in` is deep: no writes or `[Mut]` calls at any depth |
| Memory reclamation | GC, eventually | Invisible by rule 2. Banned: finalizers, weak references, `GC.*`, default `GetHashCode` |
| Destructors with effects | Run only via `using` or an explicit call | Resource types must be `using` or consumed on all paths. A `using` variable cannot be moved (C# already forbids `ref` and assignment: CS1657, CS1656) |
| Owner of a resource | Nothing automatic | The owning type must implement `Dispose` and dispose its fields (CA2213 promoted to an error) |
| Overwriting an owning field | Old object becomes garbage | Memory-only: native frees the old value. Resource: the slot must be empty, or use `Mem.Replace` |
| Generics | Reified at run time | Monomorphized; same semantics once reflection, `typeof(T)` tests, generic virtual methods and unbounded polymorphic recursion are banned. `unmanaged` constraint is `Copy`. Type parameters are assumed memory-only unless marked to accept resources |
| Null | NRT warnings, with holes | NRT as errors, plus bans on `!`, `default` of non-nullable classes, and `new T[n]` for non-nullable class `T` |
| Panics | Exception unwinds and runs `finally` | Abort on both sides. Verified: an `UnhandledException` hook calling `Environment.Exit(101)` exits before any `finally` runs |
| Recoverable errors | Exceptions | No `throw`/`catch` in v1. Use `bool TryX(out T)` or a `Result<T,E>` struct. Later: Midori-style checked errors |
| Integer overflow | Depends on `checked` context and a project option | Roslyn exposes `IsChecked` per operation; the backend honours it. Also check divide-by-zero and `MinValue / -1`, mask shift counts, saturate float-to-int (I believe .NET 9+ does) |
| Bounds | `IndexOutOfRangeException` | Same checks, panic. LLVM hoisted three per-iteration checks to one in my test |
| Strings and formatting | UTF-16, culture-sensitive | v1: literals only, printed through Vibranium.Core. Owned text is a library type later |
| Evaluation order | Strict left to right | Taken from Roslyn's control-flow graph |
| Statics, reflection, `object`, boxing | Lazy init order, identity hashes | Banned in v1 |

What cannot be removed: stack depth, out-of-memory point, timing, thread interleavings. One sharp case: native drop of a long linked list recurses and can overflow the stack where the GC would not. Because memory-only drop order is invisible, the compiler may emit iterative drop glue.

### Borrowing
Syntax mapping: `in T` is `&T`, `ref T` is `&mut T`, by-value is a move, `out T` is initialise-only. `ref readonly` and `ref` locals are local borrows. `ReadOnlySpan<T>` and `Span<T>` are slices. A `ref struct` is a type with one implicit lifetime.

**C#'s own ref-safety rules are not enough.** They only protect stack memory, because C# assumes the GC keeps heap memory alive. Verified: plain C# compiles all of these with warnings as errors:
- `ref int first = ref v.At(0); v.Push(2); first = 99;` — on the CLR the write lands in the old array and is silently lost; natively it is a use-after-free.
- `static Span<int> Leak() { var a = new int[3]; return a; }`
- `static ref int FieldOf(Node n) => ref n.Value;`
- `Two(ref x, ref x)`

C# did reject returning an `[UnscopedRef]` ref to a local struct (CS8168) and a `stackalloc` span (CS8352). So Vibranium reuses C#'s syntax but needs its own complete analysis.

**No lifetime annotations in v1.** The rule is C#'s:
- A returned `ref` or span may borrow from every `ref`, `in` or ref-struct argument not marked `scoped`.
- It borrows from a struct receiver only if the method has `[UnscopedRef]`.
- It always borrows from a class receiver (C# forbids `[UnscopedRef]` there: CS9101).

`scoped` says "the result does not borrow from this one", which covers the common multi-argument case. What this cannot express:
- Borrows stored in heap objects (C# forbids ref fields in classes).
- Two distinct lifetimes in one ref struct.

Reserve `[Lifetime("a")]` for later; do not build it now.

**How equivalence is kept honest:**
- The subset is a whitelist; anything unlisted is an error.
- Vibranium.Core is written in Vibranium on a tiny intrinsic kernel.
- Every test runs on both backends and is diffed.
- Native test builds run under AddressSanitizer.

---

## 3. Surface design

This exact file compiles as C# 13 with nullable enabled and warnings as errors, and runs on .NET 10. It prints `110 10 2 1 8 open 1 open 2 open 3 finishing close 3 close 2 close 1`. It needs only a 15-line support file (`Mem.Take`, `Io.Print`, `[Mut]`).

```csharp
using System;
using System.Diagnostics.CodeAnalysis;
using Vibranium;

sealed class Node                               // class = single owner, heap allocated   (Rust: Box<Node>)
{
    public int Value;
    public Node? Next;                          // owned, optional                        (Option<Box<Node>>)
    public Node(int value, Node? next) { Value = value; Next = next; }
}

struct IntVec                                   // struct with an owned field = move-only (Vec<i32>)
{
    int[] _buf; int _len;
    public IntVec(int capacity) { _buf = new int[capacity]; _len = 0; }
    public readonly int Count => _len;                                   // &self
    public void Push(int x)                                              // &mut self
    {
        if (_len == _buf.Length)
        {
            var bigger = new int[_buf.Length * 2 + 4];
            _buf.AsSpan(0, _len).CopyTo(bigger);
            _buf = bigger;                      // old buffer unreachable: native build frees it here
        }
        _buf[_len] = x; _len += 1;
    }
    [UnscopedRef] public ref int At(int i) => ref _buf.AsSpan(0, _len)[i];          // &mut self -> &mut i32
    [UnscopedRef] public readonly ReadOnlySpan<int> Items => _buf.AsSpan(0, _len);  // &self -> &[i32]
}

sealed class Tracer : IDisposable               // destructor with visible effects: must be `using` or moved
{
    readonly int _id;
    public Tracer(int id) { _id = id; Io.Print("open ");  Io.Print(_id); Io.Line(); }
    public void Dispose()  {          Io.Print("close "); Io.Print(_id); Io.Line(); }
}

sealed class Stack<T>                           // generic: reified on the CLR, monomorphized natively
{
    sealed class Cell { public T Item; public Cell? Below; public Cell(T item, Cell? below) { Item = item; Below = below; } }
    Cell? _top;
    [Mut] public void Push(T item) { _top = new Cell(item, Mem.Take(ref _top)); }
    [Mut] public bool TryPop([MaybeNullWhen(false)] out T item)
    {
        var cell = Mem.Take(ref _top);          // move the top cell out of the field, leaving null
        if (cell is null) { item = default; return false; }
        _top = cell.Below;                      // move out of a cell we own
        item = cell.Item;
        return true;                            // the emptied cell is freed here in the native build
    }
}

static class Program
{
    static int Sum(in Node? head)               // shared borrow of the whole list
    {
        int total = 0;
        ref readonly Node? cur = ref head;
        while (cur is not null) { total += cur.Value; cur = ref cur.Next; }
        return total;
    }
    static void Bump(ref Node node, int by) { node.Value += by; }             // exclusive borrow
    static Node Prepend(Node? tail, int value) => new Node(value, tail);      // takes and returns ownership
    static void Finish(Tracer t) { using var owned = t; Io.Print("finishing\n"); }
    static void Saxpy(Span<float> y, ReadOnlySpan<float> x, float a)          // native: both pointers noalias
    { x = x.Slice(0, y.Length); for (int i = 0; i < y.Length; i++) y[i] += a * x[i]; }

    static int Main()
    {
        Node? list = null;
        for (int i = 1; i <= 4; i++) list = Prepend(list, i);   // moved in, new owner moved back
        if (list is not null) Bump(ref list, 100);
        Io.Print(Sum(in list)); Io.Line();

        var v = new IntVec(2);
        for (int i = 0; i < 10; i++) v.Push(i * i);
        ref int first = ref v.At(0);            // exclusive borrow of v, alive until the last use of `first`
        // v.Push(1);                           // Vibranium error: v is borrowed by `first`
        first = 7;
        var w = v;                              // move
        // Io.Print(v.Count);                   // Vibranium error: use of moved value `v`
        Io.Print(w.Count); Io.Line();

        var st = new Stack<int>(); st.Push(1); st.Push(2);
        while (st.TryPop(out var top)) { Io.Print(top); Io.Line(); }
        Span<float> ys = stackalloc float[4]; ReadOnlySpan<float> xs = [1f, 2f, 3f, 4f];
        Saxpy(ys, xs, 2f); Io.Print((int)ys[3]); Io.Line();

        using var a = new Tracer(1);
        {
            using var b = new Tracer(2);
            var c = new Tracer(3);
            Finish(c);                          // ownership moves to the callee, which closes it
        }                                       // b closed here
        return 0;
    }                                           // a closed here; list and w freed here in the native build
}
```

Other mappings not shown:
- `[InlineArray(N)]` struct for `[T; N]`.
- Interface constraints with `static abstract` members for traits.
- `ref struct` with a ref field and `Dispose` for lock guards.
- Attributes on type parameters (`<[Send] T>`) for thread-safety markers.

I compiled all of these.

**Warts where C# has no syntax:**
- Class methods need a `[Mut]` attribute for `&mut self`; structs use C#'s built-in `readonly`.
- A resource parameter needs `using var owned = t;`.
- Moving out of a field needs `Mem.Take`.
- No `?` operator and no sum types.

### Implementation language: C# on Roslyn, not Rust
This is not close.
- **The semantics are defined by Roslyn.** Overload resolution, generic inference, nullable flow, definite assignment and ref safety would take years to reimplement in Rust and would drift forever. "Strict subset" would stop being a guarantee.
- **IDE squiggles require a .NET assembly** loaded into the compiler process. A Rust checker would need a side-car process and a second copy of the rules.
- **One analysis, two consumers.** A single library takes a method's control-flow graph and returns diagnostics plus facts: which reads are moves, where each value dies, which parameters cannot alias. The analyzer reports the diagnostics; the backend generates code from the facts and never re-derives ownership.
- **Roslyn's public control-flow graph is close to Rust's MIR.** Verified: it lowers `using` to try/finally with the `Dispose` call, lists the locals that die on each edge (`LeavingRegions`), and marks `using` locals.
- **LLVM needs no bindings.** Emit textual IR from C# and run `clang-18`.

Costs of this choice:
- Roslyn's fully lowered tree is internal, so Vibranium must lower any sugar it admits (`foreach`, patterns, interpolation, later closures).
- The analyzer must be per-method and fast, which forces everything cross-method into signatures. That is good discipline anyway.
- Dependencies must ship source or serialized bodies for monomorphization.

---

## 4. Prototype blueprint (one session)

### Layout
```
Vibranium.sln
src/Vibranium.Core/     net10.0: [Mut], Mem.Take/Replace, Io.Print, panic hook in a [ModuleInitializer]
src/Vibranium.Check/    netstandard2.0, Roslyn 4.14.0: subset whitelist + analysis + DiagnosticAnalyzer (one assembly)
src/vib/         net10.0 exe, references Vibranium.Check: `check` and `build` (emit .ll, run clang-18)
runtime/vib_rt.c  vib_panic (prints "panic: ...", exit 101), print helpers
samples/demo/      valid program; csproj references Vibranium.Core and the analyzer
samples/bad/       one file per rejected bug
tests/run.sh       the evidence script
```

### Language slice
- **Types:** `int`, `long`, `bool`, `float`; string literals for printing; sealed classes and structs with fields, constructors, methods; `T?`; `int[]` and `float[]`.
- **Statements and expressions:** `if`, `while`, `for`, `return`, `using var`, calls with `in` and `ref`, `ref` locals, `Mem.Take`.
- **Stretch, in this order:** `Span<T>`, one generic class monomorphized, DWARF line info.

### Analysis (shared by analyzer and `vib`)
A *place* is a local, parameter or `this`, plus a field path. Class fields count as part of the owner's path, since single ownership makes object graphs trees. All array elements are one step.

| ID | Check |
|---|---|
| VIB000 | Construct or compiler option outside the whitelist |
| VIB001 | Use after move. Forward dataflow of moved places. A by-value read of an owning place as initializer, assignment source, by-value argument or return is a move; whole assignment re-initialises |
| VIB002 | In one call, two of {receiver, `ref` args, `in` args} overlap and at least one is exclusive |
| VIB003 | A `ref` or span local holds a loan on the places it came from, for as long as the local is live (backward liveness). Writing, moving, exclusively borrowing or leaving the scope of a loaned place while the loan is live is an error |
| VIB004 | Write or `[Mut]` call through `in`, `ref readonly`, or a non-`[Mut]` `this`, at any depth |
| VIB005 | Move out of a field without `Mem.Take` |
| VIB006 | Resource value neither `using` nor consumed on all paths; or a `using` variable moved |
| VIB007 | Returned `ref` or span derived from a local or by-value parameter |

### Code generation, from the same control-flow graph
- One LLVM block per graph block. Locals and temporaries are allocas; `mem2reg` cleans up.
- Class: `malloc`'d struct; `T?` is a nullable pointer. Struct: LLVM struct value. Array: `malloc`'d length plus data.
- **Move:** load, then store zero into the source. Drop glue tolerates zero, so "drop every owning local on every scope-exit edge" is always correct and LLVM deletes the dead ones. Also drop the old value when overwriting an owning place.
- **`using`:** the graph already contains the `Dispose` call in a finally region. Run it on each edge that lists it in `FinallyRegions` (selector variable plus `switch`).
- **Attributes:** `ref`, `in` and owned pointer parameters get `noalias nonnull noundef`; `in` adds `readonly`. All functions `nounwind`; allocations return `noalias`.
- **Checks:** bounds, overflow where `IsChecked`, division; all call `vib_panic`.
- `--asan` adds `sanitize_address` to every function.

### Evidence the script must produce
1. `dotnet build` and `dotnet run` on the demo: zero diagnostics, output A.
2. `vib build` then run: output B. `diff A B` is empty and exit codes match, including a panic sample (both print `panic: index out of bounds`, exit 101).
3. `ldd` shows libc only; print binary size.
4. IR excerpts: `noalias` on parameters, `call void @free` at drop points, drop glue, no runtime symbols.
5. ASan and leak-checker run of the native demo: zero reports.
6. Each bad sample fails with the expected ID from `dotnet build` and from `vib check`, with identical lists.
7. **Negative control:** compile the stale-reference sample natively with the borrow check disabled and ASan on, showing a real heap-use-after-free, while the same code under `dotnet run` silently prints a wrong value. This shows the checker is load-bearing.
8. `opt-18 -O3` on `Saxpy`: no `vector.memcheck` block.

### Order and cut lines
1. Wiring: analyzer in `dotnet build` and `vib check` give the same VIB001 list.
2. Backend for ints, control flow, calls, classes, printing; managed and native outputs match.
3. Moves, drops, drop glue, arrays, `using`; ASan clean.
4. `in`/`ref` with `noalias`; VIB002–007.
5. Evidence script.

If time runs short, cut generics first, then spans (show `noalias` on `ref` parameters instead), then VIB005 and VIB007. Rough size: 2,000–2,500 lines of C#.

### Gotchas found while spiking
- `OperationBlockAnalysisContext.GetControlFlowGraph` must be given the exact block from `OperationBlocks`; passing its parent throws.
- Tag diagnostics `WellKnownDiagnosticTags.NotConfigurable`, or a pragma can silence them.
- An analyzer built against Roslyn 4.14.0 loads in SDK 10.0.301 (Roslyn 5.6). It parses C# up to 13, so pin `LangVersion` 13.
- `vib` can build its compilation from `TRUSTED_PLATFORM_ASSEMBLIES`.
- `clang-18` needs `-Wno-override-module` for `.ll` input.
- Checked integer arithmetic adds panic exits that block vectorization; use `float` or `unchecked` in the showcase loop.
- Print through `Console.Out.Write` with explicit `\n`, and avoid printing floats: formatting will not match C's.

---

## What I verified on this machine
- The section 3 file compiles and runs as shown.
- Plain C# accepts the four aliasing and lifetime bugs listed in section 2, and rejects the stack cases (`traps/`).
- Roslyn's control-flow graph exposes scope exits, `using` lowering and ref-kind of locals (`cfg/`).
- A 104-line use-after-move analyzer flagged 5 of 5 buggy methods and 0 of 3 correct ones, covering branches, loops and re-initialisation (`spike/`). It also ran through real `dotnet build` as a non-suppressible error (`wire/`).
- LLVM 18 `noalias` test (`llvm/`): the vectorized `Saxpy` loses its runtime overlap check (optimized IR 98 lines vs 161).
- ASan and the leak checker work on `.ll` input.
- Panic hook exits 101 without running `finally` (`panic/`).
- nuget.org is reachable; the local NuGet cache is empty; valgrind is not installed (use ASan).

Not verified: IDE behaviour in Rider, VS or VS Code (expected to work, since nothing is rewritten); NativeAOT size; the .NET 9 float-to-int claim.

## Decisions needed
1. **Integer overflow default.** Panic (safer, but integer hot loops need `unchecked` to vectorize) or wrap (C#'s default, free). I recommend panic; the prototype honours whichever the project sets.
2. **Resource destruction.** Explicit `using` or move, checked (recommended; nothing is rewritten), or Rust-style implicit drops by rewriting debug builds (less typing, weaker "it is just C#" guarantee).
3. **Class method mutability.** A `[Mut]` attribute (recommended, keeps checking per-method) or inference from bodies (less typing, but signatures change silently).
4. **Panics.** Abort on both sides (recommended for v1) or unwinding with cleanup.