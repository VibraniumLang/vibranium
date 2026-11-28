namespace Vibranium.Self;

// The declaration pass runs even when a different subset cut has rejected a
// declaration. It only reads syntax arenas: no rejected body is lowered to MIR.
// This also makes cuts visible in unused and open-generic functions.
public struct SubsetCutContext
{
    public Vec<int> Scope;
    public Vec<int> End;
    public Vec<int> Function;
    public Vec<int> LocalNext;
    public Vec<int> BindingStart;
    public Vec<int> BindingEnd;
    public Vec<int> Bindings;
    public IntMap<int> LocalHead;

    public SubsetCutContext(int tokens, int nodes)
    {
        Scope = new Vec<int>(tokens);
        Scope.Resize(tokens, -1);
        End = new Vec<int>(tokens);
        End.Resize(tokens, tokens);
        Function = new Vec<int>(tokens);
        Function.Resize(tokens, -1);
        LocalNext = new Vec<int>(nodes);
        LocalNext.Resize(nodes, -1);
        BindingStart = new Vec<int>(nodes);
        BindingStart.Resize(nodes, -1);
        BindingEnd = new Vec<int>(nodes);
        BindingEnd.Resize(nodes, tokens);
        Bindings = new Vec<int>();
        LocalHead = new IntMap<int>(128);
    }
}

public static class SubsetCutChecker
{
    public static void Check(in AstArena ast, in TokenArena ta, in Interner names,
        in IntMap<int> expressionStarts, ref DiagnosticBag diags)
    {
        var context = new SubsetCutContext(ta.Tokens.Count, ast.Nodes.Count);
        BuildContext(in ast, in ta, ref context);
        CheckTokens(in ast, in ta, in names, in context, ref diags);
        for (int i = 0; i < ast.Nodes.Count; i++)
        {
            AstNode node = ast.Nodes[i];
            if (node.Kind == AstKind.TypePrim || node.Kind == AstKind.TypeNamed)
            {
                if (IsNativeType(i, in ast, in ta, in names, 0))
                    SubsetCuts.Report(17, ta.Tokens[node.TokIdx], ref diags);
            }
            else if (node.Kind == AstKind.ExprMember)
            {
                if (IsNativeTypeExpression(node.A, in ast, in ta, in names, in context))
                    SubsetCuts.Report(17, ta.Tokens[FirstToken(node.A, in ast, in expressionStarts)], ref diags);
            }
            else if (node.Kind == AstKind.ExprUnary && node.B >= 0 && ast.Nodes[node.B].Kind == AstKind.ExprThis &&
                ((TokKind)node.A == TokKind.PlusPlus || (TokKind)node.A == TokKind.MinusMinus))
            {
                SubsetCuts.Report(14, ta.Tokens[ast.Nodes[node.B].TokIdx], ref diags);
            }
            else if (node.Kind == AstKind.ExprAssign)
            {
                if (node.B >= 0 && ast.Nodes[node.B].Kind == AstKind.ExprThis)
                    SubsetCuts.Report(14, ta.Tokens[ast.Nodes[node.B].TokIdx], ref diags);
            }
            else if (node.Kind == AstKind.ExprCollLit)
            {
                for (int element = 0; element < node.B; element++)
                    CheckCollectionBranch(ast.Extra[node.A + element], in ast, in ta, ref diags);
            }
            else if (node.Kind == AstKind.ExprCond)
            {
                CheckCollectionBranch(node.B, in ast, in ta, ref diags);
                CheckCollectionBranch(node.C, in ast, in ta, ref diags);
            }
            else if (node.Kind == AstKind.SwitchArm)
            {
                CheckCollectionBranch(node.B, in ast, in ta, ref diags);
            }
            if (node.Kind == AstKind.ExprCall || node.Kind == AstKind.ExprNew || node.Kind == AstKind.ExprIndex)
                CheckNamedArguments(i, in ast, in ta, in names, in context, ref diags);

            int condition = -1;
            bool allowTrue = false;
            if (node.Kind == AstKind.StmtIf || node.Kind == AstKind.StmtSwitch || node.Kind == AstKind.ExprSwitch || node.Kind == AstKind.ExprCond)
                condition = node.A;
            else if (node.Kind == AstKind.StmtWhile)
            {
                condition = node.A;
                allowTrue = true;
            }
            else if (node.Kind == AstKind.StmtDoWhile || node.Kind == AstKind.StmtFor)
            {
                condition = node.B;
                allowTrue = true;
            }
            else if (node.Kind == AstKind.SwitchCaseLabel) condition = node.B;
            else if (node.Kind == AstKind.SwitchArm) condition = node.C;
            if (condition < 0) continue;
            int firstToken = FirstToken(condition, in ast, in expressionStarts);
            if (allowTrue && ast.Nodes[condition].Kind == AstKind.ExprBoolLit &&
                ast.Nodes[condition].A == 1 && ta.Tokens[firstToken].Kind == TokKind.KwTrue) continue;
            if (IsConstant(condition, in ast, in ta, in names, in context, 0))
                SubsetCuts.Report(19, ta.Tokens[firstToken], ref diags);
        }
        SubsetCuts.Normalize(ref diags);
    }

    public static bool IsOptionalAttribute(Token token, bool qualified, in AstArena ast, in Interner names)
    {
        int name = token.AuxId;
        for (int u = 0; !qualified && u < ast.Usings.Count; u++)
        {
            UsingEntry entry = ast.Usings[u];
            if (entry.Kind != UsingKinds.Alias || entry.AliasOrNsId != name ||
                (entry.ScopeLevel != 0 && entry.SourceId != token.SourceId)) continue;
            AstNode target = ast.Nodes[entry.TargetNode];
            if (target.Kind == AstKind.TypeNamed) name = target.A;
            break;
        }
        ReadOnlySpan<byte> text = names.Text(name);
        return Bytes.Eq(text, "Optional"u8) || Bytes.Eq(text, "OptionalAttribute"u8) ||
            Bytes.Eq(text, "DefaultParameterValue"u8) || Bytes.Eq(text, "DefaultParameterValueAttribute"u8);
    }

