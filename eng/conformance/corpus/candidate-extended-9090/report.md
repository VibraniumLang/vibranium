### Run seed 9090: 39863 programs in 5400 s

* throughput: **442.9 programs/min** including reduction; frontends 246 ms/program, execution 579 ms/executed program, reduction 6766 oracle runs in 2193 worker-seconds
* C# validity: valid 24275, warning-only 1969, invalid 13619; executed (all frontends accept) 7088, of which non-terminating (managed run timed out or overflowed the call stack, comparison skipped) 8
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
| CUT2 | fixed | 8773 / 1399 | 1 | piecewise init of a not-yet-initialized struct with owned fields (v0.2 cut, implemented on the candidate: every frontend rejects with VIB000) |
| N1 | fixed | 197 / 77 | 8 | self rejects a `ref` argument for a `ref readonly` or `in` parameter (no applicable overload); on the candidate only the `in` form (warning-only CS9191) remains |
| N2 | fixed | 238 / 100 | 10 | self treats a constant-true guard (`when true`, or a folded constant such as `when 0.0 >= 0.0`) as a real guard (false 'switch expression must be exhaustive' / 'not all code paths return') |
| N3 | fixed | 445 / 195 | 9 | Stage0 native compound assignment `x += <rhs writing x via out/ref>` reads x after the rhs (managed/self read it first) |
| N4 | fixed | 3909 / 800 | 3 | self parses `(nint)-2` as a cast; C# parses a binary minus on the type name (CS0119) |
| N5 | fixed | **not found** | 0 | self skips checks inside a switch-expression arm it deems unreachable (`_` after complete Ok/Err) and accepts a non-exhaustive nested switch (CS8846/CS8509); possibly O5-family |
| N6 | fixed | 3109 / 636 | 6 | self accepts a pattern that can never match (CS8518, e.g. `x is > 0 and < 0`); residual of the O10 family |
| N7 | fixed | 2630 / 521 | 6 | self parses `a % b switch { … }` as `(a % b) switch { … }`; C# binds `switch` tighter than binary operators |
| N8 | fixed | 4 / 33 | 47 | reading a `Span<T>` local inside `foreach (var\|ref var e in span)`: Stage-0 reports VIB001 (treats foreach as a move), self VIB003; SPEC §3.2 says foreach reborrows (shared for `var`, so reading is allowed; exclusive for `ref var`, so VIB003) |
| N9 | fixed | **not found** | 0 | self ignores an always-true constant `is` pattern (`0 is > -1 and var q`) in definite assignment and reports VIB001; the program is warning-only (CS8793) |
| N10 | fixed | 12481 / 1768 | 2 | self accepts a non-constant `case` label or pattern (`case k:` with a non-const local `k`; CS9135/CS0150) |
| N11 | fixed | 987 / 286 (re-attributed) | 2 | self reports a reachable end point (VIB000, CS0163/CS8070) for a switch section that Roslyn proves unreachable: a `default:` section without `break` after cases that already cover the input (`case >= 0` on ushort, `>= 0`/`< 0` on int, `true`/`false` on bool); Roslyn only warns CS0162, and Stage-0/Analyzer accept |
| N12 | fixed | 18649 / 2562 (re-attributed) | 2 | self accepts a `ref` argument whose operand is a readonly variable (a `ref readonly` local, or an `in`/`ref readonly` parameter) for a `ref readonly` parameter (CS1510/CS8329); for a `ref` parameter self rejects with VIB004 |
| N13 | fixed | **not found** | 0 | Stage-0/Analyzer run flow checks (use after move VIB001, overlap VIB002/VIB003, definite assignment VIB001 after the switch) on a `default:` section that Roslyn proves unreachable (earlier value cases cover the input, CS0162); self (after its N11 fix) does not. Decided: flow checks follow Roslyn reachability, so Stage-0/Analyzer change. All frontends already skip `when false` sections, code after `return`, `if (false)` and code after Panic |
| N14 | new | **not found** | 0 | self cannot resolve a union-pattern variable (`Err<int>(var e)`, `Some<int>(var v)`) declared in one label of a multi-label switch section whose other label has a `when false` guard (VIB000 'Unresolved or captured identifier'); Roslyn treats the `when false` label as never matching, so the variable is definitely assigned, and Stage-0 accepts. Int patterns work |

