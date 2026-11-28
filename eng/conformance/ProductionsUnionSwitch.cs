using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>
/// Anchor <c>UnionSwitch</c> (release-7.1, every profile): switch statements and switch expressions over
/// <c>Option&lt;T&gt;</c> and <c>Result&lt;T, int&gt;</c> whose arms mix the union cases with labels that test the whole
/// union: <c>not</c> of a case type, an <c>or</c> of the case types, a <c>var</c> designation or a case type with a
/// designation, with and without <c>when</c> guards, exhaustive or with a default. Payloads are <c>Copy</c>
/// (<c>int</c>), owners (<c>Vec&lt;int&gt;</c>, a class) or linear (an <c>IDisposable</c> class; union cases only, one arm
/// per case and no guard, the shapes every frontend accepts); scrutinees are places (also <c>Move(o)</c>) or calls. Each
/// scenario is a helper <c>int UsN(int p)</c> called three times, so that the run takes the None/Err path, a payload
/// path whose guards fail and a payload path whose guards hold: a label that a compiler drops or mis-tests changes the
/// printed value. Every shape is accepted (SPEC §3.4); managed, Stage-0 native and self native must agree.
/// </summary>
internal sealed partial class Gen
{
    private List<Stmt> PUnionSwitch(Inner inner)
    {
        var tags = new List<string>();
        int lo = _r.Range(0, 2), mid = _r.Range(3, 5), hi = _r.Range(6, 9);
        string body = UnionSwitchBody(tags, lo, mid);
        string helper = ActHelper("Us", n => $"int {n}(int p) {{ {body} }}", "", [.. tags]);
        return WithInner(inner, [UseInt($"{helper}({lo}) + {helper}({mid}) * 10 + {helper}({hi}) * 100")]);
    }

