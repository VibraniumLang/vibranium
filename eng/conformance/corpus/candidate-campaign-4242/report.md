### Run seed 4242: 12655 programs in 4430 s

* throughput: **171.4 programs/min** including reduction; frontends 195 ms/program, execution 752 ms/executed program, reduction 2992 oracle runs in 6733 worker-seconds
* C# validity: valid 6290, warning-only 732, invalid 5633; executed (all frontends accept) 2413
* catalog tags never generated: none
* generator profile: default; ordered anchor pairs covered: 324 of 324

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
| N1 | fixed | 20 / 33 | 9 | self rejects a `ref` argument for a `ref readonly` or `in` parameter (no applicable overload); on the candidate only the `in` form (warning-only CS9191) remains |
| N2 | fixed | 142 / 55 | 10 | self treats a constant-true guard (`when true`, or a folded constant such as `when 0.0 >= 0.0`) as a real guard (false 'switch expression must be exhaustive' / 'not all code paths return') |
| N3 | fixed | 253 / 114 | 7 | Stage0 native compound assignment `x += <rhs writing x via out/ref>` reads x after the rhs (managed/self read it first) |
| N4 | fixed | **not found** | 0 | self parses `(nint)-2` as a cast; C# parses a binary minus on the type name (CS0119) |
| N5 | fixed | 10331 / 2274 (re-attributed) | 1 | self skips checks inside a switch-expression arm it deems unreachable (`_` after complete Ok/Err) and accepts a non-exhaustive nested switch (CS8846/CS8509); possibly O5-family |
| N6 | fixed | 1374 / 240 (re-attributed) | 4 | self accepts a pattern that can never match (CS8518, e.g. `x is > 0 and < 0`); residual of the O10 family |
| N7 | fixed | 2617 / 412 (re-attributed) | 8 | self parses `a % b switch { … }` as `(a % b) switch { … }`; C# binds `switch` tighter than binary operators |
| N8 | fixed | 233 / 76 (re-attributed) | 4 | reading a `Span<T>` local inside `foreach (var\|ref var e in span)`: Stage-0 reports VIB001 (treats foreach as a move), self VIB003; SPEC §3.2 says foreach reborrows (shared for `var`, so reading is allowed; exclusive for `ref var`, so VIB003) |
| N9 | fixed | 5664 / 914 (re-attributed) | 1 | self ignores an always-true constant `is` pattern (`0 is > -1 and var q`) in definite assignment and reports VIB001; the program is warning-only (CS8793) |
| N10 | fixed | 9981 / 2227 (re-attributed) | 1 | self accepts a non-constant `case` label or pattern (`case k:` with a non-const local `k`; CS9135/CS0150) |
| N11 | fixed | **not found** | 0 | self reports a reachable end point (VIB000, CS0163/CS8070) for a switch section that Roslyn proves unreachable: a `default:` section without `break` after cases that already cover the input (`case >= 0` on ushort, `>= 0`/`< 0` on int, `true`/`false` on bool); Roslyn only warns CS0162, and Stage-0/Analyzer accept |
| N12 | fixed | **not found** | 0 | self accepts a `ref` argument whose operand is a readonly variable (a `ref readonly` local, or an `in`/`ref readonly` parameter) for a `ref readonly` parameter (CS1510/CS8329); for a `ref` parameter self rejects with VIB004 |
| N13 | fixed | **not found** | 0 | Stage-0/Analyzer run flow checks (use after move VIB001, overlap VIB002/VIB003, definite assignment VIB001 after the switch) on a `default:` section that Roslyn proves unreachable (earlier value cases cover the input, CS0162); self (after its N11 fix) does not. Decided: flow checks follow Roslyn reachability, so Stage-0/Analyzer change. All frontends already skip `when false` sections, code after `return`, `if (false)` and code after Panic |
| N14 | new | **not found** | 0 | self cannot resolve a union-pattern variable (`Err<int>(var e)`, `Some<int>(var v)`) declared in one label of a multi-label switch section whose other label has a `when false` guard (VIB000 'Unresolved or captured identifier'); Roslyn treats the `when false` label as never matching, so the variable is definitely assigned, and Stage-0 accepts. Int patterns work |

