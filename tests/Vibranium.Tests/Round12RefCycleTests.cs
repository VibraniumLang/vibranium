using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// N105: a ref local re-pointed into a borrow that was made through it (`rx = ref k.Next` with `k` reached through `rx`,
// through a second ref local taken from `rx`, or through a cursor taken from it). The re-pointed ref is then a derived
// borrow of that binding, ref or cursor: any use of it while the ref is live (a write, Take, Replace, Set, a `ref` or
// `out` argument, a [Mut] call, a move, another match on the cursor) is exactly VIB003.
public sealed class Round12RefCycleTests
{
    public static IEnumerable<object[]> UseAfterFreeRows()
    {
        yield return ["a01-write", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["a02-take", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            Option<Node> t = r2.Take();
            t = None;
            WriteLine(rx.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["a04-set", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            r2.AsMut().Set(None);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["a05-out-arg", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            Fill(out r2);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            static void Fill(out Option<Node> o) { o = None; }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["a06-ref-arg", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            Clear(ref r2);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            static void Clear(ref Option<Node> o) { o = None; }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["a07-write-some", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            r2 = Some(new Node(5, None));
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["a08-take-read-payload", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            Option<Node> t = r2.Take();
            t = None;
            WriteLine(rx.AsRef() switch { Some<Node>(var z) => z.V, _ => -1 });
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["a09-replace-drop", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            Option<Node> old = r2.Replace(None);
            old = None;
            WriteLine(rx.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["c12-branch-uaf", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k))
            {
                rx = ref k.Next;
                r2 = None;
                rx = Some(new Node(7, None));
            }
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["d01-3deep-rx-into-r3-write-r3", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            ref Option<Node> r3 = ref r2;
            if (r3.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            r3 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["d02-3deep-rx-into-r3-write-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            ref Option<Node> r3 = ref r2;
            if (r3.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["d03-3deep-r2-into-r3-write-r3", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            ref Option<Node> r3 = ref r2;
            if (r3.AsMut() is Some<Node>(var k)) r2 = ref k.Next;
            r3 = None;
            r2 = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["d05-3deep-r3-made-after-repoint", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            ref Option<Node> r3 = ref r2;
            r3 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["d08-3deep-rx-into-r2-write-r2-r3-dead", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            ref Option<Node> r3 = ref r2;
            r3 = Some(new Node(4, Some(new Node(5, None))));
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e07-cursor-of-r2-then-write-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            var m = r2.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e30-cursor-move", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            var m2 = m;
            m2.Set(None);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e31-cursor-move-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            var m = r2.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            var m2 = m;
            m2.Set(None);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e32-cursor-arg", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            Cut(m);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            static void Cut(OptionMut<Node> c) { c.Set(None); }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e36-cursor-second-match", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
            }
            if (m is Some<Node>(var z)) z.Next = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e38-cursor-second-match-mutcall", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
            }
            switch (m)
            {
                case Some<Node>(var z):
                    z.Cut();
                    break;
                default:
                    break;
            }
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; [Mut] public void Cut() { Next = None; } }
            """];
        yield return ["f01-is-and-guard", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k) && k.V > 0) rx = ref k.Next;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["f02-switch-stmt", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            switch (r2.AsMut())
            {
                case Some<Node>(var k):
                    rx = ref k.Next;
                    break;
                default:
                    break;
            }
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["f03-switch-stmt-when", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            switch (r2.AsMut())
            {
                case Some<Node>(var k) when k.V > 0:
                    rx = ref k.Next;
                    break;
                default:
                    break;
            }
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["f05-switch-expr-arm", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            int n = r2.AsMut() switch { Some<Node>(var k) => (rx = ref k.Next).IsSome ? 1 : 0, _ => 2 };
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(n + (a.IsSome ? 10 : 20));
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["f06-when-guard-repoints", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            switch (r2.AsMut())
            {
                case Some<Node>(var k) when (rx = ref k.Next).IsSome:
                    break;
                default:
                    break;
            }
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["f07-is-not-none-else", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is not Some<Node>(var k)) { WriteLine(0); return; }
            rx = ref k.Next;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["g01-one-ref-two-levels-write", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    k.Next = None;
                    rx = Some(new Node(7, None));
                }
            }
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["g04-struct-field-path", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.L.Next;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Link L = new Link(next); }
            public struct Link(Option<Node> next) { public Option<Node> Next = next; }
            """];
        yield return ["g05-two-levels-through-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
            }
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["g07-one-ref-two-levels-take", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    Option<Node> t = k.Next.Take();
                    t = None;
                    WriteLine(rx.IsSome);
                }
            }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["g08-one-ref-two-levels-mut-call", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    k.Cut();
                    rx = Some(new Node(7, None));
                }
            }
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; [Mut] public void Cut() { Next = None; } }
            """];
        yield return ["g11-one-ref-two-levels-ref-arg", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    Clear(ref k.Next);
                    rx = Some(new Node(7, None));
                }
            }
            WriteLine(a.IsSome);
            static void Clear(ref Option<Node> o) { o = None; }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["h02-helper-id-of-payload-field", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref Id(ref k.Next);
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            static ref Option<Node> Id(ref Option<Node> o) => ref o;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["l06-loop-repoint-then-write-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            for (int i = 0; i < 2; i++)
            {
                if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            }
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["l07-one-ref-loop-cut-behind", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            int steps = 0;
            while (rx.AsMut() is Some<Node>(var k))
            {
                steps++;
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    k.Next = None;
                }
                else break;
            }
            WriteLine(steps);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q02-method-ref-return", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.NextRef();
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; [Mut] public ref Option<Node> NextRef() => ref Next; }
            """];
        yield return ["q03-lifetimeof-two-args", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            Option<Node> spare = None;
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref Pick(ref spare, ref k.Next);
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            [return: LifetimeOf("x", "y")] static ref Option<Node> Pick(ref Option<Node> x, ref Option<Node> y) => ref y;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q04-e36-root-ref-param", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            Run(ref a);
            WriteLine(a.IsSome);
            static void Run(ref Option<Node> head)
            {
                ref Option<Node> rx = ref head;
                var m = rx.AsMut();
                if (m is Some<Node>(var k))
                {
                    if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
                }
                if (m is Some<Node>(var z)) z.Next = None;
                rx = Some(new Node(7, None));
            }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q05-e36-root-class-field", """
            var h = new Holder(Some(new Node(1, Some(new Node(2, Some(new Node(3, None)))))));
            ref Option<Node> rx = ref h.First;
            var m = rx.AsMut();
            if (m is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
            }
            if (m is Some<Node>(var z)) z.Next = None;
            rx = Some(new Node(7, None));
            WriteLine(h.First.IsSome);
            public sealed class Holder(Option<Node> f) { public Option<Node> First = f; }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q06-e36-while-second-match", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
            }
            int i = 0;
            while (i < 1 && m is Some<Node>(var z)) { z.Next = None; i++; }
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q07-e36-cursor-after-step", """
            Option<Node> a = Some(new Node(0, Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k0)) rx = ref k0.Next;
            var m = rx.AsMut();
            if (m is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
            }
            if (m is Some<Node>(var z)) z.Next = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q08-e36-cursor-from-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            var m = r2.AsMut();
            if (m is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
            }
            if (m is Some<Node>(var z)) z.Next = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q09-3deep-cursor-second-match", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            ref Option<Node> r3 = ref r2;
            var m = r3.AsMut();
            if (m is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
            }
            if (m is Some<Node>(var z)) z.Next = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q10-loop-r2-repointed-each-turn", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            Option<Node> spare = None;
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref spare;
            int i = 0;
            while (i < 2)
            {
                r2 = ref rx;
                if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
                i++;
            }
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q12-e36-nested-loop-cursor", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            for (int i = 0; i < 2; i++)
            {
                if (m is Some<Node>(var z))
                {
                    if (i == 1) z.Next = None;
                    else if (z.Next.AsMut() is Some<Node>(var k2)) rx = ref k2.Next;
                }
            }
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q13-struct-field-through-ref", """
            var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
            ref Pair rx = ref p;
            ref Pair r2 = ref rx;
            if (r2.X.AsMut() is Some<Box>(var b)) rx = ref b.P;
            r2.X = None;
            rx.V = 9;
            WriteLine(p.V);
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["q16-snapshot-old-value-read", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            rx = ref Pick(Clear(ref r2), ref rx);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            static int Clear(ref Option<Node> o) { o = None; return 0; }
            static ref Option<Node> Pick(int x, ref Option<Node> y) => ref y;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q17-out-arg-payload-one-ref", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    Fill(out k.Next);
                }
            }
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            static void Fill(out Option<Node> o) { o = None; }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q18-move-cursor-prelude-move", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            var m2 = Move(m);
            m2.Set(None);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q21-one-ref-three-levels-middle-write", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k) && k.Next.AsMut() is Some<Node>(var k2) && k2.Next.AsMut() is Some<Node>(var k3))
            {
                rx = ref k3.Next;
                k.Next = None;
            }
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["q22-one-ref-three-levels-k2-write", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k) && k.Next.AsMut() is Some<Node>(var k2) && k2.Next.AsMut() is Some<Node>(var k3))
            {
                rx = ref k3.Next;
                k2.Next = None;
            }
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["u01-shared-ancestor-repointed-write-r2", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            r2 = None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["u02-shared-ancestor-repointed-read-payload", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            r2 = None;
            WriteLine(ro.AsRef() is Some<Node>(var z) ? z.V : -1);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["u03-shared-ancestor-repointed-replace", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            Option<Node> old = r2.Replace(None);
            old = None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["u04-shared-ancestor-repointed-take", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            Option<Node> t = r2.Take();
            t = None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["u06-shared-ancestor-two-levels-write-r2", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k) && k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
            r2 = Some(new Node(5, None));
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["u07-shared-ancestor-field-write-k-next", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                k.Next = None;
            }
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["u11-shared-ancestor-struct-field-write", """
            var p0 = new Pair(None, 0);
            var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
            ref readonly Pair ro = ref p0;
            ref Pair r2 = ref ChooseP(ref p, in ro);
            if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
            r2.X = None;
            WriteLine(ro.V);
            WriteLine(p.V);
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t01-rhs-repoint-ro", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            r2 = (r2.AsRef() is Some<Node>(var k) && (ro = ref k.Next).IsSome) ? None : None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t02-rhs-repoint-field-store", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsMut() is Some<Node>(var k))
            {
                k.Next = (k.Next.AsRef() is Some<Node>(var k2) && (ro = ref k2.Next).IsSome) ? None : None;
            }
            WriteLine(ro.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t03-cond-rhs-write", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            r2 = flag ? None : None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t04-switch-rhs-write", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            r2 = flag switch { true => None, false => None };
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t05-replace-cond-arg", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            Option<Node> old = r2.Replace(flag ? None : None);
            old = None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t06-class-field-cond-rhs", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                k.Next = flag ? None : None;
            }
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t07-struct-field-cond-rhs", """
            bool flag = true;
            var p0 = new Pair(None, 0);
            var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
            ref readonly Pair ro = ref p0;
            ref Pair r2 = ref ChooseP(ref p, in ro);
            if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
            r2.X = flag ? None : None;
            WriteLine(ro.V);
            WriteLine(p.V);
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t08-vec-push-cond-arg", """
            bool flag = true;
            Vec<int> v = [1, 2, 3];
            int zero = 0;
            ref readonly int ro = ref zero;
            ref Vec<int> r2 = ref ChooseV(ref v, in ro);
            ro = ref r2[0];
            for (int i = 0; i < 64; i++) r2.Push(flag ? i : 0);
            WriteLine(ro);
            WriteLine(v.Count);
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["t09-andand-rhs", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            r2 = flag && b.IsSome ? None : Some(new Node(5, None));
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t10-loop-cond-rhs", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            for (int i = 0; i < 2; i++)
            {
                if (i == 1) r2 = flag ? None : None;
                else if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            }
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t11-out-arg-cond-other", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            Fill(out r2, flag ? 1 : 2);
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            static void Fill(out Option<Node> o, int n) { o = None; }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t12-coalesce-like-or", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            r2 = flag || b.IsSome ? None : None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t13-cond-rhs-two-levels", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k) && k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
            r2 = flag ? Some(new Node(5, None)) : None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t14-cond-rhs-one-ref-mut-binding", """
            bool flag = true;
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            Option<Node> z = None;
            ref readonly Option<Node> ro = ref z;
            if (a.AsMut() is Some<Node>(var k))
            {
                ref Option<Node> r2 = ref Choose(ref k.Next, in ro);
                if (r2.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                r2 = flag ? None : None;
            }
            WriteLine(ro.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t15-branch-rhs-write-r2", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = f ? None : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t16-branch-rhs-read-payload", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = f ? None : Some(new Node(5, None));
                WriteLine(ro.AsRef() is Some<Node>(var z) ? z.V : -1);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t17-branch-rhs-switch-write-r2", """
            Run(1);
            static void Run(int sel)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = sel switch { 1 => None, _ => None };
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t18-branch-arg-replace-r2", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                Option<Node> old = r2.Replace(f ? None : None);
                old = None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t19-branch-rhs-class-field-k-next", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsMut() is Some<Node>(var k))
                {
                    if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                    k.Next = f ? None : None;
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t20-branch-rhs-struct-field-r2-x", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2.X = f ? None : None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t21-branch-arg-vec-push-realloc", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                for (int i = 0; i < 64; i++)
                {
                    r2.Push(f ? i : i + 1);
                }
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["t22-branch-arg-vec-compound-add-realloc", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                for (int i = 0; i < 64; i++)
                {
                    r2 += f ? i : i + 1;
                }
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["t23-branch-arg-mut-method-k", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsMut() is Some<Node>(var k))
                {
                    if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                    k.Cut(f ? 1 : 2);
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next)
            {
                public int V = v;
                public Option<Node> Next = next;
                [Mut] public void Cut(int d) { if (d > 0) Next = None; }
            }
            """];
        yield return ["t24-branch-arg-mut-method-struct-r2", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2.Clear(f ? 1 : 2);
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                [Mut] public void Clear(int d) { if (d > 0) X = None; }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t25-branch-arg-ref-call-r2", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                Clear2(ref r2, f ? 1 : 2);
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            static void Clear2(ref Option<Node> o, int d) { if (d > 0) o = None; }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t26-branch-arg-out-call-r2", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                Fill2(out r2, f ? 1 : 2);
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            static void Fill2(out Option<Node> o, int d) { o = d > 0 ? None : Some(new Node(9, None)); }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t27-branch-idx-indexer-setter-r2", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[f ? 0 : 0] = None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public Option<Box> this[int i] { [Mut] set { if (i == 0) X = value; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t28-branch-arg-vec-resize", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                r2.Resize(100, f ? 0 : 1);
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["t29-branch-arg-vec-reserve", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                r2.Reserve(f ? 100 : 120);
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["t30-branch-arg-replace-struct-field", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                Option<Box> old = r2.X.Replace(f ? None : None);
                old = None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t31-branch-arg-ref-call-k-next", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsMut() is Some<Node>(var k))
                {
                    if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                    Clear2(ref k.Next, f ? 1 : 2);
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            static void Clear2(ref Option<Node> o, int d) { if (d > 0) o = None; }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t32-branch-arg-out-call-k-next", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsMut() is Some<Node>(var k))
                {
                    if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                    Fill2(out k.Next, f ? 1 : 2);
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            static void Fill2(out Option<Node> o, int d) { o = d > 0 ? None : Some(new Node(9, None)); }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t33-branch-rhs-3deep-r3", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                ref Option<Node> r3 = ref r2;
                if (r3.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r3 = f ? None : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t34-branch-rhs-3deep-r2", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                ref Option<Node> r3 = ref r2;
                if (r3.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = f ? None : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t35-branch-rhs-loop-repoint", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                for (int i = 0; i < 2; i++)
                {
                    if (i == 0 && r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                    if (i == 1) r2 = f ? None : None;
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t36-in-param-origin-branch-rhs", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            Run(in a, ref b, true);
            static void Run(in Option<Node> aIn, ref Option<Node> b, bool f)
            {
                ref readonly Option<Node> ro = ref aIn;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = f ? None : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t37-lifetimeof-3args-branch-rhs", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> c = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose3(ref b, in c, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = f ? None : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r1", "r2")]
            static ref Option<Node> Choose3(ref Option<Node> s, in Option<Node> r1, in Option<Node> r2) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t38-ro-out-struct-field-branch-arg", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                FillBox(out r2.X, f ? 1 : 2);
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            static void FillBox(out Option<Box> o, int d) { o = d > 0 ? None : None; }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t39-chain-3hop-nested-ternary-write-r2", """
            Run(true, false);
            static void Run(bool f1, bool f2)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = f1 ? (f2 ? Some(new Node(9, None)) : None) : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t40-chain-shortcircuit-rhs-write-r2", """
            Run(true, false);
            static void Run(bool f1, bool f2)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = (f1 || f2) ? None : Some(new Node(9, None));
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t41-ro-through-rospan-branch-push", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ReadOnlySpan<int> ros = r2.AsReadOnlySpan();
                ro = ref ros[0];
                for (int i = 0; i < 64; i++) r2.Push(f ? i : i + 1);
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["t42-ro-extension-method-branch-arg", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2.ClearExt(f ? 1 : 2);
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            
            public static class Ext { public static void ClearExt(this ref Option<Node> o, int d) { if (d > 0) o = None; } }
            """];
        yield return ["t43-ro-user-ref-indexer-branch-idx", """
            using System.Diagnostics.CodeAnalysis;
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[f ? 0 : 0] = None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public ref Option<Box> this[int i] { [Mut] [UnscopedRef] get => ref X; }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t44-ro-user-ref-method-branch-arg", """
            using System.Diagnostics.CodeAnalysis;
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2.XRef(f ? 1 : 2) = None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                [Mut] [UnscopedRef] public ref Option<Box> XRef(int d) => ref X;
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t45-ro-class-ref-method-branch-arg", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsMut() is Some<Node>(var k))
                {
                    if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                    k.NextRef(f ? 1 : 2) = None;
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next)
            {
                public int V = v;
                public Option<Node> Next = next;
                [Mut] public ref Option<Node> NextRef(int d) => ref Next;
            }
            """];
        yield return ["t46-ro-vec-extend-branch-arg", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20];
                Vec<int> extra1 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
                Vec<int> extra2 = [1, 2, 3, 4];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                r2.Extend(f ? extra1.AsReadOnlySpan() : extra2.AsReadOnlySpan());
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["t47-ro-compound-add-user-operator-branch", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2 += f ? 1 : 2;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                [Mut] public void operator +=(int d) { if (d > 0) X = None; }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["t48-ro-mut-method-bool-shortcircuit-arg", """
            Run(true, true);
            static void Run(bool f1, bool f2)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsMut() is Some<Node>(var k))
                {
                    if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                    k.CutBool(f1 && f2);
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next)
            {
                public int V = v;
                public Option<Node> Next = next;
                [Mut] public void CutBool(bool d) { if (d) Next = None; }
            }
            """];
        yield return ["t49-ro-write-r2-call-shortcircuit-arg", """
            Run(true, true);
            static void Run(bool f1, bool f2)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                r2 = Make(f1 && f2);
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            static Option<Node> Make(bool d) => d ? None : Some(new Node(9, None));
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t50-ro-replace-call-shortcircuit-arg", """
            Run(true, true);
            static void Run(bool f1, bool f2)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                Option<Node> old = r2.Replace(Make(f1 && f2));
                old = None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            static Option<Node> Make(bool d) => d ? None : Some(new Node(9, None));
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t51-ro-clear-ref-shortcircuit-arg", """
            Run(true, true);
            static void Run(bool f1, bool f2)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                ClearBool(ref r2, f1 && f2);
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            static void ClearBool(ref Option<Node> o, bool d) { if (d) o = None; }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t52-ro-fill-out-shortcircuit-arg", """
            Run(true, true);
            static void Run(bool f1, bool f2)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                FillBool(out r2, f1 && f2);
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            static void FillBool(out Option<Node> o, bool d) { o = d ? None : Some(new Node(9, None)); }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t53-ro-is-pattern-rhs-call", """
            Option<Node> flag = Some(new Node(9, None));
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            r2 = Make(flag is Some<Node>(var q) && q.V > 0);
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            static Option<Node> Make(bool d) => d ? None : Some(new Node(9, None));
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t54-class-2level-field-uaf-branch", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Outer> b = Some(new Outer(new Node(1, Some(new Node(2, Some(new Node(3, None)))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Outer> r2 = ref ChooseO(ref b, in ro);
                if (r2.AsMut() is Some<Outer>(var k))
                {
                    if (k.Inner.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                    k.Inner.Next = f ? None : None;
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Outer> ChooseO(ref Option<Outer> s, in Option<Node> r) => ref s;
            public sealed class Outer(Node inner) { public Node Inner = inner; }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["t55-optionref-param-optionmut-param-field-branch", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            Run(a.AsRef(), b.AsMut(), true);
            static void Run(OptionRef<Node> ro, OptionMut<Node> m, bool f)
            {
                OptionMut<Node> m2 = ChooseM(m, ro);
                if (m2 is Some<Node>(var k))
                {
                    if (k.Next.AsRef() is Some<Node>(var k2)) ro = k2.Next.AsRef();
                    k.Next = f ? None : None;
                }
                WriteLine(ro is Some<Node>(var z) ? z.V : -1);
            }
            [return: LifetimeOf("s", "r")]
            static OptionMut<Node> ChooseM(OptionMut<Node> s, OptionRef<Node> r) => s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["z01-implicit-index-setter", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[^1] = None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                public Option<Box> this[int i] { [Mut] set { if (i == 0) X = value; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z02-implicit-index-setter-branch", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[^1] = f ? None : None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                public Option<Box> this[int i] { [Mut] set { if (i == 0) X = value; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z03-property-compound", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2.Q += 200;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Q { get => V; [Mut] set { V = value; X = None; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z04-property-compound-branch", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2.Q += f ? 200 : 300;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Q { get => V; [Mut] set { V = value; X = None; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z05-implicit-index-compound", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[^1] += 200;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                public int this[int i] { get => V; [Mut] set { V = value; X = None; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z06-implicit-index-compound-branch", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[^1] += f ? 200 : 300;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                public int this[int i] { get => V; [Mut] set { V = value; X = None; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z07-property-increment", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2.Q++;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Q { get => V; [Mut] set { V = value; X = None; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z08-implicit-index-increment", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[^1]++;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                public int this[int i] { get => V; [Mut] set { V = value; X = None; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z09-indexer-increment-branch-index", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[f ? 0 : 0]++;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int this[int i] { get => V; [Mut] set { V = value; X = None; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z10-mut-getter-arg", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                int q = r2[0];
                WriteLine(q);
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int this[int i] { [Mut] get { X = None; return V; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z11-mut-getter-arg-branch", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                int q = r2[f ? 0 : 1];
                WriteLine(q);
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int this[int i] { [Mut] get { X = None; return V; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z12-implicit-index-mut-getter", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                int q = r2[^1];
                WriteLine(q);
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                public int this[int i] { [Mut] get { X = None; return V; } }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z13-implicit-range-mut-slice", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                Pair q = r2[0..1];
                WriteLine(q.V);
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                [Mut] public Pair Slice(int s, int l) { X = None; return new Pair(None, s + l); }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z14-ref-indexer-two-args", """
            using System.Diagnostics.CodeAnalysis;
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[0, 1] = None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public ref Option<Box> this[int i, int j] { [Mut] [UnscopedRef] get => ref X; }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z15-ref-indexer-two-args-branch", """
            using System.Diagnostics.CodeAnalysis;
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[0, f ? 1 : 2] = None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public ref Option<Box> this[int i, int j] { [Mut] [UnscopedRef] get => ref X; }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z16-implicit-ref-indexer", """
            using System.Diagnostics.CodeAnalysis;
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[^1] = None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                public ref Option<Box> this[int i] { [Mut] [UnscopedRef] get => ref X; }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z17-implicit-ref-indexer-branch", """
            using System.Diagnostics.CodeAnalysis;
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2[^1] = f ? None : None;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
                public int Length => 1;
                public ref Option<Box> this[int i] { [Mut] [UnscopedRef] get => ref X; }
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """];
        yield return ["z30-unwrapmut-field-store", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k) && k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                r2.UnwrapMut().Next = None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["z31-unwrapmut-field-store-branch", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k) && k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                r2.UnwrapMut().Next = f ? None : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
    }

    // Sound at run time, rejected by Q-GB2-2 (a): a loan held through a ref local survives its re-pointing, so neither
    // the re-pointed ref nor the borrow it now derives from may be used while the other is live.
    public static IEnumerable<object[]> ConservativeRows()
    {
        yield return ["c01-merge-read-rx-then-write-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            WriteLine(rx.IsSome);
            r2 = None;
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["c02-branch-read-rx-then-write-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k))
            {
                rx = ref k.Next;
                WriteLine(rx.IsSome);
                r2 = None;
            }
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["c03-branch-write-rx-then-write-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k))
            {
                rx = ref k.Next;
                rx = Some(new Node(7, None));
                r2 = None;
            }
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["c10-trivial-cycle-rx-then-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref r2;
            rx = Some(new Node(7, None));
            r2 = None;
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e44-cursor-use-rx-then-match", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            rx = Some(new Node(7, None));
            if (m is Some<Node>(var z)) WriteLine(z.V);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["g09-one-ref-two-levels-sound-order", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    rx = Some(new Node(7, None));
                    k.Next = None;
                }
            }
            int c = 0;
            for (var p = a.AsRef(); p is Some<Node>(var q); p = q.Next.AsRef()) c += q.V;
            WriteLine(c);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["h03-helper-id-sound-order", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref Id(ref r2);
            rx = Some(new Node(7, None));
            r2 = None;
            WriteLine(a.IsSome);
            static ref Option<Node> Id(ref Option<Node> o) => ref o;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["l05-leapfrog-outer-ref", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> cur = ref a;
            ref Option<Node> nxt = ref cur;
            while (true)
            {
                nxt = ref cur;
                if (nxt.AsMut() is Some<Node>(var k)) cur = ref k.Next;
                else break;
            }
            cur = Some(new Node(9, None));
            int c = 0;
            for (var p = a.AsRef(); p is Some<Node>(var q); p = q.Next.AsRef()) c += q.V;
            WriteLine(c);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["v03-span-param-rebind-child-then-parent", """
            Vec<int> v = [1, 2, 3, 4];
            WriteLine(F(v.AsSpan()) + v[1]);
            static int F(Span<int> s)
            {
                Span<int> t = s[..];
                s = t[1..];
                s[0] = 5;
                t[1] = 6;
                return t[1];
            }
            """];
        yield return ["v06-asspan-of-repointed-sound-order", """
            Vec<int> v = [1, 2, 3, 4];
            ref Vec<int> rx = ref v;
            ref Vec<int> r2 = ref rx;
            rx = ref r2;
            Span<int> s = rx.AsSpan();
            s[0] = 9;
            for (int i = 0; i < 64; i++) r2.Add(i);
            WriteLine(v[0] + v.Count);
            """];
        yield return ["a03-replace", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            _ = r2.Replace(None);
            WriteLine(rx.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["b01-read-issome", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            WriteLine(r2.IsSome);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["b02-read-asref", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            int n = r2.AsRef() switch { Some<Node>(var z) => z.V, _ => 0 };
            rx = Some(new Node(7, None));
            WriteLine(n);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["c09-trivial-cycle-r2-then-rx", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref r2;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e39-cursor-second-match-read", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            if (m is Some<Node>(var z)) WriteLine(z.V);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e40-cursor-issome", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            WriteLine(m.IsSome);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["e41-cursor-asref", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            int n = m.AsRef() switch { Some<Node>(var z) => z.V, _ => 0 };
            rx = Some(new Node(7, None));
            WriteLine(n);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["g02-one-ref-one-level-write", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                rx = ref k.Next;
                k.Next = None;
                rx = Some(new Node(7, None));
            }
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["h01-helper-id-of-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref Id(ref r2);
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            static ref Option<Node> Id(ref Option<Node> o) => ref o;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["l09-ref-walk-stale-binding-write-next", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n))
            {
                cur = ref n.Next;
                if (n.V == 1) n.Next = None;
            }
            int c = 0;
            for (var p = a.AsRef(); p is Some<Node>(var q); p = q.Next.AsRef()) c += q.V;
            WriteLine(c);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["m02-span-param-move-parent", """
            Vec<int> v = [1, 2, 3, 4];
            WriteLine(F(v.AsSpan()) + v[1]);
            static int F(Span<int> s)
            {
                Span<int> t = s[..];
                s = t[1..];
                Span<int> u = t;
                u[1] = 6;
                s[0] = 5;
                return s[0];
            }
            """];
        yield return ["v02-span-param-rebind-parent-then-child", """
            Vec<int> v = [1, 2, 3, 4];
            WriteLine(F(v.AsSpan()) + v[1]);
            static int F(Span<int> s)
            {
                Span<int> t = s[..];
                s = t[1..];
                t[1] = 6;
                s[0] = 5;
                return s[0];
            }
            """];
        yield return ["v08-vec-trivial-cycle-push-both", """
            Vec<int> v = [1, 2, 3, 4];
            ref Vec<int> rx = ref v;
            ref Vec<int> r2 = ref rx;
            rx = ref r2;
            r2.Add(5);
            rx.Add(6);
            WriteLine(v.Count);
            """];
        yield return ["w01-lifetimeof-in-r2-write-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            Option<Node> spare = Some(new Node(9, None));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref Choose(ref spare, in r2);
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            WriteLine(spare.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["w02-lifetimeof-in-k-next-write-k-next", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            Option<Node> spare = Some(new Node(9, None));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                rx = ref Choose(ref spare, in k.Next);
                k.Next = None;
                rx = Some(new Node(7, None));
            }
            WriteLine(a.IsSome);
            WriteLine(spare.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["w03-lifetimeof-in-r2-write-r2-read-rx", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            Option<Node> spare = Some(new Node(9, Some(new Node(8, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref Choose(ref spare, in r2);
            r2 = None;
            WriteLine(rx.AsRef() switch { Some<Node>(var z) => z.V, _ => -1 });
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            WriteLine(spare.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["w04-lifetimeof-in-r2-vec-write-r2", """
            Vec<int> v = [1, 2, 3, 4];
            Vec<int> spare = [9, 8];
            ref Vec<int> rx = ref v;
            ref Vec<int> r2 = ref rx;
            rx = ref ChooseV(ref spare, in r2);
            r2 = [5];
            for (int i = 0; i < 64; i++) rx.Add(i);
            WriteLine(v.Count + rx.Count + spare[0]);
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in Vec<int> r) => ref s;
            """];
        yield return ["w05-lifetimeof-in-r2-replace-drop", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            Option<Node> spare = Some(new Node(9, None));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref Choose(ref spare, in r2);
            Option<Node> old = r2.Replace(None);
            old = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            WriteLine(spare.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["w06-lifetimeof-in-r2-vec-push-r2", """
            Vec<int> v = [1, 2, 3];
            Vec<int> spare = [9, 8];
            ref Vec<int> rx = ref v;
            ref Vec<int> r2 = ref rx;
            rx = ref ChooseV(ref spare, in r2);
            for (int i = 0; i < 64; i++) r2.Push(i);
            rx.Push(77);
            WriteLine(v.Count + spare.Count);
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in Vec<int> r) => ref s;
            """];
        yield return ["i01-vec-index-write-shared-ancestor", """
            int a = 0;
            Vec<int> v = [10, 20];
            ref readonly int ro = ref a;
            ref Vec<int> r2 = ref ChooseV(ref v, in ro);
            ro = ref r2[0];
            r2[0] = 99;
            WriteLine(ro);
            WriteLine(v[0]);
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["i02-vec-index-compound-add-shared-ancestor", """
            int a = 0;
            Vec<int> v = [10, 20];
            ref readonly int ro = ref a;
            ref Vec<int> r2 = ref ChooseV(ref v, in ro);
            ro = ref r2[0];
            r2[0] += 5;
            WriteLine(ro);
            WriteLine(v[0]);
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["i03-vec-from-end-index-write-shared-ancestor", """
            int a = 0;
            Vec<int> v = [10, 20];
            ref readonly int ro = ref a;
            ref Vec<int> r2 = ref ChooseV(ref v, in ro);
            ro = ref r2[^1];
            r2[^1] = 99;
            WriteLine(ro);
            WriteLine(v[1]);
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["i04-vec-struct-elem-field-write-shared-ancestor", """
            var a = new Pt(0, 0);
            Vec<Pt> v = [new Pt(1, 2)];
            ref readonly int ro = ref a.X;
            ref Vec<Pt> r2 = ref ChooseP(ref v, in ro);
            ro = ref r2[0].X;
            r2[0].X = 99;
            WriteLine(ro);
            WriteLine(v[0].X);
            [return: LifetimeOf("s", "r")]
            static ref Vec<Pt> ChooseP(ref Vec<Pt> s, in int r) => ref s;
            public struct Pt(int x, int y) { public int X = x; public int Y = y; }
            """];
        yield return ["i05-vec-struct-elem-sibling-field-sound", """
            var a = new Pt(0, 0);
            Vec<Pt> v = [new Pt(1, 2)];
            ref readonly int ro = ref a.X;
            ref Vec<Pt> r2 = ref ChooseP(ref v, in ro);
            ro = ref r2[0].X;
            r2[0].Y = 99;
            WriteLine(ro);
            WriteLine(v[0].Y);
            [return: LifetimeOf("s", "r")]
            static ref Vec<Pt> ChooseP(ref Vec<Pt> s, in int r) => ref s;
            public struct Pt(int x, int y) { public int X = x; public int Y = y; }
            """];
        yield return ["i06-span-val-index-write-shared-ancestor", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                s2[0] = 99;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["i07-span-val-from-end-index-write-shared-ancestor", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[^1];
                s2[^1] = 99;
                WriteLine(ro);
                WriteLine(s2[1]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["i08-span-val-index-compound-add-shared-ancestor", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                s2[0] += 5;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["i09-vec-index-write-branch-rhs-fixed", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                r2[0] = f ? 99 : 88;
                WriteLine(ro);
                WriteLine(v[0]);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["i10-vec-elem-mut-method-call", """
            var a = new Pt(0, 0);
            Vec<Pt> v = [new Pt(1, 2)];
            ref readonly int ro = ref a.X;
            ref Vec<Pt> r2 = ref ChooseP(ref v, in ro);
            ro = ref r2[0].X;
            r2[0].SetX(99);
            WriteLine(ro);
            WriteLine(v[0].X);
            [return: LifetimeOf("s", "r")]
            static ref Vec<Pt> ChooseP(ref Vec<Pt> s, in int r) => ref s;
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int nx) { X = nx; }
            }
            """];
        yield return ["i11-vec-elem-ref-arg-call", """
            int a = 0;
            Vec<int> v = [10, 20];
            ref readonly int ro = ref a;
            ref Vec<int> r2 = ref ChooseV(ref v, in ro);
            ro = ref r2[0];
            SetInt(ref r2[0]);
            WriteLine(ro);
            WriteLine(v[0]);
            static void SetInt(ref int x) { x = 99; }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["i12-vec-elem-out-arg-call", """
            int a = 0;
            Vec<int> v = [10, 20];
            ref readonly int ro = ref a;
            ref Vec<int> r2 = ref ChooseV(ref v, in ro);
            ro = ref r2[0];
            OutInt(out r2[0]);
            WriteLine(ro);
            WriteLine(v[0]);
            static void OutInt(out int x) { x = 99; }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["i13-span-elem-ref-arg-call", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                SetInt(ref s2[0]);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void SetInt(ref int x) { x = 99; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["i14-rospan-param-span-param-index-write", """
            Vec<int> v1 = [1, 2];
            Vec<int> v2 = [10, 20];
            Run(v1.AsReadOnlySpan(), v2.AsSpan());
            static void Run(ReadOnlySpan<int> ro, Span<int> s)
            {
                Span<int> s2 = ChooseS(s, ro);
                ro = s2;
                s2[0] = 99;
                WriteLine(ro[0]);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, ReadOnlySpan<int> r) => s;
            """];
        yield return ["i15-vec-elem-write-sound", """
            Vec<int> v = [1, 2, 3];
            int zero = 0;
            ref readonly int ro = ref zero;
            ref Vec<int> r2 = ref ChooseV(ref v, in ro);
            ro = ref r2[0];
            r2[1] = 9;
            WriteLine(ro);
            WriteLine(v[1]);
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["i16-vec-elem-write-cond", """
            bool flag = true;
            Vec<int> v = [1, 2, 3];
            int zero = 0;
            ref readonly int ro = ref zero;
            ref Vec<int> r2 = ref ChooseV(ref v, in ro);
            ro = ref r2[0];
            r2[1] = flag ? 9 : 8;
            WriteLine(ro);
            WriteLine(v[1]);
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["w07-w01-cond-rhs-sound", """
            bool flag = true;
            Option<Node> a = Some(new Node(1, None));
            Option<Node> spare = Some(new Node(9, None));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref Choose(ref spare, in r2);
            r2 = flag ? None : None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            WriteLine(spare.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["w08-ro-compound-int-branch-rhs", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                int b = 10;
                ref readonly int ro = ref a;
                ref int r2 = ref ChooseI(ref b, in ro);
                ro = ref r2;
                r2 += f ? 5 : 6;
                WriteLine(ro);
                WriteLine(b);
            }
            [return: LifetimeOf("s", "r")]
            static ref int ChooseI(ref int s, in int r) => ref s;
            """];
        yield return ["w09-w01-branch-rhs-conservative", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
                Option<Node> spare = Some(new Node(9, None));
                ref Option<Node> rx = ref a;
                ref Option<Node> r2 = ref rx;
                rx = ref Choose(ref spare, in r2);
                r2 = f ? None : None;
                rx = Some(new Node(7, None));
                WriteLine(a.IsSome);
                WriteLine(spare.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["w10-ro-class-2level-field-write-branch", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Outer> b = Some(new Outer(new Node(1, Some(new Node(2, None)))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Outer> r2 = ref ChooseO(ref b, in ro);
                if (r2.AsMut() is Some<Outer>(var k))
                {
                    ro = ref k.Inner.Next;
                    k.Inner.Next = f ? None : None;
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Outer> ChooseO(ref Option<Outer> s, in Option<Node> r) => ref s;
            public sealed class Outer(Node inner) { public Node Inner = inner; }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """];
        yield return ["z19-vec-truncate-branch", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20, 30];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                r2.Truncate(f ? 1 : 2);
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["z20-vec-slice-write-branch", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20, 30];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                Span<int> t = r2.Slice(0, f ? 1 : 2);
                t[0] = 9;
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["z21-vec-range-view-write", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20, 30];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                Span<int> t = r2[0..1];
                t[0] = 9;
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["z22-vec-elem-compound-branch", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20, 30];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                r2[0] += f ? 5 : 6;
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["z23-vec-elem-from-end-branch", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20, 30];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                r2[^3] = f ? 5 : 6;
                WriteLine(ro);
                WriteLine(v.Count);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """];
        yield return ["z24-span-slice-write-branch", """
            Vec<int> v = [10, 20, 30];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                Span<int> t = s2.Slice(0, f ? 1 : 2);
                t[0] = 9;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["z25-span-elem-branch", """
            Vec<int> v = [10, 20, 30];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                s2[0] = f ? 9 : 8;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["z26-span-foreach-ref", """
            Vec<int> v = [10, 20, 30];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                foreach (ref int x in s2) x = 9;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r01-span-arg-plain", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                Set0(s2, 9);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void Set0(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["r02-span-arg-cond", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                Set0(s2, ro > 5 ? 9 : 8);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void Set0(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["r03-span-arg-rhs-repoint", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                Set0(s2, (ro = ref s2[0]) > 5 ? 9 : 8);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void Set0(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["r04-span-param-as-ancestor", """
            Vec<int> v = [10, 20];
            Vec<int> w = [1];
            Run(v.AsSpan(), w.AsReadOnlySpan(), true);
            static void Run(Span<int> s, ReadOnlySpan<int> ro, bool f)
            {
                Span<int> s2 = ChooseS(s, ro);
                ro = s2;
                Set0(s2, f ? 9 : 8);
                WriteLine(ro[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, ReadOnlySpan<int> r) => s;
            static void Set0(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["r05-span-ext-receiver", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                s2.Set0(9);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            public static class Ext { public static void Set0(this Span<int> t, int x) { t[0] = x; } }
            """];
        yield return ["r06-span-generic-nested-branch", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true, false);
            static void Run(Span<int> s, bool f, bool g)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                SetG(s2, f ? (g ? 7 : 9) : 8);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void SetG<T>(Span<T> t, T x) where T : unmanaged { t[0] = x; }
            """];
        yield return ["r07-span-in-param-ancestor-loop", """
            Vec<int> v = [10, 20];
            int z = 0;
            Run(v.AsSpan(), in z);
            static void Run(Span<int> s, in int a)
            {
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[1];
                for (int i = 0; i < 2; i++) Set(s2, i, 50 + i);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void Set(Span<int> t, int i, int x) { t[i] = x; }
            """];
        yield return ["r08-span-3deep", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                Span<int> s3 = s2.Slice(0, 2);
                ro = ref s3[0];
                Set0(s3, 9);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void Set0(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["r09-span-param-ancestor-rospan", """
            Vec<int> v1 = [1, 2];
            Vec<int> v2 = [10, 20];
            Run(v1.AsReadOnlySpan(), v2.AsSpan());
            static void Run(ReadOnlySpan<int> ro, Span<int> s)
            {
                Span<int> s2 = ChooseS(s, ro);
                ro = s2;
                Fill(s2, 99, 0);
                WriteLine(ro[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, ReadOnlySpan<int> r) => s;
            static void Fill(Span<int> t, int x, int i) { t[i] = x; }
            """];
        yield return ["r10-span-ctor-arg", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                var w = new Writer(s2, 9);
                WriteLine(ro);
                WriteLine(w.N);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            public sealed class Writer { public int N; public Writer(Span<int> t, int x) { t[0] = x; N = x; } }
            """];
        yield return ["r11-span-arg-then-ro-cached", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                int before = ro;
                Set0(s2, 9);
                WriteLine(before + ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void Set0(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["r12-span-2arg-call-ro", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutateSpan(s2, 99);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r13-span-2arg-call-rospan", """
            Vec<int> v1 = [1, 2];
            Vec<int> v2 = [10, 20];
            Run(v1.AsReadOnlySpan(), v2.AsSpan());
            static void Run(ReadOnlySpan<int> ro, Span<int> s)
            {
                Span<int> s2 = ChooseS(s, ro);
                ro = s2;
                MutateSpan(s2, 99);
                WriteLine(ro[0]);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, ReadOnlySpan<int> r) => s;
            """];
        yield return ["r14-span-ext-method-ro", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                s2.MutExt(99);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            public static class SpanExt
            {
                public static void MutExt(this Span<int> x, int val) { x[0] = val; }
            }
            """];
        yield return ["r15-span-ext-method-rospan", """
            Vec<int> v1 = [1, 2];
            Vec<int> v2 = [10, 20];
            Run(v1.AsReadOnlySpan(), v2.AsSpan());
            static void Run(ReadOnlySpan<int> ro, Span<int> s)
            {
                Span<int> s2 = ChooseS(s, ro);
                ro = s2;
                s2.MutExt(99);
                WriteLine(ro[0]);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, ReadOnlySpan<int> r) => s;
            public static class SpanExt
            {
                public static void MutExt(this Span<int> x, int val) { x[0] = val; }
            }
            """];
        yield return ["r16-span-1arg-mut-instance-method", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                var h = new Helper(0);
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                h.TakeSpan(s2);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            public struct Helper(int c)
            {
                public int C = c;
                [Mut] public void TakeSpan(Span<int> x) { C += 1; x[0] = 99; }
            }
            """];
        yield return ["r17-span-2arg-ref-return-assign", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                Elem(s2, 0) = 99;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("x")]
            static ref int Elem(Span<int> x, int i) => ref x[i];
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r18-span-ext-ref-return-assign", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                s2.ElemExt(0) = 99;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            public static class SpanExt
            {
                [return: LifetimeOf("x")]
                public static ref int ElemExt(this Span<int> x, int i) => ref x[i];
            }
            """];
        yield return ["r19-span-1arg-mut-method-ref-return", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                var h = new Helper(0);
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                h.Elem(s2) = 99;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            public struct Helper(int c)
            {
                public int C = c;
                [Mut]
                [return: LifetimeOf("x")]
                public ref int Elem(Span<int> x) { C += 1; return ref x[0]; }
            }
            """];
        yield return ["r20-span-2arg-branch", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutateSpan(s2, f ? 99 : 88);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r21-span-2arg-nested-branch", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true, false, true);
            static void Run(Span<int> s, bool f, bool g, bool h)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutateSpan(s2, f ? (g ? 99 : 88) : (h switch { true => 77, _ => 66 }));
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r22-span-2arg-ctor", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                var c = new Consumer(s2, 99);
                WriteLine(ro);
                WriteLine(c.Seen);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            public struct Consumer
            {
                public int Seen;
                public Consumer(Span<int> x, int val) { Seen = x[0]; x[0] = val; }
            }
            """];
        yield return ["r23-span-2arg-generic", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutateSpanG<int>(s2, 99);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpanG<T>(Span<T> x, T val) where T : unmanaged { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r24-span-2arg-local-fn", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutLocal(s2, 99);
                WriteLine(ro);
                WriteLine(s2[0]);
                static void MutLocal(Span<int> x, int val) { x[0] = val; }
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r25-span-2arg-loop", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                for (int i = 0; i < 2; i++)
                {
                    if (i == 0) ro = ref s2[0];
                    else MutateSpan(s2, 99);
                }
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r26-span-2arg-noalias-read-before-after", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                int before = ro;
                MutateSpan(s2, 99);
                int after = ro;
                WriteLine(before);
                WriteLine(after);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r27-span-2arg-3arg-lifetimeof", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                int d = 5;
                ref readonly int ro = ref a;
                Span<int> s2 = Choose3(s, in d, in ro);
                ro = ref s2[0];
                MutateSpan(s2, 99);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r1", "r2")]
            static Span<int> Choose3(Span<int> s, in int r1, in int r2) => s;
            """];
        yield return ["r28-span-mut-indexer-setter-arg", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                var h = new Setter(0);
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                h[s2] = 99;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            public struct Setter(int c)
            {
                public int C = c;
                public int this[Span<int> x]
                {
                    [Mut] set { C += 1; x[0] = value; }
                }
            }
            """];
        yield return ["r29-span-2arg-ref-return-ref-arg", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                SetInt(ref Elem(s2, 0));
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void SetInt(ref int x) { x = 99; }
            [return: LifetimeOf("x")]
            static ref int Elem(Span<int> x, int i) => ref x[i];
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r30-span-2arg-ref-return-compound", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                Elem(s2, 0) += 5;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("x")]
            static ref int Elem(Span<int> x, int i) => ref x[i];
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r31-span-2arg-ref-return-double-branch", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true, false);
            static void Run(Span<int> s, bool f, bool g)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                Elem(s2, f ? 0 : 1) = g ? 99 : 88;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("x")]
            static ref int Elem(Span<int> x, int i) => ref x[i];
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r32-span-2arg-in-param-origin", """
            int a = 0;
            Vec<int> v = [10, 20];
            Run(in a, v.AsSpan());
            static void Run(in int p, Span<int> s)
            {
                ref readonly int ro = ref p;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutateSpan(s2, 99);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r33-span-2arg-arg0-of-two-spans", """
            Vec<int> v1 = [1, 2];
            Vec<int> v2 = [10, 20];
            Run(v1.AsSpan(), v2.AsSpan());
            static void Run(Span<int> other, Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutFirst(s2, other);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutFirst(Span<int> x, Span<int> o) { x[0] = 99; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r34-span-2arg-ref-local-alias-write-then-ro", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                ref int mutAlias = ref Elem(s2, 0);
                mutAlias = 99;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("x")]
            static ref int Elem(Span<int> x, int i) => ref x[i];
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r35-span-2arg-return-span-write-then-ro", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                Span<int> s3 = Id2(s2, 1);
                s3[0] = 99;
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("x")]
            static Span<int> Id2(Span<int> x, int dummy) => x;
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r36-span-2arg-loop-noalias-hoist", """
            Vec<int> v = [10, 20];
            WriteLine(Run(v.AsSpan(), 5));
            static int Run(Span<int> s, int n)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                int sum = 0;
                for (int i = 1; i <= n; i++)
                {
                    MutateSpan(s2, i);
                    sum += ro;
                }
                return sum;
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r37-span-2arg-rospan-before-after", """
            Vec<int> v1 = [1, 2];
            Vec<int> v2 = [10, 20];
            Run(v1.AsReadOnlySpan(), v2.AsSpan());
            static void Run(ReadOnlySpan<int> ro, Span<int> s)
            {
                Span<int> s2 = ChooseS(s, ro);
                ro = s2;
                int before = ro[0];
                MutateSpan(s2, 99);
                int after = ro[0];
                WriteLine(before);
                WriteLine(after);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, ReadOnlySpan<int> r) => s;
            """];
        yield return ["r38-span-2arg-reborrow-read-helper", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                int b = ReadRo(in ro);
                MutateSpan(s2, 99);
                int c = ReadRo(in ro);
                WriteLine(b);
                WriteLine(c);
            }
            static int ReadRo(in int r) => r;
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r39-3deep-span-chain-2arg-call", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                Span<int> s3 = s2[..];
                ro = ref s3[0];
                MutateSpan(s3, 99);
                WriteLine(ro);
                WriteLine(s3[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["r40-3deep-span-chain-mutate-s2-2arg", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                Span<int> s3 = s2[..];
                ro = ref s3[0];
                MutateSpan(s2, 99);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """];
        yield return ["j01-span-cond-value-first-arg", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutateSpan(f ? s2 : s2, 99);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void MutateSpan(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["j02-span-slice-first-arg", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutateSpan(s2.Slice(0, 1), 99);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void MutateSpan(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["j03-span-range-first-arg", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                MutateSpan(s2[0..1], 99);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void MutateSpan(Span<int> t, int x) { t[0] = x; }
            """];
        yield return ["j05-span-arg-then-out-arg", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                SetOut(s2, out int x);
                WriteLine(ro + x);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void SetOut(Span<int> t, out int x) { t[0] = 9; x = 1; }
            """];
    }

    [Theory]
    [MemberData(nameof(UseAfterFreeRows))]
    [MemberData(nameof(ConservativeRows))]
    public Task UseWhileRepointedRefIsLive_IsExactlyVib003(string shape, string source) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, "VIB003");

    public static IEnumerable<object[]> ControlRows()
    {
        yield return ["c08-no-repoint-parent-read-child-live", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            WriteLine(rx.IsSome);
            r2 = None;
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003"];
        yield return ["c11-unrelated-repoint-rx-while-r2-live", """
            Option<Node> a = Some(new Node(1, None));
            Option<Node> b = Some(new Node(2, None));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref b;
            rx = None;
            r2 = None;
            WriteLine(a.IsSome || b.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003"];
        yield return ["c13-other-order-r2-repointed-write-rx", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (rx.AsMut() is Some<Node>(var k)) r2 = ref k.Next;
            rx = None;
            r2 = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003"];
        yield return ["d04-3deep-r2-into-r3-write-rx", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            ref Option<Node> r3 = ref r2;
            if (r3.AsMut() is Some<Node>(var k)) r2 = ref k.Next;
            rx = None;
            r2 = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003"];
        yield return ["e01-cursor-set", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k))
            {
                rx = ref k.Next;
            }
            m.Set(None);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003"];
        yield return ["e10-asref-mutable-rx-through-shared", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref readonly Option<Node> r2 = ref rx;
            if (r2.AsRef() is Some<Node>(var k)) rx = ref k.Next;
            WriteLine(rx.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB004"];
        yield return ["e42-cursor-move-no-repoint", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            var m2 = m;
            m2.Set(None);
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, ""];
        yield return ["e46-cursor-move-then-match-m", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            var m2 = m;
            m2.Set(None);
            rx = Some(new Node(7, None));
            if (m is Some<Node>(var z)) WriteLine(z.V); else WriteLine(0);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003"];
        yield return ["f04-place-scrutinee", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2 is Some<Node>(var k)) rx = ref k.Next;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003,VIB004"];
        yield return ["h05-helper-self-repoint-child-live", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            rx = ref Id(ref rx);
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            static ref Option<Node> Id(ref Option<Node> o) => ref o;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003"];
        yield return ["m01-move-out-through-r2", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            Option<Node> t = r2;
            t = None;
            WriteLine(rx.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB003,VIB005"];
        yield return ["m03-span-move-twin-no-rebind", """
            Vec<int> v = [1, 2, 3, 4];
            Span<int> t = v.AsSpan();
            Span<int> s = t[1..];
            Span<int> u = t;
            u[1] = 6;
            s[0] = 5;
            WriteLine(v[0] + v[1]);
            """, "VIB003"];
        yield return ["v01-span-local-rebind", """
            Vec<int> v = [1, 2, 3, 4];
            Span<int> s = v.AsSpan();
            Span<int> t = s[..];
            s = t[1..];
            t[1] = 6;
            s[0] = 5;
            WriteLine(v[0] + v[1]);
            """, "VIB003"];
        yield return ["v04-asspan-of-repointed-then-push", """
            Vec<int> v = [1, 2, 3, 4];
            ref Vec<int> rx = ref v;
            ref Vec<int> r2 = ref rx;
            rx = ref r2;
            Span<int> s = rx.AsSpan();
            for (int i = 0; i < 64; i++) r2.Add(i);
            WriteLine(s[0]);
            """, "VIB003"];
        yield return ["x01-nested-repoint-snapshot", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            Option<Node> spare = None;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            rx = ref Pick(Clear(ref r2), ref (rx = ref spare));
            WriteLine(rx.IsSome);
            WriteLine(a.IsSome);
            static ref Option<Node> Pick(bool b, ref Option<Node> o) => ref o;
            static bool Clear(ref Option<Node> o) { o = None; return true; }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, ""];
        yield return ["u09-shared-ancestor-repointed-away", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            ro = ref a;
            r2 = None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, ""];
        yield return ["y17-sound-ro-repointed-away-branch-write", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                ro = ref a;
                r2 = f ? None : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, ""];
        yield return ["z18-tryparse-out-first-field", """
            Run(true);
            static void Run(bool f)
            {
                var p = new Pair(None, 1);
                int a = 0;
                ref readonly int ro = ref a;
                ref Pair r2 = ref ChooseI(ref p, in ro);
                ro = ref r2.V;
                bool ok = int.TryParse(result: out r2.V, s: f ? "5" : "6");
                WriteLine(ok);
                WriteLine(ro);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseI(ref Pair s, in int r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """, "VIB000"];
        yield return ["z29-vec-elem-no-cycle-control", """
            Vec<int> v = [10, 20, 30];
            ref readonly int e = ref v[0];
            v[1] = 9;
            WriteLine(e);
            WriteLine(v[1]);
            """, "VIB003"];
        yield return ["z32-conditional-ref-target", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k) && k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                Option<Node> spare = None;
                (f ? ref r2 : ref spare) = None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "VIB000"];
        yield return ["z33-vec-mut-elem-ref-read-owner", """
            Vec<int> v = [1, 2, 3];
            ref int e = ref v[0];
            WriteLine(v.Count);
            e = 9;
            WriteLine(v[0]);
            """, "VIB003"];
        yield return ["z34-span-mut-elem-ref-read-owner", """
            Vec<int> v = [1, 2, 3];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                ref int e = ref s[0];
                WriteLine(s.Length);
                e = 9;
                WriteLine(s[0]);
            }
            """, "VIB003"];
        yield return ["n02-span-arg-sibling-twoargs-sound", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                Set0(s2, ro);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void Set0(Span<int> t, int x) { t[0] = x; }
            """, ""];
        yield return ["n03-span-forms", """
            Vec<int> v = [10, 20, 30];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s2, bool f)
            {
                Use(s2, f ? 1 : 2);
                Span<int> t = s2.Slice(f ? 0 : 1);
                Span<int> u = s2;
                u[f ? 0 : 1] = 5;
                Id(u, f ? 1 : 2)[0] = 7;
            }
            static void Use(Span<int> s, int i) { s[0] = i; }
            static Span<int> Id(Span<int> s, int i) => s;
            """, ""];
        yield return ["n05-span-2arg-repoint-back-sound", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 7;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                ro = ref a;
                MutateSpan(s2, 99);
                WriteLine(ro);
                WriteLine(s2[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """, ""];
    }

    [Theory]
    [MemberData(nameof(ControlRows))]
    public Task Controls_HaveExactRoslynIds(string shape, string source, string ids) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, ids);

    public static IEnumerable<object[]> AcceptedRows()
    {
        yield return ["c04-only-rx-after", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\n"];
        yield return ["c05-only-r2-after", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            r2 = None;
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "False\n"];
        yield return ["c06-two-writes-r2-after", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            if (r2.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            r2 = None;
            r2 = Some(new Node(5, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\n"];
        yield return ["c07-no-repoint-child-then-parent", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            r2 = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\n"];
        yield return ["d06-3deep-no-repoint-sound", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            ref Option<Node> r3 = ref r2;
            r3 = None;
            r2 = Some(new Node(5, None));
            rx = None;
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "False\n"];
        yield return ["d07-3deep-rx-into-r3-only-rx-after", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            ref Option<Node> r2 = ref rx;
            ref Option<Node> r3 = ref r2;
            if (r3.AsMut() is Some<Node>(var k)) rx = ref k.Next;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\n"];
        yield return ["e08-asref-all-shared", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> rx = ref a;
            ref readonly Option<Node> r2 = ref rx;
            if (r2.AsRef() is Some<Node>(var k)) rx = ref k.Next;
            WriteLine(r2.IsSome);
            WriteLine(rx.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\nTrue\n"];
        yield return ["e43-cursor-two-matches-no-repoint", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) k.V = 5;
            if (m is Some<Node>(var z)) z.Next = None;
            rx = Some(new Node(7, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\n"];
        yield return ["e45-cursor-only-m-after", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k)) rx = ref k.Next;
            m.Set(None);
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "False\n"];
        yield return ["g03-one-ref-read-other-field", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                rx = ref k.Next;
                WriteLine(k.V);
                rx = Some(new Node(7, None));
            }
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "1\nTrue\n"];
        yield return ["g06-one-ref-write-other-field", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    k.V = 5;
                    rx = Some(new Node(7, None));
                }
            }
            int c = 0;
            for (var p = a.AsRef(); p is Some<Node>(var q); p = q.Next.AsRef()) c += q.V;
            WriteLine(c);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "14\n"];
        yield return ["g10-one-ref-binding-dead-after-repoint", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> rx = ref a;
            if (rx.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsMut() is Some<Node>(var k2))
                {
                    rx = ref k2.Next;
                    rx = Some(new Node(7, None));
                }
            }
            int c = 0;
            for (var p = a.AsRef(); p is Some<Node>(var q); p = q.Next.AsRef()) c += q.V;
            WriteLine(c);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "10\n"];
        yield return ["l01-ref-walk-append", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n)) cur = ref n.Next;
            cur = Some(new Node(9, None));
            int c = 0;
            for (var p = a.AsRef(); p is Some<Node>(var q); p = q.Next.AsRef()) c += q.V;
            WriteLine(c);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "15\n"];
        yield return ["l02-ref-walk-read-before-advance", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> cur = ref a;
            int sum = 0;
            while (cur.AsMut() is Some<Node>(var n))
            {
                sum += n.V;
                cur = ref n.Next;
            }
            cur = Some(new Node(9, None));
            WriteLine(sum);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "6\n"];
        yield return ["l03-ref-walk-read-after-advance", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> cur = ref a;
            int sum = 0;
            while (cur.AsMut() is Some<Node>(var n))
            {
                cur = ref n.Next;
                sum += n.V;
            }
            cur = Some(new Node(9, None));
            WriteLine(sum);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "6\n"];
        yield return ["l04-leapfrog-inner-ref", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> cur = ref a;
            while (true)
            {
                ref Option<Node> nxt = ref cur;
                if (nxt.AsMut() is Some<Node>(var k)) cur = ref k.Next;
                else break;
            }
            cur = Some(new Node(9, None));
            int c = 0;
            for (var p = a.AsRef(); p is Some<Node>(var q); p = q.Next.AsRef()) c += q.V;
            WriteLine(c);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "15\n"];
        yield return ["l08-ref-walk-remove-matching", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n))
            {
                if (n.V == 2) break;
                cur = ref n.Next;
            }
            Option<Node> cut = cur.Take();
            cut = None;
            int c = 0;
            for (var p = a.AsRef(); p is Some<Node>(var q); p = q.Next.AsRef()) c += q.V;
            WriteLine(c);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "1\n"];
        yield return ["p01-ref-param-walk", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            WriteLine(Count(ref a));
            WriteLine(a.IsSome);
            static int Count(ref Option<Node> head)
            {
                ref Option<Node> cur = ref head;
                int n = 0;
                while (cur.AsMut() is Some<Node>(var k))
                {
                    n++;
                    cur = ref k.Next;
                }
                if (head.IsSome) n += 100;
                head = None;
                return n;
            }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "103\nFalse\n"];
        yield return ["p02-span-param-write-then-read", """
            Vec<int> v = [1, 2, 3];
            WriteLine(F(v.AsSpan()) + v[0]);
            static int F(Span<int> s)
            {
                s[0] = 5;
                s[1] = s[0] + 1;
                return s[1];
            }
            """, "11\n"];
        yield return ["p03-span-param-self-rebind-walk", """
            Vec<int> v = [1, 2, 3, 4];
            WriteLine(Sum(v.AsSpan()) + v[3]);
            static int Sum(Span<int> s)
            {
                int t = 0;
                while (s.Length > 0)
                {
                    t += s[0];
                    s[0] = 0;
                    s = s[1..];
                }
                return t;
            }
            """, "10\n"];
        yield return ["e48-cursor-local-repoint-read-sibling-field", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            ref Option<Node> rx = ref a;
            var m = rx.AsMut();
            if (m is Some<Node>(var k))
            {
                rx = ref k.Next;
                WriteLine(k.V);
                rx = Some(new Node(7, None));
            }
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "1\nTrue\n"];
        yield return ["q14-struct-field-through-ref-sound", """
            var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
            ref Pair rx = ref p;
            ref Pair r2 = ref rx;
            if (r2.X.AsMut() is Some<Box>(var b)) rx = ref b.P;
            r2.V = 5;
            rx.V = 9;
            WriteLine(p.V);
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """, "5\n"];
        yield return ["s02-walk-truncate-inside-then-use-cur", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n))
            {
                if (n.V == 3) { n.Next = None; break; }
                cur = ref n.Next;
            }
            cur = Some(new Node(9, None));
            int sum = 0; ref readonly Option<Node> w = ref a; while (w.AsRef() is Some<Node>(var q)) { sum = sum * 10 + q.V; w = ref q.Next; } WriteLine(sum);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "129\n"];
        yield return ["s04-walk-set-value-each", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n))
            {
                n.V = n.V * 2;
                cur = ref n.Next;
            }
            cur = Some(new Node(9, None));
            int sum = 0; ref readonly Option<Node> w = ref a; while (w.AsRef() is Some<Node>(var q)) { sum = sum * 10 + q.V; w = ref q.Next; } WriteLine(sum);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "24689\n"];
        yield return ["s06-walk-truncate-noloop-use", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n))
            {
                if (n.V == 3) { n.Next = None; break; }
                cur = ref n.Next;
            }
            int sum = 0; ref readonly Option<Node> w = ref a; while (w.AsRef() is Some<Node>(var q)) { sum = sum * 10 + q.V; w = ref q.Next; } WriteLine(sum);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "123\n"];
        yield return ["s09-walk-cursor-set-inside", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            var cur = a.AsMut();
            while (cur is Some<Node>(var n))
            {
                if (n.V == 3) { n.Next.AsMut().Set(None); break; }
                cur = n.Next.AsMut();
            }
            int sum = 0; ref readonly Option<Node> w = ref a; while (w.AsRef() is Some<Node>(var q)) { sum = sum * 10 + q.V; w = ref q.Next; } WriteLine(sum);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "123\n"];
        yield return ["u05-shared-ancestor-no-repoint-control", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            r2 = None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "False\nFalse\n"];
        yield return ["u08-shared-ancestor-read-before-write", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            WriteLine(ro.IsSome);
            r2 = None;
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\nFalse\n"];
        yield return ["u10-shared-ancestor-struct-sibling-field-sound", """
            var p0 = new Pair(None, 0);
            var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
            ref readonly Pair ro = ref p0;
            ref Pair r2 = ref ChooseP(ref p, in ro);
            if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
            r2.V = 5;
            WriteLine(ro.V);
            WriteLine(p.V);
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """, "2\n5\n"];
        yield return ["u12-shared-ancestor-field-write-k-next-sound", """
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                k.V = 7;
            }
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\nTrue\n"];
        yield return ["y01-no-repoint-cond-rhs-sound", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            r2 = flag ? None : None;
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "False\nFalse\n"];
        yield return ["y02-walk-truncate-cond", """
            bool flag = true;
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n))
            {
                if (n.V == 3) { n.Next = flag ? None : None; break; }
                cur = ref n.Next;
            }
            Print(in a);
            static void Print(in Option<Node> o) { var c = o.AsRef(); while (c is Some<Node>(var n)) { Write(n.V); c = n.Next.AsRef(); } WriteLine(); }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "123\n"];
        yield return ["y03-walk-append-cond", """
            bool flag = true;
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n)) cur = ref n.Next;
            cur = flag ? Some(new Node(9, None)) : None;
            Print(in a);
            static void Print(in Option<Node> o) { var c = o.AsRef(); while (c is Some<Node>(var n)) { Write(n.V); c = n.Next.AsRef(); } WriteLine(); }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "12349\n"];
        yield return ["y04-walk-set-value-cond", """
            bool flag = true;
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n))
            {
                n.V = flag ? n.V * 2 : n.V;
                cur = ref n.Next;
            }
            cur = flag ? Some(new Node(5, None)) : None;
            Print(in a);
            static void Print(in Option<Node> o) { var c = o.AsRef(); while (c is Some<Node>(var n)) { Write(n.V); c = n.Next.AsRef(); } WriteLine(); }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "24685\n"];
        yield return ["y05-walk-compound-cond", """
            bool flag = true;
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, Some(new Node(4, None))))))));
            ref Option<Node> cur = ref a;
            while (cur.AsMut() is Some<Node>(var n))
            {
                n.V += flag ? 1 : 2;
                cur = ref n.Next;
            }
            Print(in a);
            static void Print(in Option<Node> o) { var c = o.AsRef(); while (c is Some<Node>(var n)) { Write(n.V); c = n.Next.AsRef(); } WriteLine(); }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "2345\n"];
        yield return ["y06-u08-read-before-cond-write", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
            WriteLine(ro.IsSome);
            r2 = flag ? None : None;
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\nFalse\n"];
        yield return ["y07-u12-sibling-class-field-cond", """
            bool flag = true;
            Option<Node> a = None;
            Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            ref readonly Option<Node> ro = ref a;
            ref Option<Node> r2 = ref Choose(ref b, in ro);
            if (r2.AsMut() is Some<Node>(var k))
            {
                if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                k.V = flag ? 7 : 8;
            }
            WriteLine(ro.IsSome);
            WriteLine(b.IsSome);
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\nTrue\n"];
        yield return ["y08-u10-sibling-struct-field-cond", """
            bool flag = true;
            var p0 = new Pair(None, 0);
            var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
            ref readonly Pair ro = ref p0;
            ref Pair r2 = ref ChooseP(ref p, in ro);
            if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
            r2.V = flag ? 5 : 6;
            WriteLine(ro.V);
            WriteLine(p.V);
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """, "2\n5\n"];
        yield return ["y09-ref-param-walk-cond", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, None))));
            WriteLine(Count(ref a, true));
            WriteLine(a.IsSome);
            static int Count(ref Option<Node> head, bool flag)
            {
                int c = 0;
                ref Option<Node> cur = ref head;
                while (cur.AsMut() is Some<Node>(var k)) { c++; cur = ref k.Next; }
                WriteLine(head.IsSome);
                head = flag ? None : None;
                return c;
            }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\n2\nFalse\n"];
        yield return ["y10-span-param-cond", """
            Vec<int> v = [1, 2, 3];
            WriteLine(F(v.AsSpan(), true));
            static int F(Span<int> s, bool flag) { s[0] = flag ? 5 : 6; s[1] = s[0] + 1; return s[1]; }
            """, "6\n"];
        yield return ["y11-plain-ref-cond-write", """
            bool flag = true;
            Option<Node> a = Some(new Node(1, None));
            ref Option<Node> rx = ref a;
            rx = flag ? Some(new Node(2, None)) : None;
            ref Option<Node> r2 = ref rx;
            r2 = flag ? None : Some(new Node(3, None));
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "False\n"];
        yield return ["y12-replace-cond-arg-sound", """
            bool flag = true;
            Option<Node> a = Some(new Node(1, None));
            ref Option<Node> rx = ref a;
            Option<Node> old = rx.Replace(flag ? Some(new Node(2, None)) : None);
            WriteLine(old.IsSome);
            WriteLine(a.IsSome);
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\nTrue\n"];
        yield return ["y13-branch-rhs-sound-control-sibling", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2.V = f ? 5 : 6;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v) { public Option<Box> X = x; public int V = v; }
            public sealed class Box(Pair p) { public Pair P = p; }
            """, "2\n5\n"];
        yield return ["y14-branch-rhs-sound-control-no-repoint", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                r2 = f ? None : None;
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "False\nFalse\n"];
        yield return ["y15-sound-list-walk-branch-set", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref Option<Node> cur = ref a;
                while (cur.AsMut() is Some<Node>(var n))
                {
                    n.V = n.V > 1 ? n.V * 10 : n.V;
                    cur = ref n.Next;
                }
                cur = f ? Some(new Node(9, None)) : None;
                int sum = 0;
                ref readonly Option<Node> r = ref a;
                while (r.AsRef() is Some<Node>(var x)) { sum += x.V; r = ref x.Next; }
                WriteLine(sum);
            }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "60\n"];
        yield return ["y16-sound-ro-read-before-branch-write", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, None))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsRef() is Some<Node>(var k)) ro = ref k.Next;
                WriteLine(ro.IsSome);
                r2 = f ? None : None;
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\nFalse\n"];
        yield return ["y18-sound-ro-class-sibling-field-branch-rhs", """
            Run(true);
            static void Run(bool f)
            {
                Option<Node> a = None;
                Option<Node> b = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
                ref readonly Option<Node> ro = ref a;
                ref Option<Node> r2 = ref Choose(ref b, in ro);
                if (r2.AsMut() is Some<Node>(var k))
                {
                    if (k.Next.AsRef() is Some<Node>(var k2)) ro = ref k2.Next;
                    k.V = f ? 7 : 8;
                }
                WriteLine(ro.IsSome);
                WriteLine(b.IsSome);
            }
            [return: LifetimeOf("s", "r")]
            static ref Option<Node> Choose(ref Option<Node> s, in Option<Node> r) => ref s;
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "True\nTrue\n"];
        yield return ["y19-sound-span-param-branch-write", """
            Vec<int> v = [1, 2, 3];
            WriteLine(Run(v.AsSpan(), true));
            static int Run(Span<int> s, bool f)
            {
                s[0] = f ? 5 : 6;
                s[1] = s[0] + 1;
                return s[1];
            }
            """, "6\n"];
        yield return ["y20-sound-span-param-walk-branch", """
            Vec<int> v = [1, 2, 3, 4];
            WriteLine(Run(v.AsSpan(), true));
            static int Run(Span<int> s, bool f)
            {
                int t = 0;
                while (s.Length > 0)
                {
                    t += s[0];
                    s[0] = f ? 0 : 1;
                    s = s[1..];
                }
                return t;
            }
            """, "10\n"];
        yield return ["y21-sound-in-param-read-only-walk", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            WriteLine(Sum(in a));
            static int Sum(in Option<Node> head)
            {
                int s = 0;
                ref readonly Option<Node> cur = ref head;
                while (cur.AsRef() is Some<Node>(var n)) { s += n.V; cur = ref n.Next; }
                return s;
            }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "6\n"];
        yield return ["y22-sound-optionref-param-walk", """
            Option<Node> a = Some(new Node(1, Some(new Node(2, Some(new Node(3, None))))));
            WriteLine(Sum(a.AsRef()));
            static int Sum(OptionRef<Node> cur)
            {
                int s = 0;
                while (cur is Some<Node>(var n)) { s += n.V; cur = n.Next.AsRef(); }
                return s;
            }
            public sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }
            """, "6\n"];
        yield return ["y23-sound-rospan-param-rebind-walk", """
            Vec<int> v = [1, 2, 3, 4];
            WriteLine(Sum(v.AsReadOnlySpan()));
            static int Sum(ReadOnlySpan<int> s)
            {
                int t = 0;
                while (s.Length > 0) { t += s[0]; s = s[1..]; }
                return t;
            }
            """, "10\n"];
        yield return ["z27-vec-other-elem-after-ro-dead-sound", """
            Run(true);
            static void Run(bool f)
            {
                int a = 0;
                Vec<int> v = [10, 20, 30];
                ref readonly int ro = ref a;
                ref Vec<int> r2 = ref ChooseV(ref v, in ro);
                ro = ref r2[0];
                WriteLine(ro);
                r2[1] = f ? 5 : 6;
                WriteLine(v[1]);
            }
            [return: LifetimeOf("s", "r")]
            static ref Vec<int> ChooseV(ref Vec<int> s, in int r) => ref s;
            """, "10\n5\n"];
        yield return ["z28-pair-field-compound-sibling-sound", """
            Run(true);
            static void Run(bool f)
            {
                var p0 = new Pair(None, 0);
                var p = new Pair(Some(new Box(new Pair(None, 2))), 1);
                ref readonly Pair ro = ref p0;
                ref Pair r2 = ref ChooseP(ref p, in ro);
                if (r2.X.AsRef() is Some<Box>(var bx)) ro = ref bx.P;
                r2.V += f ? 5 : 6;
                WriteLine(ro.V);
                WriteLine(p.V);
            }
            [return: LifetimeOf("s", "r")]
            static ref Pair ChooseP(ref Pair s, in Pair r) => ref s;
            public struct Pair(Option<Box> x, int v)
            {
                public Option<Box> X = x;
                public int V = v;
            }
            public sealed class Box(Pair p) { public Pair P = p; }
            """, "2\n6\n"];
        yield return ["z35-vec-mut-elem-ref-dead-sound", """
            Vec<int> v = [1, 2, 3];
            ref int e = ref v[0];
            e = 9;
            WriteLine(v.Count + v[0]);
            """, "12\n"];
        yield return ["n01-span-arg-after-ro-dead", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                WriteLine(ro);
                Set0(s2, 9);
                WriteLine(s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void Set0(Span<int> t, int x) { t[0] = x; }
            """, "10\n9\n"];
        yield return ["n04-span-2arg-sound-param-ctrl", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            WriteLine(v[0]);
            static void Run(Span<int> s)
            {
                MutateSpan(s, 99);
                WriteLine(s[0]);
            }
            static void MutateSpan(Span<int> x, int val) { x[0] = val; }
            """, "99\n99\n"];
        yield return ["n06-span-param-reads-writes-2arg-ext", """
            Vec<int> v = [1, 2, 3, 4];
            WriteLine(Walk(v.AsSpan(), true));
            WriteLine(v[0]);
            static int Walk(Span<int> s, bool f)
            {
                int total = 0;
                MutateSpan(s, 10);
                s.MutExt(20);
                while (s.Length > 0)
                {
                    total += s[f ? 0 : 0];
                    s = s[1..];
                }
                return total;
            }
            static void MutateSpan(Span<int> x, int val) { x[0] += val; }
            public static class SpanExt
            {
                public static void MutExt(this Span<int> x, int val) { x[0] += val; }
            }
            """, "40\n31\n"];
        yield return ["n07-span-to-rospan-2arg-sound", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                int r = ReadSpan(s2, 1);
                WriteLine(r + ro);
            }
            static int ReadSpan(ReadOnlySpan<int> x, int k) => x[0] + k;
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            """, "21\n"];
        yield return ["j04-rospan-reborrow-read-sound", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan());
            static void Run(Span<int> s)
            {
                int a = 0;
                ref readonly int ro = ref a;
                ReadOnlySpan<int> r2 = ChooseR(s, in ro);
                ro = ref r2[0];
                int n = Sum(r2, 1);
                WriteLine(n + ro);
            }
            [return: LifetimeOf("s", "r")]
            static ReadOnlySpan<int> ChooseR(ReadOnlySpan<int> s, in int r) => s;
            static int Sum(ReadOnlySpan<int> t, int k) => t[0] + k;
            """, "21\n"];
        yield return ["j06-span-arg-after-ro-dead-out-sound", """
            Vec<int> v = [10, 20];
            Run(v.AsSpan(), true);
            static void Run(Span<int> s, bool f)
            {
                int a = 0;
                ref readonly int ro = ref a;
                Span<int> s2 = ChooseS(s, in ro);
                ro = ref s2[0];
                int r0 = ro;
                SetOut(s2, out int x);
                WriteLine(r0 + x + s2[0]);
            }
            [return: LifetimeOf("s", "r")]
            static Span<int> ChooseS(Span<int> s, in int r) => s;
            static void SetOut(Span<int> t, out int x) { t[0] = 9; x = 1; }
            """, "20\n"];
    }

    [Theory]
    [MemberData(nameof(AcceptedRows))]
    public async Task SoundRows_AcceptWithNativeParity(string shape, string source, string expected)
    {
        var result = await Round12RuleWitness.AcceptAsync(source, expected);
        Assert.True(result.Managed.Exit == 0, $"{shape}: managed exit {result.Managed.Exit}");
    }
}
