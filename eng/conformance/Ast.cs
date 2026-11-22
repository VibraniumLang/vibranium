using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>Type kinds the generator reasons about.</summary>
internal enum TK
{
    Int, Long, UInt, ULong, UShort, Short, Byte, SByte, Char, Bool, Float, Double, Nint,
    String, Enum, Struct, Class, Resource, Option, Result, Vec, ROSpan, Span, Void,
}

/// <summary>A generator-level type; <see cref="Cs"/> is its C# spelling.</summary>
internal sealed record Ty(TK Kind, string? Name = null, Ty? Arg = null, Ty? Arg2 = null)
{
    public static readonly Ty Int = new(TK.Int);
    public static readonly Ty Long = new(TK.Long);
    public static readonly Ty UInt = new(TK.UInt);
    public static readonly Ty ULong = new(TK.ULong);
    public static readonly Ty UShort = new(TK.UShort);
    public static readonly Ty Short = new(TK.Short);
    public static readonly Ty Byte = new(TK.Byte);
    public static readonly Ty SByte = new(TK.SByte);
    public static readonly Ty Char = new(TK.Char);
    public static readonly Ty Bool = new(TK.Bool);
    public static readonly Ty Float = new(TK.Float);
    public static readonly Ty Double = new(TK.Double);
    public static readonly Ty Nint = new(TK.Nint);
    public static readonly Ty String = new(TK.String);
    public static readonly Ty Void = new(TK.Void);
    public static readonly Ty OptInt = new(TK.Option, Arg: Int);
    public static readonly Ty ResIntInt = new(TK.Result, Arg: Int, Arg2: Int);
    public static readonly Ty VecInt = new(TK.Vec, Arg: Int);

    public static Ty ROSpan(Ty e) => new(TK.ROSpan, Arg: e);
    public static Ty Span(Ty e) => new(TK.Span, Arg: e);
    public static Ty Named(TK k, string name) => new(k, name);

    public string Cs => Kind switch
    {
        TK.Int => "int",
        TK.Long => "long",
        TK.UInt => "uint",
        TK.ULong => "ulong",
        TK.UShort => "ushort",
        TK.Short => "short",
        TK.Byte => "byte",
        TK.SByte => "sbyte",
        TK.Char => "char",
        TK.Bool => "bool",
        TK.Float => "float",
        TK.Double => "double",
        TK.Nint => "nint",
        TK.String => "string",
        TK.Void => "void",
        TK.Option => $"Option<{Arg!.Cs}>",
        TK.Result => $"Result<{Arg!.Cs}, {Arg2!.Cs}>",
        TK.Vec => $"Vec<{Arg!.Cs}>",
        TK.ROSpan => $"ReadOnlySpan<{Arg!.Cs}>",
        TK.Span => $"Span<{Arg!.Cs}>",
        _ => Name ?? "?",
    };

    /// <summary>Integral types whose arithmetic promotes to int.</summary>
    public bool PromotesToInt => Kind is TK.Byte or TK.SByte or TK.Short or TK.UShort or TK.Char;
    public bool IsIntegral => Kind is TK.Int or TK.Long or TK.UInt or TK.ULong or TK.UShort or TK.Short or TK.Byte or TK.SByte or TK.Char or TK.Nint;
    public bool IsFloating => Kind is TK.Float or TK.Double;
    public bool IsNumeric => IsIntegral || IsFloating;
    public bool IsUnsigned => Kind is TK.UInt or TK.ULong or TK.UShort or TK.Byte or TK.Char;
    /// <summary>Types WriteLine accepts directly (SPEC §3.5 Prelude/Io row).</summary>
    public bool IsPrintable => Kind is TK.Int or TK.Long or TK.UInt or TK.ULong or TK.UShort or TK.Short or TK.Byte or TK.SByte or TK.Bool or TK.Nint;

    /// <summary>Result type of a binary arithmetic operator on two operands of this type.</summary>
    public Ty ArithResult => PromotesToInt ? Int : this;

    public override string ToString() => Cs;
}

/// <summary>Context handed to tag and print visitors: the ancestor chain.</summary>
internal sealed class NodeCtx(IReadOnlyList<Node> ancestors)
{
    public IReadOnlyList<Node> Ancestors { get; } = ancestors;
    public Node? Parent => Ancestors.Count > 0 ? Ancestors[^1] : null;
    public bool Inside<T>() where T : Node => Ancestors.Any(a => a is T);

    /// <summary>True when the nearest enclosing function-like node is a static local function.</summary>
    public bool InStaticLocalFunc
    {
        get
        {
            for (int i = Ancestors.Count - 1; i >= 0; i--)
            {
                if (Ancestors[i] is FuncDecl f)
                {
                    return f.IsLocal;
                }
            }
            return false;
        }
    }

    public bool InClassMember => Ancestors.Any(a => a is ClassDecl or StructDecl);
}

/// <summary>Base AST node. Children are exposed generically so the reducer can rewrite any tree.</summary>
internal abstract class Node
{
    /// <summary>Ordered children; null entries are absent optional children.</summary>
    public abstract Node?[] Kids { get; }

    /// <summary>Builds a copy of this node with the given children (same arity unless <see cref="IsList"/>).</summary>
    public abstract Node Rebuild(Node?[] kids);

    /// <summary>List nodes allow deleting children down to <see cref="MinKids"/>.</summary>
    public virtual bool IsList => false;
    public virtual int MinKids => 0;

