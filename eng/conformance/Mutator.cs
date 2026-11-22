using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>Generic tree paths and rewriting used by the mutator and the reducer.</summary>
internal static class TreeOps
{
    public static IEnumerable<(int[] Path, Node Node)> Walk(Node root)
    {
        var stack = new List<int>();
        return WalkRec(root, stack);
    }

    private static IEnumerable<(int[] Path, Node Node)> WalkRec(Node n, List<int> path)
    {
        yield return ([.. path], n);
        var kids = n.Kids;
        for (int i = 0; i < kids.Length; i++)
        {
            if (kids[i] is { } k)
            {
                path.Add(i);
                foreach (var x in WalkRec(k, path))
                {
                    yield return x;
                }
                path.RemoveAt(path.Count - 1);
            }
        }
    }

    /// <summary>Bottom-up rewrite; nodes are rebuilt only when a child changed.</summary>
    public static Node Rewrite(Node n, Func<Node, Node> f)
    {
        var kids = n.Kids;
        Node?[]? changed = null;
        for (int i = 0; i < kids.Length; i++)
        {
            if (kids[i] is { } k)
            {
                var nk = Rewrite(k, f);
                if (!ReferenceEquals(nk, k))
                {
                    changed ??= (Node?[])kids.Clone();
                    changed[i] = nk;
                }
            }
        }
        return f(changed is null ? n : n.Rebuild(changed));
    }

    public static Node At(Node root, int[] path)
    {
        var n = root;
        foreach (int i in path)
        {
            n = n.Kids[i]!;
        }
        return n;
    }

    /// <summary>Returns a new tree with the node at <paramref name="path"/> replaced (null deletes from a list parent).</summary>
    public static Node Replace(Node root, int[] path, Node? replacement, int depth = 0)
    {
        if (depth == path.Length)
        {
            return replacement ?? throw new InvalidOperationException("cannot delete the root");
        }
        var kids = root.Kids.ToArray();
        int i = path[depth];
        if (depth == path.Length - 1 && replacement is null && root.IsList)
        {
            var list = kids.ToList();
            list.RemoveAt(i);
            return root.Rebuild([.. list]);
        }
        kids[i] = depth == path.Length - 1 ? replacement : Replace(kids[i]!, path, replacement, depth + 1);
        return root.Rebuild(kids);
    }

    /// <summary>Inserts <paramref name="item"/> into the list node at <paramref name="listPath"/> at <paramref name="index"/>.</summary>
    public static Node Insert(Node root, int[] listPath, int index, Node item)
    {
        var list = At(root, listPath);
        var kids = list.Kids.ToList();
        kids.Insert(Math.Clamp(index, 0, kids.Count), item);
        return Replace(root, listPath, list.Rebuild([.. kids]));
    }
}

/// <summary>Near-miss mutations: each breaks (or probes) one C# or Vibranium rule.</summary>
internal static class Mutator
{
    private static readonly string[] Kinds =
    [
        "dup-name", "decl-later", "drop-init", "arg-mod", "embed-decl", "drop-terminator",
        "drop-default", "reverse-cases", "scope-leak", "const-to-var", "nonstatic-localfunc", "dup-expr-var",
    ];

    public static (ProgramNode, string?) Mutate(ProgramNode p, Rng r)
    {
        var order = Kinds.ToList();
        r.Shuffle(order);
        foreach (var k in order)
        {
            var res = Apply(p, k, r);
            if (res is not null)
            {
                return ((ProgramNode)res, k);
            }
        }
        return (p, null);
    }

