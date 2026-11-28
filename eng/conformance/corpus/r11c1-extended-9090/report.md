### Run seed 9090: 41979 programs in 5406 s

* throughput: **465.9 programs/min** including reduction; frontends 265 ms/program, execution 600 ms/executed program, reduction 442 oracle runs in 99 worker-seconds
* C# validity: valid 25523, warning-only 1987, invalid 14469; executed (all frontends accept) 7888, of which non-terminating (managed run timed out or overflowed the call stack, comparison skipped) 11
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
| N13 | fixed | 987 / 170 (re-attributed) | 5 | Stage-0/Analyzer run flow checks (use after move VIB001, overlap VIB002/VIB003, definite assignment VIB001 after the switch) on a `default:` section that Roslyn proves unreachable (earlier value cases cover the input, CS0162); self (after its N11 fix) does not. Decided: flow checks follow Roslyn reachability, so Stage-0/Analyzer change. All frontends already skip `when false` sections, code after `return`, `if (false)` and code after Panic |
| N14 | new | **not found** | 0 | self cannot resolve a union-pattern variable (`Err<int>(var e)`, `Some<int>(var v)`) declared in one label of a multi-label switch section whose other label has a `when false` guard (VIB000 'Unresolved or captured identifier'); Roslyn treats the `when false` label as never matching, so the variable is definitely assigned, and Stage-0 accepts. Int patterns work |

| Bucket | Category | Signature | Hits | First program | min.cs lines |
|---|---|---|---|---|---|
| 001-7c0710b3c4 | regression:N13 (run: new) | accept-reject \| self-accepts \| stage0:VIB002,VIB003 \| overlapping borrows in a single call where at least one borrow is exclusive. | 1 | 987 | 22 |
| 002-4e2d4e378f | regression:N13 (run: regression:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of moved value 'x'. | 1 | 1651 | 11 |
| 003-6c98974c14 | regression:N13 (run: regression:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of uninitialized value 'x'. | 1 | 20787 | 22 |
| 004-00d0b6115c | regression:N13 (run: regression:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of uninitialized value 'x'. | 1 | 22749 | 17 |
| 005-f8ae030381 | regression:N13 (run: regression:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of moved value 'x'. | 1 | 40601 | 18 |

| Warning (warning-only programs) | Programs | Analyzer clean | Stage-0 accepts | self accepts | First program |
|---|---|---|---|---|---|
| CS0162 | 1257 | 792 | 792 | 797 | 11 |
| CS0168 | 507 | 291 | 291 | 291 | 0 |
| CS0219 | 150 | 91 | 91 | 91 | 95 |
| CS1718 | 74 | 52 | 52 | 52 | 843 |
| CS8321 | 30 | 17 | 17 | 17 | 3752 |
| CS1717 | 11 | 4 | 4 | 4 | 4799 |
| CS1522 | 5 | 4 | 4 | 4 | 4648 |
| CS9195 | 3 | 0 | 0 | 0 | 479 |

| Near-miss tag (C#-valid, Vibranium-invalid) | Programs | Accepted by every frontend | First accepted program |
|---|---|---|---|
| `mut.nm-borrow-live` | 439 | 0 | — |
| `mut.nm-foreach-mutate` | 476 | 1 | 21277 |
| `mut.nm-from-shared` | 484 | 0 | — |
| `mut.nm-in-param` | 431 | 0 | — |
| `mut.nm-moved` | 218 | 2 | 27347 |
| `mut.nm-two-phase` | 248 | 2 | 12177 |
| `optmut.nm-alias` | 523 | 2 | 21368 |
| `optmut.nm-move` | 468 | 0 | — |
| `optmut.nm-overlap` | 471 | 1 | 12486 |
| `optref.nm-write` | 480 | 1 | 14927 |
| `optslot.nm-place-write` | 478 | 0 | — |
| `span.nm-moved` | 942 | 3 | 4794 |
| `span.nm-overlap` | 594 | 2 | 9509 |
| `span.nm-parent-while-live` | 588 | 2 | 14754 |
| `span.nm-vec-grow` | 321 | 1 | 38551 |
| `span.two-slices-one-call` | 1108 | 2 | 19076 |
| `std.nm-buf-borrow` | 629 | 1 | 11233 |
| `std.nm-map-key-borrow` | 622 | 1 | 24594 |
| `std.nm-map-moved` | 634 | 2 | 29514 |
| `utf8.nm-mutate-owner` | 305 | 3 | 5628 |
| `utf8.nm-slice-after-clear` | 335 | 0 | — |
| `utf8.nm-view-local` | 1916 | 0 | — |

Discovery curve (one row per 5 minutes; last event in each window): 

| Minute | Programs | Signatures | Buckets | New (not Round-10/cut) | Unattributed |
|---|---|---|---|---|---|
| ≤5 | 1750 | 2 | 2 | 1 | 1 |
| ≤50 | 20935 | 3 | 3 | 1 | 1 |
| ≤55 | 22827 | 3 | 4 | 1 | 1 |
| ≤90 | 40692 | 3 | 5 | 1 | 1 |