    /// <summary>Indexes of children that may be set to null by the reducer.</summary>
    public virtual IEnumerable<int> OptionalKids => [];

    /// <summary>Simpler replacement candidates for this node (reducer).</summary>
    public virtual IEnumerable<Node> Simplify() => [];

    /// <summary>Feature tags contributed by this node (see Features.cs).</summary>
    public virtual IEnumerable<string> Tags(NodeCtx ctx) => [];

    public Node DeepClone() => Rebuild(Kids.Select(k => k?.DeepClone()).ToArray());
}

internal abstract class Expr : Node
{
    public Ty? Type { get; set; }

    public T Typed<T>(Ty? t) where T : Expr
    {
        Type = t;
        return (T)this;
    }

    public override IEnumerable<Node> Simplify()
    {
        if (Type is not null && Lits.Default(Type) is { } lit && !(this is Lit l && l.Text == lit.Text))
        {
            yield return lit;
        }
        foreach (var k in Kids)
        {
            if (k is Expr e && e.Type is not null && Type is not null && e.Type == Type)
            {
                yield return e;
            }
        }
    }
}

internal abstract class Stmt : Node;

internal abstract class Pat : Node;

internal static class Lits
{
    public static Lit? Default(Ty t) => t.Kind switch
    {
        TK.Int => new Lit("0", t),
        TK.Long => new Lit("0L", t),
        TK.UInt => new Lit("0u", t),
        TK.ULong => new Lit("0UL", t),
        TK.Bool => new Lit("false", t),
        TK.Double => new Lit("0.0", t),
        TK.Float => new Lit("0f", t),
        TK.Char => new Lit("'a'", t),
        TK.UShort => new Lit("(ushort)0", t),
        TK.Byte => new Lit("(byte)0", t),
        TK.Short => new Lit("(short)0", t),
        TK.SByte => new Lit("(sbyte)0", t),
        _ => null,
    };
}

// ───────────────────────────── Program & declarations ─────────────────────────────

internal sealed class ProgramNode(Block top, TypeList types) : Node
{
    public Block Top { get; } = top;
    public TypeList Types { get; } = types;
    public override Node?[] Kids => [Top, Types];
    public override Node Rebuild(Node?[] k) => new ProgramNode((Block)k[0]!, (TypeList)k[1]!);
}

internal sealed class TypeList(List<Node> items) : Node
{
    public List<Node> Items { get; } = items;
    public override Node?[] Kids => [.. Items];
    public override Node Rebuild(Node?[] k) => new TypeList([.. k.Where(x => x is not null).Select(x => x!)]);
    public override bool IsList => true;
}

internal sealed class EnumDecl(string name, List<(string Name, int Value)> members) : Node
{
    public string Name { get; } = name;
    public List<(string Name, int Value)> Members { get; } = members;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new EnumDecl(Name, [.. Members]);
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["type.enum"];
}

/// <summary>A class/struct field, const, or raw member line.</summary>
internal sealed class FieldDecl(Ty type, string name, Expr? init, string mods) : Node
{
    public Ty Type { get; } = type;
    public string Name { get; } = name;
    public Expr? Init { get; } = init;
    public string Mods { get; } = mods;
    public bool IsConst => Mods.Contains("const", StringComparison.Ordinal);
    public override Node?[] Kids => [Init];
    public override Node Rebuild(Node?[] k) => new FieldDecl(Type, Name, (Expr?)k[0], Mods);

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (IsConst)
        {
            yield return "const.class";
        }
        else if (Mods.Contains("static", StringComparison.Ordinal))
        {
            yield return "banned.static-field";
        }
        else
        {
            yield return "type.field";
        }
        if (Name == "_")
        {
            yield return "field.underscore";
        }
        if (Type.Kind is TK.Resource && !IsConst)
        {
            yield return "banned.linear-field";
        }
    }
}

internal sealed class StructDecl(string name, string mods, string? bases, List<Node> members) : Node
{
    public string Name { get; } = name;
    public string Mods { get; } = mods;
    public string? Bases { get; } = bases;
    public List<Node> Members { get; } = members;
    public override Node?[] Kids => [.. Members];
    /// <summary>Primary constructor parameter list text, if any.</summary>
    public string? PrimaryCtor { get; init; }
    public override Node Rebuild(Node?[] k) => new StructDecl(Name, Mods, Bases, [.. k.Where(x => x is not null).Select(x => x!)]) { PrimaryCtor = PrimaryCtor };
    public override bool IsList => true;

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return "type.struct";
        if (Members.OfType<FieldDecl>().Any(f => f.Type.Kind is TK.Vec or TK.Class))
        {
            yield return "struct.owned-field";
        }
        if (Bases?.Contains("IDisposable", StringComparison.Ordinal) == true)
        {
            yield return "banned.struct-idisposable";
        }
    }
}

internal sealed class ClassDecl(string name, string mods, string? primaryCtor, string? bases, List<Node> members) : Node
{
    public string Name { get; } = name;
    public string Mods { get; } = mods;
    public string? PrimaryCtor { get; } = primaryCtor;
    public string? Bases { get; } = bases;
    public List<Node> Members { get; } = members;
    public override Node?[] Kids => [.. Members];
    public override Node Rebuild(Node?[] k) => new ClassDecl(Name, Mods, PrimaryCtor, Bases, [.. k.Where(x => x is not null).Select(x => x!)]);
    public override bool IsList => true;

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return Bases?.Contains("IDisposable", StringComparison.Ordinal) == true ? "type.resource" : "type.class";
        if (PrimaryCtor is not null)
        {
            yield return "class.primary-ctor";
        }
        if (Bases is not null && !Bases.Contains("IDisposable", StringComparison.Ordinal))
        {
            yield return "banned.inheritance";
        }
    }
}