    private static void BuildContext(in AstArena ast, in TokenArena ta, ref SubsetCutContext context)
    {
        var braces = new Vec<int>();
        var delimiters = new Vec<int>();
        for (int i = 0; i < ta.Tokens.Count; i++)
        {
            TokKind kind = ta.Tokens[i].Kind;
            if (kind == TokKind.Eof)
            {
                braces.Clear();
                delimiters.Clear();
                continue;
            }
            context.Scope[i] = braces.Count == 0 ? -1 : braces[braces.Count - 1];
            if (kind == TokKind.LBrace) braces.Push(i);
            else if (kind == TokKind.RBrace && braces.Count > 0) braces.Truncate(braces.Count - 1);
            if (kind == TokKind.LBrace || kind == TokKind.LParen || kind == TokKind.LBracket) delimiters.Push(i);
            else if ((kind == TokKind.RBrace || kind == TokKind.RParen || kind == TokKind.RBracket) && delimiters.Count > 0)
            {
                int opening = delimiters[delimiters.Count - 1];
                context.End[opening] = i;
                delimiters.Truncate(delimiters.Count - 1);
            }
        }
        for (int f = 0; f < ast.Funcs.Count; f++)
        {
            FuncDecl function = ast.Funcs[f];
            if (function.BodyNode < 0 || function.TokIdx < 0 || function.TokIdx >= ta.Tokens.Count) continue;
            int body = ast.Nodes[function.BodyNode].TokIdx;
            int end = body;
            if (ta.Tokens[body].Kind == TokKind.LBrace) end = context.End[body];
            else
            {
                while (end < ta.Tokens.Count && ta.Tokens[end].Kind != TokKind.Semi && ta.Tokens[end].Kind != TokKind.Eof)
                {
                    if (context.End[end] < ta.Tokens.Count) end = context.End[end];
                    end += 1;
                }
            }
            for (int t = function.TokIdx; t <= end && t < ta.Tokens.Count; t++)
            {
                int existing = context.Function[t];
                if (existing < 0 || ast.Funcs[existing].TokIdx < function.TokIdx) context.Function[t] = f;
            }
        }
        for (int i = 0; i < ast.Nodes.Count; i++)
        {
            AstNode node = ast.Nodes[i];
            int name = BindingName(in node);
            if (name < 0) continue;
            if (context.LocalHead.Get(name) is Some<int> previous) context.LocalNext[i] = previous.Value;
            context.LocalHead.Set(name, i);
            context.Bindings.Push(i);
            int scope = context.Scope[node.TokIdx];
            context.BindingStart[i] = scope;
            context.BindingEnd[i] = scope < 0 ? ta.Tokens.Count : context.End[scope];
        }
        // Variables introduced in a loop header end with that loop, including
        // an unbraced body. They must not shadow an enclosing const afterward.
        for (int i = 0; i < ast.Nodes.Count; i++)
        {
            AstNode loop = ast.Nodes[i];
            if (loop.Kind != AstKind.StmtFor && loop.Kind != AstKind.StmtForeach && loop.Kind != AstKind.StmtWhile) continue;
            int body = loop.Kind == AstKind.StmtWhile ? loop.B : loop.D;
            if (body < 0) continue;
            int bodyStart = ast.Nodes[body].TokIdx;
            int end = StatementEnd(bodyStart, in ta, in context);
            for (int b = 0; b < context.Bindings.Count; b++)
            {
                int binding = context.Bindings[b];
                int token = ast.Nodes[binding].TokIdx;
                if (token < loop.TokIdx || token >= bodyStart) continue;
                context.BindingStart[binding] = loop.TokIdx;
                context.BindingEnd[binding] = end;
            }
        }
        // Switch-expression pattern/out variables belong to their arm, not the
        // enclosing brace. In particular a pattern variable shadows a const field
        // only in its own guard and value, never in a sibling arm.
        for (int i = 0; i < ast.Nodes.Count; i++)
        {
            AstNode node = ast.Nodes[i];
            if (node.Kind != AstKind.ExprSwitch && node.Kind != AstKind.StmtSwitch) continue;
            int opening = node.TokIdx;
            while (opening < ta.Tokens.Count && ta.Tokens[opening].Kind != TokKind.LBrace && ta.Tokens[opening].Kind != TokKind.Eof) opening++;
            if (opening >= ta.Tokens.Count) continue;
            int closing = context.End[opening];
            for (int armIndex = 0; armIndex < node.C; armIndex++)
            {
                AstNode arm = ast.Nodes[ast.Extra[node.B + armIndex]];
                int start = arm.TokIdx;
                int end = armIndex + 1 < node.C ? ast.Nodes[ast.Extra[node.B + armIndex + 1]].TokIdx : closing;
                int headerEnd = end;
                if (node.Kind == AstKind.StmtSwitch && arm.D > 0) headerEnd = ast.Nodes[ast.Extra[arm.C]].TokIdx;
                for (int b = 0; b < context.Bindings.Count; b++)
                {
                    int binding = context.Bindings[b];
                    AstNode declaration = ast.Nodes[binding];
                    if (declaration.TokIdx < start || declaration.TokIdx >= headerEnd || declaration.Kind == AstKind.StmtVarDecl) continue;
                    context.BindingStart[binding] = start;
                    context.BindingEnd[binding] = end;
                }
            }
        }
    }

    private static int StatementEnd(int start, in TokenArena ta, in SubsetCutContext context)
    {
        if (ta.Tokens[start].Kind == TokKind.LBrace) return context.End[start] + 1;
        int end = start;
        while (end < ta.Tokens.Count && ta.Tokens[end].Kind != TokKind.Semi && ta.Tokens[end].Kind != TokKind.Eof)
        {
            if (context.End[end] < ta.Tokens.Count) end = context.End[end];
            end += 1;
        }
        return end + 1;
    }

    private static int BindingName(in AstNode node)
    {
        if (node.Kind == AstKind.StmtVarDecl || node.Kind == AstKind.StmtForeach) return node.A;
        if (node.Kind == AstKind.PatDecl || node.Kind == AstKind.PatUnion) return node.B;
        if (node.Kind == AstKind.CallArg && node.C >= -1) return node.D;
        return -1;
    }

    public static int FirstToken(int expression, in AstArena ast, in IntMap<int> expressionStarts)
    {
        if (expressionStarts.Get(expression) is Some<int> first) return first.Value;
        AstNode node = ast.Nodes[expression];
        if (node.Kind == AstKind.ExprBinary || node.Kind == AstKind.ExprAssign) return FirstToken(node.B, in ast, in expressionStarts);
        if (node.Kind == AstKind.ExprMember || node.Kind == AstKind.ExprIndex || node.Kind == AstKind.ExprCall ||
            node.Kind == AstKind.ExprIsPattern || node.Kind == AstKind.ExprCond || node.Kind == AstKind.ExprSwitch)
            return FirstToken(node.A, in ast, in expressionStarts);
        return node.TokIdx;
    }

    private static void CheckCollectionBranch(int expression, in AstArena ast, in TokenArena ta, ref DiagnosticBag diags)
    {
        if (expression < 0) return;
        AstNode node = ast.Nodes[expression];
        while (node.Kind == AstKind.ExprCast || (node.Kind == AstKind.ExprRange && node.A < 0))
        {
            expression = node.B;
            if (expression < 0) return;
            node = ast.Nodes[expression];
        }
        if (node.Kind == AstKind.ExprCollLit) SubsetCuts.Report(16, ta.Tokens[node.TokIdx], ref diags);
    }

    public static bool DeclaresSwitchLocal(int statement, in AstArena ast)
    {
        if (statement < 0) return false;
        AstNode node = ast.Nodes[statement];
        if (node.Kind == AstKind.StmtVarDecl || node.Kind == AstKind.StmtMulti) return true;
        if (node.Kind == AstKind.StmtExpr || node.Kind == AstKind.StmtIf || node.Kind == AstKind.StmtReturn || node.Kind == AstKind.StmtSwitch)
            return DeclaresExpressionLocal(node.A, in ast);
        return false;
    }

    private static bool DeclaresExpressionLocal(int expression, in AstArena ast)
    {
        if (expression < 0) return false;
        AstNode node = ast.Nodes[expression];
        if (node.Kind == AstKind.CallArg)
            return node.C >= -1 || DeclaresExpressionLocal(node.B, in ast);
        if (node.Kind == AstKind.PatDecl) return node.B >= 0;
        if (node.Kind == AstKind.PatUnion) return node.B >= 0 || DeclaresExpressionLocal(node.D, in ast);
        if (node.Kind == AstKind.PatNot) return DeclaresExpressionLocal(node.A, in ast);
        if (node.Kind == AstKind.PatLogic) return DeclaresExpressionLocal(node.B, in ast) || DeclaresExpressionLocal(node.C, in ast);
        if (node.Kind == AstKind.ExprIsPattern || node.Kind == AstKind.ExprCast)
            return DeclaresExpressionLocal(node.B, in ast);
        if (node.Kind == AstKind.ExprUnary) return DeclaresExpressionLocal(node.B, in ast);
        if (node.Kind == AstKind.ExprBinary || node.Kind == AstKind.ExprAssign || node.Kind == AstKind.ExprCond)
            return DeclaresExpressionLocal(node.B, in ast) || DeclaresExpressionLocal(node.C, in ast) ||
                (node.Kind == AstKind.ExprCond && DeclaresExpressionLocal(node.A, in ast));
        if (node.Kind == AstKind.ExprCall || node.Kind == AstKind.ExprNew || node.Kind == AstKind.ExprIndex)
        {
            for (int i = 0; i < node.C; i++)
                if (DeclaresExpressionLocal(ast.Extra[node.B + i], in ast)) return true;
        }
        return false;
    }