    private string UnionSwitchBody(List<string> tags, int lo, int mid)
    {
        bool opt = _r.Chance(0.6);
        // payload: 0 Copy int, 1 Vec<int> owner, 2 class owner (Option only), 3 linear IDisposable class
        int pk = opt ? _r.Weighted((3, 0), (3, 1), (2, 2), (2, 3)) : _r.Weighted((3, 0), (3, 1), (2, 3));
        bool copy = pk == 0, linear = pk == 3;
        string t, pv, mkBody;
        switch (pk)
        {
            case 0:
                t = "int";
                pv = "v";
                mkBody = opt ? "if (a > 2) return Some(a); return None;" : "if (a > 2) return Ok(a); return Err(a + 20);";
                tags.Add("uswitch.payload-copy");
                break;
            case 1:
                t = "Vec<int>";
                pv = "v.Count + v[0]";
                mkBody = opt ? "if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None;" : "if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20);";
                tags.Add("uswitch.payload-owner");
                break;
            case 2:
                t = Fresh("Nd");
                ActType($"public sealed class {t}(int v) {{ public int V = v; }}");
                pv = "v.V";
                mkBody = $"if (a > 2) return Some(new {t}(a)); return None;";
                tags.Add("uswitch.payload-class");
                break;
            default:
                t = Fresh("Rs");
                ActType($"public sealed class {t}(int id) : IDisposable {{ public int Id = id; public void Dispose() {{ WriteLine(Id); }} }}");
                string eat = ActHelper("Eat", n => $"int {n}({t} r) {{ using var u = r; return u.Id; }}");
                pv = $"{eat}(v)";
                mkBody = opt ? $"if (a > 2) return Some(new {t}(a)); return None;" : $"if (a > 2) return Ok(new {t}(a)); return Err(a + 20);";
                tags.Add("uswitch.payload-linear");
                break;
        }
        string union = opt ? $"Option<{t}>" : $"Result<{t}, int>";
        string mk = ActHelper("UsMk", n => $"{union} {n}(int a) {{ {mkBody} }}");
        tags.Add(opt ? "uswitch.option" : "uswitch.result");

        bool stmt = _r.Chance(0.7);
        tags.Add(stmt ? "uswitch.stmt" : "uswitch.expr");
        bool loop = stmt && _r.Chance(0.15);
        if (loop)
        {
            tags.Add("uswitch.loop");
        }
        string pvar = loop ? "(p + i)" : "p";

        // scrutinee: a place (owned local, borrowed in place; linear: Move(o)) or a call
        int sk = _r.Weighted((5, 0), (4, 1), (linear ? 2 : 0, 2));
        if ((linear && sk == 0) || (loop && sk == 2))
        {
            sk = 1; // a linear union cannot be borrow-matched in place (VIB006); Move(o) in a loop would move it twice
        }
        string pre = sk == 1 ? "" : $"{union} o = {mk}(p); ";
        string scr = sk switch { 0 => "o", 1 => $"{mk}({pvar})", _ => "Move(o)" };
        tags.Add(sk switch { 0 => "uswitch.place", 1 => "uswitch.rvalue", _ => "uswitch.move-place" });
        string post = sk == 0 ? (opt ? " n += o.IsSome ? 1000 : 0;" : " n += o.IsOk ? 1000 : 0;") : "";

        // the case types: A carries the payload (Some/Ok), B is the other case (None/Err(int))
        string aTy = opt ? $"Some<{t}>" : $"Ok<{t}>";
        string bTy = opt ? "NoneType" : "Err<int>";
        string aArm = $"{aTy}(var v)";
        string bArm = opt ? "NoneType" : "Err<int>(var e)";
        string bVal = opt ? "7" : "e";
        string isA = opt ? "IsSome" : "IsOk";
        string someGuard() => _r.Pick([$"{pvar} > 5", $"{pvar} == {mid}", $"{pvar} >= {mid}", $"{pvar} != {mid}", $"{pvar} % 2 == 0"]);
        string noneGuard() => _r.Pick([$"{pvar} < 3", $"{pvar} != {mid}", $"{pvar} <= {lo}"]);
        string anyGuard() => _r.Chance(0.5) ? someGuard() : noneGuard();

        // arms: (pattern, guard, value); value is an int expression; "brk" sections use continue in a loop
        var arms = new List<(string Pat, string? Guard, string Val, bool Lead)>();
        bool aCovered = false, bCovered = false, nonUnion = false;
        int lead = linear ? 7 : _r.Weighted((4, 0), (2, 1), (3, 2), (2, 3), (3, 4), (copy ? 2 : 0, 5), (copy ? 1 : 0, 6), (2, 7));
        switch (lead)
        {
            case 0:
                arms.Add(($"not {bTy}", someGuard(), "100", true));
                break;
            case 1:
                arms.Add(($"not {bTy}", null, "100", true));
                aCovered = true;
                break;
            case 2:
                arms.Add(($"not {aTy}", null, "3", true));
                bCovered = true;
                break;
            case 3:
                arms.Add(($"not {aTy}", noneGuard(), "3", true));
                break;
            case 4:
                arms.Add((_r.Chance(0.5) ? $"{aTy} or {bTy}" : $"{bTy} or {aTy}", anyGuard(), "100", true));
                tags.Add("uswitch.or-cases");
                break;
            case 5:
                arms.Add(("var w", anyGuard(), $"w.{isA} ? 100 : 200", true));
                tags.Add("uswitch.var");
                break;
            case 6:
                arms.Add(($"{aTy} s", someGuard(), "s.Value * 10", true));
                tags.Add("uswitch.type-designation");
                break;
        }
        if (lead <= 3)
        {
            tags.Add("uswitch.not-case");
        }
        if (lead is >= 0 and <= 5)
        {
            nonUnion = true;
        }
        if (arms.Count > 0 && arms[0].Guard is not null)
        {
            tags.Add("uswitch.guard");
            if (_r.Chance(0.3))
            {
                // a second whole-union fallback (both guarded)
                bool some = _r.Chance(0.5);
                arms.Add((some ? $"not {bTy}" : $"not {aTy}", some ? someGuard() : noneGuard(), "30", true));
                tags.Add("uswitch.not-case");
                tags.Add("uswitch.two-fallbacks");
            }
        }

        // the union cases after the lead labels
        bool varFinal = false;
        if (!aCovered)
        {
            if (!linear && _r.Chance(0.3))
            {
                string g = copy && _r.Chance(0.5) ? "v > 5" : someGuard();
                arms.Add((aArm, g, $"50 + {pv}", false));
                tags.Add("uswitch.case-guard");
            }
            // a final `var` catch-all after a guarded payload arm (Copy, or an owner matched from a call)
            varFinal = !linear && !bCovered && (copy || sk == 1) && _r.Chance(0.2);
            if (varFinal)
            {
                if (arms.Count == 0 || arms[^1].Pat != aArm)
                {
                    arms.Add((aArm, copy && _r.Chance(0.5) ? "v > 5" : someGuard(), $"50 + {pv}", false));
                }
                arms.Add(("var w", null, $"w.{isA} ? 300 : 400", false));
                tags.Add("uswitch.var-final");
                nonUnion = true;
            }
            else
            {
                arms.Add((aArm, null, pv, false));
            }
        }
        bool withDefault = false;
        if (!varFinal && !bCovered)
        {
            withDefault = _r.Chance(0.45);
            if (!withDefault)
            {
                arms.Add((bArm, null, bVal, false));
            }
        }
        tags.Add(withDefault ? "uswitch.default" : "uswitch.exhaustive");
        if (nonUnion)
        {
            tags.Add("uswitch.nonunion-label");
            tags.Add(stmt ? "uswitch.stmt-nonunion-label" : "uswitch.expr-whole-union-arm");
        }

        string sw;
        if (stmt)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"switch ({scr}) {{ ");
            foreach (var (pat, guard, val, isLead) in arms)
            {
                string g = guard is null ? "" : $" when {guard}";
                string end = loop && isLead ? "continue;" : "break;";
                sb.Append($"case {pat}{g}: n += {val}; {end} ");
            }
            if (withDefault)
            {
                sb.Append("default: n += 9; break; ");
            }
            sb.Append('}');
            sw = sb.ToString();
            if (loop)
            {
                sw = $"for (int i = 0; i < 2; i++) {{ {sw} n += 1; }}";
            }
        }
        else
        {
            string items = string.Join(", ", arms.Select(a => $"{a.Pat}{(a.Guard is null ? "" : " when " + a.Guard)} => {a.Val}").Concat(withDefault ? ["_ => 9"] : []));
            sw = $"n += {scr} switch {{ {items} }};";
        }
        return $"int n = 0; {pre}{sw}{post} return n;";
    }
}