internal sealed class ParamDecl(Ty type, string name, string mod) : Node
{
    public Ty Type { get; } = type;
    public string Name { get; } = name;
    /// <summary>"", "ref", "out", "in", "ref readonly", "scoped" ...</summary>
    public string Mod { get; } = mod;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new ParamDecl(Type, Name, Mod);

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (Mod.Length > 0)
        {
            yield return "param." + Mod.Replace(' ', '-');
        }
        if (Mod == "out" && Type.Kind == TK.Struct)
        {
            yield return Type.Name is "SO" or "SNO" ? "cut.struct-piecewise-owned" : "struct.out-fieldwise";
        }
        if (Name == "_")
        {
            yield return "param.underscore";
        }
    }
}

internal sealed class ParamList(List<ParamDecl> items) : Node
{
    public List<ParamDecl> Items { get; } = items;
    public override Node?[] Kids => [.. Items];
    public override Node Rebuild(Node?[] k) => new ParamList([.. k.OfType<ParamDecl>()]);
}

/// <summary>A method (in a type) or a local function (statement) declaration.</summary>
internal sealed class FuncDecl(string mods, Ty ret, string name, ParamList ps, Block? body, Expr? exprBody, bool isLocal) : Node
{
    public string Mods { get; } = mods;
    public Ty Ret { get; } = ret;
    public string Name { get; } = name;
    public ParamList Params { get; } = ps;
    public Block? Body { get; } = body;
    public Expr? ExprBody { get; } = exprBody;
    public bool IsLocal { get; } = isLocal;
    public bool IsStatic => Mods.Contains("static", StringComparison.Ordinal);
    public override Node?[] Kids => [Params, Body, ExprBody];
    public override Node Rebuild(Node?[] k) => new FuncDecl(Mods, Ret, Name, (ParamList)k[0]!, (Block?)k[1], (Expr?)k[2], IsLocal);

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (IsLocal)
        {
            yield return IsStatic ? "localfunc.static" : "banned.nonstatic-localfunc";
            if (ctx.Ancestors.OfType<FuncDecl>().Any())
            {
                yield return ctx.InClassMember ? "localfunc.in-class-method" : "localfunc.nested";
            }
        }
        else
        {
            yield return IsStatic ? "method.static" : "method.instance";
        }
        if (ExprBody is not null)
        {
            yield return "func.expr-body";
        }
        if (Ret.Kind is TK.ROSpan or TK.Span)
        {
            yield return "func.returns-span";
        }
    }
}

// ───────────────────────────── Statements ─────────────────────────────

internal sealed class Block(List<Stmt> items) : Stmt
{
    public List<Stmt> Items { get; } = items;
    public override Node?[] Kids => [.. Items];
    public override Node Rebuild(Node?[] k) => new Block([.. k.OfType<Stmt>()]);
    public override bool IsList => true;

    public override IEnumerable<Node> Simplify()
    {
        if (Items.Count == 1 && Items[0] is not LocalDeclStmt and not LocalFuncStmt)
        {
            yield return Items[0];
        }
    }

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (ctx.Parent is Block or SwitchSection)
        {
            yield return "stmt.nested-block";
        }
        // a collection expression returned as a span on a path that is not after an unconditional Panic:
        // banned by design (SPEC §3.7 storage contract, VIB007)
        foreach (var s in Items)
        {
            if (s is ExprStmt { E: CallExpr { Callee: "VibraniumRuntime.Panic" } })
            {
                yield break;
            }
            if (s is ReturnStmt { E: CollExpr { Type.Kind: TK.ROSpan or TK.Span } })
            {
                yield return "banned.collection-return-span";
                yield break;
            }
        }
    }
}

internal sealed class LocalDeclStmt(Ty? type, string name, Expr? init, string mods) : Stmt
{
    /// <summary>Null type means <c>var</c>.</summary>
    public Ty? Type { get; } = type;
    public string Name { get; } = name;
    public Expr? Init { get; } = init;
    /// <summary>"", "const", "using", "ref", "ref readonly", "scoped".</summary>
    public string Mods { get; } = mods;
    public override Node?[] Kids => [Init];
    public override Node Rebuild(Node?[] k) => new LocalDeclStmt(Type, Name, (Expr?)k[0], Mods);
    public override IEnumerable<int> OptionalKids => Mods is "" && Type is not null ? [0] : [];

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return Mods switch
        {
            "const" => "const.local",
            "using" => "using.var",
            "ref" or "ref readonly" => "local.ref",
            _ => "local.decl",
        };
        if (Type is null)
        {
            yield return "local.var";
        }
        if (Name == "_")
        {
            yield return "local.underscore";
        }
        if (Init is null)
        {
            yield return "local.uninit";
        }
        if (ctx.Parent is SwitchSection)
        {
            yield return "switch.section-local";
        }
        if (ctx.Parent is IfStmt or WhileStmt or ForeachStmt or DoStmt || ctx.Parent is ForStmt f && !ReferenceEquals(f.Init, this))
        {
            yield return "mut.embedded-decl";
        }
        if (Type is { Kind: TK.Struct })
        {
            yield return "struct.local";
            if (Init is null && Mods == "")
            {
                yield return Type.Name is "SO" or "SNO" ? "cut.struct-piecewise-owned" : "struct.fieldwise-init";
            }
        }
        if (Mods == "" && Init is CollExpr or CondExpr { A: CollExpr or ParenExpr { E: CollExpr } })
        {
            yield return "coll.local-init";
        }
    }
}

