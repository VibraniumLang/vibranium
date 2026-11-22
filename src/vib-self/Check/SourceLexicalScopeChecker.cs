namespace Vibranium.Self;

public readonly struct SourceLexicalBinding(int nameId, int scope, int tokIdx, int nodeId, int kind, int functionId, int expressionNodeId)
{
    public readonly int NameId = nameId;
    public readonly int Scope = scope;
    public readonly int TokIdx = tokIdx;
    public readonly int NodeId = nodeId;
    // 0: ordered local; 1: parameter/local function; 2: no self-reference in initializer.
    public readonly int Kind = kind;
    public readonly int FunctionId = functionId;
    // Owning expression for type-only reservation of out/pattern declarations.
    public readonly int ExpressionNodeId = expressionNodeId;
}

public readonly struct SourceLexicalExpression(int nodeId, int scope, int parent, int initializer, bool discarded)
{
    public readonly int NodeId = nodeId;
    public readonly int Scope = scope;
    public readonly int Parent = parent;
    public readonly int Initializer = initializer;
    public readonly bool Discarded = discarded;
}

public struct SourceLexicalScopes
{
    public int FunctionId;
    public int DiscardedReturnNode;
    public Vec<int> Parents;
    public Vec<SourceLexicalBinding> Bindings;
    public Vec<SourceLexicalExpression> Expressions;

    public SourceLexicalScopes()
    {
        FunctionId = -1;
        DiscardedReturnNode = -1;
        Parents = new Vec<int>();
        Bindings = new Vec<SourceLexicalBinding>();
        Expressions = new Vec<SourceLexicalExpression>();
        Parents.Push(-1);
    }

    public int AddScope(int parent)
    {
        int id = Parents.Count;
        Parents.Push(parent);
        return id;
    }
}

