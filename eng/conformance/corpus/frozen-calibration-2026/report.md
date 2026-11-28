### Run seed 2026: 1000 programs in 612 s

* throughput: **98.1 programs/min** including reduction; frontends 400 ms/program, execution 746 ms/executed program, reduction 6439 oracle runs in 2443 worker-seconds
* C# validity: valid 496, warning-only 69, invalid 435; executed (all frontends accept) 157
* catalog tags never generated: `out.other`
* generator profile: default; ordered anchor pairs covered: 324 of 324

| Root | Status | First hit (program / s) | Buckets | Title |
|---|---|---|---|---|
| O1 | fixed | 70 / 94 | 14 | named `out _` ignores an in-scope variable `_` (self wrong output / accepts field form) |
| O2a | fixed | 367 / 268 | 1 | switch-section expression variables scoped to the section in self (accepts CS0128) |
| O2b | fixed | **not found** | 0 | switch-section expression variable used across sections: self false VIB000 |
| O3 | fixed | 609 / 378 | 1 | class const by simple name inside a static local function in a class method: self false VIB000 |
| O4 | fixed | 3 / 19 | 7 | embedded declaration statement accepted by self (CS1023) |
| O5 | fixed | 349 / 243 | 1 | Option/Result switch statements treated as exhaustive by self (accepts CS0161/CS0165/CS0163) |
| O6 | fixed | 80 / 92 | 9 | Stage0/Analyzer false VIB001 after exhaustive bool/relational switch statement |
| O7 | fixed | 12 / 43 | 9 | self false VIB001 on fieldwise-initialized plain struct |
| O8 | fixed | 165 / 137 | 5 | conditional collection-expression argument not target-typed by self overload resolution |
| O9 | fixed | 19 / 38 | 7 | self accepts rvalue arguments to ref readonly (CS9193) / explicit in (CS8156) |
| O10 | fixed | 43 / 41 | 10 | self does not diagnose subsumed/impossible patterns (CS8510/CS8120) |
| O11 | fixed | 204 / 164 | 4 | static local function directly in a switch section (v0.2 cut, implemented on the candidate: every frontend rejects with VIB000) |
| A1 | fixed | 62 / 94 | 4 | dynamic float/double `%` breaks the libc-only link contract: Stage0 `vib build` fails with undefined fmod/fmodf |
| A1-self | fixed | 2 / 35 | 2 | A1 (same group): self `build` links every program against libm (DT_NEEDED libm.so.6 beyond the libc-only contract) |
| A2 | fixed | 575 / 354 | 1 | constant enum collection returned as ReadOnlySpan after Panic: self false VIB007 |
| A3 | fixed | 124 / 126 | 8 | `c ? [x] : [y]` parsed as null-conditional `?[` by self (false VIB008/VIB000) |
| CUT2 | fixed | 77 / 82 | 14 | piecewise init of a not-yet-initialized struct with owned fields (v0.2 cut, implemented on the candidate: every frontend rejects with VIB000) |
| N1 | fixed | 57 / 64 | 4 | self rejects a `ref` argument for a `ref readonly` or `in` parameter (no applicable overload); on the candidate only the `in` form (warning-only CS9191) remains |
| N2 | fixed | 46 / 60 | 2 | self treats a constant-true guard (`when true`, or a folded constant such as `when 0.0 >= 0.0`) as a real guard (false 'switch expression must be exhaustive' / 'not all code paths return') |
| N3 | fixed | 314 / 250 | 1 | Stage0 native compound assignment `x += <rhs writing x via out/ref>` reads x after the rhs (managed/self read it first) |
| N4 | fixed | **not found** | 0 | self parses `(nint)-2` as a cast; C# parses a binary minus on the type name (CS0119) |
| N5 | fixed | 844 / 535 | 1 | self skips checks inside a switch-expression arm it deems unreachable (`_` after complete Ok/Err) and accepts a non-exhaustive nested switch (CS8846/CS8509); possibly O5-family |
| N6 | fixed | **not found** | 0 | self accepts a pattern that can never match (CS8518, e.g. `x is > 0 and < 0`); residual of the O10 family |
| N7 | fixed | **not found** | 0 | self parses `a % b switch { … }` as `(a % b) switch { … }`; C# binds `switch` tighter than binary operators |
| N8 | fixed | **not found** | 0 | reading a `Span<T>` local inside `foreach (var\|ref var e in span)`: Stage-0 reports VIB001 (treats foreach as a move), self VIB003; SPEC §3.2 says foreach reborrows (shared for `var`, so reading is allowed; exclusive for `ref var`, so VIB003) |
| N9 | fixed | **not found** | 0 | self ignores an always-true constant `is` pattern (`0 is > -1 and var q`) in definite assignment and reports VIB001; the program is warning-only (CS8793) |
| N10 | fixed | **not found** | 0 | self accepts a non-constant `case` label or pattern (`case k:` with a non-const local `k`; CS9135/CS0150) |
| N11 | fixed | **not found** | 0 | self reports a reachable end point (VIB000, CS0163/CS8070) for a switch section that Roslyn proves unreachable: a `default:` section without `break` after cases that already cover the input (`case >= 0` on ushort, `>= 0`/`< 0` on int, `true`/`false` on bool); Roslyn only warns CS0162, and Stage-0/Analyzer accept |
| N12 | fixed | **not found** | 0 | self accepts a `ref` argument whose operand is a readonly variable (a `ref readonly` local, or an `in`/`ref readonly` parameter) for a `ref readonly` parameter (CS1510/CS8329); for a `ref` parameter self rejects with VIB004 |
| N13 | fixed | **not found** | 0 | Stage-0/Analyzer run flow checks (use after move VIB001, overlap VIB002/VIB003, definite assignment VIB001 after the switch) on a `default:` section that Roslyn proves unreachable (earlier value cases cover the input, CS0162); self (after its N11 fix) does not. Decided: flow checks follow Roslyn reachability, so Stage-0/Analyzer change. All frontends already skip `when false` sections, code after `return`, `if (false)` and code after Panic |
| N14 | new | **not found** | 0 | self cannot resolve a union-pattern variable (`Err<int>(var e)`, `Some<int>(var v)`) declared in one label of a multi-label switch section whose other label has a `when false` guard (VIB000 'Unresolved or captured identifier'); Roslyn treats the `when false` label as never matching, so the variable is definitely assigned, and Stage-0 accepts. Int patterns work |

