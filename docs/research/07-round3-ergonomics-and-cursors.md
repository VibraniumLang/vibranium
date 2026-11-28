# Vibranium Round 3 Ergonomics & Soundness Audit Summary (Fable + Astra + Carbon)

All three advisors ran empirical verification against `.NET 11.0.100` RTM, `Microsoft.CodeAnalysis.CSharp 5.12.0`, and `rustc 1.99.1` (including `-Zpolonius`), locking in the final ergonomics and soundness rules:

1. **100% Pure Base C# 15 Confirmed:**
   - Zero custom parser keywords (`mut var`, `let`, postfix `?`) and zero `.vibranium` transpiler.
   - Vibranium SDK `.props` inject `<Using Include="Vibranium" />` and `<Using Include="Vibranium.Prelude" Static="true" />` so Vibranium source files need zero boilerplate `using` directives.
   - Zero C files in the repository: `vib` emits `malloc`, `realloc`, `free`, `write`, `_exit(101)`, integer formatting, and panic reporting directly in LLVM 18 IR.

2. **Hardened `OptionRef<T>` and `OptionMut<T>` Slot-Cursor Design:**
   - `OptionRef<T>` (`[Union] public readonly ref struct OptionRef<T>`) is `Copy` (like `&Option<T>`).
   - `OptionMut<T>` (`[Union] public ref struct OptionMut<T> where T : class`) is a mutable `ref struct` (fitting the struct-receiver mutability rule on `.Set` / `.Take` / `.Replace`), constrained to `where T : class` (eliminating the CLR struct-copy lost-write trap; struct payloads use `[UnscopedRef] public ref T UnwrapMut()` on `Option<T>`), and classified as **intrinsic `!Copy`** in Vibranium (`var alias = cur;` moves `cur`).
   - Both `OptionRef<T>` and `OptionMut<T>` are **non-zero-safe** (`VIB008` rejects `default`).
   - `.AsMut()` and `.Take()` are declared as `this ref Option<T>` extension methods so stock `csc` automatically rejects property, temporary, `in`, and `readonly`-field receivers (`CS0206`, `CS1510`, `CS8329`, `CS0192`). `.AsRef()` uses a `this ref readonly Option<T>` extension (`CS9193` promoted to error on temporaries).
   - Union protocol plumbing (`OptionRef`/`OptionMut` case constructors, `.Value`, `.HasValue`, `.TryGetValue`) is marked `[Obsolete("Internal union protocol", error: true), EditorBrowsable(EditorBrowsableState.Never)]` — keeping `switch` and `is Some<T>(var x)` clean while stock `csc` rejects direct calls and conversions with `CS0619`.

3. **Polonius-Style Flow-Sensitive Loan Solver:**
   - Verified `netstandard2.0` `Vibranium.Analyzer` built against Roslyn `5.12.0` inspects `net11.0` `Vibranium.Core` (`ref` fields, `[Union]`, `IsRefLikeType`) with zero issues.
   - Tracking per-program-point loan provenance (`prov(local)`) allows both 4-line `Append` (`cur = n.Next.AsMut()`) and safe mid-list `RemoveFirst` (`var next = n.Next.Take(); cur.Set(next);`, which `rustc` stable NLL rejects with `E0506` and `-Zpolonius` accepts!), while rejecting stale `n` access (`VIB003`) and local heap-field escapes (`VIB007`).

4. **Collection & Loop Polish:**
   - `Vec<T>` supports `[CollectionBuilder]` (`Vec<int> v = [1, 2, 3];`), `ref T this[int i]`, `Slice(int, int)` (`v[1..3]`), and zero-allocation `foreach (var x in v)` / `foreach (ref var x in v)`.
   - `Option<Node>.Iter()` provides zero-allocation `foreach (var n in head.Iter())`, alongside `for (var cur = head.AsRef(); cur is Some<Node>(var n); cur = n.Next.AsRef())`.