internal sealed class ExprStmt(Expr e) : Stmt
{
    public Expr E { get; } = e;
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new ExprStmt((Expr)k[0]!);
}

internal sealed class AssignStmt(Expr target, string op, Expr value) : Stmt
{
    public Expr Target { get; } = target;
    public string Op { get; } = op;
    public Expr Value { get; } = value;
    public override Node?[] Kids => [Target, Value];
    public override Node Rebuild(Node?[] k) => new AssignStmt((Expr)k[0]!, Op, (Expr)k[1]!);

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return Op == "=" ? "assign.simple" : "assign.compound";
        if (Op.Length == 2 && Op[1] == '=' && Op[0] is '%' or '/' or '+' or '-' or '*')
        {
            var t = Target.Type;
            string cls = t is null ? "int" : t.IsFloating ? (t.Kind == TK.Float ? "float" : "double") : t.Kind is TK.Long or TK.ULong ? "long" : t.IsUnsigned ? "unsigned" : "int";
            yield return Op[0] switch
            {
                '%' => "arith.rem-" + cls,
                '/' => "arith.div-" + cls,
                _ => "arith." + cls,
            };
        }
        if (Target is NameExpr { Name: "_" })
        {
            yield return "assign.underscore";
        }
        if (Target is MemberExpr)
        {
            yield return "assign.field";
        }
        if (Target is IndexExpr)
        {
            yield return "assign.index";
        }
    }
}

internal sealed class IfStmt(Expr cond, Stmt then, Stmt? els) : Stmt
{
    public Expr Cond { get; } = cond;
    public Stmt Then { get; } = then;
    public Stmt? Else { get; } = els;
    public override Node?[] Kids => [Cond, Then, Else];
    public override Node Rebuild(Node?[] k) => new IfStmt((Expr)k[0]!, (Stmt)k[1]!, (Stmt?)k[2]);
    public override IEnumerable<int> OptionalKids => [2];

    public override IEnumerable<Node> Simplify()
    {
        yield return Then;
        if (Else is not null)
        {
            yield return Else;
        }
    }

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return Else is null ? "stmt.if" : "stmt.if-else";
    }
}

internal sealed class WhileStmt(Expr cond, Stmt body) : Stmt
{
    public Expr Cond { get; } = cond;
    public Stmt Body { get; } = body;
    public override Node?[] Kids => [Cond, Body];
    public override Node Rebuild(Node?[] k) => new WhileStmt((Expr)k[0]!, (Stmt)k[1]!);
    public override IEnumerable<Node> Simplify() => [Body];
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["loop.while"];
}

internal sealed class DoStmt(Stmt body, Expr cond) : Stmt
{
    public Stmt Body { get; } = body;
    public Expr Cond { get; } = cond;
    public override Node?[] Kids => [Body, Cond];
    public override Node Rebuild(Node?[] k) => new DoStmt((Stmt)k[0]!, (Expr)k[1]!);
    public override IEnumerable<Node> Simplify() => [Body];
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["loop.do"];
}

internal sealed class ForStmt(LocalDeclStmt? init, Expr? cond, AssignStmt? iter, Stmt body) : Stmt
{
    public LocalDeclStmt? Init { get; } = init;
    public Expr? Cond { get; } = cond;
    public AssignStmt? Iter { get; } = iter;
    public Stmt Body { get; } = body;
    public override Node?[] Kids => [Init, Cond, Iter, Body];
    public override Node Rebuild(Node?[] k) => new ForStmt((LocalDeclStmt?)k[0], (Expr?)k[1], (AssignStmt?)k[2], (Stmt)k[3]!);
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["loop.for"];
}

internal sealed class ForeachStmt(bool isRef, Ty? type, string name, Expr coll, Stmt body) : Stmt
{
    public bool IsRef { get; } = isRef;
    public Ty? Type { get; } = type;
    public string Name { get; } = name;
    public Expr Coll { get; } = coll;
    public Stmt Body { get; } = body;
    public override Node?[] Kids => [Coll, Body];
    public override Node Rebuild(Node?[] k) => new ForeachStmt(IsRef, Type, Name, (Expr)k[0]!, (Stmt)k[1]!);

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return IsRef ? "loop.foreach-ref" : "loop.foreach";
        if (Coll.Type?.Kind is TK.Vec)
        {
            yield return "loop.foreach-vec";
        }
        if (Coll is CollExpr)
        {
            yield return "coll.foreach-literal";
        }
    }
}

