### Run seed 7373: 77475 programs in 7200 s

* throughput: **645.6 programs/min** including reduction; frontends 193 ms/program, execution 444 ms/executed program, reduction 0 oracle runs in 0 worker-seconds
* C# validity: valid 46417, warning-only 3607, invalid 27451; executed (all frontends accept) 14534, of which non-terminating (managed run timed out or overflowed the call stack, comparison skipped) 14
* catalog tags never generated: none
* generator profile: extended; ordered anchor pairs covered: 205 of 205

| Root | Status | First hit (program / s) | Buckets | Title |
|---|---|---|---|---|
| O1 | fixed | **not found** | 0 | named `out _` ignores an in-scope variable `_` (self wrong output / accepts field form) |
| O2a | fixed | **not found** | 0 | switch-section expression variables scoped to the section in self (accepts CS0128) |
| O2b | fixed | **not found** | 0 | switch-section expression variable used across sections: self false VIB000 |
| O3 | fixed | **not found** | 0 | class const by simple name inside a static local function in a class method: self false VIB000 |
| O4 | fixed | **not found** | 0 | embedded declaration statement accepted by self (CS1023) |
| O5 | fixed | **not found** | 0 | Option/Result switch statements treated as exhaustive by self (accepts CS0161/CS0165/CS0163) |
| O6 | fixed | **not found** | 0 | Stage0/Analyzer false VIB001 after exhaustive bool/relational switch statement |
| O7 | fixed | **not found** | 0 | self false VIB001 on fieldwise-initialized plain struct |
| O8 | fixed | **not found** | 0 | conditional collection-expression argument not target-typed by self overload resolution |
| O9 | fixed | **not found** | 0 | self accepts rvalue arguments to ref readonly (CS9193) / explicit in (CS8156) |
| O10 | fixed | **not found** | 0 | self does not diagnose subsumed/impossible patterns (CS8510/CS8120) |
| O11 | fixed | **not found** | 0 | static local function directly in a switch section (v0.2 cut, implemented on the candidate: every frontend rejects with VIB000) |
| A1 | fixed | **not found** | 0 | dynamic float/double `%` breaks the libc-only link contract: Stage0 `vib build` fails with undefined fmod/fmodf |
| A1-self | fixed | **not found** | 0 | A1 (same group): self `build` links every program against libm (DT_NEEDED libm.so.6 beyond the libc-only contract) |
| A2 | fixed | **not found** | 0 | constant enum collection returned as ReadOnlySpan after Panic: self false VIB007 |
| A3 | fixed | **not found** | 0 | `c ? [x] : [y]` parsed as null-conditional `?[` by self (false VIB008/VIB000) |
| CUT2 | fixed | **not found** | 0 | piecewise init of a not-yet-initialized struct with owned fields (v0.2 cut, implemented on the candidate: every frontend rejects with VIB000) |
| N1 | fixed | **not found** | 0 | self rejects a `ref` argument for a `ref readonly` or `in` parameter (no applicable overload); on the candidate only the `in` form (warning-only CS9191) remains |
| N2 | fixed | **not found** | 0 | self treats a constant-true guard (`when true`, or a folded constant such as `when 0.0 >= 0.0`) as a real guard (false 'switch expression must be exhaustive' / 'not all code paths return') |
| N3 | fixed | **not found** | 0 | Stage0 native compound assignment `x += <rhs writing x via out/ref>` reads x after the rhs (managed/self read it first) |
| N4 | fixed | **not found** | 0 | self parses `(nint)-2` as a cast; C# parses a binary minus on the type name (CS0119) |
| N5 | fixed | **not found** | 0 | self skips checks inside a switch-expression arm it deems unreachable (`_` after complete Ok/Err) and accepts a non-exhaustive nested switch (CS8846/CS8509); possibly O5-family |
| N6 | fixed | **not found** | 0 | self accepts a pattern that can never match (CS8518, e.g. `x is > 0 and < 0`); residual of the O10 family |
| N7 | fixed | **not found** | 0 | self parses `a % b switch { … }` as `(a % b) switch { … }`; C# binds `switch` tighter than binary operators |
| N8 | fixed | **not found** | 0 | reading a `Span<T>` local inside `foreach (var\|ref var e in span)`: Stage-0 reports VIB001 (treats foreach as a move), self VIB003; SPEC §3.2 says foreach reborrows (shared for `var`, so reading is allowed; exclusive for `ref var`, so VIB003) |
| N9 | fixed | **not found** | 0 | self ignores an always-true constant `is` pattern (`0 is > -1 and var q`) in definite assignment and reports VIB001; the program is warning-only (CS8793) |
| N10 | fixed | **not found** | 0 | self accepts a non-constant `case` label or pattern (`case k:` with a non-const local `k`; CS9135/CS0150) |
| N11 | fixed | **not found** | 0 | self reports a reachable end point (VIB000, CS0163/CS8070) for a switch section that Roslyn proves unreachable: a `default:` section without `break` after cases that already cover the input (`case >= 0` on ushort, `>= 0`/`< 0` on int, `true`/`false` on bool); Roslyn only warns CS0162, and Stage-0/Analyzer accept |
| N12 | fixed | **not found** | 0 | self accepts a `ref` argument whose operand is a readonly variable (a `ref readonly` local, or an `in`/`ref readonly` parameter) for a `ref readonly` parameter (CS1510/CS8329); for a `ref` parameter self rejects with VIB004 |
| N13 | fixed | **not found** | 0 | Stage-0/Analyzer run flow checks (use after move VIB001, overlap VIB002/VIB003, definite assignment VIB001 after the switch) on a `default:` section that Roslyn proves unreachable (earlier value cases cover the input, CS0162); self (after its N11 fix) does not. Decided: flow checks follow Roslyn reachability, so Stage-0/Analyzer change. All frontends already skip `when false` sections, code after `return`, `if (false)` and code after Panic |
| N14 | fixed | **not found** | 0 | self cannot resolve a union-pattern variable (`Err<int>(var e)`, `Some<int>(var v)`) declared in one label of a multi-label switch section whose other label has a `when false` guard (VIB000 'Unresolved or captured identifier'); Roslyn treats the `when false` label as never matching, so the variable is definitely assigned, and Stage-0 accepts. Int patterns work |