    public static void CheckPatternTokens(int start, int end, in TokenArena ta, in Interner names, ref DiagnosticBag diags)
    {
        bool expectPattern = true;
        int genericDepth = 0;
        for (int i = start; i < end; i++)
        {
            Token token = ta.Tokens[i];
            TokKind kind = token.Kind;
            if (kind == TokKind.LAngle && !expectPattern) { genericDepth += 1; continue; }
            if (genericDepth > 0)
            {
                if (kind == TokKind.RAngle) genericDepth -= 1;
                else if (kind == TokKind.GtGt || kind == TokKind.GtGtGt) genericDepth = 0;
                continue;
            }
            if (expectPattern && (kind == TokKind.LAngle || kind == TokKind.LtEq || kind == TokKind.RAngle || kind == TokKind.GtEq))
            {
                SubsetCuts.Report(20, token, ref diags);
                expectPattern = false;
                continue;
            }
            if (kind == TokKind.Ident)
            {
                ReadOnlySpan<byte> text = names.Text(token.AuxId);
                if (!expectPattern && (Bytes.Eq(text, "and"u8) || Bytes.Eq(text, "or"u8)))
                {
                    if (Bytes.Eq(text, "and"u8)) SubsetCuts.Report(20, token, ref diags);
                    expectPattern = true;
                    continue;
                }
                if (expectPattern && Bytes.Eq(text, "not"u8)) continue;
                if (Bytes.Eq(text, "_"u8) && token.Length != 1 && !expectPattern)
                    SubsetCuts.Report(11, token, ref diags);
            }
            if (kind == TokKind.LParen || kind == TokKind.LBrace || kind == TokKind.LBracket || kind == TokKind.Comma || kind == TokKind.Colon)
                expectPattern = true;
            else if (kind != TokKind.Dot) expectPattern = false;
        }
    }

    private static void CheckTokens(in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context, ref DiagnosticBag diags)
    {
        for (int i = 0; i < ta.Tokens.Count; i++)
        {
            Token token = ta.Tokens[i];
            TokKind previous = i > 0 ? ta.Tokens[i - 1].Kind : TokKind.Eof;
            TokKind next = i + 1 < ta.Tokens.Count ? ta.Tokens[i + 1].Kind : TokKind.Eof;
            if (token.Kind == TokKind.KwThis && (previous == TokKind.KwOut ||
                (previous == TokKind.KwRef && !IsReadOnlyReference(i, in ast, in ta, in context))))
                SubsetCuts.Report(14, token, ref diags);
            if (token.Kind == TokKind.KwThis && (next == TokKind.Comma || next == TokKind.RParen))
            {
                for (int opening = i - 1; opening >= 0 && ta.Tokens[opening].Kind != TokKind.Semi && ta.Tokens[opening].Kind != TokKind.LBrace; opening--)
                {
                    if (ta.Tokens[opening].Kind != TokKind.LParen) continue;
                    int close = context.End[opening];
                    if (close > i && close + 1 < ta.Tokens.Count && ta.Tokens[close + 1].Kind == TokKind.Eq)
                    {
                        SubsetCuts.Report(14, token, ref diags);
                        break;
                    }
                }
            }
            if ((token.Kind == TokKind.KwNInt || token.Kind == TokKind.KwNUInt) && previous == TokKind.LParen && i >= 2 &&
                ta.Tokens[i - 2].Kind == TokKind.Ident && Bytes.Eq(names.Text(ta.Tokens[i - 2].AuxId), "sizeof"u8))
                SubsetCuts.Report(17, token, ref diags);
            if (token.Kind == TokKind.Question && next == TokKind.KwRef)
                SubsetCuts.Report(15, token, ref diags);
            if (token.Kind != TokKind.Ident || !Bytes.Eq(names.Text(token.AuxId), "_"u8)) continue;
            int parenthesis = -1;
            for (int opening = i - 1; opening >= 0; opening--)
            {
                TokKind openingKind = ta.Tokens[opening].Kind;
                if (openingKind == TokKind.LParen && context.End[opening] > i) { parenthesis = opening; break; }
                if (openingKind == TokKind.LBrace || openingKind == TokKind.Semi) break;
            }
            if (parenthesis >= 0)
            {
                int close = context.End[parenthesis];
                TokKind before = parenthesis > 0 ? ta.Tokens[parenthesis - 1].Kind : TokKind.Eof;
                bool lambdaContext = before == TokKind.Eq || before == TokKind.LParen || before == TokKind.Comma ||
                    before == TokKind.FatArrow || before == TokKind.KwReturn || before == TokKind.Colon || before == TokKind.Question ||
                    before == TokKind.KwStatic;
                bool lambda = lambdaContext && close + 1 < ta.Tokens.Count && ta.Tokens[close + 1].Kind == TokKind.FatArrow;
                if (lambda)
                {
                    int underscoreParameters = 0;
                    for (int parameter = parenthesis + 1; parameter < close; parameter++)
                        if (ta.Tokens[parameter].Kind == TokKind.Ident && Bytes.Eq(names.Text(ta.Tokens[parameter].AuxId), "_"u8) &&
                            (ta.Tokens[parameter + 1].Kind == TokKind.Comma || ta.Tokens[parameter + 1].Kind == TokKind.RParen)) underscoreParameters++;
                    if (underscoreParameters < 2 || token.Length != 1) SubsetCuts.Report(11, token, ref diags);
                }
                else if (before == TokKind.KwVar && token.Length != 1) SubsetCuts.Report(11, token, ref diags);
                else if (next == TokKind.Colon && (before == TokKind.Eq || before == TokKind.KwReturn || before == TokKind.FatArrow ||
                    before == TokKind.LParen || before == TokKind.Comma)) SubsetCuts.Report(11, token, ref diags);
            }
            int scope = context.Scope[i];
            if (scope > 0 && ta.Tokens[scope - 1].Kind == TokKind.KwNew && next == TokKind.Eq)
                SubsetCuts.Report(11, token, ref diags);
            // A namespace component, alias, range variable, label or explicit tuple
            // element name is a real symbol; `_` designations are handled separately.
            if (previous == TokKind.KwNamespace ||
                (previous == TokKind.KwUsing && next == TokKind.Eq) ||
                (previous == TokKind.Ident && (Bytes.Eq(names.Text(ta.Tokens[i - 1].AuxId), "from"u8) ||
                 Bytes.Eq(names.Text(ta.Tokens[i - 1].AuxId), "join"u8) || Bytes.Eq(names.Text(ta.Tokens[i - 1].AuxId), "let"u8) ||
                 Bytes.Eq(names.Text(ta.Tokens[i - 1].AuxId), "into"u8))))
                SubsetCuts.Report(11, token, ref diags);
        }
    }

    private static bool IsReadOnlyReference(int token, in AstArena ast, in TokenArena ta, in SubsetCutContext context)
    {
        for (int i = token - 2; i >= 0; i--)
        {
            TokKind kind = ta.Tokens[i].Kind;
            if (kind == TokKind.Semi || kind == TokKind.LBrace || kind == TokKind.RBrace) break;
            if (kind == TokKind.KwReadonly && i > 0 && ta.Tokens[i - 1].Kind == TokKind.KwRef) return true;
            if (kind == TokKind.KwReturn || kind == TokKind.FatArrow)
            {
                int function = context.Function[token];
                return function >= 0 && (ast.Funcs[function].Flags & DeclFlags.ReturnsByRef) != 0 &&
                    (ast.Funcs[function].Flags & DeclFlags.ReturnRefMut) == 0;
            }
            if (kind == TokKind.Eq && i > 0 && ta.Tokens[i - 1].Kind == TokKind.Ident)
            {
                int local = FindLocal(ta.Tokens[i - 1].AuxId, token, in ast, in ta, in context);
                if (local >= 0 && (ast.Nodes[local].D & 4) != 0) return true;
            }
        }
        return false;
    }