internal sealed class SwitchStmt(Expr sel, List<SwitchSection> sections) : Stmt
{
    public Expr Sel { get; } = sel;
    public List<SwitchSection> Sections { get; } = sections;
    public override Node?[] Kids => [Sel, .. Sections];
    public override Node Rebuild(Node?[] k) => new SwitchStmt((Expr)k[0]!, [.. k.Skip(1).OfType<SwitchSection>()]);
    public override bool IsList => true;
    public override int MinKids => 1;

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return "switch.stmt";
        yield return "switch.sel." + SelTag(Sel.Type);
        // switch-block declaration space: variables declared directly in a section, seen by later sections
        var declared = new List<HashSet<string>>();
        foreach (var sec in Sections)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var st in sec.Stmts)
            {
                if (st is LocalDeclStmt { Mods: "" or "const" } ld)
                {
                    names.Add(ld.Name);
                }
                foreach (var n in SectionLevelExprVars(st))
                {
                    if (!names.Add(n) || declared.Any(d => d.Contains(n)))
                    {
                        yield return "switch.cross-section-dup";
                    }
                }
            }
            if (declared.Any(d => d.Overlaps(names)))
            {
                yield return "switch.cross-section-dup";
            }
            var used = sec.Stmts.SelectMany(st => TreeOpsLite.Names(st)).ToHashSet(StringComparer.Ordinal);
            if (declared.Any(d => d.Overlaps(used)))
            {
                yield return "switch.cross-section-var";
            }
            declared.Add(names);
        }
        if (!Sections.Any(s => s.Labels.Items.Any(l => l.Pat is null)))
        {
            yield return "switch.no-default";
        }
        if (ctx.Inside<SwitchSection>())
        {
            yield return "switch.nested";
        }
    }

    /// <summary>Expression variables a statement directly in a switch section declares into the switch block.</summary>
    public static IEnumerable<string> SectionLevelExprVars(Stmt st)
    {
        Node? scan = st switch
        {
            ExprStmt e => e.E,
            LocalDeclStmt l => l.Init,
            AssignStmt a => a.Value,
            IfStmt i => i.Cond,
            ReturnStmt r => r.E,
            _ => null,
        };
        if (scan is null)
        {
            yield break;
        }
        foreach (var n in TreeOpsLite.Nodes(scan))
        {
            switch (n)
            {
                case OutDeclExpr { Name: not "_" } o:
                    yield return o.Name;
                    break;
                case VarPat v:
                    yield return v.Name;
                    break;
                case DeclPat { Name: not "_" } d:
                    yield return d.Name;
                    break;
            }
        }
    }

    public static string SelTag(Ty? t) => t?.Kind switch
    {
        null => "unknown",
        TK.Option => "option",
        TK.Result => "result",
        TK.Enum => "enum",
        TK.Bool => "bool",
        TK.Char => "char",
        TK.UShort or TK.Byte or TK.UInt or TK.ULong => "unsigned",
        TK.Float or TK.Double => "float",
        _ => "int",
    };
}

internal sealed class SwitchSection(LabelList labels, List<Stmt> stmts) : Node
{
    public LabelList Labels { get; } = labels;
    public List<Stmt> Stmts { get; } = stmts;
    public override Node?[] Kids => [Labels, .. Stmts];
    public override Node Rebuild(Node?[] k) => new SwitchSection((LabelList)k[0]!, [.. k.Skip(1).OfType<Stmt>()]);
    public override bool IsList => true;
    public override int MinKids => 1;

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        static bool Terminates(List<Stmt> l) => l.Count > 0 && (l[^1] is BreakStmt or ReturnStmt or ContinueStmt || l[^1] is Block b && Terminates(b.Items));
        if (!Terminates(Stmts))
        {
            yield return "switch.section-no-terminator";
        }
        if (Stmts.OfType<LocalFuncStmt>().Any())
        {
            yield return "cut.localfunc-in-switch-section";
        }
    }
}

internal sealed class LabelList(List<CaseLabel> items) : Node
{
    public List<CaseLabel> Items { get; } = items;
    public override Node?[] Kids => [.. Items];
    public override Node Rebuild(Node?[] k) => new LabelList([.. k.OfType<CaseLabel>()]);
    public override bool IsList => true;
    public override int MinKids => 1;
}

/// <summary><c>case pat when guard:</c>; <see cref="Pat"/> null means <c>default:</c>.</summary>
internal sealed class CaseLabel(Pat? pat, Expr? guard) : Node
{
    public Pat? Pat { get; } = pat;
    public Expr? Guard { get; } = guard;
    public override Node?[] Kids => [Pat, Guard];
    public override Node Rebuild(Node?[] k) => new CaseLabel((Pat?)k[0], (Expr?)k[1]);
    public override IEnumerable<int> OptionalKids => [1];

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (Pat is null)
        {
            yield return "switch.default";
        }
        foreach (var t in GuardTags(Guard))
        {
            yield return t;
        }
    }

    public static IEnumerable<string> GuardTags(Expr? guard) => guard switch
    {
        null => [],
        Lit { Text: "true" } => ["switch.guard-true"],
        Lit { Text: "false" } => ["switch.guard-false"],
        Expr g when IsLiteralConstant(g) => ["switch.guard-const"],
        _ => ["switch.guard"],
    };

    /// <summary>An expression built only from literals and operators: C# folds it to a constant (for example `0.0 >= 0.0`).</summary>
    private static bool IsLiteralConstant(Expr e) => e switch
    {
        Lit => true,
        ParenExpr p => IsLiteralConstant(p.E),
        UnExpr u => IsLiteralConstant(u.E),
        CastExpr c => IsLiteralConstant(c.E),
        BinExpr b => IsLiteralConstant(b.L) && IsLiteralConstant(b.R),
        _ => false,
    };
}

internal sealed class ReturnStmt(Expr? e) : Stmt
{
    public Expr? E { get; } = e;
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new ReturnStmt((Expr?)k[0]);
}

internal sealed class BreakStmt : Stmt
{
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new BreakStmt();
}

internal sealed class ContinueStmt : Stmt
{
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new ContinueStmt();
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["loop.continue"];
}

internal sealed class LocalFuncStmt(FuncDecl func) : Stmt
{
    public FuncDecl Func { get; } = func;
    public override Node?[] Kids => [Func];
    public override Node Rebuild(Node?[] k) => new LocalFuncStmt((FuncDecl)k[0]!);
}