| Bucket | Category | Signature | Hits | First program | min.cs lines |
|---|---|---|---|---|---|
| 001-d0de174874 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 4 | 10 |
| 002-1cfbcbb318 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001,VIB003 \| cannot borrow 'x' mutably while borrowed by 'x'. | 1 | 11 | 18 |
| 003-a2c5695110 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001,VIB003 \| cannot borrow 'x' mutably while borrowed by 'x'. | 1 | 136 | 12 |
| 004-490ed04072 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 175 | 10 |
| 005-de321b4fec | regression:N1 (run: known:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 197 | 10 |
| 006-113ab34a8c | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{} \|  | 1 | 180 | 15 |
| 007-943d9be005 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001,VIB003 \| cannot borrow 'x' mutably while borrowed by 'x'. | 1 | 216 | 18 |
| 008-3fba406e83 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001 \| use of moved value 'x'. | 1 | 250 | 12 |
| 009-e4a9bf5f60 | regression:N2 (run: known:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 238 | 7 |
| 010-8157370c11 | regression:N2 (run: known:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 291 | 10 |
| 011-b50fcad095 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001 \| use of moved value 'x'. | 1 | 384 | 20 |
| 012-f4b5c1e72e | regression:N1 (run: known:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 416 | 12 |
| 013-15f9541d79 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001 \| use of moved value 'x'. | 1 | 480 | 12 |
| 014-e3f16a8bae | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 487 | 11 |
| 015-49487bef4c | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001,VIB003 \| cannot borrow 'x' mutably while borrowed by 'x'. | 1 | 509 | 12 |
| 016-17ae3ec45b | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 515 | 13 |
| 017-d7eee47a62 | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 445 | 19 |
| 018-0743d76bf1 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001,VIB003 \| cannot borrow 'x' mutably while borrowed by 'x'. | 1 | 618 | 16 |
| 019-d3003ecbc5 | regression:N2 (run: known:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 785 | 8 |
| 020-dd9267ab1e | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{} \|  | 1 | 824 | 20 |
| 021-1b265ad7d4 | regression:N2 (run: known:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 887 | 10 |
| 022-ded37a335f | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001 \| use of moved value 'x'. | 1 | 943 | 15 |
| 023-a964436646 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 958 | 17 |
| 024-cf3caf0f0b | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001,VIB003 \| cannot borrow 'x' mutably while borrowed by 'x'. | 1 | 955 | 13 |
| 025-4cc9afd3b5 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{} \|  | 1 | 1009 | 16 |
| 026-a39f771a55 | regression:N11 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 987 | 19 |
| 027-d53e70ae26 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{} \|  | 1 | 1028 | 14 |
| 028-f25918dadd | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{} \|  | 1 | 1029 | 19 |
| 029-cb678ca5b3 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001 \| use of moved value 'x'. | 1 | 1120 | 14 |
| 030-94e31ca088 | regression:N2 (run: known:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 1123 | 7 |
| 031-2d84b238ae | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001 \| use of moved value 'x'. | 1 | 1204 | 15 |
| 032-e9508c6f5e | regression:N2 (run: known:N2) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 1183 | 17 |
| 033-0f9cf4cf91 | regression:N1 (run: known:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 2443 | 17 |
| 034-3822cd71b7 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{VIB000} \|  | 1 | 2530 | 15 |
| 035-934d38f6ac | regression:N7 (run: known:N7) | invalid-accepted \| self \| CS0266 \|  | 1 | 2630 | 8 |
| 036-9fff1f1235 | regression:N2 (run: known:N2) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 2633 | 26 |
| 037-72323457a8 | regression:N2 (run: known:N2) | accept-reject \| stage0-accepts \| self:VIB000 \| switch expression must be exhaustive (include a 'x' or 'x' catch-all arm) in vibranium. | 1 | 2901 | 11 |
| 038-acef0a4754 | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 2899 | 26 |
| 039-5d023a628d | regression:N6 (run: known:N6) | invalid-accepted \| self \| CS8518 \|  | 1 | 3109 | 11 |
| 040-d33cc3cccb | regression:N2 (run: known:N2) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 3350 | 12 |
| 041-388ea7cb96 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{} \|  | 1 | 3445 | 20 |
| 042-097691045a | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0,stage0-O3-asan | 1 | 3205 | 22 |
| 043-de766be310 | regression:N1 (run: known:N1) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 3799 | 22 |
| 044-6d4c2f9a71 | regression:N4 (run: known:N4) | invalid-accepted \| self \| CS0119 \|  | 1 | 3909 | 15 |
| 045-31c007a8f9 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{} \|  | 1 | 5043 | 13 |
| 046-aaa19d665c | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 5051 | 15 |
| 047-cb6ee24d67 | regression:N2 (run: known:N2) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB000} \|  | 1 | 5263 | 13 |
| 048-428747be7e | regression:N1 (run: known:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 5528 | 16 |
| 049-116f4d3e66 | regression:N1 (run: known:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable method overload on enclosing type in vibranium. | 1 | 5801 | 11 |
| 050-d833a27de8 | regression:N1 (run: known:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 6510 | 12 |
| 051-36deb75147 | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 6403 | 22 |
| 052-0e0117a8e3 | regression:N6 (run: known:N6) | invalid-accepted \| self \| CS8518 \|  | 1 | 7314 | 9 |
| 053-aed48b6ea6 | regression:N1 (run: known:N1) | accept-reject \| stage0-accepts \| self:VIB000 \| no applicable local function overload in vibranium. | 1 | 8388 | 15 |
| 054-c00ac13bd4 | regression:CUT2 | banned-accepted \| analyzer+stage0+self \| cut.struct-piecewise-owned \|  | 1 | 8773 | 21 |
| 055-0235032c21 | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0,stage0-O3-asan | 1 | 10974 | 16 |
| 056-d5f3c7beab | regression:N10 (run: known:N10) | invalid-accepted \| self \| CS9135 \|  | 1 | 12481 | 15 |
| 057-0d740fb7be | regression:N11 (run: new) | accept-reject \| stage0-accepts \| self:VIB000 \| c# switch section endpoints must not be reachable; panic is an ordinary invocation for sou | 1 | 12587 | 14 |
| 058-9afeb44373 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 13049 | 25 |
| 059-34e7f579ee | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 14031 | 12 |
| 060-07c40dbe6b | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB003} self-only:{} \|  | 1 | 15148 | 23 |
| 061-f97651c7c8 | regression:N7 (run: known:N7) | invalid-accepted \| self \| CS0266 \|  | 1 | 15806 | 10 |
| 062-e1063ca860 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{VIB000} \|  | 1 | 16196 | 20 |
| 063-ddfea62bbd | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{} self-only:{VIB003} \|  | 1 | 16434 | 14 |
| 064-f4da555518 | regression:N7 (run: known:N7) | invalid-accepted \| self \| CS0266 \|  | 1 | 16488 | 12 |
| 065-d30adb7059 | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB003} self-only:{} \|  | 1 | 16993 | 25 |
| 066-2c7c0744a2 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{VIB000} \|  | 1 | 17841 | 12 |
| 067-d62f4ea01f | regression:N12 (run: new) | invalid-accepted \| self \| CS1510 \|  | 1 | 18649 | 18 |
| 068-2e6b71aab8 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB000} \|  | 1 | 19003 | 16 |
| 069-cb71db8d38 | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB003} self-only:{} \|  | 1 | 19321 | 23 |
| 070-b87c973514 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 19802 | 11 |
| 071-1eda4272a8 | regression:N7 (run: known:N7) | invalid-accepted \| self \| CS0266 \|  | 1 | 20055 | 16 |
| 072-8b72b969aa | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 20607 | 17 |
| 073-91ef03ca08 | new | managed-crash \| managed \| exit134 \| stack overflow. | 1 | 20809 | 23 |
| 074-0d95f3fd5c | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0,stage0-O3-asan | 1 | 21001 | 12 |
| 075-66858c8cf5 | new | output-mismatch \| stage0+self \| exit+stderr \| stage0-O0,self-O0 | 1 | 20809 | 23 |
| 076-03ff036da6 | regression:N3 (run: known:N3) | output-mismatch \| stage0 \| stdout \| stage0-O0 | 1 | 21224 | 27 |
| 077-5acc28fa4f | regression:N6 (run: known:N6) | invalid-accepted \| self \| CS8518 \|  | 1 | 21829 | 12 |
| 078-fb8e81d01a | regression:N7 (run: known:N7) | invalid-accepted \| self \| CS0266 \|  | 1 | 21935 | 15 |
| 079-4b60658972 | regression:N6 (run: known:N6) | invalid-accepted \| self \| CS8518 \|  | 1 | 22133 | 13 |
| 080-086d6bffcf | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 23048 | 25 |
| 081-7c67e886bf | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB000} \|  | 1 | 23071 | 12 |
| 082-897e35c51d | regression:N7 (run: known:N7) | invalid-accepted \| self \| CS0266 \|  | 1 | 23813 | 8 |
| 083-bc4f26b837 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB000} \|  | 1 | 23918 | 11 |
| 084-5ab1401fd9 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB000} \|  | 1 | 24606 | 15 |
| 085-0deb14406c | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 24721 | 8 |
| 086-5f72f506d4 | regression:N8 (run: known:N8) | accept-reject \| self-accepts \| stage0:VIB001,VIB003 \| use of moved value 'x'. | 1 | 25881 | 13 |
| 087-08bd2c6b97 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB000} \|  | 1 | 26378 | 15 |
| 088-9adccfce76 | regression:N4 (run: new) | invalid-accepted \| self \| CS0075 \|  | 1 | 27279 | 11 |
| 089-1941e77e10 | regression:N12 (run: new) | invalid-accepted \| self \| CS1510 \|  | 1 | 28163 | 13 |
| 090-9fff13d5a7 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB003} \|  | 1 | 28424 | 27 |
| 091-003218b43c | new | managed-crash \| managed \| exit134 \| stack overflow. | 1 | 28263 | 15 |
| 092-1e2bc42b05 | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001} self-only:{VIB000} \|  | 1 | 29308 | 16 |
| 093-a525e62261 | regression:N10 (run: known:N10) | invalid-accepted \| self \| CS9135 \|  | 2 | 29966 | 23 |
| 094-152ac1adff | new | output-mismatch \| stage0+self \| exit+stderr \| stage0-O0,self-O0 | 1 | 28263 | 15 |
| 095-f74062e156 | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB003} self-only:{} \|  | 1 | 32368 | 22 |
| 096-5b8e5a2b1c | regression:N8 (run: known:N8) | id-mismatch \| stage0+self \| stage0-only:{VIB001,VIB003} self-only:{VIB000} \|  | 1 | 32616 | 11 |
| 097-79efffb671 | regression:N6 (run: known:N6) | invalid-accepted \| self \| CS8518 \|  | 1 | 33296 | 9 |
| 098-b2835141f6 | regression:N6 (run: known:N6) | invalid-accepted \| self \| CS8518 \|  | 1 | 34324 | 22 |
| 099-aebbbf6d32 | regression:N8 (run: new) | id-mismatch \| stage0+self \| stage0-only:{VIB003} self-only:{} \|  | 1 | 38518 | 24 |
| 100-843b3d9902 | regression:N4 (run: known:N4) | invalid-accepted \| self \| CS0119 \|  | 1 | 39510 | 15 |