    private static bool IsScopeAncestor(int ancestor, int scope, in SubsetCutContext context)
    {
        if (ancestor < 0) return true;
        for (int s = scope; s >= 0; s = context.Scope[s])
            if (s == ancestor) return true;
        return false;
    }

    private static int FindLocal(int name, int token, in AstArena ast, in TokenArena ta, in SubsetCutContext context)
    {
        int current = context.LocalHead.Get(name).UnwrapOr(-1);
        if (current < 0) return -1;
        int result = -1;
        int scope = context.Scope[token];
        for (int nodeId = current; nodeId >= 0; nodeId = context.LocalNext[nodeId])
        {
            AstNode node = ast.Nodes[nodeId];
            if (ta.Tokens[node.TokIdx].SourceId != ta.Tokens[token].SourceId || token < context.BindingStart[nodeId] || token >= context.BindingEnd[nodeId] ||
                !IsScopeAncestor(context.Scope[node.TokIdx], scope, in context)) continue;
            int bindingFunction = context.Function[node.TokIdx];
            int useFunction = context.Function[token];
            if (bindingFunction < 0 && useFunction >= 0 && ast.Funcs[useFunction].OwnerDeclId >= 0) continue;
            if (result < 0 || context.BindingStart[nodeId] > context.BindingStart[result]) result = nodeId;
        }
        return result;
    }