internal sealed class UsingBlockStmt(string name, Expr init, Block body) : Stmt
{
    public string Name { get; } = name;
    public Expr Init { get; } = init;
    public Block Body { get; } = body;
    public override Node?[] Kids => [Init, Body];
    public override Node Rebuild(Node?[] k) => new UsingBlockStmt(Name, (Expr)k[0]!, (Block)k[1]!);
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["using.block"];
}

/// <summary>Verbatim statement (banned-by-design constructs); tags are explicit.</summary>
internal sealed class RawStmt(string text, string[] tags) : Stmt
{
    public string Text { get; } = text;
    public string[] TagList { get; } = tags;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new RawStmt(Text, TagList);
    public override IEnumerable<string> Tags(NodeCtx ctx) => TagList;
}

// ───────────────────────────── Expressions ─────────────────────────────

internal sealed class Lit(string text) : Expr
{
    public Lit(string text, Ty? type)
        : this(text) => Type = type;

    public string Text { get; } = text;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new Lit(Text, Type);

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (Text.StartsWith('"') && Text.EndsWith("u8", StringComparison.Ordinal))
        {
            yield return "expr.u8-literal";
        }
    }
}

internal sealed class NameExpr(string name) : Expr
{
    public string Name { get; } = name;
    /// <summary>Refers to a C# constant (const local/field, enum member): such a name keeps a condition constant (C19).</summary>
    public bool IsConst { get; init; }
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new NameExpr(Name) { Type = Type, IsConst = IsConst };

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (Name == "_" && ctx.Parent is not AssignStmt { Target: NameExpr { Name: "_" } })
        {
            yield return "local.underscore-read";
        }
    }
}

internal sealed class BinExpr(string op, Expr l, Expr r) : Expr
{
    public string Op { get; } = op;
    public Expr L { get; } = l;
    public Expr R { get; } = r;
    public override Node?[] Kids => [L, R];
    public override Node Rebuild(Node?[] k) => new BinExpr(Op, (Expr)k[0]!, (Expr)k[1]!) { Type = Type };

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        var t = L.Type;
        string cls = t is null ? "int" : t.IsFloating ? (t.Kind == TK.Float ? "float" : "double") : t.Kind is TK.Long or TK.ULong ? "long" : t.IsUnsigned ? "unsigned" : "int";
        switch (Op)
        {
            case "%":
                yield return "arith.rem-" + cls;
                break;
            case "/":
                yield return "arith.div-" + cls;
                break;
            case "<<" or ">>":
                yield return "arith.shift";
                break;
            case "+" or "-" or "*":
                yield return "arith." + cls;
                break;
            case "&&" or "||":
                yield return "expr.logical";
                break;
            case "==" or "!=" or "<" or ">" or "<=" or ">=":
                yield return "expr.compare";
                break;
            default:
                yield return "arith.bitwise";
                break;
        }
    }
}

internal sealed class UnExpr(string op, Expr e) : Expr
{
    public string Op { get; } = op;
    public Expr E { get; } = e;
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new UnExpr(Op, (Expr)k[0]!) { Type = Type };
}

internal sealed class CondExpr(Expr c, Expr a, Expr b, bool compact) : Expr
{
    public Expr C { get; } = c;
    public Expr A { get; } = a;
    public Expr B { get; } = b;
    public bool Compact { get; } = compact;
    public override Node?[] Kids => [C, A, B];
    public override Node Rebuild(Node?[] k) => new CondExpr((Expr)k[0]!, (Expr)k[1]!, (Expr)k[2]!, Compact) { Type = Type };

    public override IEnumerable<Node> Simplify()
    {
        foreach (var s in base.Simplify())
        {
            yield return s;
        }
        yield return A;
        yield return B;
    }

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return "expr.conditional";
        static bool IsColl(Expr e) => e is CollExpr or ParenExpr { E: CollExpr };
        static bool IsCollLocal(Expr e) => e is NameExpr { Type.Kind: TK.ROSpan or TK.Span };
        if (IsCollLocal(A) && IsCollLocal(B))
        {
            yield return "coll.conditional-locals";
        }
        if (IsColl(A) || IsColl(B))
        {
            yield return "coll.conditional";
            if (A is CollExpr || B is CollExpr)
            {
                yield return "coll.conditional-unparenthesized";
            }
        }
        if (ctx.Parent is Arg)
        {
            yield return "expr.conditional-arg";
        }
    }
}

internal sealed class CastExpr(Ty to, Expr e) : Expr
{
    public Ty To { get; } = to;
    public Expr E { get; } = e;
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new CastExpr(To, (Expr)k[0]!) { Type = Type };

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (E.Type is { IsFloating: true } && To.IsIntegral)
        {
            yield return "arith.float-to-int-cast";
        }
        else
        {
            yield return "expr.cast";
        }
    }
}

internal sealed class ParenExpr(Expr e) : Expr
{
    public Expr E { get; } = e;
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new ParenExpr((Expr)k[0]!) { Type = Type };
    public override IEnumerable<Node> Simplify() => [E];
}

internal sealed class Arg(string mod, Expr e) : Node
{
    /// <summary>"", "ref", "out", "in".</summary>
    public string Mod { get; } = mod;
    public Expr E { get; } = e;