| Bucket | Category | Signature | Hits | First program | min.cs lines |
|---|---|---|---|---|---|

| Warning (warning-only programs) | Programs | Analyzer clean | Stage-0 accepts | self accepts | First program |
|---|---|---|---|---|---|
| CS0162 | 2171 | 1359 | 1359 | 1359 | 38 |
| CS0168 | 975 | 595 | 595 | 595 | 142 |
| CS0219 | 292 | 187 | 187 | 187 | 299 |
| CS1718 | 142 | 72 | 72 | 72 | 1050 |
| CS8321 | 77 | 36 | 36 | 36 | 591 |
| CS1717 | 21 | 10 | 10 | 10 | 782 |
| CS9195 | 12 | 0 | 0 | 0 | 1165 |
| CS1522 | 9 | 4 | 4 | 4 | 8372 |

| Near-miss tag (C#-valid, Vibranium-invalid) | Programs | Accepted by every frontend | First accepted program |
|---|---|---|---|
| `mut.nm-borrow-live` | 734 | 0 | — |
| `mut.nm-foreach-mutate` | 898 | 4 | 2979 |
| `mut.nm-from-shared` | 930 | 0 | — |
| `mut.nm-in-param` | 829 | 0 | — |
| `mut.nm-moved` | 359 | 0 | — |
| `mut.nm-two-phase` | 458 | 0 | — |
| `optmut.nm-alias` | 861 | 2 | 20989 |
| `optmut.nm-move` | 906 | 1 | 3445 |
| `optmut.nm-overlap` | 890 | 1 | 49777 |
| `optref.nm-write` | 939 | 2 | 23945 |
| `optslot.nm-place-write` | 860 | 2 | 29554 |
| `span.nm-moved` | 1744 | 3 | 8402 |
| `span.nm-overlap` | 1097 | 2 | 8402 |
| `span.nm-parent-while-live` | 1109 | 3 | 35119 |
| `span.nm-vec-grow` | 553 | 2 | 1842 |
| `span.two-slices-one-call` | 1978 | 0 | — |
| `std.nm-buf-borrow` | 1111 | 4 | 12793 |
| `std.nm-map-key-borrow` | 1085 | 2 | 6995 |
| `std.nm-map-moved` | 1138 | 3 | 1610 |
| `utf8.nm-mutate-owner` | 553 | 2 | 19737 |
| `utf8.nm-slice-after-clear` | 548 | 3 | 877 |
| `utf8.nm-view-local` | 3382 | 0 | — |

Discovery curve (one row per 5 minutes; last event in each window): 

| Minute | Programs | Signatures | Buckets | New (not Round-10/cut) | Unattributed |
|---|---|---|---|---|---|