    private static int OwnerAt(int token, in AstArena ast, in TokenArena ta, in SubsetCutContext context)
    {
        int function = context.Function[token];
        if (function >= 0 && ast.Funcs[function].OwnerDeclId >= 0) return ast.Funcs[function].OwnerDeclId;
        int result = -1;
        int closest = -1;
        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl type = ast.Types[i];
            if (type.SourceId != ta.Tokens[token].SourceId || type.TokIdx > token) continue;
            int body = type.TokIdx;
            while (body < ta.Tokens.Count && ta.Tokens[body].Kind != TokKind.LBrace && ta.Tokens[body].Kind != TokKind.Semi && ta.Tokens[body].Kind != TokKind.Eof) body++;
            if (body < ta.Tokens.Count && token <= context.End[body] && body > closest)
            {
                result = i;
                closest = body;
            }
        }
        return result;
    }

    private static bool IsConstant(int expression, in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context, int depth)
    {
        if (expression < 0 || depth > 128) return false;
        AstNode node = ast.Nodes[expression];
        if (node.Kind == AstKind.ExprIntLit)
        {
            TokKind literalKind = ta.Tokens[node.TokIdx].Kind;
            return literalKind == TokKind.IntLit || literalKind == TokKind.UIntLit || literalKind == TokKind.LongLit || literalKind == TokKind.ULongLit;
        }
        if (node.Kind == AstKind.ExprFloatLit || node.Kind == AstKind.ExprBoolLit || node.Kind == AstKind.ExprCharLit ||
            node.Kind == AstKind.ExprStrLit || node.Kind == AstKind.ExprNull) return true;
        if (node.Kind == AstKind.ExprDefault)
        {
            if (node.A < 0) return true;
            TokKind primitive = PrimitiveType(node.A, in ast, in names, 0);
            if (primitive != TokKind.Eof) return primitive != TokKind.KwVoid;
            int declaration = FindType(node.A, in ast, in names, 0);
            return declaration >= 0 && ast.Types[declaration].Kind != TypeDeclKinds.Struct;
        }
        if (node.Kind == AstKind.ExprUnary)
        {
            TokKind operation = (TokKind)node.A;
            return (operation == TokKind.Plus || operation == TokKind.Minus || operation == TokKind.Bang || operation == TokKind.Tilde ||
                operation == TokKind.KwChecked || operation == TokKind.KwUnchecked) &&
                IsConstant(node.B, in ast, in ta, in names, in context, depth + 1);
        }
        if (node.Kind == AstKind.ExprCast)
        {
            TokKind primitive = PrimitiveType(node.A, in ast, in names, 0);
            if (primitive == TokKind.KwObject)
                return ast.Nodes[node.B].Kind == AstKind.ExprNull;
            if (primitive == TokKind.KwNInt || primitive == TokKind.KwNUInt)
            {
                // Native-sized constant conversions must have the same value on
                // both 32-bit and 64-bit targets. An unchecked wide conversion is
                // runtime-dependent, not a C# constant expression.
                if (!TryNumericValue(node.B, in ast, in ta, in names, in context, depth + 1, out double number)) return false;
                if (!(number > -2147483649.0 && number < 4294967296.0)) return false;
                long value = (long)number;
                return primitive == TokKind.KwNInt ? value >= -2147483648L && value <= 2147483647L : value >= 0L && value <= 4294967295L;
            }
            if (primitive == TokKind.Eof)
            {
                int declaration = FindType(node.A, in ast, in names, 0);
                if (declaration < 0 || ast.Types[declaration].Kind != TypeDeclKinds.Enum)
                    return ast.Nodes[node.B].Kind == AstKind.ExprNull;
            }
            return IsConstant(node.B, in ast, in ta, in names, in context, depth + 1);
        }
        if (node.Kind == AstKind.ExprBinary)
        {
            if ((TokKind)node.A == TokKind.QuestionQuestion) return false;
            if ((TokKind)node.A == TokKind.Plus)
            {
                bool leftString = IsStringConstant(node.B, in ast, in ta, in names, in context, depth + 1);
                bool rightString = IsStringConstant(node.C, in ast, in ta, in names, in context, depth + 1);
                if (leftString != rightString && ast.Nodes[leftString ? node.C : node.B].Kind != AstKind.ExprNull) return false;
            }
            return IsConstant(node.B, in ast, in ta, in names, in context, depth + 1) &&
                IsConstant(node.C, in ast, in ta, in names, in context, depth + 1);
        }
        if (node.Kind == AstKind.ExprCond)
            return IsConstant(node.A, in ast, in ta, in names, in context, depth + 1) &&
                IsConstant(node.B, in ast, in ta, in names, in context, depth + 1) &&
                IsConstant(node.C, in ast, in ta, in names, in context, depth + 1);
        if (node.Kind == AstKind.ExprCall && node.A >= 0)
        {
            AstNode callee = ast.Nodes[node.A];
            if (callee.Kind != AstKind.ExprIdent) return false;
            if (Bytes.Eq(names.Text(callee.A), "nameof"u8)) return true;
            if (Bytes.Eq(names.Text(callee.A), "sizeof"u8) && node.TokIdx + 1 < ta.Tokens.Count)
            {
                TokKind type = ta.Tokens[node.TokIdx + 1].Kind;
                return type >= TokKind.KwBool && type <= TokKind.KwDouble && type != TokKind.KwNInt && type != TokKind.KwNUInt;
            }
            return false;
        }
        if (node.Kind == AstKind.ExprIdent)
        {
            int local = FindLocal(node.A, node.TokIdx, in ast, in ta, in context);
            if (local >= 0)
            {
                AstNode declaration = ast.Nodes[local];
                return declaration.Kind == AstKind.StmtVarDecl && (declaration.D & 8) != 0 && IsConstant(declaration.C, in ast, in ta, in names, in context, depth + 1);
            }
            int function = context.Function[node.TokIdx];
            if (function >= 0)
            {
                FuncDecl declaration = ast.Funcs[function];
                for (int p = 0; p < declaration.ParamCount; p++)
                    if (ast.Params[declaration.ParamStart + p].NameId == node.A) return false;
            }
            for (int owner = OwnerAt(node.TokIdx, in ast, in ta, in context); owner >= 0; owner = ast.Types[owner].OwnerDeclId)
            {
                int field = FindField(owner, node.A, in ast);
                if (field >= 0) return IsConstantField(field, in ast, in ta, in names, in context, depth + 1);
            }
            int sourceId = ta.Tokens[node.TokIdx].SourceId;
            for (int u = 0; u < ast.Usings.Count; u++)
            {
                UsingEntry entry = ast.Usings[u];
                if (entry.Kind != UsingKinds.Static || (entry.ScopeLevel != 0 && entry.SourceId != sourceId)) continue;
                int owner = FindType(entry.TargetNode, in ast, in names, 0);
                int field = FindField(owner, node.A, in ast);
                if (field >= 0) return IsConstantField(field, in ast, in ta, in names, in context, depth + 1);
            }
            return false;
        }
        if (node.Kind == AstKind.ExprMember)
        {
            int owner = FindExpressionType(node.A, in ast, in ta, in names, in context);
            int field = FindField(owner, node.B, in ast);
            if (field >= 0) return IsConstantField(field, in ast, in ta, in names, in context, depth + 1);
            AstNode target = ast.Nodes[node.A];
            int name = target.Kind == AstKind.ExprIdent ? target.A : (target.Kind == AstKind.ExprMember ? target.B : -1);
            if (name < 0 || owner >= 0) return false;
            return DeclBinder.TryEvalBuiltinNumericConstMember(names.Text(name), names.Text(node.B), out _, out _, out _);
        }
        return false;
    }

    private static bool TryNumericValue(int expression, in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context, int depth, out double value)
    {
        value = 0.0;
        if (expression < 0 || depth > 128) return false;
        if (TryIntegerValue(expression, in ast, in ta, in names, in context, depth + 1, out long integer)) { value = integer; return true; }
        AstNode node = ast.Nodes[expression];
        if (node.Kind == AstKind.ExprFloatLit)
        {
            Token literal = ta.Tokens[node.A];
            value = literal.Kind == TokKind.FloatLit ? (double)DeclBinder.BitsToFloat((int)literal.IntVal) : DeclBinder.BitsToDouble(literal.IntVal);
            return true;
        }
        if (node.Kind == AstKind.ExprUnary)
        {
            if (!TryNumericValue(node.B, in ast, in ta, in names, in context, depth + 1, out double operand)) return false;
            TokKind operation = (TokKind)node.A;
            if (operation == TokKind.Minus) value = -operand;
            else if (operation == TokKind.Plus || operation == TokKind.KwChecked || operation == TokKind.KwUnchecked) value = operand;
            else return false;
            return true;
        }
        if (node.Kind == AstKind.ExprBinary)
        {
            if (!TryNumericValue(node.B, in ast, in ta, in names, in context, depth + 1, out double left) ||
                !TryNumericValue(node.C, in ast, in ta, in names, in context, depth + 1, out double right)) return false;
            TokKind operation = (TokKind)node.A;
            if (operation == TokKind.Plus) value = left + right;
            else if (operation == TokKind.Minus) value = left - right;
            else if (operation == TokKind.Star) value = left * right;
            else if (operation == TokKind.Slash) value = left / right;
            else if (operation == TokKind.Percent) value = left % right;
            else return false;
            return true;
        }
        if (node.Kind == AstKind.ExprCast)
        {
            if (!TryNumericValue(node.B, in ast, in ta, in names, in context, depth + 1, out double operand)) return false;
            TokKind primitive = PrimitiveType(node.A, in ast, in names, 0);
            if (primitive == TokKind.KwFloat) value = (float)operand;
            else if (primitive == TokKind.KwDouble) value = operand;
            else if (primitive == TokKind.KwInt && operand > -2147483649.0 && operand < 2147483648.0) value = (int)operand;
            else if (primitive == TokKind.KwUInt && operand > -1.0 && operand < 4294967296.0) value = (uint)operand;
            else return false;
            return true;
        }
        if (node.Kind == AstKind.ExprIdent)
        {
            int local = FindLocal(node.A, node.TokIdx, in ast, in ta, in context);
            if (local >= 0)
            {
                AstNode declaration = ast.Nodes[local];
                return declaration.Kind == AstKind.StmtVarDecl && (declaration.D & 8) != 0 &&
                    TryNumericValue(declaration.C, in ast, in ta, in names, in context, depth + 1, out value);
            }
            for (int owner = OwnerAt(node.TokIdx, in ast, in ta, in context); owner >= 0; owner = ast.Types[owner].OwnerDeclId)
            {
                int field = FindField(owner, node.A, in ast);
                if (field >= 0) return (ast.Fields[field].Flags & DeclFlags.IsConst) != 0 &&
                    TryNumericValue(ast.Fields[field].InitExpr, in ast, in ta, in names, in context, depth + 1, out value);
            }
        }
        if (node.Kind == AstKind.ExprMember)
        {
            int owner = FindExpressionType(node.A, in ast, in ta, in names, in context);
            int field = FindField(owner, node.B, in ast);
            if (field >= 0) return (ast.Fields[field].Flags & DeclFlags.IsConst) != 0 &&
                TryNumericValue(ast.Fields[field].InitExpr, in ast, in ta, in names, in context, depth + 1, out value);
            AstNode target = ast.Nodes[node.A];
            int name = target.Kind == AstKind.ExprIdent ? target.A : (target.Kind == AstKind.ExprMember ? target.B : -1);
            if (name < 0 || owner >= 0) return false;
            if (!DeclBinder.TryEvalBuiltinNumericConstMember(names.Text(name), names.Text(node.B), out int type, out long bits, out bool floating) || !floating) return false;
            value = type == TypeIds.F32 ? (double)DeclBinder.BitsToFloat((int)bits) : DeclBinder.BitsToDouble(bits);
            return true;
        }
        return false;
    }

    private static bool TryIntegerValue(int expression, in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context, int depth, out long value)
    {
        value = 0L;
        if (expression < 0 || depth > 128) return false;
        AstNode node = ast.Nodes[expression];
        if (node.Kind == AstKind.ExprIntLit)
        {
            Token literal = ta.Tokens[node.A];
            if (literal.Kind == TokKind.ULongLit && literal.IntVal < 0L) return false;
            value = literal.IntVal;
            return (literal.AuxId & 4) == 0;
        }
        if (node.Kind == AstKind.ExprCharLit) { value = node.A; return true; }
        if (node.Kind == AstKind.ExprDefault) return true;
        if (node.Kind == AstKind.ExprUnary)
        {
            if (!TryIntegerValue(node.B, in ast, in ta, in names, in context, depth + 1, out long operand)) return false;
            TokKind operation = (TokKind)node.A;
            if (operation == TokKind.Minus) value = unchecked(-operand);
            else if (operation == TokKind.Tilde) value = ~operand;
            else if (operation == TokKind.Plus || operation == TokKind.KwChecked || operation == TokKind.KwUnchecked) value = operand;
            else return false;
            return true;
        }
        if (node.Kind == AstKind.ExprCast)
        {
            if (!TryIntegerValue(node.B, in ast, in ta, in names, in context, depth + 1, out long operand)) return false;
            TokKind primitive = PrimitiveType(node.A, in ast, in names, 0);
            if (primitive == TokKind.KwByte) value = unchecked((byte)operand);
            else if (primitive == TokKind.KwSByte) value = unchecked((sbyte)operand);
            else if (primitive == TokKind.KwShort) value = unchecked((short)operand);
            else if (primitive == TokKind.KwUShort || primitive == TokKind.KwChar) value = unchecked((ushort)operand);
            else if (primitive == TokKind.KwInt) value = unchecked((int)operand);
            else if (primitive == TokKind.KwUInt) value = unchecked((uint)operand);
            else if (primitive == TokKind.KwLong || primitive == TokKind.KwULong || primitive == TokKind.KwNInt || primitive == TokKind.KwNUInt) value = operand;
            else return false;
            return IsConstant(expression, in ast, in ta, in names, in context, depth + 1);
        }
        if (node.Kind == AstKind.ExprBinary)
        {
            if (!TryIntegerValue(node.B, in ast, in ta, in names, in context, depth + 1, out long left) ||
                !TryIntegerValue(node.C, in ast, in ta, in names, in context, depth + 1, out long right)) return false;
            TokKind operation = (TokKind)node.A;
            if (operation == TokKind.Plus) value = unchecked(left + right);
            else if (operation == TokKind.Minus) value = unchecked(left - right);
            else if (operation == TokKind.Star) value = unchecked(left * right);
            else if (operation == TokKind.Slash && right != 0L && !(left == long.MinValue && right == -1L)) value = left / right;
            else if (operation == TokKind.Percent && right != 0L && !(left == long.MinValue && right == -1L)) value = left % right;
            else if (operation == TokKind.Amp) value = left & right;
            else if (operation == TokKind.Pipe) value = left | right;
            else if (operation == TokKind.Caret) value = left ^ right;
            else return false;
            return true;
        }
        if (node.Kind == AstKind.ExprIdent)
        {
            int local = FindLocal(node.A, node.TokIdx, in ast, in ta, in context);
            if (local >= 0)
            {
                AstNode declaration = ast.Nodes[local];
                return declaration.Kind == AstKind.StmtVarDecl && (declaration.D & 8) != 0 &&
                    TryIntegerValue(declaration.C, in ast, in ta, in names, in context, depth + 1, out value);
            }
            for (int owner = OwnerAt(node.TokIdx, in ast, in ta, in context); owner >= 0; owner = ast.Types[owner].OwnerDeclId)
            {
                int field = FindField(owner, node.A, in ast);
                if (field >= 0) return (ast.Fields[field].Flags & DeclFlags.IsConst) != 0 &&
                    TryIntegerValue(ast.Fields[field].InitExpr, in ast, in ta, in names, in context, depth + 1, out value);
            }
        }
        if (node.Kind == AstKind.ExprMember)
        {
            int owner = FindExpressionType(node.A, in ast, in ta, in names, in context);
            int field = FindField(owner, node.B, in ast);
            if (field >= 0) return (ast.Fields[field].Flags & DeclFlags.IsConst) != 0 &&
                TryIntegerValue(ast.Fields[field].InitExpr, in ast, in ta, in names, in context, depth + 1, out value);
            AstNode target = ast.Nodes[node.A];
            int name = target.Kind == AstKind.ExprIdent ? target.A : (target.Kind == AstKind.ExprMember ? target.B : -1);
            if (name < 0 || owner >= 0) return false;
            if (!DeclBinder.TryEvalBuiltinNumericConstMember(names.Text(name), names.Text(node.B), out int type, out value, out bool floating)) return false;
            return !floating && !(type == TypeIds.U64 && value < 0L);
        }
        return false;
    }

    private static bool IsStringConstant(int expression, in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context, int depth)
    {
        if (expression < 0 || depth > 128) return false;
        AstNode node = ast.Nodes[expression];
        if (node.Kind == AstKind.ExprStrLit) return true;
        if (node.Kind == AstKind.ExprIdent)
        {
            int local = FindLocal(node.A, node.TokIdx, in ast, in ta, in context);
            if (local >= 0 && ast.Nodes[local].Kind == AstKind.StmtVarDecl && (ast.Nodes[local].D & 8) != 0)
                return IsStringConstant(ast.Nodes[local].C, in ast, in ta, in names, in context, depth + 1);
            for (int owner = OwnerAt(node.TokIdx, in ast, in ta, in context); owner >= 0; owner = ast.Types[owner].OwnerDeclId)
            {
                int field = FindField(owner, node.A, in ast);
                if (field >= 0 && (ast.Fields[field].Flags & DeclFlags.IsConst) != 0)
                    return IsStringConstant(ast.Fields[field].InitExpr, in ast, in ta, in names, in context, depth + 1);
            }
        }
        if (node.Kind == AstKind.ExprMember)
        {
            int field = FindField(FindExpressionType(node.A, in ast, in ta, in names, in context), node.B, in ast);
            if (field >= 0 && (ast.Fields[field].Flags & DeclFlags.IsConst) != 0)
                return IsStringConstant(ast.Fields[field].InitExpr, in ast, in ta, in names, in context, depth + 1);
        }
        if (node.Kind == AstKind.ExprCall && ast.Nodes[node.A].Kind == AstKind.ExprIdent)
            return Bytes.Eq(names.Text(ast.Nodes[node.A].A), "nameof"u8);
        if (node.Kind == AstKind.ExprBinary && (TokKind)node.A == TokKind.Plus)
            return IsStringConstant(node.B, in ast, in ta, in names, in context, depth + 1) ||
                IsStringConstant(node.C, in ast, in ta, in names, in context, depth + 1);
        if (node.Kind == AstKind.ExprCond) return IsStringConstant(node.B, in ast, in ta, in names, in context, depth + 1);
        if (node.Kind == AstKind.ExprCast || node.Kind == AstKind.ExprDefault)
        {
            return PrimitiveType(node.A, in ast, in names, 0) == TokKind.KwString;
        }
        return false;
    }

    private static bool IsConstantField(int field, in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context, int depth)
    {
        FieldDecl declaration = ast.Fields[field];
        return (declaration.Flags & DeclFlags.IsConst) != 0 && (declaration.HasConstVal != 0 ||
            IsConstant(declaration.InitExpr, in ast, in ta, in names, in context, depth + 1));
    }

    private static int FindField(int owner, int name, in AstArena ast)
    {
        if (owner < 0) return -1;
        TypeDecl declaration = ast.Types[owner];
        for (int f = 0; f < declaration.FieldCount; f++)
            if (ast.Fields[declaration.FieldStart + f].NameId == name) return declaration.FieldStart + f;
        return -1;
    }

    private static int FindType(int typeNode, in AstArena ast, in Interner names, int depth)
    {
        if (typeNode < 0 || depth > 32) return -1;
        AstNode node = ast.Nodes[typeNode];
        if (node.Kind != AstKind.TypeNamed) return -1;
        TypeRefContext reference = ast.TypeRefs[node.D];
        if (reference.PrefixNsId < 0)
        {
            for (int u = 0; u < reference.UsingCount; u++)
            {
                UsingEntry entry = ast.Usings[ast.Extra[reference.UsingExtraStart + u]];
                if (entry.Kind == UsingKinds.Alias && entry.AliasOrNsId == node.A)
                    return FindType(entry.TargetNode, in ast, in names, depth + 1);
            }
        }
        for (int t = 0; t < ast.Types.Count; t++)
        {
            TypeDecl type = ast.Types[t];
            if (type.NameId != node.A || type.TypeParamCount != node.C) continue;
            if (reference.PrefixNsId >= 0 && type.NsId >= 0 && !Bytes.Eq(names.Text(reference.PrefixNsId), names.Text(type.NsId))) continue;
            return t;
        }
        return -1;
    }

    private static TokKind PrimitiveType(int typeNode, in AstArena ast, in Interner names, int depth)
    {
        if (typeNode < 0 || depth > 32) return TokKind.Eof;
        AstNode node = ast.Nodes[typeNode];
        if (node.Kind == AstKind.TypePrim) return (TokKind)node.A;
        if (node.Kind != AstKind.TypeNamed || node.C != 0) return TokKind.Eof;
        TypeRefContext reference = ast.TypeRefs[node.D];
        if (reference.PrefixNsId < 0)
        {
            for (int u = 0; u < reference.UsingCount; u++)
            {
                UsingEntry entry = ast.Usings[ast.Extra[reference.UsingExtraStart + u]];
                if (entry.Kind == UsingKinds.Alias && entry.AliasOrNsId == node.A)
                    return PrimitiveType(entry.TargetNode, in ast, in names, depth + 1);
            }
            if (FindType(typeNode, in ast, in names, 0) >= 0) return TokKind.Eof;
        }
        else if (!Bytes.Eq(names.Text(reference.PrefixNsId), "System"u8) &&
                 !Bytes.Eq(names.Text(reference.PrefixNsId), "global::System"u8)) return TokKind.Eof;
        ReadOnlySpan<byte> name = names.Text(node.A);
        if (Bytes.Eq(name, "Boolean"u8)) return TokKind.KwBool;
        if (Bytes.Eq(name, "SByte"u8)) return TokKind.KwSByte;
        if (Bytes.Eq(name, "Byte"u8)) return TokKind.KwByte;
        if (Bytes.Eq(name, "Int16"u8)) return TokKind.KwShort;
        if (Bytes.Eq(name, "UInt16"u8)) return TokKind.KwUShort;
        if (Bytes.Eq(name, "Int32"u8)) return TokKind.KwInt;
        if (Bytes.Eq(name, "UInt32"u8)) return TokKind.KwUInt;
        if (Bytes.Eq(name, "Int64"u8)) return TokKind.KwLong;
        if (Bytes.Eq(name, "UInt64"u8)) return TokKind.KwULong;
        if (Bytes.Eq(name, "IntPtr"u8)) return TokKind.KwNInt;
        if (Bytes.Eq(name, "UIntPtr"u8)) return TokKind.KwNUInt;
        if (Bytes.Eq(name, "Single"u8)) return TokKind.KwFloat;
        if (Bytes.Eq(name, "Double"u8)) return TokKind.KwDouble;
        if (Bytes.Eq(name, "Char"u8)) return TokKind.KwChar;
        if (Bytes.Eq(name, "String"u8)) return TokKind.KwString;
        if (Bytes.Eq(name, "Object"u8)) return TokKind.KwObject;
        return TokKind.Eof;
    }

    private static bool NativeName(ReadOnlySpan<byte> name) =>
        Bytes.Eq(name, "nint"u8) || Bytes.Eq(name, "nuint"u8) || Bytes.Eq(name, "IntPtr"u8) || Bytes.Eq(name, "UIntPtr"u8);

    private static bool IsNativeType(int typeNode, in AstArena ast, in TokenArena ta, in Interner names, int depth)
    {
        if (typeNode < 0 || depth > 32) return false;
        AstNode node = ast.Nodes[typeNode];
        if (node.Kind == AstKind.TypePrim) return (TokKind)node.A == TokKind.KwNInt || (TokKind)node.A == TokKind.KwNUInt;
        if (node.Kind != AstKind.TypeNamed || node.C != 0) return false;
        TypeRefContext reference = ast.TypeRefs[node.D];
        if (reference.PrefixNsId < 0)
        {
            for (int u = 0; u < reference.UsingCount; u++)
            {
                UsingEntry entry = ast.Usings[ast.Extra[reference.UsingExtraStart + u]];
                if (entry.Kind == UsingKinds.Alias && entry.AliasOrNsId == node.A)
                    return IsNativeType(entry.TargetNode, in ast, in ta, in names, depth + 1);
            }
            if (FindType(typeNode, in ast, in names, 0) >= 0) return false;
            return NativeName(names.Text(node.A));
        }
        ReadOnlySpan<byte> prefix = names.Text(reference.PrefixNsId);
        return (Bytes.Eq(prefix, "System"u8) || Bytes.Eq(prefix, "global::System"u8)) && NativeName(names.Text(node.A));
    }

    private static bool IsValueName(int name, int token, in AstArena ast, in TokenArena ta, in SubsetCutContext context)
    {
        if (FindLocal(name, token, in ast, in ta, in context) >= 0) return true;
        int function = context.Function[token];
        if (function >= 0)
        {
            FuncDecl declaration = ast.Funcs[function];
            for (int p = 0; p < declaration.ParamCount; p++)
                if (ast.Params[declaration.ParamStart + p].NameId == name) return true;
        }
        for (int owner = OwnerAt(token, in ast, in ta, in context); owner >= 0; owner = ast.Types[owner].OwnerDeclId)
            if (FindField(owner, name, in ast) >= 0) return true;
        return false;
    }

    private static bool IsNativeTypeExpression(int expression, in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context)
    {
        if (expression < 0) return false;
        AstNode node = ast.Nodes[expression];
        if (node.Kind == AstKind.ExprIdent)
        {
            if (FindLocal(node.A, node.TokIdx, in ast, in ta, in context) >= 0) return false;
            int function = context.Function[node.TokIdx];
            if (function >= 0)
            {
                FuncDecl declaration = ast.Funcs[function];
                for (int p = 0; p < declaration.ParamCount; p++)
                    if (ast.Params[declaration.ParamStart + p].NameId == node.A) return false;
            }
            for (int owner = OwnerAt(node.TokIdx, in ast, in ta, in context); owner >= 0; owner = ast.Types[owner].OwnerDeclId)
                if (FindField(owner, node.A, in ast) >= 0) return false;
            int source = ta.Tokens[node.TokIdx].SourceId;
            for (int u = 0; u < ast.Usings.Count; u++)
            {
                UsingEntry entry = ast.Usings[u];
                if (entry.Kind == UsingKinds.Alias && entry.AliasOrNsId == node.A && (entry.ScopeLevel == 0 || entry.SourceId == source))
                    return IsNativeType(entry.TargetNode, in ast, in ta, in names, 0);
            }
            for (int t = 0; t < ast.Types.Count; t++)
                if (ast.Types[t].NameId == node.A) return false;
            return NativeName(names.Text(node.A));
        }
        if (node.Kind != AstKind.ExprMember || !NativeName(names.Text(node.B))) return false;
        AstNode prefix = ast.Nodes[node.A];
        return prefix.Kind == AstKind.ExprIdent && Bytes.Eq(names.Text(prefix.A), "System"u8);
    }

    private static int FindExpressionType(int expression, in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context)
    {
        if (expression < 0) return -1;
        AstNode node = ast.Nodes[expression];
        if (node.Kind == AstKind.ExprNew) return FindType(node.A, in ast, in names, 0);
        if (node.Kind == AstKind.ExprThis) return OwnerAt(node.TokIdx, in ast, in ta, in context);
        if (node.Kind == AstKind.ExprIdent)
        {
            int local = FindLocal(node.A, node.TokIdx, in ast, in ta, in context);
            if (local >= 0)
            {
                AstNode declaration = ast.Nodes[local];
                if (declaration.Kind == AstKind.PatDecl) return FindType(declaration.A, in ast, in names, 0);
                if (declaration.Kind != AstKind.StmtVarDecl) return -1;
                if (declaration.B >= 0) return FindType(declaration.B, in ast, in names, 0);
                if (declaration.C >= 0 && ast.Nodes[declaration.C].Kind == AstKind.ExprNew)
                    return FindType(ast.Nodes[declaration.C].A, in ast, in names, 0);
                return -1;
            }
            int function = context.Function[node.TokIdx];
            if (function >= 0)
            {
                FuncDecl declaration = ast.Funcs[function];
                for (int p = 0; p < declaration.ParamCount; p++)
                {
                    ParamDecl parameter = ast.Params[declaration.ParamStart + p];
                    if (parameter.NameId == node.A) return FindType(parameter.TypeNode, in ast, in names, 0);
                }
            }
            int source = ta.Tokens[node.TokIdx].SourceId;
            for (int u = 0; u < ast.Usings.Count; u++)
            {
                UsingEntry entry = ast.Usings[u];
                if (entry.Kind == UsingKinds.Alias && entry.AliasOrNsId == node.A && (entry.ScopeLevel == 0 || entry.SourceId == source))
                    return FindType(entry.TargetNode, in ast, in names, 0);
            }
        }
        int name = node.Kind == AstKind.ExprIdent ? node.A : (node.Kind == AstKind.ExprMember ? node.B : -1);
        for (int t = 0; t < ast.Types.Count; t++)
            if (ast.Types[t].NameId == name) return t;
        return -1;
    }

    private static void CheckNamedArguments(int callId, in AstArena ast, in TokenArena ta, in Interner names, in SubsetCutContext context, ref DiagnosticBag diags)
    {
        AstNode call = ast.Nodes[callId];
        bool named = false;
        for (int a = 0; a < call.C; a++)
            if (CallArgBits.NameId(in ast.Nodes[ast.Extra[call.B + a]]) >= 0) named = true;
        if (!named) return;
        int name = -1;
        int owner = -1;
        bool constructor = call.Kind == AstKind.ExprNew;
        bool instance = false;
        if (constructor) owner = FindType(call.A, in ast, in names, 0);
        else if (call.Kind == AstKind.ExprIndex)
        {
            owner = FindExpressionType(call.A, in ast, in ta, in names, in context);
        }
        else
        {
            AstNode callee = ast.Nodes[call.A];
            if (callee.Kind == AstKind.ExprIdent)
            {
                name = callee.A;
                owner = OwnerAt(callee.TokIdx, in ast, in ta, in context);
            }
            else if (callee.Kind == AstKind.ExprMember)
            {
                name = callee.B;
                owner = FindExpressionType(callee.A, in ast, in ta, in names, in context);
                AstNode receiver = ast.Nodes[callee.A];
                instance = receiver.Kind != AstKind.ExprIdent || IsValueName(receiver.A, receiver.TokIdx, in ast, in ta, in context);
            }
        }
        int bestScore = -1;
        int firstBad = -1;
        for (int f = 0; f < ast.Funcs.Count; f++)
        {
            FuncDecl function = ast.Funcs[f];
            if (constructor)
            {
                if (owner < 0 || function.OwnerDeclId != owner || (function.Flags & DeclFlags.IsCtor) == 0) continue;
            }
            else if (call.Kind == AstKind.ExprIndex)
            {
                if (owner < 0 || function.OwnerDeclId != owner || (function.Flags & DeclFlags.IsIndexer) == 0 || (function.Flags & DeclFlags.IsPropGet) == 0) continue;
            }
            else
            {
                if (function.NameId != name) continue;
                if ((function.Flags & DeclFlags.IsLocalFunc) != 0)
                {
                    int scope = context.Scope[function.TokIdx];
                    if (function.SourceId != ta.Tokens[call.TokIdx].SourceId ||
                        !IsScopeAncestor(scope, context.Scope[call.TokIdx], in context) || (scope < 0 && owner >= 0)) continue;
                }
                else if (owner >= 0 && function.OwnerDeclId != owner && (function.Flags & DeclFlags.IsExtension) == 0) continue;
            }
            int offset = instance && (function.Flags & DeclFlags.IsExtension) != 0 ? 1 : 0;
            int count = function.ParamCount - offset;
            if (call.C > count) continue;
            int score = (function.Flags & DeclFlags.IsLocalFunc) != 0 ? 1000 : 0;
            int bad = -1;
            bool valid = true;
            var supplied = new Vec<int>(count);
            supplied.Resize(count, 0);
            for (int a = 0; a < call.C; a++)
            {
                AstNode argument = ast.Nodes[ast.Extra[call.B + a]];
                int argumentName = CallArgBits.NameId(in argument);
                int parameter = a;
                if (argumentName >= 0)
                {
                    parameter = -1;
                    for (int p = 0; p < count; p++)
                        if (ast.Params[function.ParamStart + offset + p].NameId == argumentName) parameter = p;
                    if (parameter < 0) { valid = false; break; }
                    if (parameter != a && bad < 0) bad = argument.TokIdx;
                }
                if (supplied[parameter] != 0) { valid = false; break; }
                supplied[parameter] = 1;
                ParamDecl formal = ast.Params[function.ParamStart + offset + parameter];
                int match = ArgumentTypeScore(argument.B, formal.TypeNode, in ast, in ta, in context);
                if (match < 0) { valid = false; break; }
                score += match;
            }
            // A named-argument candidate is applicable only if every required
            // parameter is supplied. Otherwise an earlier, longer overload can
            // hide the selected overload or falsely report its argument order.
            for (int p = 0; p < count; p++)
                if (supplied[p] == 0 && ast.Params[function.ParamStart + offset + p].DefaultExpr < 0) valid = false;
            if (valid && score > bestScore)
            {
                bestScore = score;
                firstBad = bad;
            }
        }
        if (bestScore >= 0)
        {
            if (firstBad >= 0) SubsetCuts.Report(13, ta.Tokens[firstBad], ref diags);
            return;
        }
        // Compiler intrinsics have no declaration AST. Their parameter names are
        // the same BCL-shaped names used by the normal call binder.
        if (name < 0) return;
        ReadOnlySpan<byte> method = names.Text(name);
        ReadOnlySpan<byte> first = ""u8;
        ReadOnlySpan<byte> second = ""u8;
        if (Bytes.Eq(method, "Slice"u8) || Bytes.Eq(method, "RemoveRange"u8)) { first = "start"u8; second = "length"u8; }
        else if (Bytes.Eq(method, "Resize"u8)) { first = "length"u8; second = "fill"u8; }
        else if (Bytes.Eq(method, "TryParse"u8)) { first = "s"u8; second = "result"u8; }
        else if (Bytes.Eq(method, "MapOr"u8)) { first = "fallback"u8; second = "map"u8; }
        else if (Bytes.Eq(method, "Match"u8)) { first = "ok"u8; second = "err"u8; }
        if (first.Length == 0) return;
        for (int a = 0; a < call.C; a++)
        {
            AstNode argument = ast.Nodes[ast.Extra[call.B + a]];
            int argumentName = CallArgBits.NameId(in argument);
            if (argumentName < 0) continue;
            ReadOnlySpan<byte> text = names.Text(argumentName);
            if ((Bytes.Eq(text, first) && a != 0) || (Bytes.Eq(text, second) && a != 1))
                SubsetCuts.Report(13, ta.Tokens[argument.TokIdx], ref diags);
        }
    }

    private static int ArgumentTypeScore(int expression, int typeNode, in AstArena ast, in TokenArena ta, in SubsetCutContext context)
    {
        if (expression < 0 || typeNode < 0) return 0;
        AstNode target = ast.Nodes[typeNode];
        if (target.Kind != AstKind.TypePrim) return 0;
        AstNode value = ast.Nodes[expression];
        TokKind actual = TokKind.Eof;
        if (value.Kind == AstKind.ExprIntLit)
        {
            TokKind literal = ta.Tokens[value.TokIdx].Kind;
            actual = literal == TokKind.LongLit ? TokKind.KwLong : literal == TokKind.UIntLit ? TokKind.KwUInt : literal == TokKind.ULongLit ? TokKind.KwULong : TokKind.KwInt;
        }
        else if (value.Kind == AstKind.ExprStrLit) actual = TokKind.KwString;
        else if (value.Kind == AstKind.ExprBoolLit) actual = TokKind.KwBool;
        else if (value.Kind == AstKind.ExprCharLit) actual = TokKind.KwChar;
        else if (value.Kind == AstKind.ExprIdent)
        {
            int local = FindLocal(value.A, value.TokIdx, in ast, in ta, in context);
            if (local >= 0 && ast.Nodes[local].Kind == AstKind.StmtVarDecl && ast.Nodes[local].B >= 0)
            {
                AstNode localType = ast.Nodes[ast.Nodes[local].B];
                if (localType.Kind == AstKind.TypePrim) actual = (TokKind)localType.A;
            }
        }
        if (actual == TokKind.Eof) return 0;
        TokKind expected = (TokKind)target.A;
        if (expected == actual) return 4;
        if (actual == TokKind.KwBool || expected == TokKind.KwBool || actual == TokKind.KwString || expected == TokKind.KwString) return -1;
        return 1;
    }
}