    /// <summary>The corresponding parameter is <c>in</c> or <c>ref readonly</c>.</summary>
    public bool ToReadonlyRefParam { get; init; }
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new Arg(Mod, (Expr)k[0]!) { ToReadonlyRefParam = ToReadonlyRefParam };

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (Mod.Length > 0)
        {
            yield return "arg." + Mod;
        }
        if (Mod == "out")
        {
            yield return E switch
            {
                OutDeclExpr { Name: "_", DeclType: null } => "out.var-discard",
                OutDeclExpr { Name: "_" } => "out.typed-discard",
                OutDeclExpr { DeclType: null } => "out.var",
                OutDeclExpr => "out.typed",
                NameExpr { Name: "_" } => "out.underscore",
                NameExpr => "out.existing-local",
                MemberExpr => "out.field",
                _ => "out.other",
            };
        }
        if (Mod is "" or "in" && E is Lit or BinExpr && ToReadonlyRefParam)
        {
            yield return "arg.rvalue-to-ref-param";
        }
        if (E is CollExpr)
        {
            yield return "coll.arg";
        }
    }
}

internal sealed class OutDeclExpr(Ty? declType, string name) : Expr
{
    /// <summary>Null means <c>var</c>.</summary>
    public Ty? DeclType { get; } = declType;
    public string Name { get; } = name;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new OutDeclExpr(DeclType, Name) { Type = Type };
    public override IEnumerable<Node> Simplify() => [];
}

internal sealed class CallExpr(Expr? receiver, string callee, List<Arg> args) : Expr
{
    public Expr? Receiver { get; } = receiver;
    public string Callee { get; } = callee;
    public List<Arg> Args { get; } = args;
    /// <summary>Generator hint: the callee has a ref readonly / in parameter.</summary>
    public bool RefReadonlyArgs { get; init; }
    public override Node?[] Kids => [Receiver, .. Args];
    public override Node Rebuild(Node?[] k) => new CallExpr((Expr?)k[0], Callee, [.. k.Skip(1).OfType<Arg>()]) { Type = Type, RefReadonlyArgs = RefReadonlyArgs };

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        switch (Callee)
        {
            case "VibraniumRuntime.Panic":
                yield return "panic.call";
                break;
            case "int.TryParse" or "long.TryParse":
                yield return "bcl.tryparse";
                break;
            case "Unwrap" or "Expect" or "UnwrapOr" or "IsSome":
                yield return "union.consume";
                break;
            case "IsNone" or "IsErr":
                yield return "union.guard";
                break;
            case "Some" or "None" or "Ok" or "Err":
                yield return "union.ctor";
                break;
            case "Dispose":
                yield return "banned.manual-dispose";
                break;
            default:
                break;
        }
    }
}

internal sealed class MemberExpr(Expr e, string name) : Expr
{
    public Expr E { get; } = e;
    public string Name { get; } = name;
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new MemberExpr((Expr)k[0]!, Name) { Type = Type };

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (E is NameExpr n && n.Type is null && char.IsUpper(n.Name[0]))
        {
            yield return "expr.qualified-member";
        }
    }
}

internal sealed class IndexExpr(Expr e, Expr index) : Expr
{
    public Expr E { get; } = e;
    public Expr Index { get; } = index;
    public override Node?[] Kids => [E, Index];
    public override Node Rebuild(Node?[] k) => new IndexExpr((Expr)k[0]!, (Expr)k[1]!) { Type = Type };
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["expr.index"];
}

internal sealed class CollExpr(List<Expr> elems) : Expr
{
    public List<Expr> Elems { get; } = elems;
    public override Node?[] Kids => [.. Elems];
    public override Node Rebuild(Node?[] k) => new CollExpr([.. k.OfType<Expr>()]) { Type = Type };
    public override bool IsList => true;

    public override IEnumerable<Node> Simplify() => [];

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return Type?.Kind switch
        {
            TK.Vec => "coll.vec",
            TK.Span => "coll.span",
            _ => "coll.rospan",
        };
        if (Type?.Arg is { } el && el.Kind != TK.Int)
        {
            yield return "coll.elem-" + (el.Kind == TK.Enum ? "enum" : el.Cs);
        }
        if (Elems.Count == 0)
        {
            yield return "coll.empty";
        }
        if (ctx.Parent is ReturnStmt || ctx.Parent is FuncDecl)
        {
            yield return "coll.returned";
        }
        if (ctx.Parent is FuncDecl && Type?.Kind is TK.ROSpan or TK.Span)
        {
            // expression-bodied function returning a collection as a span (always reachable)
            yield return "banned.collection-return-span";
        }
    }
}

internal sealed class SwitchExpr(Expr sel, List<SwitchArm> arms) : Expr
{
    public Expr Sel { get; } = sel;
    public List<SwitchArm> Arms { get; } = arms;
    public override Node?[] Kids => [Sel, .. Arms];
    public override Node Rebuild(Node?[] k) => new SwitchExpr((Expr)k[0]!, [.. k.Skip(1).OfType<SwitchArm>()]) { Type = Type };
    public override bool IsList => true;
    public override int MinKids => 2;

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        yield return "switch.expr";
        yield return "switch.sel." + SwitchStmt.SelTag(Sel.Type);
        if (!Arms.Any(a => a.Pat is DiscardPat or VarPat))
        {
            yield return "switch.expr-no-discard";
        }
        if (Sel is BinExpr or UnExpr or CastExpr)
        {
            // printed without parentheses: C# binds `switch` tighter than binary operators,
            // so `a % b switch { … }` is `a % (b switch { … })`; the selector tag describes the AST, not the parse
            yield return "switch.sel-unparenthesized";
        }
    }
}