| Warning (warning-only programs) | Programs | Analyzer clean | Stage-0 accepts | self accepts | First program |
|---|---|---|---|---|---|
| CS0162 | 1187 | 710 | 710 | 739 | 11 |
| CS0168 | 484 | 263 | 263 | 279 | 0 |
| CS0219 | 146 | 86 | 86 | 88 | 95 |
| CS9191 | 79 | 50 | 50 | 0 | 6 |
| CS1718 | 65 | 45 | 45 | 45 | 843 |
| CS8321 | 29 | 13 | 13 | 16 | 3752 |
| CS1717 | 10 | 3 | 3 | 3 | 4799 |
| CS8793 | 9 | 7 | 7 | 7 | 2905 |
| CS1522 | 5 | 4 | 4 | 4 | 4648 |
| CS8519 | 5 | 3 | 3 | 3 | 7561 |
| CS9195 | 3 | 0 | 0 | 0 | 479 |
| CS8520 | 1 | 1 | 1 | 1 | 33788 |

| Near-miss tag (C#-valid, Vibranium-invalid) | Programs | Accepted by every frontend | First accepted program |
|---|---|---|---|
| `mut.nm-borrow-live` | 415 | 0 | — |
| `mut.nm-foreach-mutate` | 455 | 1 | 21277 |
| `mut.nm-from-shared` | 465 | 0 | — |
| `mut.nm-in-param` | 414 | 0 | — |
| `mut.nm-moved` | 206 | 1 | 27347 |
| `mut.nm-two-phase` | 235 | 2 | 12177 |
| `optmut.nm-alias` | 505 | 2 | 21368 |
| `optmut.nm-move` | 451 | 0 | — |
| `optmut.nm-overlap` | 444 | 1 | 12486 |
| `optref.nm-write` | 446 | 1 | 14927 |
| `optslot.nm-place-write` | 457 | 0 | — |
| `span.nm-moved` | 890 | 3 | 4794 |
| `span.nm-overlap` | 560 | 2 | 9509 |
| `span.nm-parent-while-live` | 563 | 2 | 14754 |
| `span.nm-vec-grow` | 305 | 1 | 38551 |
| `span.two-slices-one-call` | 1053 | 2 | 19076 |
| `std.nm-buf-borrow` | 608 | 1 | 11233 |
| `std.nm-map-key-borrow` | 588 | 1 | 24594 |
| `std.nm-map-moved` | 602 | 2 | 29514 |
| `utf8.nm-mutate-owner` | 289 | 3 | 5628 |
| `utf8.nm-slice-after-clear` | 317 | 0 | — |
| `utf8.nm-view-local` | 1831 | 0 | — |

Discovery curve (one row per 5 minutes; last event in each window): 

| Minute | Programs | Signatures | Buckets | New (not Round-10/cut) | Unattributed |
|---|---|---|---|---|---|
| ≤5 | 1063 | 8 | 28 | 1 | 1 |
| ≤10 | 2935 | 10 | 37 | 1 | 1 |
| ≤15 | 4008 | 13 | 44 | 1 | 1 |
| ≤20 | 6650 | 14 | 51 | 1 | 1 |
| ≤25 | 8865 | 15 | 54 | 1 | 1 |
| ≤30 | 12705 | 17 | 57 | 2 | 2 |
| ≤35 | 15206 | 19 | 60 | 3 | 3 |
| ≤40 | 17036 | 20 | 65 | 5 | 5 |
| ≤45 | 19393 | 22 | 69 | 7 | 7 |
| ≤50 | 21385 | 24 | 76 | 9 | 9 |
| ≤55 | 22270 | 24 | 79 | 9 | 9 |
| ≤60 | 24795 | 24 | 85 | 9 | 9 |
| ≤65 | 27314 | 26 | 88 | 10 | 10 |
| ≤70 | 29999 | 26 | 93 | 12 | 12 |
| ≤75 | 32436 | 26 | 95 | 14 | 14 |
| ≤80 | 34414 | 26 | 98 | 14 | 14 |
| ≤90 | 39590 | 26 | 100 | 15 | 15 |