// Lexical declaration spaces must be established before constant folding or MIR
// allocation: a later local shadows a field throughout its entire source scope.
public static class SourceLexicalScopeChecker
{
    public static void CheckFunction(int funcId, in AstArena ast, in TokenArena ta, in Interner names, ref DiagnosticBag diags)
    {
        FuncDecl fn = ast.Funcs[funcId];
        var scopes = new SourceLexicalScopes();
        if (fn.BodyNode >= 0 && fn.RetTypeNode >= 0 && ast.Nodes[fn.RetTypeNode].Kind == AstKind.TypePrim &&
            (TokKind)ast.Nodes[fn.RetTypeNode].A == TokKind.KwVoid)
        {
            AstNode body = ast.Nodes[fn.BodyNode];
            if (body.Kind == AstKind.StmtBlock && body.B == 1)
            {
                int statement = ast.Extra[body.A];
                AstNode ret = ast.Nodes[statement];
                if (ret.Kind == AstKind.StmtReturn && ta.Tokens[ret.TokIdx].Kind != TokKind.KwReturn)
                {
                    scopes.DiscardedReturnNode = statement;
                }
            }
        }
        CollectFunction(funcId, ref scopes, in ast);
        var outerBindings = new Vec<SourceLexicalBinding>();
        CollectEnclosingBindings(funcId, in ast, ref outerBindings);
        int owner = fn.OwnerDeclId;
        for (int i = 0; i < outerBindings.Count && owner < 0; i++) owner = ast.Funcs[outerBindings[i].FunctionId].OwnerDeclId;
        for (int i = 0; i < scopes.Bindings.Count; i++)
        {
            SourceLexicalBinding binding = scopes.Bindings[i];
            for (int j = 0; j < i; j++)
            {
                SourceLexicalBinding prior = scopes.Bindings[j];
                if (binding.NameId == prior.NameId &&
                    (ContainsScope(binding.Scope, prior.Scope, in scopes) || ContainsScope(prior.Scope, binding.Scope, in scopes)))
                {
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[binding.TokIdx], "Local name conflicts with an enclosing or overlapping declaration space."u8);
                    break;
                }
            }
        }
        for (int i = 0; i < scopes.Expressions.Count; i++)
        {
            SourceLexicalExpression use = scopes.Expressions[i];
            AstNode n = ast.Nodes[use.NodeId];
            if (n.Kind == AstKind.ExprIdent)
            {
                int found = FindBinding(n.A, use.Scope, in scopes);
                if (found >= 0)
                {
                    SourceLexicalBinding binding = scopes.Bindings[found];
                    if (use.Initializer >= 0 && (ast.Nodes[use.Initializer].D & 8) != 0 && !IsConstant(in binding, in ast))
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[n.TokIdx], "Constant initializer cannot reference a runtime local or parameter."u8);
                    }
                    if (binding.Kind != 1 && n.TokIdx < binding.TokIdx)
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[n.TokIdx], "Cannot use a local variable before its declaration; the later local shadows any field of the same name."u8);
                    }
                    else if (binding.Kind == 2 && use.Initializer == binding.NodeId)
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[n.TokIdx], "A const, ref, or implicitly typed local cannot refer to itself in its initializer."u8);
                    }
                }
                else
                {
                    for (int j = outerBindings.Count - 1; j >= 0; j--)
                    {
                        SourceLexicalBinding outer = outerBindings[j];
                        if (outer.NameId != n.A) continue;
                        bool localFunction = outer.Kind == 1 && outer.NodeId >= 0;
                        if (!localFunction && !IsConstant(in outer, in ast))
                        {
                            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[n.TokIdx], "Static local functions cannot capture enclosing runtime variables or parameters."u8);
                        }
                        else if (!localFunction && n.TokIdx < outer.TokIdx)
                        {
                            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[n.TokIdx], "Cannot use an enclosing local constant before its declaration, including inside a local function body."u8);
                        }
                        break;
                    }
                }
            }
            if (n.Kind == AstKind.ExprAssign && n.D == 0 && (TokKind)n.A == TokKind.Eq)
            {
                AstNode lhs = ast.Nodes[n.B];
                if (lhs.Kind == AstKind.ExprAssign && lhs.D != 0 &&
                    !IsDiscarded(i, owner, in scopes, in ast, in names))
                {
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[n.TokIdx], "Consuming a simple assignment whose direct target is a ref-assignment expression is unsupported with .NET SDK 11.0.100; discard its result or split the assignments."u8);
                }
            }
        }
    }

    private static void CollectFunction(int funcId, ref SourceLexicalScopes scopes, in AstArena ast)
    {
        scopes.FunctionId = funcId;
        FuncDecl fn = ast.Funcs[funcId];
        for (int i = 0; i < fn.ParamCount; i++)
        {
            ParamDecl p = ast.Params[fn.ParamStart + i];
            AddBinding(p.NameId, 0, p.TokIdx, -1, 1, ref scopes);
        }
        for (int i = 0; i < fn.LocalFuncCount; i++)
        {
            int localId = ast.Extra[fn.LocalFuncExtraStart + i];
            FuncDecl local = ast.Funcs[localId];
            AddBinding(local.NameId, fn.BodyNode >= 0 && ast.Nodes[fn.BodyNode].Kind == AstKind.StmtBlock ? 1 : 0, local.TokIdx, localId, 1, ref scopes);
        }
        VisitStatement(fn.BodyNode, 0, 0, false, ref scopes, in ast);
    }

    public static bool IsConstant(in SourceLexicalBinding binding, in AstArena ast) =>
        binding.Kind != 1 && binding.NodeId >= 0 && ast.Nodes[binding.NodeId].Kind == AstKind.StmtVarDecl &&
        (ast.Nodes[binding.NodeId].D & 8) != 0;

    public static void CollectEnclosingBindings(int target, in AstArena ast, ref Vec<SourceLexicalBinding> bindings)
    {
        if ((ast.Funcs[target].Flags & DeclFlags.IsLocalFunc) == 0) return;
        for (int i = 0; i < ast.Funcs.Count; i++)
        {
            if ((ast.Funcs[i].Flags & DeclFlags.IsLocalFunc) == 0 && TryCollectEnclosing(i, target, in ast, ref bindings)) return;
        }
    }

    private static bool TryCollectEnclosing(int funcId, int target, in AstArena ast, ref Vec<SourceLexicalBinding> bindings)
    {
        var scopes = new SourceLexicalScopes();
        CollectFunction(funcId, ref scopes, in ast);
        for (int i = 0; i < scopes.Bindings.Count; i++)
        {
            SourceLexicalBinding localFunction = scopes.Bindings[i];
            if (localFunction.Kind != 1 || localFunction.NodeId < 0) continue;
            int mark = bindings.Count;
            var path = new Vec<int>();
            for (int scope = localFunction.Scope; scope >= 0; scope = scopes.Parents[scope]) path.Push(scope);
            for (int depth = path.Count - 1; depth >= 0; depth--)
            {
                for (int j = 0; j < scopes.Bindings.Count; j++)
                {
                    SourceLexicalBinding binding = scopes.Bindings[j];
                    if (binding.Scope == path[depth]) bindings.Push(binding);
                }
            }
            if (localFunction.NodeId == target || TryCollectEnclosing(localFunction.NodeId, target, in ast, ref bindings)) return true;
            bindings.Truncate(mark);
        }
        return false;
    }

    private static void AddBinding(int nameId, int scope, int tokIdx, int nodeId, int kind, ref SourceLexicalScopes scopes) => AddBinding(nameId, scope, tokIdx, nodeId, kind, ref scopes, -1);

    private static void AddBinding(int nameId, int scope, int tokIdx, int nodeId, int kind, ref SourceLexicalScopes scopes, int expressionNodeId)
    {
        if (nameId >= 0) scopes.Bindings.Push(new SourceLexicalBinding(nameId, scope, tokIdx, nodeId, kind, scopes.FunctionId, expressionNodeId));
    }

    private static bool ContainsScope(int ancestor, int scope, in SourceLexicalScopes scopes)
    {
        for (int s = scope; s >= 0; s = scopes.Parents[s])
        {
            if (s == ancestor) return true;
        }
        return false;
    }

    private static int FindBinding(int nameId, int scope, in SourceLexicalScopes scopes)
    {
        for (int s = scope; s >= 0; s = scopes.Parents[s])
        {
            for (int i = 0; i < scopes.Bindings.Count; i++)
            {
                SourceLexicalBinding b = scopes.Bindings[i];
                if (b.Scope == s && b.NameId == nameId) return i;
            }
        }
        return -1;
    }

    public static bool HasField(int nameId, int owner, in AstArena ast)
    {
        for (int d = owner; d >= 0; d = ast.Types[d].OwnerDeclId)
        {
            TypeDecl type = ast.Types[d];
            for (int i = 0; i < type.FieldCount; i++)
            {
                if (ast.Fields[type.FieldStart + i].NameId == nameId) return true;
            }
        }
        return false;
    }

    private static bool IsDiscarded(int expression, int owner, in SourceLexicalScopes scopes, in AstArena ast, in Interner names)
    {
        int current = expression;
        while (current >= 0)
        {
            SourceLexicalExpression ctx = scopes.Expressions[current];
            if (ctx.Discarded) return true;
            if (ctx.Parent < 0) return false;
            SourceLexicalExpression parent = scopes.Expressions[ctx.Parent];
            AstNode assignment = ast.Nodes[parent.NodeId];
            if (assignment.Kind == AstKind.ExprUnary && (TokKind)assignment.A == TokKind.KwUnchecked)
            {
                current = ctx.Parent;
                continue;
            }
            if (assignment.Kind != AstKind.ExprAssign || assignment.D != 0 ||
                (TokKind)assignment.A != TokKind.Eq || assignment.C != ctx.NodeId) return false;
            AstNode target = ast.Nodes[assignment.B];
            if (target.Kind != AstKind.ExprIdent || !Bytes.Eq(names.Text(target.A), "_"u8) ||
                FindBinding(target.A, parent.Scope, in scopes) >= 0 || HasField(target.A, owner, in ast)) return false;
            current = ctx.Parent;
        }
        return false;
    }

    private static void VisitStatement(int nodeId, int scope, int declarationScope, bool flatten, ref SourceLexicalScopes scopes, in AstArena ast)
    {
        if (nodeId < 0) return;
        AstNode n = ast.Nodes[nodeId];
        switch (n.Kind)
        {
            case AstKind.StmtBlock:
            case AstKind.StmtMulti:
            {
                int block = scope;
                if (n.Kind == AstKind.StmtBlock && !flatten)
                {
                    block = scopes.AddScope(scope);
                    declarationScope = block;
                }
                if (n.Kind == AstKind.StmtBlock)
                {
                    for (int i = 0; i < n.D; i++)
                    {
                        int localId = ast.Extra[n.C + i];
                        FuncDecl local = ast.Funcs[localId];
                        AddBinding(local.NameId, block, local.TokIdx, localId, 1, ref scopes);
                    }
                }
                for (int i = 0; i < n.B; i++) VisitStatement(ast.Extra[n.A + i], block, declarationScope, false, ref scopes, in ast);
                return;
            }
            case AstKind.StmtUncheckedBlock:
                VisitStatement(n.A, scope, declarationScope, false, ref scopes, in ast);
                return;
            case AstKind.StmtVarDecl:
                AddBinding(n.A, declarationScope, n.TokIdx, nodeId, n.B < 0 || (n.D & 14) != 0 ? 2 : 0, ref scopes);
                VisitExpression(n.C, scope, declarationScope, -1, nodeId, false, ref scopes, in ast);
                return;
            case AstKind.StmtExpr:
                VisitExpression(n.A, scope, declarationScope, -1, -1, true, ref scopes, in ast);
                return;
            case AstKind.StmtReturn:
                VisitExpression(n.A, scope, declarationScope, -1, -1, nodeId == scopes.DiscardedReturnNode, ref scopes, in ast);
                return;
            case AstKind.StmtIf:
                VisitExpression(n.A, scope, declarationScope, -1, -1, false, ref scopes, in ast);
                VisitEmbedded(n.B, scope, ref scopes, in ast);
                VisitEmbedded(n.C, scope, ref scopes, in ast);
                return;
            case AstKind.StmtWhile:
            case AstKind.StmtDoWhile:
            {
                int loop = scopes.AddScope(scope);
                VisitExpression(n.Kind == AstKind.StmtWhile ? n.A : n.B, loop, loop, -1, -1, false, ref scopes, in ast);
                VisitStatement(n.Kind == AstKind.StmtWhile ? n.B : n.A, loop, loop, false, ref scopes, in ast);
                return;
            }
            case AstKind.StmtFor:
            {
                int loop = scopes.AddScope(scope);
                VisitStatement(n.A, loop, loop, true, ref scopes, in ast);
                VisitExpression(n.B, loop, loop, -1, -1, false, ref scopes, in ast);
                VisitStatement(n.C, loop, loop, true, ref scopes, in ast);
                VisitStatement(n.D, loop, loop, false, ref scopes, in ast);
                return;
            }
            case AstKind.StmtForeach:
            {
                // The iteration variable is not in scope in the collection expression.
                int header = scopes.AddScope(scope);
                VisitExpression(n.C, header, header, -1, -1, false, ref scopes, in ast);
                int body = scopes.AddScope(header);
                AddBinding(n.A, body, n.TokIdx, nodeId, 0, ref scopes);
                VisitStatement(n.D, body, body, false, ref scopes, in ast);
                return;
            }
            case AstKind.StmtSwitch:
            {
                VisitExpression(n.A, scope, declarationScope, -1, -1, false, ref scopes, in ast);
                _ = CollectSwitchBody(in n, scope, ref scopes, in ast);
                return;
            }
        }
    }

    public static int CollectSwitchBody(in AstNode n, int scope, ref SourceLexicalScopes scopes, in AstArena ast)
    {
        int block = scopes.AddScope(scope);
        for (int i = 0; i < n.C; i++)
        {
            AstNode section = ast.Nodes[ast.Extra[n.B + i]];
            int sectionScope = scopes.AddScope(block);
            for (int j = 0; j < section.B; j++)
            {
                AstNode label = ast.Nodes[ast.Extra[section.A + j]];
                VisitPattern(label.A, sectionScope, sectionScope, -1, ref scopes, in ast);
                VisitExpression(label.B, sectionScope, sectionScope, -1, -1, false, ref scopes, in ast);
            }
            // Labels and guards own section-local bindings, but expression
            // variables in unbraced section statements belong to the switch block.
            for (int j = 0; j < section.D; j++) VisitStatement(ast.Extra[section.C + j], sectionScope, block, false, ref scopes, in ast);
        }
        return block;
    }

    private static void VisitEmbedded(int nodeId, int parent, ref SourceLexicalScopes scopes, in AstArena ast)
    {
        if (nodeId < 0) return;
        int scope = scopes.AddScope(parent);
        VisitStatement(nodeId, scope, scope, false, ref scopes, in ast);
    }

    private static void VisitArguments(int start, int count, int scope, int declarationScope, int parent, int initializer, ref SourceLexicalScopes scopes, in AstArena ast)
    {
        for (int i = 0; i < count; i++)
        {
            int argId = ast.Extra[start + i];
            AstNode arg = ast.Nodes[argId];
            if (arg.C == -2) VisitExpression(arg.B, scope, declarationScope, parent, initializer, false, ref scopes, in ast);
            else if (arg.C != -3) AddBinding(arg.D, declarationScope, arg.TokIdx, argId, 0, ref scopes, parent >= 0 ? scopes.Expressions[parent].NodeId : -1);
        }
    }

    private static void VisitExpression(int nodeId, int scope, int declarationScope, int parent, int initializer, bool discarded, ref SourceLexicalScopes scopes, in AstArena ast)
    {
        if (nodeId < 0) return;
        int index = scopes.Expressions.Count;
        scopes.Expressions.Push(new SourceLexicalExpression(nodeId, scope, parent, initializer, discarded));
        AstNode n = ast.Nodes[nodeId];
        switch (n.Kind)
        {
            case AstKind.ExprMember:
            case AstKind.ExprFromEnd:
                VisitExpression(n.A, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                return;
            case AstKind.ExprUnary:
            case AstKind.ExprCast:
                VisitExpression(n.B, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                return;
            case AstKind.ExprBinary:
            case AstKind.ExprAssign:
                VisitExpression(n.B, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                VisitExpression(n.C, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                return;
            case AstKind.ExprCond:
                VisitExpression(n.A, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                VisitExpression(n.B, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                VisitExpression(n.C, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                return;
            case AstKind.ExprRange:
                VisitExpression(n.A, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                VisitExpression(n.B, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                return;
            case AstKind.ExprCall:
            case AstKind.ExprIndex:
                VisitExpression(n.A, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                VisitArguments(n.B, n.C, scope, declarationScope, index, initializer, ref scopes, in ast);
                return;
            case AstKind.ExprNew:
                VisitArguments(n.B, n.C, scope, declarationScope, index, initializer, ref scopes, in ast);
                return;
            case AstKind.ExprCollLit:
                for (int i = 0; i < n.B; i++) VisitExpression(ast.Extra[n.A + i], scope, declarationScope, index, initializer, false, ref scopes, in ast);
                return;
            case AstKind.ExprIsPattern:
                VisitExpression(n.A, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                VisitPattern(n.B, scope, declarationScope, initializer, ref scopes, in ast, nodeId);
                return;
            case AstKind.ExprSwitch:
                VisitExpression(n.A, scope, declarationScope, index, initializer, false, ref scopes, in ast);
                for (int i = 0; i < n.C; i++)
                {
                    AstNode arm = ast.Nodes[ast.Extra[n.B + i]];
                    int armScope = scopes.AddScope(scope);
                    VisitPattern(arm.A, armScope, armScope, initializer, ref scopes, in ast);
                    VisitExpression(arm.C, armScope, armScope, index, initializer, false, ref scopes, in ast);
                    VisitExpression(arm.B, armScope, armScope, index, initializer, false, ref scopes, in ast);
                }
                return;
        }
    }

    private static void VisitPattern(int nodeId, int scope, int declarationScope, int initializer, ref SourceLexicalScopes scopes, in AstArena ast) => VisitPattern(nodeId, scope, declarationScope, initializer, ref scopes, in ast, -1);

    private static void VisitPattern(int nodeId, int scope, int declarationScope, int initializer, ref SourceLexicalScopes scopes, in AstArena ast, int expressionNodeId)
    {
        if (nodeId < 0) return;
        AstNode n = ast.Nodes[nodeId];
        switch (n.Kind)
        {
            case AstKind.PatDecl:
                AddBinding(n.B, declarationScope, n.TokIdx, nodeId, 0, ref scopes, expressionNodeId);
                return;
            case AstKind.PatUnion:
                AddBinding(n.B, declarationScope, n.TokIdx, nodeId, 0, ref scopes, expressionNodeId);
                VisitPattern(n.D, scope, declarationScope, initializer, ref scopes, in ast, expressionNodeId);
                return;
            case AstKind.PatConst:
                VisitExpression(n.A, scope, declarationScope, -1, initializer, false, ref scopes, in ast);
                return;
            case AstKind.PatRel:
                VisitExpression(n.B, scope, declarationScope, -1, initializer, false, ref scopes, in ast);
                return;
            case AstKind.PatNot:
                VisitPattern(n.A, scope, declarationScope, initializer, ref scopes, in ast, expressionNodeId);
                return;
            case AstKind.PatLogic:
                VisitPattern(n.B, scope, declarationScope, initializer, ref scopes, in ast, expressionNodeId);
                VisitPattern(n.C, scope, declarationScope, initializer, ref scopes, in ast, expressionNodeId);
                return;
        }
    }
}
