using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vibranium.Analyzer;
using Vibranium.Cli;
using Vibranium.Frontend.Roslyn;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SwitchGuardBorrowTests
{
    // On a place selector a matching guard holds a shared borrow, even with no live payload binding.
    public static IEnumerable<object[]> RejectCases()
    {
        yield return new object[] { "01-local-guard-expr-ref-false", """
            Option<int> o=Some(3);
            WriteLine(o switch {Some<int> when Clear(ref o)=>1, not NoneType=>2, _=>3});
            static bool Clear(ref Option<int> x){x=None; return false;}
            """ };
        yield return new object[] { "02-local-guard-stmt-ref-false", """
            Option<int> o=Some(3);
            switch(o){
             case Some<int> when Clear(ref o): WriteLine(1); break;
             case not NoneType: WriteLine(2); break;
             default: WriteLine(3); break;
            }
            static bool Clear(ref Option<int> x){x=None; return false;}
            """ };
        yield return new object[] { "03-local-guard-payload-ref-false", """
            Option<int> o=Some(3);
            WriteLine(o switch {Some<int> when Change(ref o)=>1, Some<int>(var n)=>n, _=>0});
            static bool Change(ref Option<int> x){x=Some(8); return false;}
            """ };
        yield return new object[] { "04-local-assign", """
            Option<int> o = Some(3);
            WriteLine(o switch { Some<int> when (o = None).IsSome => 1, _ => 2 });
            """ };
        yield return new object[] { "05-value-param-out", """
            WriteLine(F(Some(3)));
            static int F(Option<int> o) => o switch { Some<int> when Change(out o) => 1, _ => 2 };
            static bool Change(out Option<int> o) { o = None; return true; }
            """ };
        yield return new object[] { "06-ref-param", """
            Option<int> o = Some(3);
            WriteLine(F(ref o));
            static int F(ref Option<int> o) => o switch { Some<int> when Change(ref o) => 1, _ => 2 };
            static bool Change(ref Option<int> o) { o = None; return true; }
            """ };
        yield return new object[] { "07-local-struct-field", """
            S s = new S();
            s.O = Some(3);
            WriteLine(s.O switch { Some<int> when Change(ref s.O) => 1, _ => 2 });
            static bool Change(ref Option<int> o) { o = None; return false; }
            struct S { public Option<int> O; }
            """ };
        yield return new object[] { "08-local-struct-owner", """
            S s = new S();
            s.O = Some(3);
            WriteLine(s.O switch { Some<int> when Change(ref s) => 1, _ => 2 });
            static bool Change(ref S s) { s = default; return true; }
            struct S { public Option<int> O; }
            """ };
        yield return new object[] { "09-span-element", """
            Span<Option<int>> s = [Some(3), Some(4)];
            WriteLine(s[0] switch { Some<int> when Change(ref s[0]) => 1, _ => 2 });
            static bool Change(ref Option<int> o) { o = None; return false; }
            """ };
        yield return new object[] { "11-move-noncopy", """
            Vec<int> v = [3];
            WriteLine(v switch { var _ when Take(v) => 1, _ => 2 });
            static bool Take(Vec<int> x) => x.Count > 0;
            """ };
        yield return new object[] { "12-ref-local-alias", """
            Option<int> o = Some(3);
            ref Option<int> a = ref o;
            WriteLine(o switch { Some<int> when Change(ref a) => 1, _ => 2 });
            static bool Change(ref Option<int> r) { r = None; return false; }
            """ };
        yield return new object[] { "13-last-arm-stmt", """
            Option<int> o = Some(3);
            switch (o)
            {
                case NoneType: WriteLine(0); break;
                case Some<int> when Change(ref o): WriteLine(1); break;
            }
            static bool Change(ref Option<int> r) { r = None; return true; }
            """ };
        yield return new object[] { "14-nested-guard-mutates-outer", """
            Option<int> o = Some(3);
            Option<int> inner = Some(4);
            WriteLine(o switch
            {
                Some<int> when (inner switch { Some<int> when Change(ref o) => true, _ => false }) => 1,
                _ => 2
            });
            static bool Change(ref Option<int> r) { r = None; return false; }
            """ };
        yield return new object[] { "15-noncopy-guard-ref", """
            Vec<int> v = [3];
            Option<Vec<int>> o = Some(v);
            WriteLine(o switch { Some<Vec<int>>(var payload) when Change(ref o) => payload.Count, _ => 2 });
            static bool Change(ref Option<Vec<int>> r) { r = None; return false; }
            """ };
        yield return new object[] { "24-scalar-guard-assign", """
            int i = 3;
            WriteLine(((i)) switch { 3 when (i = 0) > 0 => 1, 3 => 2, _ => 3 });
            """ };
        yield return new object[] { "27-alias-write", """
            Option<int> o = Some(3);
            ref Option<int> p = ref o;
            WriteLine(p switch { Some<int> when (p = None).IsSome => 1, _ => 2 });
            """ };
        yield return new object[] { "28-short-circuit-guard", """
            Option<int> o = Some(3);
            WriteLine(o switch { Some<int> when o.IsSome && Change(ref o) => 1, _ => 2 });
            static bool Change(ref Option<int> x) { x = None; return false; }
            """ };
        yield return new object[] { "29-contained-part", """
            S s = new S();
            s.N = 3;
            WriteLine(s switch { var _ when (s.N = 0) > 0 => 1, _ => 2 });
            struct S { public int N; }
            """ };
        yield return new object[] { "39-vec-element-last-guard", """
            Vec<int> w = [4];
            switch (w[0])
            {
                case 4 when Grow(ref w): WriteLine(1); break;
            }
            static bool Grow(ref Vec<int> w) { w.Add(5); return true; }
            """ };
        yield return new object[] { "gb05-vec-element-rvalue", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Vec<int> w = [p];
                switch (w[0])
                {
                    case 4 when Grow(ref w): n = 1; break;
                    case 4: n = 20 + w.Count; break;
                    default: n = 30 + w.Count; break;
                }
                return n;
            }
            static bool Grow(ref Vec<int> w) { w.Add(5); return false; }
            """ };
        yield return new object[] { "gc09-optvec-place-stmt", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Option<Vec<int>> o = MkV(p);
                switch (o)
                {
                    case Some<Vec<int>>(var v) when ClearV(ref o): n = 1; break;
                    case Some<Vec<int>>(var v): n = 10 + v.Count; break;
                    default: n = 3; break;
                }
                return n;
            }
            static bool ClearV(ref Option<Vec<int>> x) { x = None; return false; }
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """ };
        yield return new object[] { "gc10-optcls-place-expr", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Option<Nd> o = MkN(p);
                n = o switch { Some<Nd>(var d) when ClearN(ref o) => 1, Some<Nd>(var d) => 10 + d.V, _ => 3 };
                return n;
            }
            static bool ClearN(ref Option<Nd> x) { x = None; return false; }
            static Option<Nd> MkN(int a) { if (a > 2) return Some(new Nd(a)); return None; }
            public sealed class Nd(int v) { public int V = v; }
            """ };
        yield return new object[] { "gc11-int-place-assign-in-guard", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                int x = p;
                n = x switch { 4 when (x = 14) < 0 => 1, 14 => 2, _ => 3 };
                return n;
            }
            """ };
        yield return new object[] { "gc12-struct-whole-ref", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Pt s = new Pt(p, 0);
                switch (s.X)
                {
                    case 4 when BumpS(ref s): n = 1; break;
                    case 14: n = 2; break;
                    default: n = 3; break;
                }
                return n;
            }
            static bool BumpS(ref Pt s) { s.X += 10; return false; }
            public struct Pt(int x, int y) { public int X = x; public int Y = y; }
            """ };
        yield return new object[] { "gd04-vec-elem-switch-expr", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Vec<int> w = [p];
                n = w[0] switch { 4 when Grow(ref w) => 1, 4 => 20 + w.Count, _ => 30 + w.Count };
                return n;
            }
            static bool Grow(ref Vec<int> w) { w.Add(5); return false; }
            """ };
        yield return new object[] { "gd08-class-field-scrutinee-mut-member", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Nd d = new Nd(p);
                switch (d.V) { case 4 when d.Bump(): n = 1; break; case 14: n = 2; break; default: n = 3 + d.V; break; }
                return n;
            }
            public sealed class Nd(int v) { public int V = v; [Mut] public bool Bump() { V += 10; return false; } }
            """ };
        yield return new object[] { "gd10-vec-elem-place-stmt-assign-elem", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Vec<int> w = [p];
                switch (w[0]) { case 4 when Set0(ref w): n = 1; break; case 14: n = 2; break; default: n = 3; break; }
                return n;
            }
            static bool Set0(ref Vec<int> w) { w[0] = 14; return false; }
            """ };
    }

    [Theory]
    [MemberData(nameof(RejectCases))]
    public Task GuardCannotInvalidatePlaceScrutinee(string shape, string source) =>
        AssertRoslynIdsAsync(shape, source, "VIB003");

    public static IEnumerable<object[]> ReviewWitnesses()
    {
        yield return ["f13-review-cursor-fallback-expr", "", """
            Option<Box> o = Some(new Box(9));
            var m = o.AsMut();
            int k = 1;
            WriteLine(m switch { Some<Box>(var b) => b.V, NoneType when k > 0 => 2, _ => 3 });
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-cursor-reverse-expr", "", """
            Option<Box> o = Some(new Box(9));
            var m = o.AsMut();
            int k = 1;
            WriteLine(m switch { NoneType when k > 0 => 2, Some<Box>(var b) => b.V, _ => 3 });
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-cursor-discard-guard-expr", "", """
            Option<Box> o = Some(new Box(7));
            var m = o.AsMut();
            int k = 1;
            WriteLine(m switch { Some<Box>(var b) => b.V, _ when k > 0 => 0, _ => -1 });
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-cursor-fallback-stmt", "", """
            Option<Box> o = Some(new Box(9));
            var m = o.AsMut();
            int k = 1;
            switch (m) { case Some<Box>(var b): WriteLine(b.V); break; case NoneType when k > 0: WriteLine(2); break; default: WriteLine(3); break; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-cursor-reverse-stmt", "", """
            Option<Box> o = Some(new Box(9));
            var m = o.AsMut();
            int k = 1;
            switch (m) { case NoneType when k > 0: WriteLine(2); break; case Some<Box>(var b): WriteLine(b.V); break; default: WriteLine(3); break; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-cursor-discard-guard-stmt", "", """
            Option<Box> o = Some(new Box(7));
            var m = o.AsMut();
            int k = 1;
            switch (m)
            {
                case Some<Box>(var b): WriteLine(b.V); break;
                case var _ when k > 0: WriteLine(0); break;
                default: WriteLine(-1); break;
            }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-cursor-not-none-expr", "", """
            Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;
            WriteLine(m switch { not NoneType when k>0 => 8, _=>0 });
            public sealed class Box(int v) { public int V=v; }
            """];
        yield return ["f13-review-cursor-not-none-stmt", "", """
            Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;
            switch (m) { case not NoneType when k > 0: WriteLine(8); break; default: WriteLine(0); break; }
            public sealed class Box(int v) { public int V=v; }
            """];
        yield return ["f13-review-cursor-or-expr", "", """
            Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;
            WriteLine(m switch { Some<Box> or NoneType when k>0 => 8, _=>0 });
            public sealed class Box(int v) { public int V=v; }
            """];
        yield return ["f13-review-cursor-or-stmt", "", """
            Option<Box> o=Some(new Box(9)); var m=o.AsMut(); int k=1;
            switch (m) { case Some<Box> or NoneType when k > 0: WriteLine(8); break; default: WriteLine(0); break; }
            public sealed class Box(int v) { public int V=v; }
            """];
        yield return ["f13-review-cursor-not-some-expr", "", """
            Option<Box> o = None;
            var m = o.AsMut();
            int k = 1;
            WriteLine(m switch { not Some<Box> when k > 0 => 1, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-cursor-not-some-stmt", "", """
            Option<Box> o = None;
            var m = o.AsMut();
            int k = 1;
            switch (m) { case not Some<Box> when k > 0: WriteLine(1); break; default: WriteLine(0); break; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-rvalue-a-touch-expr", "VIB003", """
            Option<Box> o = Some(new Box(9));
            WriteLine(o.AsMut() switch { Some<Box>(var n) when Touch(ref o) => n.V, _ => 0 });
            static bool Touch(ref Option<Box> x) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-rvalue-b-reset-expr", "VIB003", """
            Option<Box> o = Some(new Box(9));
            WriteLine(o.AsMut() switch { Some<Box>(var n) when Reset(ref o) => n.V, _ => 0 });
            static bool Reset(ref Option<Box> x) { x = None; return false; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-rvalue-c-assign-expr", "VIB003", """
            Option<Box> o = Some(new Box(9));
            WriteLine(o.AsMut() switch { Some<Box>(var n) when (o = None) is NoneType => n.V, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-rvalue-d-touch-stmt", "VIB003", """
            Option<Box> o = Some(new Box(9));
            switch (o.AsMut()) { case Some<Box>(var n) when Touch(ref o): WriteLine(n.V); break; default: WriteLine(0); break; }
            static bool Touch(ref Option<Box> x) => false;
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-rvalue-d-reset-stmt", "VIB003", """
            Option<Box> o = Some(new Box(9));
            switch (o.AsMut()) { case Some<Box>(var n) when Reset(ref o): WriteLine(n.V); break; default: WriteLine(0); break; }
            static bool Reset(ref Option<Box> x) { x = None; return false; }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-rvalue-e-payload-write-guard-fail", "", """
            Option<Box> o = Some(new Box(9));
            WriteLine(o.AsMut() switch { Some<Box>(var n) when (n.V = 5) < 0 => n.V, _ => 0 });
            WriteLine(o.Unwrap().V);
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-rvalue-cursor-rvalue-guard", "", """
            Option<Box> o = Some(new Box(9));
            int k = 1;
            WriteLine(o.AsMut() switch { Some<Box>(var n) when k > 0 => n.V, _ => 0 });
            public sealed class Box(int v) { public int V=v; }
            """];
        yield return ["f13-review-rvalue-cursor-rvalue-guard-stmt", "", """
            Option<Box> o = Some(new Box(9)); int k = 1;
            switch(o.AsMut()) { case Some<Box>(var n) when k > 0: WriteLine(n.V); break; default: WriteLine(0); break; }
            public sealed class Box(int v) { public int V=v; }
            """];
        yield return ["f13-review-slot-vec-index-expr", "VIB003", """
            Vec<int> v1 = [4];
            Vec<int> v2 = [7];
            ref Vec<int> x = ref v1;
            WriteLine(x[0] switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });
            WriteLine(x[0]);
            """];
        yield return ["f13-review-slot-vec-index-stmt", "VIB003", """
            Vec<int> v1 = [4];
            Vec<int> v2 = [7];
            ref Vec<int> x = ref v1;
            switch (x[0])
            {
                case 4 when (x = ref v2).Count == 0: WriteLine(1); break;
                case 7: WriteLine(2); break;
                case 4: WriteLine(9); break;
                default: WriteLine(3); break;
            }
            WriteLine(x[0]);
            """];
        yield return ["f13-review-slot-readonly-call-expr", "VIB003", """
            int a = 4;
            int b = 7;
            ref int x = ref a;
            WriteLine(AtRo(in x) switch { 4 when (x = ref b) == 0 => 1, 7 => 2, 4 => 9, _ => 3 });
            WriteLine(x);
            static ref readonly int AtRo(in int r) => ref r;
            """];
        yield return ["f13-review-slot-readonly-call-stmt", "VIB003", """
            int a = 4;
            int b = 7;
            ref int x = ref a;
            switch (AtRo(in x))
            {
                case 4 when (x = ref b) == 0: WriteLine(1); break;
                case 7: WriteLine(2); break;
                case 4: WriteLine(9); break;
                default: WriteLine(3); break;
            }
            WriteLine(x);
            static ref readonly int AtRo(in int r) => ref r;
            """];
        yield return ["f13-review-slot-refassign-index-expr", "VIB003", """
            Vec<int> v0 = [0];
            Vec<int> v1 = [4];
            Vec<int> v2 = [7];
            ref Vec<int> x = ref v0;
            WriteLine((x = ref v1)[0] switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, 4 => 9, _ => 3 });
            WriteLine(x[0]);
            """];
        yield return ["f13-review-slot-refassign-index-stmt", "VIB003", """
            Vec<int> v0 = [0];
            Vec<int> v1 = [4];
            Vec<int> v2 = [7];
            ref Vec<int> x = ref v0;
            switch ((x = ref v1)[0]) { case 4 when (x = ref v2).Count == 0: WriteLine(1); break; case 7: WriteLine(2); break; case 4: WriteLine(9); break; default: WriteLine(3); break; }
            WriteLine(x[0]);
            """];
        yield return ["f13-review-slot-mutable-call-expr", "VIB003", """
            int a = 4;
            int b = 7;
            ref int x = ref a;
            WriteLine(At(ref x) switch { 4 when (x = ref b) == 0 => 1, 7 => 2, 4 => 9, _ => 3 });
            WriteLine(x);
            static ref int At(ref int r) => ref r;
            """];
        yield return ["f13-review-slot-mutable-call-stmt", "VIB003", """
            int a = 4;
            int b = 7;
            ref int x = ref a;
            switch (At(ref x))
            {
                case 4 when (x = ref b) == 0: WriteLine(1); break;
                case 7: WriteLine(2); break;
                case 4: WriteLine(9); break;
                default: WriteLine(3); break;
            }
            WriteLine(x);
            static ref int At(ref int r) => ref r;
            """];
        yield return ["f13-review-slot-ref-property-expr", "VIB003", """
            Holder h1 = new Holder(4);
            Holder h2 = new Holder(7);
            ref Holder x = ref h1;
            WriteLine(x.RefV switch { 4 when (x = ref h2).V == 0 => 1, 7 => 2, 4 => 9, _ => 3 });
            WriteLine(x.V);
            public sealed class Holder(int v) { public int V = v; public ref readonly int RefV => ref V; }
            """];
        yield return ["f13-review-slot-unwrapmut-expr", "VIB003", """
            Option<int> o1 = Some(4);
            Option<int> o2 = Some(7);
            ref Option<int> x = ref o1;
            WriteLine(x.UnwrapMut() switch { 4 when (x = ref o2).IsEmpty => 1, 7 => 2, 4 => 9, _ => 3 });
            WriteLine(x.Unwrap());
            """];
        yield return ["f13-review-slot-index-value-stmt", "", """
            Span<int> s = [4,7]; int a=0; int b=1; ref int i=ref a;
            switch(s[i]) { case 4 when (i=ref b)==0: WriteLine(0); break; case 4: WriteLine(i+4); break; default: WriteLine(-1); break; }
            """];
        yield return ["f13-review-chain-readonly-expr", "", """
            int a = 4, b = 7;
            ref readonly int x = ref a;
            ref readonly int y = ref x;
            WriteLine(y switch { 4 when (x = ref b) == 0 => 1, 4 => 2, _ => 3 });
            """];
        yield return ["f13-review-chain-mutable-expr", "VIB003", """
            int a = 4, b = 7;
            ref int x = ref a;
            ref int y = ref x;
            WriteLine(y switch { 4 when (x = ref b) == 0 => 1, 4 => 2, _ => 3 });
            """];
        yield return ["f13-review-chain-write-referent-expr", "VIB003", """
            int a = 4;
            ref readonly int x = ref a;
            ref readonly int y = ref x;
            WriteLine(y switch { 4 when (a = 9) == 0 => 1, 4 => 2, _ => 3 });
            """];
        yield return ["f13-review-opus-b15-optmut-guard-nobind-last-arm", "", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            switch (m)
            {
                case NoneType: WriteLine(0); break;
                case Some<Box> when k > 0: WriteLine(1); break;
            }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-opus-b15c-optmut-guard-nobind-none-first-default", "", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            switch (m)
            {
                case NoneType: WriteLine(0); break;
                case Some<Box> when k > 0: WriteLine(1); break;
                default: WriteLine(2); break;
            }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-opus-b15e-optmut-guard-nobind-not-none", "", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            WriteLine(m switch { not NoneType when k > 0 => 1, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-opus-b15j-optmut-none-guarded-some-unguarded-some", "", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            switch (m)
            {
                case NoneType: WriteLine(0); break;
                case Some<Box> when k > 0: WriteLine(1); break;
                case Some<Box>: WriteLine(2); break;
            }
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-review-opus-e08-copy-payload-binding-guard-inc", "", """
            Option<int> o = Some(4);
            switch (o)
            {
                case Some<int>(var n) when Inc(ref n): WriteLine(1); break;
                case Some<int>(4): WriteLine(2); break;
                case Some<int>(var m): WriteLine(m); break;
                default: WriteLine(3); break;
            }
            static bool Inc(ref int x) { x = 5; return false; }
            """];
        yield return ["f13-review-opus-e11-result-copy-payload-binding-guard-inc", "", """
            Result<int, int> r = Ok(4);
            WriteLine(r switch { Ok<int>(var n) when Inc(ref n) => 1, Ok<int>(4) => 2, _ => 3 });
            static bool Inc(ref int x) { x = 5; return false; }
            """];
        yield return ["f13-review-opus-f01-refvec-elem-repoint-stmt", "VIB003", """
            Vec<int> v1 = [4];
            Vec<int> v2 = [7];
            ref Vec<int> x = ref v1;
            switch (x[0])
            {
                case 4 when (x = ref v2).Count == 0: WriteLine(1); break;
                case 7: WriteLine(2); break;
                default: WriteLine(3); break;
            }
            WriteLine(x[0]);
            """];
        yield return ["f13-review-opus-f03-refstruct-vecfield-elem-repoint", "VIB003", """
            S a = new S([4]);
            S b = new S([7]);
            ref S x = ref a;
            WriteLine(x.V[0] switch { 4 when (x = ref b).V.Count == 0 => 1, 7 => 2, _ => 3 });
            WriteLine(x.V[0]);
            public struct S(Vec<int> v) { public Vec<int> V = v; }
            """];
        yield return ["36-arm-copy-binding-stmt-existing", "", """
            Option<int> o = Some(3);
            switch (o)
            {
                case Some<int>(var n) when o.IsSome: o = None; WriteLine(n); break;
                default: break;
            }
            WriteLine(o.IsEmpty);
            """];
        yield return ["37-arm-copy-binding-expr-existing", "", """
            Option<int> o = Some(3);
            WriteLine(o switch { Some<int>(var n) when o.IsSome => Change(ref o) + n, _ => 0 });
            WriteLine(o.IsEmpty);
            static int Change(ref Option<int> r) { r = None; return 1; }
            """];
        yield return ["f13-review-copy-n26-option-is", "", """
            Option<int> p = Some(4);
            if (p is Some<int>(var y) && Set(ref p) && y == 4) WriteLine("copy"); else WriteLine("alias");
            WriteLine(p.IsSome);
            static bool Set(ref Option<int> q) { q = None; return true; }
            """];
        yield return ["f13-review-copy-n26-result-is", "", """
            Result<int, int> p = Ok(4);
            if (p is Ok<int>(var y) && Set(ref p) && y == 4) WriteLine("copy"); else WriteLine("alias");
            WriteLine(p is Err<int>(9));
            static bool Set(ref Result<int, int> q) { q = Err(9); return true; }
            """];
        yield return ["f13-review-copy-result-err-guard-stmt", "", """
            Result<int, int> r = Err(4);
            switch (r)
            {
                case Err<int>(var n) when Inc(ref n): WriteLine(1); break;
                case Err<int>(4): WriteLine(2); break;
                default: WriteLine(3); break;
            }
            static bool Inc(ref int n) { n = 5; return false; }
            """];
        yield return ["f13-review-copy-struct-field-value-result", "", """
            Holder h = new Holder(Ok(new P(4)));
            switch (h.R)
            {
                case Ok<P>(var p): h.R = Err(9); WriteLine(p.V); break;
                default: WriteLine(0); break;
            }
            WriteLine(h.R is Err<int>(9));
            public struct P(int v) { public int V = v; }
            public struct Holder(Result<P, int> r) { public Result<P, int> R = r; }
            """];
        yield return ["f13-review-copy-indexed-option-expr", "", """
            Span<Option<int>> s = [Some(4)];
            WriteLine(s[0] switch { Some<int>(var n) => Replace(ref s[0]) + n, _ => 0 });
            WriteLine(s[0].IsEmpty);
            static int Replace(ref Option<int> q) { q = None; return 1; }
            """];
        yield return ["f13-review-shared-payload-fieldwrite-stmt", "VIB004", """
            Option<Nd> o = Some(new Nd(4));
            switch (o)
            {
                case Some<Nd>(var d) when (d.V = 14) < 0: WriteLine(1); break;
                case Some<Nd>(var d) when d.V == 14: WriteLine(2); break;
                default: WriteLine(3); break;
            }
            public sealed class Nd(int v) { public int V = v; }
            """];
        yield return ["f13-review-shared-payload-fieldwrite-expr", "VIB004", """
            Option<Nd> o = Some(new Nd(4));
            WriteLine(o switch { Some<Nd>(var d) when (d.V = 14) < 0 => 1, Some<Nd>(var d) when d.V == 14 => 2, _ => 3 });
            public sealed class Nd(int v) { public int V = v; }
            """];
        yield return ["f13-review-shared-payload-vec-elemwrite-stmt", "VIB004", """
            Vec<int> init = [4];
            Option<Vec<int>> o = Some(init);
            switch (o)
            {
                case Some<Vec<int>>(var v) when (v[0] = 14) < 0: WriteLine(1); break;
                case Some<Vec<int>>(var v) when v[0] == 14: WriteLine(2); break;
                default: WriteLine(3); break;
            }
            """];
        yield return ["f13-review-shared-payload-vec-elemwrite-expr", "VIB004", """
            Vec<int> init = [4];
            Option<Vec<int>> o = Some(init);
            WriteLine(o switch { Some<Vec<int>>(var v) when (v[0] = 14) < 0 => 1, Some<Vec<int>>(var v) when v[0] == 14 => 2, _ => 3 });
            """];
        yield return ["f13-review-opus-f07-refvec-elem-repoint-ref-local-alias", "VIB003", """
            Vec<int> v1 = [4];
            Vec<int> v2 = [7];
            ref Vec<int> x = ref v1;
            ref int e = ref x[0];
            WriteLine(e switch { 4 when (x = ref v2).Count == 0 => 1, 7 => 2, _ => 3 });
            """];
    }

    // The fixed probe matrix checks the CLI and native parity as well; older self verdicts
    // differ for some of the logical-pattern and alias-chain cases.
    [Theory]
    [MemberData(nameof(ReviewWitnesses))]
    public Task ReviewCases_HaveExactRoslynVerdict(string shape, string ids, string source) =>
        AssertRoslynIdsAsync(shape, source, ids);

    [Fact]
    public Task C24LinearSwitchCaseClauseIsRejectedBeforeRecovery() =>
        AssertRoslynIdsAsync("c24-linear-switch-before-f13-recovery", """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => Open(p) switch { Some<R> { Value: var v } when p > 5 => Eat(v) + 100, NoneType => 7, _ => 9 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB000,VIB006");

    private static async Task AssertRoslynIdsAsync(string shape, string source, string ids)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vibranium-switch-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "Program.cs");
            File.WriteAllText(path, source);
            var compilation = VibraniumCompilerDriver.CreateCompilationFromPath(path, "SwitchGuardBorrow", includeStd: true);
            var csharp = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning);
            Assert.Empty(csharp);
            var stage0 = VibraniumFrontend.CompileAndCheck(compilation).Diagnostics;
            var analyzer = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new VibraniumDiagnosticAnalyzer())).GetAnalyzerDiagnosticsAsync();
            string[] expected = ids.Length == 0 ? [] : ids.Split(',');
            Assert.True(stage0.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal).SequenceEqual(expected),
                $"{shape}: Stage-0 reported {string.Join(", ", stage0)}");
            Assert.True(analyzer.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal).SequenceEqual(expected),
                $"{shape}: Analyzer reported {string.Join(", ", analyzer)}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public static IEnumerable<object[]> AcceptCases()
    {
        yield return ["f13-optmut-guard-nobind", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            WriteLine(m switch { Some<Box> when k > 0 => 1, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """, "1\n"];
        yield return ["f13-optmut-noguard-expr", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            WriteLine(m switch { Some<Box>(var b) => b.V, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """, "1\n"];
        yield return ["f13-optref-guard-bind", """
            Option<Box> o = Some(new Box(1));
            var r = o.AsRef();
            int k = 1;
            WriteLine(r switch { Some<Box>(var b) when k > 0 => b.V, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """, "1\n"];
        yield return ["f13-plain-place-guard-bind", """
            Option<Box> o = Some(new Box(1));
            int k = 1;
            WriteLine(o switch { Some<Box>(var b) when k > 0 => b.V, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """, "1\n"];
        yield return ["f13-optmut-is-bind", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            if (m is Some<Box>(var b) && k > 0) WriteLine(b.V);
            public sealed class Box(int v) { public int V = v; }
            """, "1\n"];
        yield return ["f13-optmut-guard-nobind-stmt", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            switch (m)
            {
                case Some<Box> when k > 0: WriteLine(1); break;
                default: WriteLine(0); break;
            }
            public sealed class Box(int v) { public int V = v; }
            """, "1\n"];
        yield return ["f13-refcall-good-expr", """
            Vec<int> w = [4];
            int k = 1;
            WriteLine(At(ref w) switch { 4 when k > 0 => 4, _ => 0 });
            w[0] = 8;
            WriteLine(w[0]);
            static ref int At(ref Vec<int> v) => ref v[0];
            """, "4\n8\n"];
        yield return ["f13-refindex-good-expr", """
            Vec<int> w = [4];
            int k = 1;
            WriteLine(w[0] switch { 4 when k > 0 => 4, _ => 0 });
            w[0] = 8;
            WriteLine(w[0]);
            """, "4\n8\n"];
        yield return ["f13-refproperty-good-expr", """
            Holder h = new Holder(4);
            int k = 1;
            WriteLine(h.Slot switch { 4 when k > 0 => 4, _ => 0 });
            h.Number = 8;
            WriteLine(h.Number);
            sealed class Holder(int n) { public int Number = n; public ref readonly int Slot => ref Number; }
            """, "4\n8\n"];
        yield return ["f13-parenthesized-good-expr", """
            int a = 4;
            int k = 1;
            WriteLine(((a)) switch { 4 when k > 0 => 4, _ => 0 });
            a = 8;
            WriteLine(a);
            """, "4\n8\n"];
        yield return ["f13-refcall-good-stmt", """
            Vec<int> w = [4];
            int k = 1;
            switch (At(ref w))
            {
                case 4 when k > 0: WriteLine(4); break;
                default: WriteLine(0); break;
            }
            w[0] = 8;
            WriteLine(w[0]);
            static ref int At(ref Vec<int> v) => ref v[0];
            """, "4\n8\n"];
        yield return ["f13-refindex-good-stmt", """
            Vec<int> w = [4];
            int k = 1;
            switch (w[0])
            {
                case 4 when k > 0: WriteLine(4); break;
                default: WriteLine(0); break;
            }
            w[0] = 8;
            WriteLine(w[0]);
            """, "4\n8\n"];
        yield return ["f13-refproperty-good-stmt", """
            Holder h = new Holder(4);
            int k = 1;
            switch (h.Slot)
            {
                case 4 when k > 0: WriteLine(4); break;
                default: WriteLine(0); break;
            }
            h.Number = 8;
            WriteLine(h.Number);
            sealed class Holder(int n) { public int Number = n; public ref readonly int Slot => ref Number; }
            """, "4\n8\n"];
        yield return ["f13-parenthesized-good-stmt", """
            int a = 4;
            int k = 1;
            switch (((a)))
            {
                case 4 when k > 0: WriteLine(4); break;
                default: WriteLine(0); break;
            }
            a = 8;
            WriteLine(a);
            """, "4\n8\n"];
        yield return ["f13-refassign-good-expr", """
            int a = 4;
            int b = 7;
            ref int x = ref b;
            WriteLine(((x = ref a)) switch { 4 when x == 4 => 1, _ => 2 });
            x = 11;
            WriteLine(x);
            """, "1\n11\n"];
        yield return ["f13-refassign-good-stmt", """
            int a = 4;
            int b = 7;
            ref int x = ref b;
            switch (((x = ref a)))
            {
                case 4 when x == 4: WriteLine(1); break;
                default: WriteLine(2); break;
            }
            x = 11;
            WriteLine(x);
            """, "1\n11\n"];
        yield return new object[] { "17-control-guard-read", """
            Option<int> o = Some(3);
            WriteLine(o switch { Some<int>(var n) when o.IsSome => n, _ => 0 });
            """, "3\n" };
        yield return new object[] { "18-control-mutate-other", """
            Option<int> o = Some(3);
            Option<int> other = Some(4);
            WriteLine(o switch { Some<int>(var n) when Change(ref other) => n, _ => 0 });
            WriteLine(other.IsEmpty);
            static bool Change(ref Option<int> r) { r = None; return true; }
            """, "3\nTrue\n" };
        yield return new object[] { "19-control-mutate-arm-statement", """
            Option<int> o = Some(3);
            switch (o)
            {
                case Some<int> when o.IsSome: o = None; WriteLine(3); break;
                default: break;
            }
            WriteLine(o.IsEmpty);
            """, "3\nTrue\n" };
        yield return new object[] { "20-control-mutate-arm-expression", """
            Option<int> o = Some(3);
            WriteLine(o switch { Some<int> when o.IsSome => Change(ref o) + 3, _ => 0 });
            WriteLine(o.IsEmpty);
            static int Change(ref Option<int> r) { r = None; return 1; }
            """, "4\nTrue\n" };
        yield return new object[] { "21-control-rvalue-call", """
            Option<int> o = Some(3);
            WriteLine(Make(o) switch { Some<int>(var n) when Change(ref o) => n, _ => 0 });
            static Option<int> Make(Option<int> copy) => copy;
            static bool Change(ref Option<int> r) { r = None; return true; }
            """, "3\n" };
        yield return new object[] { "22-control-rvalue-move", """
            Option<int> o = Some(3);
            WriteLine(Move(o) switch { Some<int>(var n) when Change(ref o) => n, _ => 0 });
            static bool Change(ref Option<int> r) { r = None; return true; }
            """, "3\n" };
        yield return new object[] { "25-control-disjoint-field", """
            S s = new S();
            s.O = Some(3);
            s.P = Some(4);
            WriteLine(s.O switch { Some<int>(var n) when Change(ref s.P) => n, _ => 0 });
            WriteLine(s.P.IsEmpty);
            static bool Change(ref Option<int> r) { r = None; return true; }
            struct S { public Option<int> O; public Option<int> P; }
            """, "3\nTrue\n" };
        // Additional controls from the F13 replacement.
        yield return new object[] { "control-readonly-wrapper-rvalue", """
            WriteLine(F(4));
            static int F(int n) => Open(n) switch { Some<Vec<int>> s when s.Value.Count > 0 => 1, _ => 2 };
            static Option<Vec<int>> Open(int n) { Vec<int> values = [n]; return Some(values); }
            """, "1\n" };
        yield return new object[] { "control-readonly-noncopy-place-payload", """
            Vec<int> values = [4];
            Option<Vec<int>> o = Some(values);
            WriteLine(o switch { Some<Vec<int>>(var v) when v.Count > 0 && o.IsSome => v[0], _ => 0 });
            """, "4\n" };
        yield return new object[] { "plain-bind-stmt", """
            Box x=new Box(3);
            switch (x) { case Box b: WriteLine(b.N); break; }
            sealed class Box(int n) { public int N=n; }
            """, "3\n" };
        yield return new object[] { "m-class-typepattern-move", """
            WriteLine(F(new Box(3)));
            static int F(Box x) { switch (x) { case Box b: return b.Value; default: return 0; } }
            public sealed class Box(int value) { public int Value = value; }
            """, "3\n" };
        yield return new object[] { "m-var-pattern-vec-stmt", """
            Vec<int> v = [1, 2];
            Vec<int> w = v;
            switch (w) { case var u: WriteLine(u.Count); break; }
            """, "2\n" };
        yield return new object[] { "m-var-pattern-linear-stmt", """
            WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
            static int Us(int p) { int n = 0; Option<R> o = MkL(p); switch (o) { case var u: n += Take(u); break; } return n; }
            static Option<R> MkL(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Take(Option<R> u) { int n = 0; switch (Move(u)) { case Some<R>(var v): n += Eat(v); break; case NoneType: n += 7; break; } return n; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
            """, "4\n8\n847\n" };
        yield return new object[] { "control-moving-binding-read-guard", """
            WriteLine(F(new Box(4)));
            static int F(Box b)
            {
                switch (b)
                {
                    case Box x when x.N > 0: return x.N;
                    default: return 0;
                }
            }
            sealed class Box(int n) { public int N = n; }
            """, "4\n" };
        yield return new object[] { "p-span-index-var", """
            Span<int> s = [4, 7];
            int i = 0;
            WriteLine(s[i] switch { 4 when (i = 1) < 0 => 1, 7 => 2, _ => 3 });
            WriteLine(i);
            """, "3\n1\n" };
        yield return new object[] { "control-span-index-ref-argument", """
            Span<int> s = [4, 7];
            int i = 0;
            switch (s[i])
            {
                case 4 when Bump(ref i): WriteLine(1); break;
                case 4: WriteLine(2); break;
                default: WriteLine(3); break;
            }
            WriteLine(i);
            static bool Bump(ref int index) { index = 1; return false; }
            """, "2\n1\n" };
        yield return new object[] { "p-refreassign-arm-body-then-loop", """
            int a = 4;
            int b = 7;
            ref int r = ref a;
            int n = 0;
            while (n < 2)
            {
                switch (r) { case 4 when n > 5: WriteLine(0); break; case 4: r = ref b; WriteLine(4); break; default: WriteLine(r); break; }
                n++;
            }
            """, "4\n7\n" };
        yield return new object[] { "gd01-vec-elem-copied-local", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Vec<int> w = [p];
                int e = w[0];
                switch (e) { case 4 when Grow(ref w): n = 1; break; case 4: n = 20 + w.Count; break; default: n = 30 + w.Count; break; }
                return n;
            }
            static bool Grow(ref Vec<int> w) { w.Add(5); return false; }
            """, "31022031\n" };
        yield return new object[] { "gd03-vec-elem-is-pattern", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Vec<int> w = [p];
                if (w[0] is 4 && Grow(ref w)) n = 1; else n = 30 + w.Count;
                return n;
            }
            static bool Grow(ref Vec<int> w) { w.Add(5); return false; }
            """, "31032031\n" };
        yield return new object[] { "gd05-vec-count-switch", """
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Vec<int> w = [p];
                switch (w.Count) { case 1 when Grow(ref w): n = 1; break; default: n = 30 + w.Count; break; }
                return n;
            }
            static bool Grow(ref Vec<int> w) { w.Add(5); return false; }
            """, "32032032\n" };
    }

    public static IEnumerable<object[]> RoslynOnlyAcceptedSelectors()
    {
        yield return ["f13-refassign-owned-ref-good-expr", """
            Option<Box> a = Some(new Box(4));
            Option<Box> b = None;
            ref Option<Box> x = ref b;
            WriteLine((x = ref a) switch { Some<Box>(var value) when value.V > 0 => value.V, _ => 0 });
            a = None;
            WriteLine(a.IsEmpty);
            public sealed class Box(int v) { public int V = v; }
            """];
        yield return ["f13-refassign-owned-ref-good-stmt", """
            Option<Box> a = Some(new Box(4));
            Option<Box> b = None;
            ref Option<Box> x = ref b;
            switch (x = ref a)
            {
                case Some<Box>(var value) when value.V > 0: WriteLine(value.V); break;
                default: WriteLine(0); break;
            }
            a = None;
            WriteLine(a.IsEmpty);
            public sealed class Box(int v) { public int V = v; }
            """];
    }

    // Older self frontends still try to move the non-Copy ref selector (VIB005).
    // The named probe files also verify Stage-0 native parity at -O0 and -O3.
    [Theory]
    [MemberData(nameof(RoslynOnlyAcceptedSelectors))]
    public Task RefAssignmentSelectorKeepsNonCopyPlace(string shape, string source) =>
        AssertRoslynIdsAsync(shape, source, string.Empty);

    [Theory]
    [MemberData(nameof(AcceptCases))]
    public async Task PlaceGuardControls_Accept(string shape, string source, string expected)
    {
        var result = await Round12RuleWitness.AcceptAsync(source, expected);
        Assert.True(result.Managed.Exit == 0, $"{shape}: managed exit {result.Managed.Exit}");
    }

    [Fact]
    public Task GuardMutationThroughSharedBinding_KeepsOnlyVib004() =>
        Round12RuleWitness.RejectAsync("""
            WriteLine(G(1) + G(4) * 1000 + G(8) * 1000000);
            static int G(int p)
            {
                int n = 0;
                Option<Nd> o = p > 2 ? Some(new Nd(p)) : None;
                switch (o) { case Some<Nd>(var d) when d.Bump(): n = 1; break; case Some<Nd>(var d) when d.V == 14: n = 2; break; default: n = 3; break; }
                return n;
            }
            public sealed class Nd(int v) { public int V = v; [Mut] public bool Bump() { V += 10; return false; } }
            """, "VIB004", exact: true);

    public static IEnumerable<object[]> AdditionalExactCases()
    {
        yield return ["f13-optmut-guard-unused-expr", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            WriteLine(m switch { Some<Box>(var b) when k > 0 => 1, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["f13-optmut-guard-unused-stmt", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            switch (m) { case Some<Box>(var b) when k > 0: WriteLine(1); break; default: WriteLine(0); break; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["f13-optmut-bind-guard-mutates-through-receiver", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            WriteLine(m switch { Some<Box>(var b) when b.Bump() => b.V, Some<Box> => 7, _ => 0 });
            public sealed class Box(int v)
            {
                public int V = v;
                [Mut] public bool Bump() { V = 9; return false; }
            }
            """, "VIB003"];
        yield return ["f13-optmut-guard-true", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            int k = 1;
            WriteLine(m switch { Some<Box>(var b) when k > 0 => b.V, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["f13-optmut-guard-expr", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            WriteLine(m switch { Some<Box>(var b) when b.V > 0 => b.V, _ => 0 });
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["f13-optmut-guard-stmt", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            switch (m) { case Some<Box>(var b) when b.V > 0: b.V = 5; WriteLine(b.V); break; default: WriteLine(0); break; }
            WriteLine(o.Unwrap().V);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["f13-optmut-bind-guard-mutates-payload", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            WriteLine(m switch { Some<Box>(var b) when Bump(b) => b.V, Some<Box> => 7, _ => 0 });
            static bool Bump(Box b) { b.V = 9; return false; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003,VIB005"];
        yield return ["f13-refassign-projected-field-expr", """
            S a = new S(4);
            S b = new S(7);
            ref S x = ref b;
            WriteLine((x = ref a).Field switch { 4 when (x = ref b).Field == 0 => 1, _ => 2 });
            public struct S(int n) { public int Field = n; }
            """, "VIB003"];
        yield return ["f13-refassign-projected-field-stmt", """
            S a = new S(4);
            S b = new S(7);
            ref S x = ref b;
            switch ((x = ref a).Field)
            {
                case 4 when (x = ref b).Field == 0: WriteLine(1); break;
                default: WriteLine(2); break;
            }
            public struct S(int n) { public int Field = n; }
            """, "VIB003"];
        yield return ["adv-f13-refassign-scrutinee-int", """
            int a = 4;
            int c = 0;
            ref int x = ref c;
            WriteLine((x = ref a) switch { 4 when (a = 9) == 0 => 1, 9 => 2, _ => 3 });
            """, "VIB003"];
        yield return ["adv-f13-refassign-scrutinee-opt", """
            Option<int> o = Some(3);
            Option<int> d = None;
            ref Option<int> x = ref d;
            WriteLine((x = ref o) switch { Some<int> when Clear(ref o) => 1, not NoneType => 2, _ => 3 });
            static bool Clear(ref Option<int> r) { r = None; return false; }
            """, "VIB003"];
        yield return ["adv-f13-refassign-scrutinee-rebind", """
            int a = 4;
            int b = 7;
            int c = 0;
            ref int x = ref c;
            WriteLine((x = ref a) switch { 4 when (x = ref b) == 0 => 1, 7 => 2, _ => 3 });
            """, "VIB003"];
        yield return ["adv-f13-typeddecl-vs-var-guard", """
            Box b = new Box(3);
            WriteLine(b switch
            {
                Box x when Reinit(out b) => x.V,
                var y => y.V
            });
            static bool Reinit(out Box slot) { slot = new Box(42); return false; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["adv-f13-var-assign-box-expr", """
            Box b = new Box(3);
            WriteLine(b switch
            {
                var x when (b = new Box(42)).V == 0 => x.V,
                var y => y.V
            });
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["adv-f13-var-assign-discard-arm", """
            Option<Vec<int>> o = Some(Make(3));
            WriteLine(o switch
            {
                var x when Reinit(out o) => x.IsSome ? 1 : 0,
                _ => 99
            });
            static Vec<int> Make(int a) { Vec<int> v = [a]; return v; }
            static bool Reinit(out Option<Vec<int>> slot) { slot = None; return false; }
            """, "VIB003"];
        yield return ["adv-f13-var-reinit-box-expr", """
            Box b = new Box(3);
            WriteLine(b switch
            {
                var x when Reinit(out b) => x.V,
                var y => y.V
            });
            static bool Reinit(out Box slot) { slot = new Box(42); return false; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["adv-f13-var-reinit-box-stmt", """
            Box b = new Box(3);
            switch (b)
            {
                case var x when Reinit(out b): WriteLine(x.V); break;
                case var y: WriteLine(y.V); break;
            }
            static bool Reinit(out Box slot) { slot = new Box(42); return false; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["adv-f13-var-reinit-optvec-expr", """
            Option<Vec<int>> o = Some(Make(3));
            WriteLine(o switch
            {
                var x when Reinit(out o) => x.IsSome ? 1 : 0,
                Some<Vec<int>>(var v) => v[0],
                _ => 99
            });
            static Vec<int> Make(int a) { Vec<int> v = [a]; return v; }
            static bool Reinit(out Option<Vec<int>> slot) { Vec<int> v = [42]; slot = Some(v); return false; }
            """, "VIB003"];
        yield return ["adv-f13-var-reinit-optvec-stmt", """
            Option<Vec<int>> o = Some(Make(3));
            switch (o)
            {
                case var x when Reinit(out o): WriteLine(x.IsSome ? 1 : 0); break;
                case Some<Vec<int>>(var v): WriteLine(v[0]); break;
                default: WriteLine(99); break;
            }
            static Vec<int> Make(int a) { Vec<int> v = [a]; return v; }
            static bool Reinit(out Option<Vec<int>> slot) { Vec<int> v = [42]; slot = Some(v); return false; }
            """, "VIB003"];
        yield return ["f13-refassign-access-expr", """
            int a = 4;
            int b = 7;
            ref int x = ref b;
            WriteLine(((x = ref a)) switch { 4 when (x = ref b) == 0 => 1, _ => 2 });
            """, "VIB003"];
        yield return ["f13-parenthesized-place-expr", """
            int a = 4;
            int b = 7;
            ref int x = ref b;
            WriteLine(((a)) switch { 4 when (a = 9) == 0 => 1, _ => 2 });
            WriteLine(x);
            """, "VIB003"];
        yield return ["f13-refcall-place-expr", """
            Vec<int> w = [4];
            WriteLine(At(ref w) switch { 4 when Grow(ref w) => 1, _ => 2 });
            static ref int At(ref Vec<int> v) => ref v[0];
            static bool Grow(ref Vec<int> v) { v.Add(5); return false; }
            """, "VIB003"];
        yield return ["f13-refindex-place-expr", """
            Vec<int> w = [4];
            WriteLine(w[0] switch { 4 when Grow(ref w) => 1, _ => 2 });
            static bool Grow(ref Vec<int> v) { v.Add(5); return false; }
            """, "VIB003"];
        yield return ["f13-refproperty-place-expr", """
            var h = new Holder(4);
            WriteLine(h.Slot switch { 4 when h.Change() => 1, _ => 2 });
            sealed class Holder(int n) {
                public int Number = n;
                public ref readonly int Slot => ref Number;
                [Mut] public bool Change() { Number = 6; return false; }
            }
            """, "VIB003"];
        yield return ["f13-c15-bare-ref-selector-expr", """
            int a = 4;
            int b = 7;
            bool c = a < b;
            WriteLine((c ? ref a : ref b) switch { 4 when (a = 9) == 0 => 1, _ => 2 });
            """, "VIB000"];
        yield return ["f13-refassign-access-stmt", """
            int a = 4;
            int b = 7;
            ref int x = ref b;
            switch (((x = ref a)))
            {
                case 4 when (x = ref b) == 0: WriteLine(1); break;
                default: WriteLine(2); break;
            }
            """, "VIB003"];
        yield return ["f13-parenthesized-place-stmt", """
            int a = 4;
            int b = 7;
            ref int x = ref b;
            switch (((a)))
            {
                case 4 when (a = 9) == 0: WriteLine(1); break;
                default: WriteLine(2); break;
            }
            WriteLine(x);
            """, "VIB003"];
        yield return ["f13-refcall-place-stmt", """
            Vec<int> w = [4];
            switch (At(ref w))
            {
                case 4 when Grow(ref w): WriteLine(1); break;
                default: WriteLine(2); break;
            }
            static ref int At(ref Vec<int> v) => ref v[0];
            static bool Grow(ref Vec<int> v) { v.Add(5); return false; }
            """, "VIB003"];
        yield return ["f13-refindex-place-stmt", """
            Vec<int> w = [4];
            switch (w[0])
            {
                case 4 when Grow(ref w): WriteLine(1); break;
                default: WriteLine(2); break;
            }
            static bool Grow(ref Vec<int> v) { v.Add(5); return false; }
            """, "VIB003"];
        yield return ["f13-refproperty-place-stmt", """
            var h = new Holder(4);
            switch (h.Slot)
            {
                case 4 when h.Change(): WriteLine(1); break;
                default: WriteLine(2); break;
            }
            sealed class Holder(int n) {
                public int Number = n;
                public ref readonly int Slot => ref Number;
                [Mut] public bool Change() { Number = 6; return false; }
            }
            """, "VIB003"];
        yield return ["f13-c15-bare-ref-selector-stmt", """
            int a = 4;
            int b = 7;
            bool c = a < b;
            switch ((c ? ref a : ref b))
            {
                case 4 when (a = 9) == 0: WriteLine(1); break;
                default: WriteLine(2); break;
            }
            """, "VIB000"];
        yield return new object[] { "extra-ref-rebind", """
            int a=4; int b=5; ref int x=ref a;
            WriteLine(x switch { 4 when (x=ref b)==0 => 1, 4=>2, _=>3 });
            """, "VIB003" };
        yield return new object[] { "p-refreassign-vecpayload", """
            Vec<int> va = [1];
            Vec<int> vb = [1, 2, 3];
            Option<Vec<int>> a = Some(va);
            Option<Vec<int>> b = Some(vb);
            ref Option<Vec<int>> r = ref a;
            WriteLine(r switch { Some<Vec<int>>(var v) when (r = ref b).IsEmpty => v.Count, Some<Vec<int>>(var w) => w.Count + 10, _ => 0 });
            """, "VIB003" };
        yield return new object[] { "p-refreassign-field-expr", """
            S a = new S(4);
            S b = new S(7);
            ref S r = ref a;
            WriteLine(r.X switch { 4 when (r = ref b).X == 0 => 1, 7 => 2, _ => 3 });
            public struct S(int x) { public int X = x; }
            """, "VIB003" };
        yield return new object[] { "p-refreassign-opt-stmt", """
            Option<int> a = Some(3);
            Option<int> b = None;
            ref Option<int> r = ref a;
            switch (r)
            {
                case Some<int> when (r = ref b).IsSome: WriteLine(1); break;
                case NoneType: WriteLine(2); break;
                default: WriteLine(3); break;
            }
            """, "VIB003" };
        yield return new object[] { "ref-rebind-by-ref-return", """
            int a = 4;
            int b = 7;
            ref int x = ref a;
            WriteLine(x switch { 4 when (x = ref At(ref b)) == 0 => 1, 4 => 2, _ => 3 });
            static ref int At(ref int n) => ref n;
            """, "VIB003" };
        yield return new object[] { "ref-rebind-intrinsic", """
            int a = 4;
            Span<int> s = [7];
            ref int x = ref a;
            WriteLine(x switch { 4 when (x = ref s[0]) == 0 => 1, 4 => 2, _ => 3 });
            """, "VIB003" };
        yield return new object[] { "r-ref-returning-method-selector", """
            Vec<int> w = [4];
            WriteLine(At(ref w) switch { 4 when Grow(ref w) => 1, _ => 2 });
            static ref int At(ref Vec<int> v) => ref v[0];
            static bool Grow(ref Vec<int> v) { v.Add(5); return false; }
            """, "VIB003" };
        yield return new object[] { "r-unwrapmut-selector", """
            Option<int> o = Some(3);
            WriteLine(o.UnwrapMut() switch { 3 when Clear(ref o) => 1, _ => 2 });
            static bool Clear(ref Option<int> r) { r = None; return false; }
            """, "VIB003" };
        yield return new object[] { "guard-ref-property", """
            Holder h = new Holder(4);
            WriteLine(h.Slot switch { 4 when h.Change() => 1, _ => 2 });
            sealed class Holder(int n)
            {
                public int Number = n;
                public ref readonly int Slot => ref Number;
                [Mut] public bool Change() { Number = 6; return false; }
            }
            """, "VIB003" };
        yield return new object[] { "guard-other-span-index-assignment", """
            Span<int> s = [4, 7];
            WriteLine(s[0] switch { 4 when (s[1] = 9) < 0 => 1, 4 => 2, _ => 3 });
            """, "VIB003" };
        yield return new object[] { "26-control-span-disjoint", """
            Span<Option<int>> s = [Some(3), Some(4)];
            WriteLine(s[0] switch { Some<int>(var n) when Change(ref s[1]) => n, _ => 0 });
            static bool Change(ref Option<int> r) { r = None; return true; }
            """, "VIB003" };
        yield return new object[] { "p-span-reassign-in-guard", """
            Span<int> s = [4];
            Span<int> t = [7];
            WriteLine(s[0] switch { 4 when (s = t).Length == 0 => 1, 7 => 2, _ => 3 });
            """, "VIB003" };
        yield return new object[] { "guard-readonlyspan-reassignment", """
            ReadOnlySpan<int> s = [4];
            ReadOnlySpan<int> t = [7];
            WriteLine(s[0] switch { 4 when (s = t).Length == 0 => 1, 7 => 2, _ => 3 });
            """, "VIB003" };
        yield return new object[] { "guard-struct-mut-member", """
            WriteLine(F(4));
            static int F(int n)
            {
                Gate g = new Gate(n);
                return g.Value switch { 4 when g.Change() => 1, _ => 2 };
            }
            struct Gate(int n)
            {
                public int Value = n;
                public bool Change() { Value = 7; return false; }
            }
            """, "VIB003" };
        yield return new object[] { "guard-nested-pattern-moves-outer", """
            WriteLine(F(new Box(4)));
            static int F(Box b) => b.N switch { 4 when (b switch { Box x => x.N < 0, _ => false }) => 1, _ => 2 };
            sealed class Box(int n) { public int N = n; }
            """, "VIB003" };
        yield return new object[] { "m-typedesig-some-guard", """
            WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
            static int Us(int p) { int n = 0; Option<Vec<int>> o = MkV(p); switch (o) { case Some<Vec<int>> s when p > 5: n += 100; break; case Some<Vec<int>>(var v): n += v.Count + v[0]; break; default: n += 7; break; } n += o.IsSome ? 1000 : 0; return n; }
            static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
            """, "VIB005" };
        yield return new object[] { "wrapper-minimal", """
            Option<Vec<int>> o = None;
            WriteLine(o switch { Some<Vec<int>> s when o.IsSome => 1, _ => 2 });
            """, "VIB005" };
        yield return new object[] { "wrapper-guard-independent", """
            Vec<int> v = [1];
            Option<Vec<int>> o = Some(v);
            WriteLine(o switch { Some<Vec<int>> s when o.IsSome => s.Value.Count, _ => 2 });
            """, "VIB005" };
        yield return new object[] { "m-class-typepattern-expr", """
            Nd d = new Nd(5);
            WriteLine(d.V switch { 5 => 1, _ => 2 });
            WriteLine(Pick(d));
            static int Pick(Nd x) { switch (x) { case Nd y when y.V > 9: return 1; case Nd z: return z.V; default: return 0; } }
            public sealed class Nd(int v) { public int V = v; }
            """, "VIB001" };
        yield return new object[] { "extra-X01-nested-then-plain-repeat", """
            WriteLine(F(4));
            static int F(int p) => Open(p) switch { Some<R>(R { Id: 4 } v) => Eat(v) + 100, Some<R>(var v) => Eat(v), NoneType => 7 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006" };
        yield return new object[] { "extra-X02-nested-guard-only", """
            WriteLine(F(4));
            static int F(int p) => Open(p) switch { Some<R>(R { Id: 4 } v) when p > 5 => Eat(v) + 100, NoneType => 7, _ => 9 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006" };
        yield return new object[] { "X01-nested-then-plain-repeat", """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => Open(p) switch { Some<R>(R { Id: 4 } v) => Eat(v) + 100, Some<R>(var v) => Eat(v), NoneType => 7 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006" };
        yield return new object[] { "X02-nested-guard-only", """
            WriteLine(F(1) + F(4) * 10 + F(8) * 100);
            static int F(int p) => Open(p) switch { Some<R>(R { Id: 4 } v) when p > 5 => Eat(v) + 100, NoneType => 7, _ => 9 };
            static Option<R> Open(int a) { if (a > 2) return Some(new R(a)); return None; }
            static int Eat(R r) { using var u = r; return u.Id; }
            public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB006" };
        yield return new object[] { "16-linear-place", """
            Option<R> o = Some(new R());
            switch (o)
            {
                case Some<R> when Change(ref o): break;
                default: break;
            }
            if (!o.IsNone(out var r)) { using var res = r; WriteLine(res.Id); }
            static bool Change(ref Option<R> r) { if (!r.IsNone(out var v)) { using var m = v; WriteLine(m.Id); } r = None; return false; }
            sealed class R : IDisposable { public int Id = 1; public void Dispose() {} }
            """, "VIB003,VIB005,VIB006" };
    }

    [Theory]
    [MemberData(nameof(AdditionalExactCases))]
    public Task AccessPathAndPatternRecovery_ReportsExactIds(string shape, string source, string ids) =>
        AssertRoslynIdsAsync(shape, source, ids);

    public static IEnumerable<object[]> BannedLinearSelectors()
    {
        yield return new object[] { "30-linear-selector-direct", """
            WriteLine(1);
            sealed class R : IDisposable { public void Dispose() {} }
            static class C {
                static Option<R> Open() => None;
                public static int F(int x) => Open() switch {
                    NoneType when x > 0 => 1, NoneType => 2, Some<R>(var r) => Eat(r) };
                static int Eat(R r) { using var u = r; return 3; }
            }
            """ };
        yield return new object[] { "31-linear-selector-parens", """
            WriteLine(1);
            sealed class R : IDisposable { public void Dispose() {} }
            static class C {
                static Option<R> Open() => None;
                public static int F(int x) => ((Open())) switch {
                    NoneType when x > 0 => 1, NoneType => 2, Some<R>(var r) => Eat(r) };
                static int Eat(R r) { using var u = r; return 3; }
            }
            """ };
        yield return new object[] { "32-linear-selector-conditional", """
            WriteLine(1);
            sealed class R : IDisposable { public void Dispose() {} }
            static class C {
                static Option<R> Open() => None;
                public static int F(int x) => (x > 0 ? Open() : Open()) switch {
                    NoneType when x > 0 => 1, NoneType => 2, Some<R>(var r) => Eat(r) };
                static int Eat(R r) { using var u = r; return 3; }
            }
            """ };
        yield return new object[] { "33-linear-selector-nested", """
            WriteLine(4);
            sealed class R : IDisposable { public void Dispose() {} }
            static class C {
                static Option<R> Open() => None;
                public static int F(int x) => Open() switch {
                    Some<R>(var a) => Eat(a), _ => ((Open())) switch {
                        NoneType when x > 0 => 1, NoneType => 2, Some<R>(var b) => Eat(b) } };
                static int Eat(R r) { using var u = r; return 3; }
            }
            """ };
        yield return new object[] { "34-linear-selector-cast", """
            WriteLine(1);
            sealed class R : IDisposable { public void Dispose() {} }
            static class C {
                static Option<R> Open() => None;
                public static int F(int x) => ((Option<R>)Open()) switch {
                    NoneType when x > 0 => 1, NoneType => 2, Some<R>(var r) => Eat(r) };
                static int Eat(R r) { using var u = r; return 3; }
            }
            """ };
        yield return new object[] { "35-linear-selector-switch-expression", """
            WriteLine(1);
            sealed class R : IDisposable { public void Dispose() {} }
            static class C {
                static Option<R> Open() => None;
                public static int F(int x) => (x switch { 1 => Open(), _ => Open() }) switch {
                    NoneType when x > 0 => 1, NoneType => 2, Some<R>(var r) => Eat(r) };
                static int Eat(R r) { using var u = r; return 3; }
            }
            """ };
        yield return new object[] { "38-linear-selector-parens-stmt", """
            WriteLine(1);
            sealed class R : IDisposable { public void Dispose() {} }
            static class C
            {
                static Option<R> Open() => None;
                public static int F(int x)
                {
                    switch (((Open())))
                    {
                        case NoneType when x > 0: return 1;
                        case NoneType: return 2;
                        case Some<R>(var r): using (var a = r) { return 3; }
                        default: return 4;
                    }
                }
            }
            """ };
    }

    [Theory]
    [MemberData(nameof(BannedLinearSelectors))]
    public async Task BannedLinearSwitch_DoesNotDiagnoseItsOwnReread(string shape, string source)
    {
        var verdict = await Round12RuleWitness.CheckAllAsync(source, requireNoWarnings: true);
        string[] expected = ["VIB006"];
        Assert.True(verdict.Stage0.SequenceEqual(expected), $"{shape}: Stage-0 [{string.Join(",", verdict.Stage0)}]");
        Assert.True(verdict.Analyzer.SequenceEqual(expected), $"{shape}: Analyzer [{string.Join(",", verdict.Analyzer)}]");
        Assert.True(verdict.Self.SequenceEqual(expected), $"{shape}: vib-self [{string.Join(",", verdict.Self)}]\n{verdict.SelfOutput}");
    }
}