internal sealed class SwitchArm(Pat pat, Expr? guard, Expr value) : Node
{
    public Pat Pat { get; } = pat;
    public Expr? Guard { get; } = guard;
    public Expr Value { get; } = value;
    public override Node?[] Kids => [Pat, Guard, Value];
    public override Node Rebuild(Node?[] k) => new SwitchArm((Pat)k[0]!, (Expr?)k[1], (Expr)k[2]!);
    public override IEnumerable<int> OptionalKids => [1];
    public override IEnumerable<string> Tags(NodeCtx ctx) => CaseLabel.GuardTags(Guard);
}

internal sealed class IsPatExpr(Expr e, Pat pat) : Expr
{
    public Expr E { get; } = e;
    public Pat Pat { get; } = pat;
    public override Node?[] Kids => [E, Pat];
    public override Node Rebuild(Node?[] k) => new IsPatExpr((Expr)k[0]!, (Pat)k[1]!) { Type = Type };
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["pattern.is"];
}

internal sealed class NewExpr(string typeText, List<Arg> args) : Expr
{
    public string TypeText { get; } = typeText;
    public List<Arg> Args { get; } = args;
    public override Node?[] Kids => [.. Args];
    public override Node Rebuild(Node?[] k) => new NewExpr(TypeText, [.. k.OfType<Arg>()]) { Type = Type };

    public override IEnumerable<string> Tags(NodeCtx ctx)
    {
        if (Type?.Kind == TK.Struct)
        {
            yield return "struct.ctor";
        }
    }
    public override IEnumerable<Node> Simplify() => [];
}

/// <summary>Verbatim expression (banned constructs or rare forms) with explicit tags.</summary>
internal sealed class RawExpr(string text, string[] tags) : Expr
{
    public string Text { get; } = text;
    public string[] TagList { get; } = tags;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new RawExpr(Text, TagList) { Type = Type };
    public override IEnumerable<string> Tags(NodeCtx ctx) => TagList;
}

// ───────────────────────────── Patterns ─────────────────────────────

internal sealed class ConstPat(Expr e) : Pat
{
    public Expr E { get; } = e;
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new ConstPat((Expr)k[0]!);
    public override IEnumerable<string> Tags(NodeCtx ctx) => [E is NameExpr or MemberExpr ? "pattern.named-const" : "pattern.const"];
}

internal sealed class RelPat(string op, Expr e) : Pat
{
    public string Op { get; } = op;
    public Expr E { get; } = e;
    public override Node?[] Kids => [E];
    public override Node Rebuild(Node?[] k) => new RelPat(Op, (Expr)k[0]!);
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["pattern.relational"];
}

internal sealed class DeclPat(Ty type, string name) : Pat
{
    public Ty Type { get; } = type;
    public string Name { get; } = name;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new DeclPat(Type, Name);
    public override IEnumerable<string> Tags(NodeCtx ctx) => [Name == "_" ? "pattern.type-discard" : "pattern.decl"];
}

internal sealed class VarPat(string name) : Pat
{
    public string Name { get; } = name;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new VarPat(Name);
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["pattern.var"];
}

internal sealed class DiscardPat : Pat
{
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new DiscardPat();
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["pattern.discard"];
}

internal sealed class TypePat(string typeText) : Pat
{
    public string TypeText { get; } = typeText;
    public override Node?[] Kids => [];
    public override Node Rebuild(Node?[] k) => new TypePat(TypeText);
    public override IEnumerable<string> Tags(NodeCtx ctx) => [TypeText == "NoneType" ? "pattern.union" : "pattern.type"];
}

/// <summary><c>Some&lt;int&gt;(var v)</c>, <c>Ok&lt;int&gt;(_)</c> ...</summary>
internal sealed class UnionPat(string caseType, Pat inner) : Pat
{
    public string CaseType { get; } = caseType;
    public Pat Inner { get; } = inner;
    public override Node?[] Kids => [Inner];
    public override Node Rebuild(Node?[] k) => new UnionPat(CaseType, (Pat)k[0]!);
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["pattern.union"];
}

internal sealed class NotPat(Pat p) : Pat
{
    public Pat P { get; } = p;
    public override Node?[] Kids => [P];
    public override Node Rebuild(Node?[] k) => new NotPat((Pat)k[0]!);
    public override IEnumerable<Node> Simplify() => [P];
    public override IEnumerable<string> Tags(NodeCtx ctx) => ["pattern.combinator"];
}

internal sealed class BinPat(string op, Pat l, Pat r) : Pat
{
    public string Op { get; } = op;
    public Pat L { get; } = l;
    public Pat R { get; } = r;
    public override Node?[] Kids => [L, R];
    public override Node Rebuild(Node?[] k) => new BinPat(Op, (Pat)k[0]!, (Pat)k[1]!);
    public override IEnumerable<Node> Simplify() => [L, R];
    public override IEnumerable<string> Tags(NodeCtx ctx) => [Op == "and" ? "pattern.and" : "pattern.combinator"];
}

/// <summary>Minimal tree walks used by tag computation.</summary>
internal static class TreeOpsLite
{
    public static IEnumerable<Node> Nodes(Node n)
    {
        yield return n;
        foreach (var k in n.Kids)
        {
            if (k is not null)
            {
                foreach (var x in Nodes(k))
                {
                    yield return x;
                }
            }
        }
    }

    public static IEnumerable<string> Names(Node n) => Nodes(n).OfType<NameExpr>().Select(x => x.Name);
}