    private static Node? Apply(ProgramNode p, string kind, Rng r)
    {
        var all = TreeOps.Walk(p).ToList();
        switch (kind)
        {
            case "dup-name":
            {
                var decls = all.Where(x => x.Node is LocalDeclStmt { Mods: "" or "const" } && x.Path.Length > 0 && TreeOps.At(p, x.Path[..^1]).IsList).ToList();
                if (decls.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(decls);
                var d = (LocalDeclStmt)node;
                var copy = new LocalDeclStmt(d.Type ?? Ty.Int, d.Name, d.Type is null || d.Init is null ? new Lit("1", Ty.Int) : d.Init.DeepClone() as Expr, d.Mods);
                int[] parent = path[..^1];
                int idx = path[^1];
                // candidate targets: same list later, a nested block/section list after the decl, a sibling switch section
                var targets = new List<(int[] List, int Index)> { (parent, idx + 1) };
                var parentNode = TreeOps.At(p, parent);
                foreach (var (q, n) in all)
                {
                    if (q.Length > parent.Length && q.Take(parent.Length).SequenceEqual(parent) && n is Block or SwitchSection && !q.SequenceEqual(path))
                    {
                        targets.Add((q, n is SwitchSection ? 1 : 0));
                    }
                }
                if (parentNode is SwitchSection && parent.Length > 0)
                {
                    var sw = TreeOps.At(p, parent[..^1]);
                    for (int i = 1; i < sw.Kids.Length; i++)
                    {
                        if (i != parent[^1])
                        {
                            targets.Add(([.. parent[..^1], i], 1));
                        }
                    }
                }
                var (lp, li) = r.Pick(targets);
                return TreeOps.Insert(p, lp, li, copy);
            }
            case "dup-expr-var":
            {
                // copy a section-level statement that declares an expression variable (out var / pattern)
                // into a sibling section: the switch block is one declaration space in C# (CS0128)
                var cands = all.Where(x => x.Node is ExprStmt or LocalDeclStmt or IfStmt && x.Path.Length >= 2 && TreeOps.At(p, x.Path[..^1]) is SwitchSection
                    && SwitchStmt.SectionLevelExprVars((Stmt)x.Node).Any()).ToList();
                if (cands.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(cands);
                int[] sw = path[..^2];
                var swNode = TreeOps.At(p, sw);
                var others = Enumerable.Range(1, swNode.Kids.Length - 1).Where(i => i != path[^2]).ToList();
                if (others.Count == 0)
                {
                    return null;
                }
                return TreeOps.Insert(p, [.. sw, r.Pick(others)], 1, node.DeepClone());
            }
            case "decl-later":
            {
                var decls = all.Where(x => x.Node is LocalDeclStmt && x.Path.Length > 0 && TreeOps.At(p, x.Path[..^1]) is Block or SwitchSection).ToList();
                if (decls.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(decls);
                var list = TreeOps.At(p, path[..^1]);
                int count = list.Kids.Length;
                if (path[^1] >= count - 2)
                {
                    return null;
                }
                var removed = TreeOps.Replace(p, path, null);
                return TreeOps.Insert(removed, path[..^1], count - 2, node);
            }
            case "drop-init":
            {
                var decls = all.Where(x => x.Node is LocalDeclStmt { Mods: "", Type: not null, Init: not null }).ToList();
                if (decls.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(decls);
                var d = (LocalDeclStmt)node;
                return TreeOps.Replace(p, path, new LocalDeclStmt(d.Type, d.Name, null, ""));
            }
            case "arg-mod":
            {
                var args = all.Where(x => x.Node is Arg a && (a.Mod.Length > 0 || a.E is NameExpr) && TreeOps.At(p, x.Path[..^1]) is CallExpr { Callee: not ("WriteLine" or "VibraniumRuntime.Panic") }).ToList();
                if (args.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(args);
                var a = (Arg)node;
                var mods = new[] { "", "ref", "in", "out" }.Where(m => m != a.Mod).ToArray();
                var e = a.E is OutDeclExpr od && od.Name != "_" ? new NameExpr(od.Name) { Type = od.Type } : a.E;
                return TreeOps.Replace(p, path, new Arg(r.Pick(mods), e));
            }
            case "embed-decl":
            {
                var cands = all.Where(x => x.Node is IfStmt { Then: Block { Items.Count: > 0 } b } && b.Items[0] is LocalDeclStmt
                    || x.Node is WhileStmt { Body: Block { Items.Count: > 0 } wb } && wb.Items[0] is LocalDeclStmt
                    || x.Node is ForStmt { Body: Block { Items.Count: > 0 } fb } && fb.Items[0] is LocalDeclStmt).ToList();
                if (cands.Count == 0)
                {
                    var ifs = all.Where(x => x.Node is IfStmt).ToList();
                    if (ifs.Count == 0)
                    {
                        return null;
                    }
                    var (ip, inode) = r.Pick(ifs);
                    var iff = (IfStmt)inode;
                    return TreeOps.Replace(p, ip, new IfStmt(iff.Cond, new LocalDeclStmt(Ty.Int, "emb", new Lit("1", Ty.Int), ""), iff.Else));
                }
                var (path, node) = r.Pick(cands);
                return node switch
                {
                    IfStmt i => TreeOps.Replace(p, path, new IfStmt(i.Cond, ((Block)i.Then).Items[0], i.Else)),
                    WhileStmt w => TreeOps.Replace(p, path, new WhileStmt(w.Cond, ((Block)w.Body).Items[0])),
                    ForStmt f => TreeOps.Replace(p, path, new ForStmt(f.Init, f.Cond, f.Iter, ((Block)f.Body).Items[0])),
                    _ => null,
                };
            }
            case "drop-terminator":
            {
                // a section whose statements are wrapped in one block (cut C18) ends with that block's terminator
                var secs = all.Where(x => x.Node is SwitchSection s && s.Stmts.Count > 0 && (s.Stmts[^1] is BreakStmt or ReturnStmt or ContinueStmt || s.Stmts[^1] is Block { Items.Count: > 0 } b && b.Items[^1] is BreakStmt or ReturnStmt or ContinueStmt)).ToList();
                if (secs.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(secs);
                var last = ((SwitchSection)node).Stmts[^1];
                return last is Block lb
                    ? TreeOps.Replace(p, [.. path, node.Kids.Length - 1, lb.Items.Count - 1], null)
                    : TreeOps.Replace(p, [.. path, node.Kids.Length - 1], null);
            }
            case "drop-default":
            {
                var secs = all.Where(x => x.Node is SwitchSection s && s.Labels.Items.Any(l => l.Pat is null) && TreeOps.At(p, x.Path[..^1]).Kids.Length > 2).ToList();
                var arms = all.Where(x => x.Node is SwitchArm { Pat: DiscardPat } && TreeOps.At(p, x.Path[..^1]).Kids.Length > 2).ToList();
                var both = secs.Concat(arms).ToList();
                if (both.Count == 0)
                {
                    return null;
                }
                var (path, _) = r.Pick(both);
                return TreeOps.Replace(p, path, null);
            }
            case "reverse-cases":
            {
                var sws = all.Where(x => x.Node is SwitchStmt { Sections.Count: > 1 } or SwitchExpr { Arms.Count: > 2 }).ToList();
                if (sws.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(sws);
                if (node is SwitchStmt s)
                {
                    var secs = s.Sections.ToList();
                    secs.Reverse();
                    return TreeOps.Replace(p, path, new SwitchStmt(s.Sel, secs));
                }
                var se = (SwitchExpr)node;
                var armList = se.Arms.Where(a => a.Pat is not DiscardPat).Reverse().Concat(se.Arms.Where(a => a.Pat is DiscardPat)).ToList();
                return TreeOps.Replace(p, path, new SwitchExpr(se.Sel, armList) { Type = se.Type });
            }
            case "scope-leak":
            {
                var holders = all.Where(x => x.Node is IfStmt or WhileStmt or ForStmt or ForeachStmt or Block or SwitchStmt && x.Path.Length > 0 && TreeOps.At(p, x.Path[..^1]) is Block).ToList();
                r.Shuffle(holders);
                foreach (var (path, node) in holders)
                {
                    var names = TreeOps.Walk(node).Select(x => x.Node switch
                    {
                        OutDeclExpr o when o.Name != "_" => o.Name,
                        VarPat v => v.Name,
                        DeclPat d when d.Name != "_" => d.Name,
                        ForeachStmt f => f.Name,
                        LocalDeclStmt l when l.Mods is "" && !ReferenceEquals(l, node) => l.Name,
                        _ => null,
                    }).Where(n => n is not null && n != "_" && n != "acc").Distinct().ToList();
                    if (names.Count == 0)
                    {
                        continue;
                    }
                    var use = new ExprStmt(new CallExpr(null, "WriteLine", [new Arg("", new NameExpr(r.Pick(names)!))]));
                    return TreeOps.Insert(p, path[..^1], path[^1] + 1, use);
                }
                return null;
            }
            case "const-to-var":
            {
                var consts = all.Where(x => x.Node is LocalDeclStmt { Mods: "const" }).ToList();
                if (consts.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(consts);
                var d = (LocalDeclStmt)node;
                return TreeOps.Replace(p, path, new LocalDeclStmt(d.Type, d.Name, d.Init, ""));
            }
            case "nonstatic-localfunc":
            {
                var fns = all.Where(x => x.Node is FuncDecl { IsLocal: true, Mods: "static" } && x.Path.Length > 2).ToList();
                if (fns.Count == 0)
                {
                    return null;
                }
                var (path, node) = r.Pick(fns);
                var f = (FuncDecl)node;
                return TreeOps.Replace(p, path, new FuncDecl("", f.Ret, f.Name, f.Params, f.Body, f.ExprBody, true));
            }
            default:
                return null;
        }
    }
}