| Bucket | Category | Signature | Hits | First program | min.cs lines |
|---|---|---|---|---|---|
| 001-f269168a89 | regression:O4 (run: known:O4) | invalid-accepted \| self \| CS1023 \|  | 1 | 3 | 6 |
| 002-5c3e914bba | regression:A1-self (run: known:A1-self) | link-contract \| self-O0 \| libm.so.6 \| dt_needed beyond libc | 1 | 2 | 2 |
| 003-1647e4e829 | regression:O9 (run: known:O9) | invalid-accepted \| self \| CS8156 \|  | 1 | 19 | 9 |
| 004-529c34d35c | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8120 \|  | 1 | 43 | 13 |
| 005-03e6ea92c0 | regression:O7 (run: known:O7) | accept-reject \| stage0-accepts \| self:VIB001 \| use of uninitialized value. | 1 | 12 | 18 |
| 006-3e1d233dc7 | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8510 \|  | 1 | 63 | 0 |
| 007-86b028a9c7 | regression:O7 (run: known:O7) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB001} \|  | 1 | 60 | 0 |
| 008-1d1113b4f7 | regression:N2 (run: new:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 46 | 16 |
| 009-b890251b42 | regression:A1-self (run: known:A1-self) | link-contract \| self-O0 \| libm.so.6 \| dt_needed beyond libc | 1 | 68 | 0 |
| 010-95544b4742 | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable method overload on enclosing type in vibranium. | 1 | 57 | 15 |
| 011-b308cdb4f8 | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8510 \|  | 1 | 73 | 0 |
| 012-d6724fba39 | regression:N1 (run: new:N1) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 79 | 15 |
| 013-80124962d6 | regression:CUT2 (run: known-cut:CUT2) | banned-wrong-id \| cut.struct-piecewise-owned \| analyzer:VIB001 stage0:VIB001 self:VIB001 \| expected VIB000 | 1 | 77 | 22 |
| 014-08bde7ac99 | regression:O6 (run: known:O6) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 80 | 20 |
| 015-273896ec41 | regression:O1 (run: known:O1) | output-mismatch \| self \| stdout \| self-O0 | 1 | 70 | 16 |
| 016-abcc29df7a | regression:A1 (run: known:A1) | native-build-failure \| stage0-O0 \|  \| undefined reference to fmod | 1 | 62 | 12 |
| 017-f67084dbc1 | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB004} self-only:{} \|  | 1 | 88 | 16 |
| 018-45a377fcba | regression:O1 (run: known:O1) | id-mismatch \| stage0+self \| stage0-only:{VIB004} self-only:{VIB000,VIB008} \|  | 1 | 85 | 0 |
| 019-c76ba05067 | regression:O7 (run: known:O7) | accept-reject \| stage0-accepts \| self:VIB001 \| use of uninitialized value. | 1 | 91 | 0 |
| 020-b5f4aebef7 | regression:O4 (run: known:O4) | invalid-accepted \| self \| CS1023 \|  | 1 | 141 | 0 |
| 021-eed3dad5fc | regression:CUT2 (run: known-cut:CUT2) | banned-accepted \| analyzer+stage0 \| cut.struct-piecewise-owned \|  | 1 | 128 | 0 |
| 022-2775453a24 | regression:A3 (run: known:A3) | accept-reject \| stage0-accepts \| self:VIB000,VIB008 \| null-conditional 'x' / 'x' operator is banned in vibranium. | 1 | 124 | 14 |
| 023-696645824b | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8510 \|  | 1 | 163 | 0 |
| 024-f465f0a350 | regression:O8 (run: known:O8) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 165 | 8 |
| 025-4f321f476c | regression:CUT2 (run: known-cut:CUT2) | banned-wrong-id \| cut.struct-piecewise-owned \| self:VIB001 \| expected VIB000 | 1 | 128 | 0 |
| 026-622aa319e2 | regression:A3 (run: known:A3) | accept-reject \| stage0-accepts \| self:VIB000,VIB008 \| null-conditional 'x' / 'x' operator is banned in vibranium. | 1 | 168 | 0 |
| 027-0f2d676776 | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8510 \|  | 1 | 176 | 0 |
| 028-a807262a8a | regression:O1 (run: known:O1) | output-mismatch \| self \| stdout \| self-O0 | 1 | 62 | 0 |
| 029-dd0fe89af8 | regression:O9 (run: known:O9) | invalid-accepted \| self \| CS8156 \|  | 1 | 182 | 0 |
| 030-dea5934adc | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB001} \|  | 1 | 218 | 9 |
| 031-2711cc0e3c | regression:O11 (run: known-cut:O11) | accept-reject \| stage0-accepts \| self:VIB000 \| unexpected token in expression. | 1 | 204 | 18 |
| 032-6db34227bd | regression:O6 (run: known:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of uninitialized value 'x'. | 1 | 205 | 0 |
| 033-f6c156f3a4 | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 211 | 20 |
| 034-42cd2c8fa2 | regression:O7 (run: known:O7) | accept-reject \| stage0-accepts \| self:VIB000,VIB001 \| no applicable local function overload in vibranium. | 1 | 212 | 0 |
| 035-557ffc62d6 | regression:O11 (run: known-cut:O11) | banned-accepted \| analyzer+stage0 \| cut.localfunc-in-switch-section \|  | 1 | 204 | 0 |
| 036-bdefc9dbee | regression:A3 (run: known:A3) | accept-reject \| stage0-accepts \| self:VIB000,VIB008 \| null-conditional 'x' / 'x' operator is banned in vibranium. | 1 | 220 | 0 |
| 037-0dd5002af9 | regression:O4 (run: known:O4) | invalid-accepted \| self \| CS1023 \|  | 1 | 255 | 0 |
| 038-7eed51ed6c | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8120 \|  | 1 | 250 | 0 |
| 039-cd7c1e4b57 | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8510 \|  | 1 | 240 | 0 |
| 040-eb6ddd94de | regression:O6 (run: known:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of uninitialized value 'x'. | 1 | 259 | 0 |
| 041-ec1391d221 | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8120 \|  | 1 | 286 | 0 |
| 042-ff331838b3 | regression:O7 (run: known:O7) | accept-reject \| stage0-accepts \| self:VIB001 \| use of uninitialized value. | 1 | 261 | 0 |
| 043-ab953cbec4 | regression:CUT2 (run: known-cut:CUT2) | banned-accepted \| analyzer+stage0 \| cut.struct-piecewise-owned \|  | 1 | 289 | 0 |
| 044-ecf16dce72 | regression:O4 (run: known:O4) | invalid-accepted \| self \| CS1023 \|  | 1 | 331 | 0 |
| 045-665fa90b17 | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB000} self-only:{} \|  | 1 | 325 | 13 |
| 046-0db0c16e12 | regression:O9 (run: known:O9) | invalid-accepted \| self \| CS8156 \|  | 1 | 339 | 0 |
| 047-7934252d3f | regression:CUT2 (run: known-cut:CUT2) | banned-wrong-id \| cut.struct-piecewise-owned \| self:VIB001 \| expected VIB000 | 1 | 289 | 0 |
| 048-4a151fde8f | regression:O6 (run: known:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of uninitialized value 'x'. | 1 | 338 | 0 |
| 049-e59ab4f034 | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB000} self-only:{} \|  | 1 | 344 | 10 |
| 050-5b35657cb0 | regression:O4 (run: known:O4) | invalid-accepted \| self \| CS1023 \|  | 1 | 347 | 0 |
| 051-7a55e1444c | regression:O5 (run: known:O5) | invalid-accepted \| self \| CS0165 \|  | 1 | 349 | 14 |
| 052-a67e17bee7 | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB001} \|  | 1 | 360 | 11 |
| 053-354c085b8d | regression:N3 (run: new:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 314 | 12 |
| 054-b913237d98 | regression:A3 (run: known:A3) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB008} \|  | 1 | 364 | 0 |
| 055-2ab404e469 | regression:O7 (run: known:O7) | accept-reject \| stage0-accepts \| self:VIB001 \| use of uninitialized value. | 1 | 366 | 0 |
| 056-a313826236 | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB008} self-only:{} \|  | 1 | 353 | 16 |
| 057-14d702cc69 | regression:O2a (run: known:O2a) | invalid-accepted \| self \| CS0128 \|  | 1 | 367 | 17 |
| 058-6ba9d00b33 | regression:O6 (run: known:O6) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 404 | 0 |
| 059-959112d2e7 | regression:O6 (run: known:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of uninitialized value 'x'. | 1 | 397 | 0 |
| 060-452e0a6974 | regression:A3 (run: known:A3) | accept-reject \| stage0-accepts \| self:VIB000,VIB008 \| null-conditional 'x' / 'x' operator is banned in vibranium. | 1 | 408 | 0 |
| 061-0b9fa46166 | regression:CUT2 (run: known-cut:CUT2) | banned-wrong-id \| cut.struct-piecewise-owned \| analyzer:VIB001 stage0:VIB001 self:VIB001 \| expected VIB000 | 1 | 401 | 0 |
| 062-837a4bf106 | regression:O4 (run: known:O4) | invalid-accepted \| self \| CS1023 \|  | 1 | 483 | 0 |
| 063-5f17007cab | regression:O1 (run: known:O1) | id-mismatch \| stage0+self \| stage0-only:{VIB000,VIB004} self-only:{} \|  | 1 | 444 | 0 |
| 064-0ac1f80284 | regression:O7 (run: known:O7) | accept-reject \| stage0-accepts \| self:VIB001 \| out parameter must be initialized before return. | 1 | 454 | 0 |
| 065-8eaf264334 | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 2 | 481 | 9 |
| 066-2f2782f657 | regression:O11 (run: known-cut:O11) | accept-reject \| stage0-accepts \| self:VIB000 \| unexpected token in expression. | 1 | 497 | 0 |
| 067-0d39f98d5f | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8510 \|  | 1 | 502 | 0 |
| 068-8a1ea6d3dc | regression:A3 (run: known:A3) | accept-reject \| stage0-accepts \| self:VIB000,VIB008 \| null-conditional 'x' / 'x' operator is banned in vibranium. | 1 | 491 | 0 |
| 069-47ef994df1 | regression:O1 (run: known:O1) | output-mismatch \| self \| stdout \| self-O0 | 1 | 499 | 0 |
| 070-3e10c79a4e | regression:O9 (run: known:O9) | invalid-accepted \| self \| CS9193 \|  | 1 | 523 | 0 |
| 071-f98ee1018b | regression:O11 (run: known-cut:O11) | banned-accepted \| analyzer+stage0 \| cut.localfunc-in-switch-section \|  | 1 | 497 | 0 |
| 072-5bf2c91cb9 | regression:O7 (run: known:O7) | accept-reject \| stage0-accepts \| self:VIB000,VIB001 \| no applicable local function overload in vibranium. | 1 | 517 | 0 |
| 073-36baa2fef7 | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB006} self-only:{} \|  | 1 | 522 | 19 |
| 074-24c88fa8ff | regression:O1 (run: known:O1) | accept-reject \| self-accepts \| stage0:VIB004 \| cannot pass shared borrow as mutable reference or receiver. | 1 | 554 | 0 |
| 075-323cdd7173 | regression:A2 (run: known:A2) | accept-reject \| stage0-accepts \| self:VIB007 \| c# value return requires a returnable safe-context (csn<path> | 1 | 575 | 10 |
| 076-0ae0dadfab | regression:O6 (run: known:O6) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB000,VIB008} \|  | 1 | 544 | 0 |
| 077-2bf14e0012 | new (run: new-low) | banned-wrong-id \| banned.linear-field \| self:VIB000 \| expected VIB006 | 1 | 522 | 19 |
| 078-6367c2cd0c | regression:O10 (run: known:O10) | invalid-accepted \| self \| CS8120 \|  | 1 | 569 | 0 |
| 079-2c40fa5fa7 | regression:A1 (run: known:A1) | native-build-failure \| stage0-O0 \|  \| undefined reference to fmod | 1 | 529 | 0 |
| 080-3ea9a0d6b7 | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB006} self-only:{} \|  | 1 | 580 | 14 |
| 081-5dd6181a0e | regression:O3 (run: known:O3) | accept-reject \| stage0-accepts \| self:VIB000 \| unresolved or captured identifier in vibranium. | 1 | 609 | 14 |
| 082-07a2040c7f | new (run: new-low) | banned-wrong-id \| banned.linear-field \| self:VIB000 \| expected VIB006 | 1 | 603 | 16 |
| 083-b487d4a757 | regression:O9 (run: known:O9) | invalid-accepted \| self \| CS9193 \|  | 1 | 634 | 0 |
| 084-b9af41e6a0 | regression:A3 (run: known:A3) | accept-reject \| stage0-accepts \| self:VIB000,VIB008 \| null-conditional 'x' / 'x' operator is banned in vibranium. | 1 | 613 | 0 |
| 085-227e2a20ee | regression:O1 (run: known:O1) | accept-reject \| stage0-accepts \| self:VIB001 \| c# requires this variable or struct field to be definitely assigned before it is read (csn | 1 | 643 | 0 |
| 086-d4d9aaae70 | regression:CUT2 (run: known-cut:CUT2) | accept-reject \| stage0-accepts \| self:VIB001 \| use of uninitialized value. | 1 | 644 | 0 |
| 087-d67840c422 | regression:CUT2 (run: known-cut:CUT2) | accept-reject \| stage0-accepts \| self:VIB001 \| out parameter must be initialized before return. | 1 | 660 | 0 |
| 088-3a4a674c1c | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB004} self-only:{} \|  | 1 | 668 | 16 |
| 089-17f54affa3 | regression:O1 (run: known:O1) | output-mismatch \| self \| stdout \| self-O0 | 1 | 645 | 0 |
| 090-b15894562c | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 665 | 19 |
| 091-459d1fe6f4 | regression:O8 (run: known:O8) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 671 | 0 |
| 092-c57cfc8d49 | regression:CUT2 (run: known-cut:CUT2) | banned-accepted \| analyzer+stage0 \| cut.struct-piecewise-owned \|  | 1 | 660 | 0 |
| 093-a00563353d | regression:O8 (run: known:O8) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable method overload on enclosing type in vibranium. | 1 | 674 | 0 |
| 094-c9ccf58bd0 | regression:O9 (run: known:O9) | invalid-accepted \| self \| CS9193 \|  | 1 | 678 | 0 |
| 095-8a08ea51b8 | regression:CUT2 (run: known-cut:CUT2) | banned-wrong-id \| cut.struct-piecewise-owned \| self:VIB001 \| expected VIB000 | 1 | 660 | 0 |
| 096-df1535cfeb | regression:N2 (run: new:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 703 | 9 |
| 097-1b8c9549ea | regression:O4 (run: known:O4) | invalid-accepted \| self \| CS1023,CS8510 \|  | 1 | 723 | 0 |
| 098-7da882ab8c | regression:O1 (run: known:O1) | accept-reject \| self-accepts \| stage0:VIB004 \| cannot pass shared borrow as mutable reference or receiver. | 1 | 717 | 0 |
| 099-6116ff8867 | regression:O1 (run: known:O1) | output-mismatch \| self \| stdout \| self-O0 | 1 | 730 | 0 |
| 100-76d7feaea3 | regression:O8 (run: known:O8) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable method overload on enclosing type in vibranium. | 1 | 732 | 0 |
| 101-a3ff22882c | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB004} self-only:{} \|  | 1 | 768 | 17 |
| 102-8955e4e29e | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB007} self-only:{} \|  | 1 | 772 | 14 |
| 103-216cbb2ea2 | regression:A3 (run: known:A3) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB006} self-only:{VIB000,VIB008} \|  | 1 | 761 | 0 |
| 104-d90b5ddf16 | regression:CUT2 (run: known-cut:CUT2) | accept-reject \| stage0-accepts \| self:VIB001 \| use of uninitialized value. | 1 | 775 | 0 |
| 105-11e1249c3a | regression:O1 (run: known:O1) | accept-reject \| self-accepts \| stage0:VIB004 \| cannot pass shared borrow as mutable reference or receiver. | 1 | 812 | 0 |
| 106-f1aff379d8 | regression:CUT2 (run: known-cut:CUT2) | banned-wrong-id \| cut.struct-piecewise-owned \| analyzer:VIB001 stage0:VIB001 self:VIB001 \| expected VIB000 | 1 | 809 | 0 |
| 107-3578d28da2 | regression:O1 (run: known:O1) | output-mismatch \| self \| stdout \| self-O0 | 1 | 808 | 0 |
| 108-488cd33b5f | regression:O6 (run: known:O6) | accept-reject \| self-accepts \| stage0:VIB001 \| use of uninitialized value 'x'. | 1 | 825 | 0 |
| 109-91165779a7 | regression:N5 (run: new:N5) | invalid-accepted \| self \| CS8846 \|  | 1 | 844 | 11 |
| 110-5992bccaeb | regression:N1 (run: new:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 849 | 14 |
| 111-fdf90e2b23 | regression:O1 (run: known:O1) | output-mismatch \| self \| stdout \| self-O0,self-O3-asan | 1 | 712 | 0 |
| 112-6d92a8de95 | regression:O1 (run: known:O1) | output-mismatch \| self \| exit+stdout+stderr \| self-O0 | 1 | 823 | 0 |
| 113-861dd5c19c | regression:O9 (run: known:O9) | invalid-accepted \| self \| CS9193 \|  | 1 | 933 | 0 |
| 114-45f872cfd8 | regression:O8 (run: known:O8) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 923 | 0 |
| 115-a7ff92da44 | regression:A1 (run: known:A1) | native-build-failure \| stage0-O0 \|  \| undefined reference to fmod | 1 | 861 | 0 |
| 116-55d2850c7f | new (run: new-low) | id-mismatch \| stage0+self \| stage0-only:{VIB008} self-only:{} \|  | 1 | 942 | 14 |
| 117-e5e30bb0dd | regression:O7 (run: known:O7) | accept-reject \| stage0-accepts \| self:VIB001 \| out parameter must be initialized before return. | 1 | 963 | 0 |
| 118-961111ab29 | regression:A1 (run: known:A1) | native-build-failure \| stage0-O0 \|  \| undefined reference to fmodf | 1 | 894 | 0 |
| 119-386095fa04 | regression:O6 (run: known:O6) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 972 | 0 |
| 120-5e753a50a5 | regression:CUT2 (run: known-cut:CUT2) | banned-accepted \| analyzer+stage0 \| cut.struct-piecewise-owned \|  | 1 | 989 | 0 |
| 121-15e8491187 | regression:CUT2 (run: known-cut:CUT2) | banned-wrong-id \| cut.struct-piecewise-owned \| self:VIB001 \| expected VIB000 | 1 | 989 | 0 |

| Warning (warning-only programs) | Programs | Analyzer clean | Stage-0 accepts | self accepts | First program |
|---|---|---|---|---|---|
| CS0162 | 41 | 37 | 37 | 34 | 6 |
| CS0219 | 11 | 11 | 11 | 10 | 2 |
| CS0168 | 10 | 7 | 7 | 7 | 39 |
| CS9192 | 3 | 2 | 2 | 3 | 268 |
| CS8321 | 2 | 1 | 1 | 1 | 30 |
| CS9191 | 2 | 2 | 2 | 0 | 57 |
| CS1718 | 1 | 1 | 1 | 1 | 672 |

Discovery curve (one row per 5 minutes; last event in each window): 

| Minute | Programs | Signatures | Buckets | New (not Round-10/cut) | Unattributed |
|---|---|---|---|---|---|
| ≤5 | 492 | 32 | 63 | 11 | 7 |
| ≤10 | 1000 | 47 | 120 | 24 | 16 |
| ≤15 | 1000 | 47 | 121 | 24 | 16 |