| Bucket | Category | Signature | Hits | First program | min.cs lines |
|---|---|---|---|---|---|
| 001-3683251e3c | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 20 | 9 |
| 002-0519758768 | regression:N2 (run: new:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 142 | 9 |
| 003-6ce551d71a | regression:N2 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 177 | 17 |
| 004-116f4d3e66 | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable method overload on enclosing type in vibranium. | 1 | 235 | 11 |
| 005-8abb80c05b | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 233 | 13 |
| 006-d54ce4d693 | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 302 | 14 |
| 007-64c6908691 | regression:N2 (run: new:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 378 | 10 |
| 008-354c085b8d | regression:N3 (run: new:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 253 | 12 |
| 009-fbc01502e5 | regression:N2 (run: new:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 393 | 15 |
| 010-3eec6bcee3 | regression:N2 (run: new:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 476 | 11 |
| 011-09712d394d | regression:N6 (run: new) | invalid-accepted \| self \| CS8518 \|  | 1 | 1374 | 9 |
| 012-6c7cd0dcf8 | regression:N3 (run: new:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 1501 | 15 |
| 013-38a3fd867f | regression:N2 (run: new) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 1737 | 10 |
| 014-ab1b789aaf | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 1762 | 12 |
| 015-dfbfc3688b | regression:N2 (run: new) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 1921 | 15 |
| 016-9222bc6dcf | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 2340 | 14 |
| 017-4a90cc7cf7 | regression:N3 (run: new:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 2230 | 12 |
| 018-4f822cc3f6 | regression:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 2617 | 9 |
| 019-1dafc6baec | regression:N3 (run: new:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 2473 | 15 |
| 020-d4f47f29ce | regression:N7 (run: new) | invalid-accepted \| self \| CS1503 \|  | 1 | 2633 | 22 |
| 021-b2d4737737 | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 2873 | 15 |
| 022-e8f062f2c6 | regression:N1 (run: new:N1) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 3193 | 12 |
| 023-45ceb18582 | regression:N6 (run: new) | invalid-accepted \| self \| CS8518 \|  | 1 | 3370 | 19 |
| 024-f2eeef5c2f | regression:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 3489 | 14 |
| 025-69462bf092 | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 3574 | 13 |
| 026-8268e2b25f | regression:N2 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 3950 | 18 |
| 027-aaa19d665c | regression:N3 (run: new:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 3897 | 15 |
| 028-5598833815 | regression:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 4370 | 12 |
| 029-a0fef00b51 | regression:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 4504 | 12 |
| 030-7dc2933c8c | regression:N6 (run: new) | invalid-accepted \| self \| CS8518 \|  | 1 | 4497 | 15 |
| 031-53bb3a9009 | regression:N2 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 4792 | 11 |
| 032-fbd229b543 | regression:N1 (run: new:N1) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 4838 | 16 |
| 033-e15cbdaca6 | regression:N2 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 5181 | 13 |
| 034-8ec95c7221 | regression:N9 (run: new) | accept-reject \| stage0-accepts \| self:VIB001 \| c# requires this variable or struct field to be definitely assigned before it is read (csn | 1 | 5664 | 10 |
| 035-9495c7fd7c | regression:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 5669 | 10 |
| 036-632e89decf | regression:N7 (run: new) | invalid-accepted \| self \| CS0266,CS1503,CS8422 \|  | 1 | 7614 | 10 |
| 037-24a0edd589 | regression:N6 (run: new) | invalid-accepted \| self \| CS8518 \|  | 1 | 7625 | 9 |
| 038-9d3d686b41 | regression:N7 (run: new) | invalid-accepted \| self \| CS0266 \|  | 1 | 7934 | 9 |
| 039-0deb14406c | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 8019 | 8 |
| 040-234e98f74d | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 8177 | 14 |
| 041-ce9f24f50c | new | managed-crash \| managed \| timeout \|  | 1 | 3001 | 13 |
| 042-e9ecfef67c | regression:N3 (run: new:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0,stage0-O3-asan | 1 | 8690 | 12 |
| 043-c65b3b8e5c | regression:N8 (run: known:O6) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 9353 | 12 |
| 044-7c6428b033 | new | output-mismatch \| stage0+self \| stdout \| stage0-O0,self-O0 | 1 | 6019 | 15 |
| 045-857cb11cc4 | regression:N10 (run: new) | invalid-accepted \| self \| CS9135 \|  | 1 | 9981 | 21 |
| 046-5c1f2e2a42 | regression:N5 (run: new) | invalid-accepted \| self \| CS0165 \|  | 1 | 10331 | 10 |
| 047-34e7f579ee | regression:N3 (run: new:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 12193 | 12 |
| 048-ebd0e76a56 | new | output-mismatch \| stage0+self \| stdout \| stage0-O0,self-O0 | 1 | 12369 | 12 |
| 049-ded166c421 | new | managed-crash \| managed \| timeout \|  | 1 | 8764 | 14 |

| Warning (warning-only programs) | Programs | Analyzer clean | Stage-0 accepts | self accepts | First program |
|---|---|---|---|---|---|
| CS0162 | 469 | 343 | 343 | 335 | 22 |
| CS0168 | 115 | 82 | 82 | 81 | 5 |
| CS0219 | 81 | 59 | 59 | 57 | 186 |
| CS9191 | 49 | 36 | 36 | 0 | 20 |
| CS8321 | 27 | 20 | 20 | 20 | 356 |
| CS8793 | 8 | 6 | 6 | 5 | 1968 |
| CS8519 | 5 | 4 | 4 | 4 | 1197 |
| CS1522 | 5 | 4 | 4 | 4 | 1334 |
| CS1718 | 5 | 3 | 3 | 3 | 3465 |
| CS1717 | 4 | 4 | 4 | 4 | 6044 |
| CS8520 | 3 | 2 | 2 | 1 | 380 |
| CS9195 | 2 | 0 | 0 | 0 | 9662 |

Discovery curve (one row per 5 minutes; last event in each window): 

| Minute | Programs | Signatures | Buckets | New (not Round-10/cut) | Unattributed |
|---|---|---|---|---|---|
| ≤5 | 1867 | 7 | 14 | 14 | 4 |
| ≤10 | 3596 | 10 | 25 | 25 | 9 |
| ≤15 | 5209 | 10 | 33 | 33 | 15 |
| ≤20 | 6211 | 12 | 35 | 35 | 17 |
| ≤30 | 8295 | 13 | 41 | 41 | 23 |
| ≤35 | 9354 | 14 | 43 | 42 | 23 |
| ≤40 | 10362 | 16 | 46 | 45 | 26 |
| ≤45 | 12287 | 16 | 47 | 46 | 26 |
| ≤70 | 12655 | 16 | 48 | 47 | 27 |
| ≤75 | 12655 | 16 | 49 | 48 | 28 |

