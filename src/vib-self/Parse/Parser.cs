namespace Vibranium.Self;

public struct ParserCtx
{
    public int Pos;
    public int EndPos;
    public int SourceId;
    public int UnitKind;
    public int CurrentNsId;
    public int CurrentOwnerDeclId;
    public int InSwitchWhenGuard;
    public int ForceConditionalAccess;
    public int ReportedSubsetCuts;
    public IntMap<int> ExpressionStarts;
    public int UsingExtraStart;
    public int UsingCount;
    public int Nesting;
    public int PendingGt;
    public int SeenTypeOrNsInFile;
    public int NamespaceDepth;
    public int HasFileScopedNs;
    public int SeenNonGlobalUsingInFile;
    public int SeenMemberInNamespace;
    public IntMap<int> KnownTypes;
    public Vec<int> PendingLocalFuncs;
    public Vec<int> CurrentUsings;
    public Vec<int> AppGlobalUsings;

    public ParserCtx(int dummy)
    {
        Pos = 0;
        EndPos = 0;
        SourceId = 0;
        UnitKind = UnitKinds.App;
        CurrentNsId = -1;
        CurrentOwnerDeclId = -1;
        InSwitchWhenGuard = 0;
        ForceConditionalAccess = 0;
        ReportedSubsetCuts = 0;
        ExpressionStarts = new IntMap<int>(128);
        UsingExtraStart = 0;
        UsingCount = 0;
        Nesting = 0;
        PendingGt = 0;
        SeenTypeOrNsInFile = 0;
        NamespaceDepth = 0;
        HasFileScopedNs = 0;
        SeenNonGlobalUsingInFile = 0;
        SeenMemberInNamespace = 0;
        KnownTypes = new IntMap<int>(128);
        PendingLocalFuncs = new Vec<int>();
        CurrentUsings = new Vec<int>(8);
        AppGlobalUsings = new Vec<int>(4);
    }
}

public static class Parser
{
    private const int MaxNesting = 200;

    public static void ParseAll(
        in SourceTable sources,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        var ctx = new ParserCtx(0);
        SeedBuiltinTypeNames(ref names, ref ctx.KnownTypes);
        PreScanDeclaredTypeNames(in ta, ref names, ref ctx.KnownTypes);

        var fileResumePos = new Vec<int>(sources.Files.Count);
        for (int s = 0; s < sources.Files.Count; s++)
        {
            SourceFile sf = sources.Files[s];
            ctx.Pos = sf.FirstTokIdx;
            ctx.EndPos = sf.FirstTokIdx + sf.TokCount;
            ctx.SourceId = s;
            ctx.UnitKind = sf.UnitKind;
            ctx.CurrentNsId = -1;
            ctx.CurrentOwnerDeclId = -1;
            ctx.InSwitchWhenGuard = 0;
            ctx.ForceConditionalAccess = 0;
            ctx.ReportedSubsetCuts = 0;
            ctx.Nesting = 0;
            ctx.PendingGt = 0;
            ctx.SeenTypeOrNsInFile = 0;
            ctx.NamespaceDepth = 0;
            ctx.HasFileScopedNs = 0;
            ctx.SeenNonGlobalUsingInFile = 0;
            ctx.SeenMemberInNamespace = 0;
            ctx.CurrentUsings.Clear();
            ctx.UsingExtraStart = 0;
            ctx.UsingCount = 0;
            if (sf.UnitKind == UnitKinds.App)
            {
                while (PeekKind(in ctx, in ta, 0) == TokKind.KwGlobal && PeekKind(in ctx, in ta, 1) == TokKind.KwUsing)
                {
                    ParseUsingDirective(ref ctx, in ta, ref names, ref ast, ref diags, isGlobal: true, preScanGlobalOnly: true);
                }
            }
            fileResumePos.Push(ctx.Pos);
        }

        for (int s = 0; s < sources.Files.Count; s++)
        {
            SourceFile sf = sources.Files[s];
            ctx.Pos = fileResumePos[s];
            ctx.EndPos = sf.FirstTokIdx + sf.TokCount;
            ctx.SourceId = s;
            ctx.UnitKind = sf.UnitKind;
            ctx.CurrentNsId = -1;
            ctx.CurrentOwnerDeclId = -1;
            ctx.InSwitchWhenGuard = 0;
            ctx.ForceConditionalAccess = 0;
            ctx.ReportedSubsetCuts = 0;
            ctx.Nesting = 0;
            ctx.PendingGt = 0;
            ctx.SeenTypeOrNsInFile = 0;
            ctx.NamespaceDepth = 0;
            ctx.HasFileScopedNs = 0;
            ctx.SeenNonGlobalUsingInFile = 0;
            ctx.SeenMemberInNamespace = 0;
            ctx.CurrentUsings.Clear();
            if (sf.UnitKind == UnitKinds.App)
            {
                for (int gu = 0; gu < ctx.AppGlobalUsings.Count; gu++)
                {
                    ctx.CurrentUsings.Push(ctx.AppGlobalUsings[gu]);
                }
            }
            ctx.UsingExtraStart = ctx.CurrentUsings.Count > 0 ? ast.PushExtraSpan(ctx.CurrentUsings.AsReadOnlySpan()) : 0;
            ctx.UsingCount = ctx.CurrentUsings.Count;
            ast.SourceUnitKinds.Push(sf.UnitKind);
            ast.SourceUsingStarts.Push(ctx.UsingExtraStart);
            ast.SourceUsingCounts.Push(ctx.UsingCount);
            ParseCompilationUnit(ref ctx, in ta, ref names, ref ast, ref diags);
        }
        ValidateFrameworkAttributeUses(in ast, in ta, in names, ref diags);
        SubsetCutChecker.Check(in ast, in ta, in names, in ctx.ExpressionStarts, ref diags);
        if (!diags.HasErrors) CheckTypeDeclarationIdentities(in ast, in ta, ref diags);
    }

    private static void CheckTypeDeclarationIdentities(in AstArena ast, in TokenArena ta, ref DiagnosticBag diags)
    {
        // All source files have been parsed and the permitted trusted partial
        // fragments have already merged into one TypeDecl. No binding or IR yet.
        var spaces = new IntMap<byte>(ast.Types.Count);
        var signatures = new IntMap<byte>(ast.Types.Count);
        var declarations = new IntMap<byte>(ast.Types.Count);
        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            // A nested type belongs to its declaring type; a top-level type
            // belongs to its unit and namespace (not its source file).
            long space = td.OwnerDeclId >= 0 ? -1L - td.OwnerDeclId : ((long)td.UnitKind << 32) | (uint)td.NsId;
            long signature = ((long)td.NameId << 32) | (uint)td.TypeParamCount;
            int scopeId = InternDeclarationPart(space, ref spaces);
            int signatureId = InternDeclarationPart(signature, ref signatures);
            long key = ((long)scopeId << 32) | (uint)signatureId;
            if (declarations.Set(key, 0).IsSome)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[td.TokIdx], "A type with this name and generic arity is already declared in this scope."u8);
        }
    }

    private static int InternDeclarationPart(long key, ref IntMap<byte> parts)
    {
        int id = parts.Find(key);
        if (id >= 0) return id;
        id = parts.EntryCount;
        parts.Set(key, 0);
        return id;
    }

    private static void SeedBuiltinTypeNames(ref Interner names, ref IntMap<int> known)
    {
        known.Set(names.Intern("Option"u8), 1);
        known.Set(names.Intern("OptionRef"u8), 1);
        known.Set(names.Intern("OptionMut"u8), 1);
        known.Set(names.Intern("Some"u8), 1);
        known.Set(names.Intern("NoneType"u8), 1);
        known.Set(names.Intern("Result"u8), 1);
        known.Set(names.Intern("Ok"u8), 1);
        known.Set(names.Intern("Err"u8), 1);
        known.Set(names.Intern("Vec"u8), 1);
        known.Set(names.Intern("Span"u8), 1);
        known.Set(names.Intern("ReadOnlySpan"u8), 1);
        known.Set(names.Intern("Utf8Lines"u8), 1);
        known.Set(names.Intern("Utf8ArgEnumerator"u8), 1);
        known.Set(names.Intern("Boolean"u8), 1);
        known.Set(names.Intern("Byte"u8), 1);
        known.Set(names.Intern("SByte"u8), 1);
        known.Set(names.Intern("Int16"u8), 1);
        known.Set(names.Intern("UInt16"u8), 1);
        known.Set(names.Intern("Int32"u8), 1);
        known.Set(names.Intern("UInt32"u8), 1);
        known.Set(names.Intern("Int64"u8), 1);
        known.Set(names.Intern("UInt64"u8), 1);
        known.Set(names.Intern("Single"u8), 1);
        known.Set(names.Intern("Double"u8), 1);
        known.Set(names.Intern("Char"u8), 1);
        known.Set(names.Intern("String"u8), 1);
        known.Set(names.Intern("Object"u8), 1);
        known.Set(names.Intern("Unit"u8), 1);
        known.Set(names.Intern("IDisposable"u8), 1);
        known.Set(names.Intern("Nullable"u8), 1);
        known.Set(names.Intern("IntPtr"u8), 1);
        known.Set(names.Intern("UIntPtr"u8), 1);
        known.Set(names.Intern("Half"u8), 1);
        known.Set(names.Intern("Int128"u8), 1);
        known.Set(names.Intern("UInt128"u8), 1);
    }

    private static void PreScanDeclaredTypeNames(in TokenArena ta, ref Interner names, ref IntMap<int> known)
    {
        int n = ta.Tokens.Count;
        for (int i = 0; i + 1 < n; i++)
        {
            TokKind k = ta.Tokens[i].Kind;
            if (k == TokKind.KwClass || k == TokKind.KwStruct || k == TokKind.KwEnum ||
                k == TokKind.KwInterface || k == TokKind.KwDelegate)
            {
                Token next = ta.Tokens[i + 1];
                if (IsIdentLike(next.Kind))
                {
                    known.Set(TokenIdentId(next, ref names), 1);
                }
            }
            else if (k == TokKind.KwUsing && i + 2 < n &&
                     IsTypeIdentHead(ta.Tokens[i + 1].Kind) &&
                     ta.Tokens[i + 2].Kind == TokKind.Eq)
            {
                known.Set(TokenIdentId(ta.Tokens[i + 1], ref names), 1);
            }
        }
    }

    public static bool IsContextualIdentKeyword(TokKind k) =>
        k == TokKind.KwScoped || k == TokKind.KwPartial || k == TokKind.KwVar ||
        k == TokKind.KwWhere || k == TokKind.KwYield || k == TokKind.KwAsync ||
        k == TokKind.KwGlobal || k == TokKind.KwDynamic || k == TokKind.KwNInt || k == TokKind.KwNUInt;

    public static bool IsIdentLike(TokKind k) =>
        k == TokKind.Ident || IsContextualIdentKeyword(k);

    private static bool IsTypeIdentHead(TokKind k) =>
        k == TokKind.Ident || (IsContextualIdentKeyword(k) && k != TokKind.KwVar && k != TokKind.KwDynamic);

    private static ReadOnlySpan<byte> ContextualKeywordText(TokKind k)
    {
        switch (k)
        {
            case TokKind.KwScoped: return "scoped"u8;
            case TokKind.KwPartial: return "partial"u8;
            case TokKind.KwVar: return "var"u8;
            case TokKind.KwWhere: return "where"u8;
            case TokKind.KwYield: return "yield"u8;
            case TokKind.KwAsync: return "async"u8;
            case TokKind.KwGlobal: return "global"u8;
            case TokKind.KwDynamic: return "dynamic"u8;
            case TokKind.KwNInt: return "nint"u8;
            case TokKind.KwNUInt: return "nuint"u8;
            default: return ""u8;
        }
    }

    public static int InternContextualKeyword(TokKind k, ref Interner names) =>
        names.Intern(ContextualKeywordText(k));

    private static int TokenIdentId(Token t, ref Interner names)
    {
        if (t.Kind == TokKind.Ident) return t.AuxId;
        if (IsContextualIdentKeyword(t.Kind)) return InternContextualKeyword(t.Kind, ref names);
        return -1;
    }

    private static int FindInternedName(ReadOnlySpan<byte> text, in Interner names)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (Bytes.Eq(names.Text(i), text)) return i;
        }
        return -1;
    }

    private static int LookupTokenKnownId(Token t, in Interner names)
    {
        if (t.Kind == TokKind.Ident) return t.AuxId;
        if (IsContextualIdentKeyword(t.Kind)) return FindInternedName(ContextualKeywordText(t.Kind), in names);
        return -1;
    }

    private static Token Peek(in ParserCtx ctx, in TokenArena ta, int offset)
    {
        int idx = ctx.Pos + offset;
        if (idx < 0) return ta.Tokens[0];
        if (idx >= ctx.EndPos) return ta.Tokens[ctx.EndPos - 1];
        return ta.Tokens[idx];
    }

    private static TokKind PeekKind(in ParserCtx ctx, in TokenArena ta, int offset)
    {
        if (offset == 0 && ctx.PendingGt > 0)
        {
            return TokKind.RAngle;
        }
        return Peek(in ctx, in ta, offset).Kind;
    }

    private static Token Advance(ref ParserCtx ctx, in TokenArena ta)
    {
        Token t = Peek(in ctx, in ta, 0);
        if (ctx.PendingGt > 0)
        {
            ctx.PendingGt -= 1;
            if (ctx.PendingGt == 0 && ctx.Pos < ctx.EndPos - 1)
            {
                ctx.Pos += 1;
            }
            return new Token(TokKind.RAngle, t.SourceId, t.Start, 1, -1, 0);
        }
        if (ctx.Pos < ctx.EndPos - 1)
        {
            ctx.Pos += 1;
        }
        return t;
    }

    private static bool IsAtTypeArgClose(in ParserCtx ctx, in TokenArena ta)
    {
        if (ctx.PendingGt > 0) return true;
        TokKind k = Peek(in ctx, in ta, 0).Kind;
        return k == TokKind.RAngle || k == TokKind.GtGt || k == TokKind.GtGtGt;
    }

    private static void ExpectTypeArgClose(ref ParserCtx ctx, in TokenArena ta, ref DiagnosticBag diags, ReadOnlySpan<byte> msg)
    {
        if (ctx.PendingGt > 0)
        {
            ctx.PendingGt -= 1;
            if (ctx.PendingGt == 0 && ctx.Pos < ctx.EndPos - 1)
            {
                ctx.Pos += 1;
            }
            return;
        }
        Token t = Peek(in ctx, in ta, 0);
        if (t.Kind == TokKind.RAngle)
        {
            if (ctx.Pos < ctx.EndPos - 1) ctx.Pos += 1;
            return;
        }
        if (t.Kind == TokKind.GtGt)
        {
            ctx.PendingGt = 1;
            return;
        }
        if (t.Kind == TokKind.GtGtGt)
        {
            ctx.PendingGt = 2;
            return;
        }
        diags.ReportTok(VibCodes.Unsupported, t, msg);
    }

    private static bool Match(ref ParserCtx ctx, in TokenArena ta, TokKind kind)
    {
        if (PeekKind(in ctx, in ta, 0) == kind)
        {
            Advance(ref ctx, in ta);
            return true;
        }
        return false;
    }

    private static Token Expect(ref ParserCtx ctx, in TokenArena ta, TokKind kind, ref DiagnosticBag diags, ReadOnlySpan<byte> msg)
    {
        Token t = Peek(in ctx, in ta, 0);
        if (t.Kind == kind)
        {
            return Advance(ref ctx, in ta);
        }
        diags.ReportTok(VibCodes.Unsupported, t, msg);
        return t;
    }

    private static int ExpectDeclaredIdent(
        ref ParserCtx ctx, in TokenArena ta, ref Interner names, ref DiagnosticBag diags,
        ReadOnlySpan<byte> message, out Token token)
    {
        int nameId = ExpectIdent(ref ctx, in ta, ref names, ref diags, message, out token);
        if (nameId >= 0 && Bytes.Eq(names.Text(nameId), "_"u8))
            SubsetCuts.Report(11, token, ref diags);
        return nameId;
    }

    private static int ExpectIdent(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref DiagnosticBag diags,
        ReadOnlySpan<byte> msg,
        out Token outTok)
    {
        outTok = Peek(in ctx, in ta, 0);
        if (outTok.Kind == TokKind.Ident)
        {
            outTok = Advance(ref ctx, in ta);
            return outTok.AuxId;
        }
        if (IsContextualIdentKeyword(outTok.Kind))
        {
            outTok = Advance(ref ctx, in ta);
            return InternContextualKeyword(outTok.Kind, ref names);
        }
        diags.ReportTok(VibCodes.Unsupported, outTok, msg);
        return -1;
    }

    private static void RecordDeclaredNamespace(int fullNsId, ref Interner names, ref AstArena ast)
    {
        if (fullNsId < 0) return;
        var fullCopy = new StringBuf(names.Text(fullNsId).Length);
        fullCopy.Append(names.Text(fullNsId));
        ReadOnlySpan<byte> fullSlice = fullCopy.AsUtf8();
        var prefixBuf = new StringBuf(fullSlice.Length);
        for (int i = 0; i < fullSlice.Length; i++)
        {
            byte b = fullSlice[i];
            if (b == (byte)'.')
            {
                int prefId = names.Intern(prefixBuf.AsUtf8());
                AddUniqueInt(prefId, ref ast.DeclaredNamespaces);
            }
            prefixBuf.AppendAscii(b);
        }
        AddUniqueInt(fullNsId, ref ast.DeclaredNamespaces);
    }

    private static void AddUniqueInt(int val, ref Vec<int> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == val) return;
        }
        list.Push(val);
    }

    private static int CombineNamespaceIds(int parentNsId, int childNsId, ref Interner names)
    {
        if (parentNsId < 0) return childNsId;
        if (childNsId < 0) return parentNsId;
        var sb = new StringBuf(64);
        sb.Append(names.Text(parentNsId));
        sb.AppendAscii((byte)'.');
        sb.Append(names.Text(childNsId));
        return names.Intern(sb.AsUtf8());
    }

    private static void ParseCompilationUnit(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        while (PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            TokKind k = PeekKind(in ctx, in ta, 0);

            if (k == TokKind.KwGlobal && PeekKind(in ctx, in ta, 1) == TokKind.KwUsing)
            {
                ParseUsingDirective(ref ctx, in ta, ref names, ref ast, ref diags, isGlobal: true, preScanGlobalOnly: false);
                continue;
            }
            if (k == TokKind.KwUsing && !IsUsingVarStatement(in ctx, in ta))
            {
                ParseUsingDirective(ref ctx, in ta, ref names, ref ast, ref diags, isGlobal: false, preScanGlobalOnly: false);
                continue;
            }

            if (k == TokKind.KwNamespace)
            {
                ParseNamespaceDeclaration(ref ctx, in ta, ref names, ref ast, ref diags);
                continue;
            }

            ParseNamespaceOrTopItem(ref ctx, in ta, ref names, ref ast, ref diags, allowTopStmts: ctx.NamespaceDepth == 0);
        }
    }

    private static void ParseNamespaceDeclaration(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        bool hadOtherNsInFile = ctx.HasFileScopedNs != 0;
        ctx.SeenTypeOrNsInFile = 1;
        ctx.SeenMemberInNamespace = 1;
        Token nsKwTok = Advance(ref ctx, in ta);
        int namespaceStart = ctx.Pos;
        int relNsId = ParseQualifiedName(ref ctx, in ta, ref names, ref diags);
        for (int component = namespaceStart; component < ctx.Pos; component++)
            if (ta.Tokens[component].Kind == TokKind.Ident && Bytes.Eq(names.Text(ta.Tokens[component].AuxId), "_"u8))
                SubsetCuts.Report(11, ta.Tokens[component], ref diags);
        int fullNsId = CombineNamespaceIds(ctx.CurrentNsId, relNsId, ref names);
        RecordDeclaredNamespace(fullNsId, ref names, ref ast);

        if (Match(ref ctx, in ta, TokKind.Semi))
        {
            if (ctx.NamespaceDepth > 0 || hadOtherNsInFile)
            {
                diags.ReportTok(VibCodes.Unsupported, nsKwTok, "File-scoped namespace declaration cannot be nested or combined with another namespace declaration."u8);
            }
            ctx.HasFileScopedNs = 1;
            ctx.CurrentNsId = fullNsId;
            ctx.NamespaceDepth = 1;
            ctx.SeenMemberInNamespace = 0;
            return;
        }

        if (hadOtherNsInFile)
        {
            diags.ReportTok(VibCodes.Unsupported, nsKwTok, "Cannot declare a block-scoped namespace in a file with a file-scoped namespace."u8);
        }

        if (Match(ref ctx, in ta, TokKind.LBrace))
        {
            int prevNs = ctx.CurrentNsId;
            int prevNsDepth = ctx.NamespaceDepth;
            int prevUsingCount = ctx.CurrentUsings.Count;
            int prevUsingStart = ctx.UsingExtraStart;
            ctx.CurrentNsId = fullNsId;
            ctx.NamespaceDepth = prevNsDepth + 1;
            ctx.SeenMemberInNamespace = 0;
            while (PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
            {
                TokKind nk = PeekKind(in ctx, in ta, 0);
                if (nk == TokKind.KwGlobal && PeekKind(in ctx, in ta, 1) == TokKind.KwUsing)
                {
                    ParseUsingDirective(ref ctx, in ta, ref names, ref ast, ref diags, isGlobal: true, preScanGlobalOnly: false);
                    continue;
                }
                if (nk == TokKind.KwUsing && !IsUsingVarStatement(in ctx, in ta))
                {
                    ParseUsingDirective(ref ctx, in ta, ref names, ref ast, ref diags, isGlobal: false, preScanGlobalOnly: false);
                    continue;
                }
                if (nk == TokKind.KwNamespace)
                {
                    ParseNamespaceDeclaration(ref ctx, in ta, ref names, ref ast, ref diags);
                    continue;
                }
                ParseNamespaceOrTopItem(ref ctx, in ta, ref names, ref ast, ref diags, allowTopStmts: false);
            }
            Expect(ref ctx, in ta, TokKind.RBrace, ref diags, "Expected '}' after namespace body."u8);
            Match(ref ctx, in ta, TokKind.Semi);
            ctx.CurrentNsId = prevNs;
            ctx.NamespaceDepth = prevNsDepth;
            ctx.SeenMemberInNamespace = 1;
            ctx.CurrentUsings.Truncate(prevUsingCount);
            ctx.UsingCount = prevUsingCount;
            ctx.UsingExtraStart = prevUsingStart;
            return;
        }

        diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "Expected '{' or ';' after namespace name."u8);
    }

    private static bool IsUsingVarStatement(in ParserCtx ctx, in TokenArena ta)
    {
        if (ctx.NamespaceDepth > 0) return false;
        TokKind k1 = PeekKind(in ctx, in ta, 1);
        if (k1 == TokKind.LParen) return true;
        if (k1 == TokKind.KwVar && IsIdentLike(PeekKind(in ctx, in ta, 2))) return true;
        if (k1 == TokKind.KwStatic || k1 == TokKind.KwUnsafe) return false;
        if (IsIdentLike(k1) && PeekKind(in ctx, in ta, 2) == TokKind.Eq) return false;
        if (TryScanTypeSyntax(in ta, ctx.Pos + 1, ctx.EndPos, out int afterType))
        {
            if (afterType < ctx.EndPos && IsIdentLike(ta.Tokens[afterType].Kind)) return true;
        }
        return false;
    }

    private static void ParseUsingDirective(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        bool isGlobal,
        bool preScanGlobalOnly)
    {
        int usingTokIdx = ctx.Pos;
        Token uTok = Peek(in ctx, in ta, 0);
        if (isGlobal)
        {
            Advance(ref ctx, in ta); // global
            if (!preScanGlobalOnly)
            {
                if (ctx.NamespaceDepth > 0)
                {
                    diags.ReportTok(VibCodes.Unsupported, uTok, "'global using' directives cannot appear inside a namespace declaration."u8);
                }
                else if (ctx.SeenNonGlobalUsingInFile != 0 || ctx.SeenTypeOrNsInFile != 0 || ast.TopSourceId == ctx.SourceId)
                {
                    diags.ReportTok(VibCodes.Unsupported, uTok, "'global using' directives must precede all non-global 'using' directives and declarations."u8);
                }
            }
        }
        else
        {
            if (ctx.NamespaceDepth == 0)
            {
                ctx.SeenNonGlobalUsingInFile = 1;
                if (ctx.SeenTypeOrNsInFile != 0 || ast.TopSourceId == ctx.SourceId)
                {
                    diags.ReportTok(VibCodes.Unsupported, uTok, "'using' directives must precede all other elements in a compilation unit."u8);
                }
            }
            else if (ctx.SeenMemberInNamespace != 0)
            {
                diags.ReportTok(VibCodes.Unsupported, uTok, "'using' directives must precede all member declarations in a namespace."u8);
            }
        }

        Advance(ref ctx, in ta); // using

        if (Match(ref ctx, in ta, TokKind.KwUnsafe))
        {
            diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, -1), "'using unsafe' directives are banned in Vibranium."u8);
            SkipToSemi(ref ctx, in ta);
            return;
        }

        int scopeLevel = isGlobal ? 0 : (1 + ctx.NamespaceDepth);
        int outerCount = 0;
        for (int i = 0; i < ctx.CurrentUsings.Count; i++)
        {
            if (ast.Usings[ctx.CurrentUsings[i]].ScopeLevel < scopeLevel)
            {
                outerCount += 1;
            }
        }
        int savedUStart = ctx.UsingExtraStart;
        int savedUCount = ctx.UsingCount;
        int savedOwner = ctx.CurrentOwnerDeclId;
        ctx.UsingExtraStart = outerCount > 0 ? ast.PushExtraSpan(ctx.CurrentUsings.AsReadOnlySpan().Slice(0, outerCount)) : 0;
        ctx.UsingCount = outerCount;
        ctx.CurrentOwnerDeclId = -1;

        UsingEntry entry;
        bool hasValidEntry = false;

        if (Match(ref ctx, in ta, TokKind.KwStatic))
        {
            int targetNode = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags, allowNullableSuffix: true);
            Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after 'using static' directive."u8);
            entry = new UsingEntry(UsingKinds.Static, ctx.UnitKind, scopeLevel, ctx.CurrentNsId, -1, targetNode, ctx.SourceId, usingTokIdx);
            hasValidEntry = true;
        }
        else if (IsIdentLike(PeekKind(in ctx, in ta, 0)) && PeekKind(in ctx, in ta, 1) == TokKind.Eq)
        {
            int aliasId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected alias identifier."u8, out Token aliasTok);
            if (aliasTok.Kind == TokKind.KwVar || aliasTok.Kind == TokKind.KwDynamic)
            {
                diags.ReportTok(VibCodes.Unsupported, aliasTok, "The contextual keyword 'var' or 'dynamic' cannot be used as a 'using' alias name."u8);
            }
            Advance(ref ctx, in ta); // '='
            int targetNode = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags, allowNullableSuffix: true);
            Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after 'using' alias directive."u8);

            if (aliasId >= 0)
            {
                for (int i = 0; i < ctx.CurrentUsings.Count; i++)
                {
                    UsingEntry prev = ast.Usings[ctx.CurrentUsings[i]];
                    if (prev.ScopeLevel == scopeLevel && prev.Kind == UsingKinds.Alias && prev.AliasOrNsId == aliasId)
                    {
                        diags.ReportTok(VibCodes.Unsupported, aliasTok, "Duplicate 'using' alias in the same scope."u8);
                        break;
                    }
                }
                if (isGlobal)
                {
                    for (int i = 0; i < ctx.AppGlobalUsings.Count; i++)
                    {
                        UsingEntry prev = ast.Usings[ctx.AppGlobalUsings[i]];
                        if (prev.Kind == UsingKinds.Alias && prev.AliasOrNsId == aliasId)
                        {
                            diags.ReportTok(VibCodes.Unsupported, aliasTok, "Duplicate 'global using' alias."u8);
                            break;
                        }
                    }
                }
                ctx.KnownTypes.Set(aliasId, 1);
                entry = new UsingEntry(UsingKinds.Alias, ctx.UnitKind, scopeLevel, ctx.CurrentNsId, aliasId, targetNode, ctx.SourceId, usingTokIdx);
                hasValidEntry = true;
            }
            else
            {
                entry = default;
            }
        }
        else
        {
            int targetNode = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags, allowNullableSuffix: true);
            Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after 'using' directive."u8);
            int uNs = ExtractNamespacePathFromTypeNode(targetNode, ref names, in ast);
            entry = new UsingEntry(UsingKinds.Namespace, ctx.UnitKind, scopeLevel, ctx.CurrentNsId, uNs, targetNode, ctx.SourceId, usingTokIdx);
            hasValidEntry = true;
        }

        ctx.UsingExtraStart = savedUStart;
        ctx.UsingCount = savedUCount;
        ctx.CurrentOwnerDeclId = savedOwner;

        if (hasValidEntry)
        {
            int uIdx = ast.Usings.Count;
            ast.Usings.Push(entry);
            if (preScanGlobalOnly)
            {
                ctx.AppGlobalUsings.Push(uIdx);
                return;
            }
            ctx.CurrentUsings.Push(uIdx);
            if (isGlobal && ctx.UnitKind == UnitKinds.App)
            {
                ctx.AppGlobalUsings.Push(uIdx);
            }
            ctx.UsingExtraStart = ast.PushExtraSpan(ctx.CurrentUsings.AsReadOnlySpan());
            ctx.UsingCount = ctx.CurrentUsings.Count;
            if (ctx.NamespaceDepth == 0)
            {
                ast.SourceUsingStarts[ctx.SourceId] = ctx.UsingExtraStart;
                ast.SourceUsingCounts[ctx.SourceId] = ctx.UsingCount;
            }
        }
    }

    private static int ExtractNamespacePathFromTypeNode(int typeNodeId, ref Interner names, in AstArena ast)
    {
        if (typeNodeId < 0) return -1;
        AstNode tn = ast.Nodes[typeNodeId];
        if (tn.Kind != AstKind.TypeNamed || tn.C != 0) return -1;
        TypeRefContext trc = ast.TypeRefs[tn.D];
        if (trc.PrefixNsId < 0)
        {
            return tn.A;
        }
        ReadOnlySpan<byte> pref = names.Text(trc.PrefixNsId);
        if (Bytes.Eq(pref, "global::"u8))
        {
            return tn.A;
        }
        if (pref.StartsWith("global::"u8))
        {
            pref = pref.Slice(8);
        }
        var sb = new StringBuf(pref.Length + 1 + names.Text(tn.A).Length);
        sb.Append(pref);
        sb.AppendAscii((byte)'.');
        sb.Append(names.Text(tn.A));
        return names.Intern(sb.AsUtf8());
    }

    private static int ParseQualifiedName(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref DiagnosticBag diags)
    {
        var sb = new StringBuf(64);
        int firstId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected identifier."u8, out _);
        if (firstId >= 0) sb.Append(names.Text(firstId));
        while (Match(ref ctx, in ta, TokKind.Dot))
        {
            sb.AppendAscii((byte)'.');
            int partId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected identifier after '.'."u8, out _);
            if (partId >= 0) sb.Append(names.Text(partId));
        }
        return names.Intern(sb.AsUtf8());
    }

    private static void ParseNamespaceOrTopItem(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        bool allowTopStmts)
    {
        int savePos = ctx.Pos;
        if (LooksLikeTypeDeclaration(in ctx, in ta, in names, in ast))
        {
            ctx.SeenTypeOrNsInFile = 1;
            ctx.SeenMemberInNamespace = 1;
            ParseTypeDeclaration(ref ctx, in ta, ref names, ref ast, ref diags);
            return;
        }

        if (!allowTopStmts)
        {
            Token bad = Advance(ref ctx, in ta);
            diags.ReportTok(VibCodes.Unsupported, bad, "Unexpected token at namespace level."u8);
            return;
        }

        if (ctx.SeenTypeOrNsInFile != 0)
        {
            diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "Top-level statements must precede namespace and type declarations."u8);
        }

        if (ast.TopSourceId < 0)
        {
            ast.TopSourceId = ctx.SourceId;
            ast.TopTokIdx = savePos;
        }

        if (LooksLikeLocalFunction(in ctx, in ta))
        {
            int lfId = ParseLocalFunction(ref ctx, in ta, ref names, ref ast, ref diags);
            if (lfId >= 0) ast.TopLocalFuncs.Push(lfId);
            return;
        }

        int stmt = ParseStatement(ref ctx, in ta, ref names, ref ast, ref diags);
        PushStmtFlattened(stmt, in ast.Nodes, in ast.Extra, ref ast.TopStmts);
        if (ctx.Pos == savePos && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            Advance(ref ctx, in ta);
        }
    }

    private static bool LooksLikeTypeDeclaration(in ParserCtx ctx, in TokenArena ta, in Interner names, in AstArena ast)
    {
        int p = ctx.Pos;
        while (p < ctx.EndPos && ta.Tokens[p].Kind == TokKind.LBracket)
        {
            p = SkipBracketBalanced(in ta, p, ctx.EndPos);
        }
        bool hasCutModifier = false;
        while (p < ctx.EndPos && (IsModifierKeyword(ta.Tokens[p].Kind) || IsFileModifier(ta.Tokens[p], in names)))
        {
            if (ta.Tokens[p].Kind == TokKind.KwPartial || IsFileModifier(ta.Tokens[p], in names)) hasCutModifier = true;
            p += 1;
        }
        // Records remain unsupported. Recognize their headers only when a new
        // declaration cut already applies, for canonical diagnostics/recovery.
        if (p < ctx.EndPos && IsRecordKeyword(ta.Tokens[p], in names) &&
            (hasCutModifier || HasGenericTypeAncestor(ctx.CurrentOwnerDeclId, in ast))) return true;
        if (p + 1 < ctx.EndPos && ta.Tokens[p].Kind == TokKind.KwRef && ta.Tokens[p + 1].Kind == TokKind.KwStruct)
        {
            return true;
        }
        if (p < ctx.EndPos)
        {
            TokKind k = ta.Tokens[p].Kind;
            return k == TokKind.KwClass || k == TokKind.KwStruct || k == TokKind.KwEnum ||
                   k == TokKind.KwInterface || k == TokKind.KwDelegate;
        }
        return false;
    }

    private static bool IsModifierKeyword(TokKind k) =>
        k == TokKind.KwPublic || k == TokKind.KwInternal || k == TokKind.KwPrivate ||
        k == TokKind.KwProtected || k == TokKind.KwStatic || k == TokKind.KwSealed ||
        k == TokKind.KwAbstract || k == TokKind.KwVirtual || k == TokKind.KwOverride ||
        k == TokKind.KwPartial || k == TokKind.KwReadonly || k == TokKind.KwUnsafe ||
        k == TokKind.KwAsync;

    private static bool IsFileModifier(Token token, in Interner names) =>
        token.Kind == TokKind.Ident && token.Length == 4 && Bytes.Eq(names.Text(token.AuxId), "file"u8);

    private static bool IsRecordKeyword(Token token, in Interner names) =>
        token.Kind == TokKind.Ident && token.Length == 6 && Bytes.Eq(names.Text(token.AuxId), "record"u8);

    private static bool HasGenericTypeAncestor(int ancestor, in AstArena ast)
    {
        while (ancestor >= 0)
        {
            if (ast.Types[ancestor].TypeParamCount > 0) return true;
            ancestor = ast.Types[ancestor].OwnerDeclId;
        }
        return false;
    }

    private static ReadOnlySpan<byte> SubsetCutMessage(int cut)
    {
        if (cut == 1) return "File-local types (file class, file struct) are not supported in Vibranium v0.2; declare the type without the file modifier."u8;
        if (cut == 2) return "Partial types and members are not supported in Vibranium v0.2."u8;
        if (cut == 4) return "Types nested inside a generic type are not supported in Vibranium v0.2; declare the type at namespace level."u8;
        if (cut == 16) return "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance."u8;
        return "Constructor chaining (: this(...)) is not supported in Vibranium v0.2; initialize every field in each constructor or use a static factory method."u8;
    }

    private static void ReportSubsetCut(ref ParserCtx ctx, int cut, Token token, ref DiagnosticBag diags)
    {
        if ((ctx.ReportedSubsetCuts & cut) != 0) return;
        ctx.ReportedSubsetCuts |= cut;
        diags.ReportTok(VibCodes.Unsupported, token, SubsetCutMessage(cut));
    }

    private static void KeepOnlySubsetCutsSince(int start, ref DiagnosticBag diags)
    {
        int write = start;
        for (int read = start; read < diags.Items.Count; read++)
        {
            DiagEntry entry = diags.Items[read];
            bool keep = SubsetCuts.IsMessage(diags.MsgPool.Get(entry.Message));
            for (int cut = 1; cut <= 16; cut *= 2)
            {
                if (entry.Code == VibCodes.Unsupported && Bytes.Eq(diags.MsgPool.Get(entry.Message), SubsetCutMessage(cut))) keep = true;
            }
            if (keep) { diags.Items[write] = entry; write += 1; }
        }
        diags.Items.Truncate(write);
    }

    private static bool IsTrustedSysType(in ParserCtx ctx, int nameId, int enclosingOwnerDeclId, in Interner names) =>
        ctx.UnitKind == UnitKinds.Std && enclosingOwnerDeclId < 0 && nameId >= 0 &&
        Bytes.Eq(names.Text(nameId), "Sys"u8) && ctx.CurrentNsId >= 0 && Bytes.Eq(names.Text(ctx.CurrentNsId), "Vibranium.Std"u8);

    private static int FindModifierToken(in ParserCtx ctx, in TokenArena ta, in Interner names, TokKind kind)
    {
        int p = ctx.Pos;
        while (p < ctx.EndPos && ta.Tokens[p].Kind == TokKind.LBracket) p = SkipBracketBalanced(in ta, p, ctx.EndPos);
        while (p < ctx.EndPos && (IsModifierKeyword(ta.Tokens[p].Kind) || IsFileModifier(ta.Tokens[p], in names)))
        {
            if (ta.Tokens[p].Kind == kind) return p;
            p += 1;
        }
        return -1;
    }

    private static int SkipBracketBalanced(in TokenArena ta, int p, int endPos)
    {
        int depth = 0;
        while (p < endPos)
        {
            TokKind k = ta.Tokens[p].Kind;
            if (k == TokKind.LBracket) depth += 1;
            else if (k == TokKind.RBracket)
            {
                depth -= 1;
                if (depth == 0) return p + 1;
            }
            else if (k == TokKind.Eof) return p;
            p += 1;
        }
        return p;
    }

    private static void ParseAttributes(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int targetKind,
        ref int flags,
        ref int nativeSymLitIdx,
        ref Vec<int> returnLifetimes)
    {
        while (PeekKind(in ctx, in ta, 0) == TokKind.LBracket)
        {
            Advance(ref ctx, in ta);
            while (PeekKind(in ctx, in ta, 0) != TokKind.RBracket && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
            {
                bool isReturnTarget = false;
                if (PeekKind(in ctx, in ta, 0) == TokKind.KwReturn && PeekKind(in ctx, in ta, 1) == TokKind.Colon)
                {
                    isReturnTarget = true;
                    Advance(ref ctx, in ta);
                    Advance(ref ctx, in ta);
                }
                else if (PeekKind(in ctx, in ta, 0) == TokKind.Ident && PeekKind(in ctx, in ta, 1) == TokKind.Colon)
                {
                    Advance(ref ctx, in ta);
                    Advance(ref ctx, in ta);
                }

                Token attrTok = Peek(in ctx, in ta, 0);
                int lastAttrNameId = ParseAttributeName(ref ctx, in ta, ref names, ref ast, ref diags,
                    out int frameworkAttribute, out int frameworkUse);
                ReadOnlySpan<byte> attrName = lastAttrNameId >= 0 ? names.Text(lastAttrNameId) : ""u8;

                if (Bytes.Eq(attrName, "Mut"u8) || Bytes.Eq(attrName, "MutAttribute"u8))
                {
                    flags |= DeclFlags.MutAttr;
                    SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                }
                else if (Bytes.Eq(attrName, "UnscopedRef"u8) || Bytes.Eq(attrName, "UnscopedRefAttribute"u8))
                {
                    if (frameworkAttribute == 1)
                        flags |= DeclFlags.UnscopedRefAttr;
                    else
                        diags.ReportTok(VibCodes.Unsupported, attrTok, "Cannot resolve the UnscopedRef attribute type (CS0400)."u8);
                    SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                }
                else if ((Bytes.Eq(attrName, "MethodImpl"u8) || Bytes.Eq(attrName, "MethodImplAttribute"u8)) &&
                    frameworkAttribute == 2)
                {
                    int argumentStart = ctx.Pos;
                    if (Match(ref ctx, in ta, TokKind.LParen) && PeekKind(in ctx, in ta, 0) != TokKind.RParen)
                    {
                        int argument = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                        // MethodImpl's positional options cannot encode the two
                        // MethodCodeType bits (CS0591), and C# reserves the
                        // MethodImplOptions.Async bit 0x2000 for the compiler
                        // (CS9330). Do not reject unrelated/unknown flag bits,
                        // or infer the value of a source constant.
                        if (TryEvalMethodImplOptions(argument, in ctx, in ast, in ta, in names, out long options))
                        {
                            int invalid = (options & 3L) != 0 ? 4 : (options & 0x2000L) != 0 ? 8 : 0;
                            if (invalid != 0)
                            {
                                // Check only after all source declarations can participate in name lookup.
                                AstNode use = ast.Nodes[frameworkUse];
                                ast.Nodes[frameworkUse] = new AstNode(use.Kind, use.TokIdx, use.A, use.B, use.C, use.D | invalid);
                            }
                        }
                    }
                    ctx.Pos = argumentStart;
                    SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                }
                else if (Bytes.Eq(attrName, "Obsolete"u8) || Bytes.Eq(attrName, "ObsoleteAttribute"u8))
                {
                    flags |= DeclFlags.HasObsoleteAttr;
                    SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                }
                else if (Bytes.Eq(attrName, "Native"u8) || Bytes.Eq(attrName, "NativeAttribute"u8))
                {
                    flags |= DeclFlags.HasNativeAttr;
                    if (targetKind != 1 || isReturnTarget)
                    {
                        diags.ReportTok(VibCodes.Unsupported, attrTok, "'[Native]' is only valid on Vibranium.Std.Sys catalog methods."u8);
                    }
                    if (Match(ref ctx, in ta, TokKind.LParen))
                    {
                        if (PeekKind(in ctx, in ta, 0) == TokKind.StrLit)
                        {
                            nativeSymLitIdx = Advance(ref ctx, in ta).AuxId;
                        }
                        SkipUntilRParen(ref ctx, in ta);
                    }
                }
                else if (Bytes.Eq(attrName, "LifetimeOf"u8) || Bytes.Eq(attrName, "LifetimeOfAttribute"u8))
                {
                    if (targetKind != 1 || !isReturnTarget)
                    {
                        diags.ReportTok(VibCodes.Unsupported, attrTok, "'[LifetimeOf]' is only supported as '[return: LifetimeOf(...)]' on methods in Vibranium."u8);
                        SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                    }
                    else
                    {
                        flags |= DeclFlags.HasReturnLifetimeAttr;
                        if (Match(ref ctx, in ta, TokKind.LParen))
                        {
                            while (PeekKind(in ctx, in ta, 0) != TokKind.RParen && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                            {
                                if (PeekKind(in ctx, in ta, 0) == TokKind.StrLit)
                                {
                                    Token st = Advance(ref ctx, in ta);
                                    returnLifetimes.Push(names.Intern(ta.GetLit(st.AuxId)));
                                }
                                else if (PeekKind(in ctx, in ta, 0) == TokKind.Ident &&
                                         Bytes.Eq(names.Text(Peek(in ctx, in ta, 0).AuxId), "nameof"u8) &&
                                         PeekKind(in ctx, in ta, 1) == TokKind.LParen)
                                {
                                    Advance(ref ctx, in ta);
                                    Advance(ref ctx, in ta);
                                    if (IsIdentLike(PeekKind(in ctx, in ta, 0)))
                                    {
                                        returnLifetimes.Push(TokenIdentId(Advance(ref ctx, in ta), ref names));
                                    }
                                    else if (PeekKind(in ctx, in ta, 0) == TokKind.KwThis)
                                    {
                                        Advance(ref ctx, in ta);
                                        returnLifetimes.Push(names.Intern("this"u8));
                                    }
                                    Match(ref ctx, in ta, TokKind.RParen);
                                }
                                else
                                {
                                    Advance(ref ctx, in ta);
                                }
                                Match(ref ctx, in ta, TokKind.Comma);
                            }
                            Match(ref ctx, in ta, TokKind.RParen);
                        }
                    }
                }
                else if (Bytes.Eq(attrName, "Union"u8) || Bytes.Eq(attrName, "UnionAttribute"u8) ||
                         Bytes.Eq(attrName, "InlineArray"u8) || Bytes.Eq(attrName, "InlineArrayAttribute"u8))
                {
                    if (ctx.UnitKind != UnitKinds.Core)
                    {
                        diags.ReportTok(VibCodes.Unsupported, attrTok, "Union and InlineArray attributes on user types are not supported in Vibranium."u8);
                    }
                    SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                }
                else if (Bytes.Eq(attrName, "FieldOffset"u8) || Bytes.Eq(attrName, "FieldOffsetAttribute"u8))
                {
                    diags.ReportTok(VibCodes.Unsupported, attrTok, "'[FieldOffset]' is banned in Vibranium."u8);
                    SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                }
                else if (Bytes.Eq(attrName, "StructLayout"u8) || Bytes.Eq(attrName, "StructLayoutAttribute"u8))
                {
                    if (Match(ref ctx, in ta, TokKind.LParen))
                    {
                        while (PeekKind(in ctx, in ta, 0) != TokKind.RParen && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                        {
                            Token tk = Advance(ref ctx, in ta);
                            if ((tk.Kind == TokKind.Ident && Bytes.Eq(names.Text(tk.AuxId), "Explicit"u8)) ||
                                (tk.Kind == TokKind.IntLit && tk.IntVal == 2))
                            {
                                diags.ReportTok(VibCodes.Unsupported, attrTok, "'[StructLayout(LayoutKind.Explicit)]' is banned in Vibranium."u8);
                            }
                        }
                        Match(ref ctx, in ta, TokKind.RParen);
                    }
                }
                else if (Bytes.Eq(attrName, "DllImport"u8) || Bytes.Eq(attrName, "DllImportAttribute"u8) ||
                         Bytes.Eq(attrName, "LibraryImport"u8) || Bytes.Eq(attrName, "LibraryImportAttribute"u8) ||
                         Bytes.Eq(attrName, "UnmanagedCallersOnly"u8) || Bytes.Eq(attrName, "UnmanagedCallersOnlyAttribute"u8))
                {
                    diags.ReportTok(VibCodes.Unsupported, attrTok, "P/Invoke and unmanaged callback attributes are banned in Vibranium."u8);
                    SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                }
                else
                {
                    SkipOptionalAttrArgs(ref ctx, in ta, in names, ref diags);
                }

                Match(ref ctx, in ta, TokKind.Comma);
            }
            Expect(ref ctx, in ta, TokKind.RBracket, ref diags, "Expected ']' after attribute."u8);
        }
    }

    // Folds a MethodImpl positional argument to its constant value with C#'s
    // unchecked integer semantics. The options checks read only bits 0..13,
    // and +, -, *, unary -, ~, &, |, ^ and << by a count below 32 compute
    // those bits from the operands' bits 0..13 alone, independently of the
    // operand width; casts truncate or sign-extend as the target type does.
    private static bool TryEvalMethodImplOptions(int exprId, in ParserCtx ctx, in AstArena ast,
        in TokenArena ta, in Interner names, out long value)
    {
        value = 0;
        if (exprId < 0) return false;
        AstNode node = ast.Nodes[exprId];
        if (node.Kind == AstKind.ExprIntLit)
        {
            value = ta.Tokens[node.A].IntVal;
            return true;
        }
        if (node.Kind == AstKind.ExprCharLit)
        {
            value = node.A;
            return true;
        }
        if (node.Kind == AstKind.ExprMember && node.D == 0)
            return TryEvalMethodImplOptionsMember(node, in ctx, in ast, in names, out value);
        if (node.Kind == AstKind.ExprCast)
        {
            AstNode type = ast.Nodes[node.A];
            bool options = type.Kind == AstKind.TypeNamed && Bytes.Eq(names.Text(type.A), "MethodImplOptions"u8) &&
                !ctx.KnownTypes.Get(type.A).IsSome;
            if (!options && type.Kind != AstKind.TypePrim) return false;
            if (!TryEvalMethodImplOptions(node.B, in ctx, in ast, in ta, in names, out long operand)) return false;
            if (options) { value = unchecked((int)operand); return true; }
            TokKind prim = (TokKind)type.A;
            if (prim == TokKind.KwSByte) value = (sbyte)operand;
            else if (prim == TokKind.KwByte) value = (byte)operand;
            else if (prim == TokKind.KwShort) value = (short)operand;
            else if (prim == TokKind.KwUShort || prim == TokKind.KwChar) value = (ushort)operand;
            else if (prim == TokKind.KwInt) value = (int)operand;
            else if (prim == TokKind.KwUInt) value = (uint)operand;
            else if (prim == TokKind.KwLong || prim == TokKind.KwULong) value = operand;
            else return false;
            return true;
        }
        if (node.Kind == AstKind.ExprUnary && node.C == 0 &&
            TryEvalMethodImplOptions(node.B, in ctx, in ast, in ta, in names, out long operand1))
        {
            TokKind op = (TokKind)node.A;
            if (op == TokKind.Minus) { value = unchecked(-operand1); return true; }
            if (op == TokKind.Tilde) { value = ~operand1; return true; }
            if (op == TokKind.Plus || op == TokKind.KwUnchecked || op == TokKind.KwChecked) { value = operand1; return true; }
        }
        if (node.Kind == AstKind.ExprBinary &&
            TryEvalMethodImplOptions(node.B, in ctx, in ast, in ta, in names, out long left) &&
            TryEvalMethodImplOptions(node.C, in ctx, in ast, in ta, in names, out long right))
        {
            TokKind op = (TokKind)node.A;
            if (op == TokKind.Plus) { value = unchecked(left + right); return true; }
            if (op == TokKind.Minus) { value = unchecked(left - right); return true; }
            if (op == TokKind.Star) { value = unchecked(left * right); return true; }
            if (op == TokKind.Pipe) { value = left | right; return true; }
            if (op == TokKind.Amp) { value = left & right; return true; }
            if (op == TokKind.Caret) { value = left ^ right; return true; }
            if (op == TokKind.LtLt && right >= 0 && right < 32) { value = left << (int)right; return true; }
        }
        // Division, remainder and right shifts need the full typed constant
        // semantics. Leaving them unknown is safer than rejecting a legal
        // attribute by guessing.
        return false;
    }

    // MethodImplOptions.Member, also written System.Runtime.CompilerServices.MethodImplOptions.Member,
    // when no source declaration of that name shadows the framework enum.
    private static bool TryEvalMethodImplOptionsMember(in AstNode node, in ParserCtx ctx, in AstArena ast,
        in Interner names, out long value)
    {
        value = 0;
        if (node.A < 0) return false;
        AstNode target = ast.Nodes[node.A];
        if (target.Kind == AstKind.ExprIdent)
        {
            if (target.C != 0 || !Bytes.Eq(names.Text(target.A), "MethodImplOptions"u8) ||
                ctx.KnownTypes.Get(target.A).IsSome) return false;
        }
        else
        {
            var path = new StringBuf();
            if (!AppendSimpleMemberPath(node.A, in ast, in names, ref path) ||
                !Bytes.Eq(path.AsUtf8(), "System.Runtime.CompilerServices.MethodImplOptions"u8)) return false;
        }
        ReadOnlySpan<byte> member = names.Text(node.B);
        if (Bytes.Eq(member, "Unmanaged"u8)) value = 0x0004;
        else if (Bytes.Eq(member, "NoInlining"u8)) value = 0x0008;
        else if (Bytes.Eq(member, "ForwardRef"u8)) value = 0x0010;
        else if (Bytes.Eq(member, "Synchronized"u8)) value = 0x0020;
        else if (Bytes.Eq(member, "NoOptimization"u8)) value = 0x0040;
        else if (Bytes.Eq(member, "PreserveSig"u8)) value = 0x0080;
        else if (Bytes.Eq(member, "AggressiveInlining"u8)) value = 0x0100;
        else if (Bytes.Eq(member, "AggressiveOptimization"u8)) value = 0x0200;
        else if (Bytes.Eq(member, "InternalCall"u8)) value = 0x1000;
        else if (Bytes.Eq(member, "Async"u8)) value = 0x2000;
        else return false;
        return true;
    }

    private static bool AppendSimpleMemberPath(int exprId, in AstArena ast, in Interner names, ref StringBuf path)
    {
        AstNode node = ast.Nodes[exprId];
        if (node.Kind == AstKind.ExprIdent && node.C == 0)
        {
            path.Append(names.Text(node.A));
            return true;
        }
        if (node.Kind != AstKind.ExprMember || node.D != 0 || node.A < 0 ||
            !AppendSimpleMemberPath(node.A, in ast, in names, ref path)) return false;
        path.AppendAscii((byte)'.');
        path.Append(names.Text(node.B));
        return true;
    }

    private static int ParseAttributeName(ref ParserCtx ctx, in TokenArena ta, ref Interner names,
        ref AstArena ast, ref DiagnosticBag diags, out int frameworkAttribute, out int frameworkUse)
    {
        int attributeTok = ctx.Pos;
        var written = new StringBuf();
        int flags = 0;
        if (PeekKind(in ctx, in ta, 0) == TokKind.KwGlobal && PeekKind(in ctx, in ta, 1) == TokKind.ColonColon)
        {
            Advance(ref ctx, in ta);
            Advance(ref ctx, in ta);
            flags = 1;
        }
        int firstName = -1;
        int lastName = -1;
        while (PeekKind(in ctx, in ta, 0) == TokKind.Ident)
        {
            Token part = Advance(ref ctx, in ta);
            if (firstName < 0) firstName = part.AuxId;
            lastName = part.AuxId;
            if (written.Length > 0) written.AppendAscii((byte)'.');
            written.Append(names.Text(lastName));
            if (written.Length == names.Text(lastName).Length && Match(ref ctx, in ta, TokKind.ColonColon))
            {
                flags = 2;
                continue;
            }
            if (!Match(ref ctx, in ta, TokKind.Dot)) break;
        }
        if (lastName < 0 && PeekKind(in ctx, in ta, 0) != TokKind.Eof) Advance(ref ctx, in ta);
        var context = new TypeRefContext(-1, ctx.UnitKind, ctx.CurrentNsId, ctx.CurrentOwnerDeclId, ctx.UsingExtraStart, ctx.UsingCount);
        int alias = firstName >= 0 ? FindAttributeAlias(names.Text(firstName), context.CurrentNsId,
            context.UsingExtraStart, context.UsingCount, in ast, in names) : -1;
        if (flags == 2 && alias < 0)
            diags.ReportTok(VibCodes.Unsupported, ta.Tokens[attributeTok], "Attribute namespace alias was not found (CS0432)."u8);

        frameworkUse = -1;
        var resolved = new StringBuf();
        frameworkAttribute = ResolveFrameworkAttributeName(written.AsUtf8(), flags, in context, allowSuffix: true,
            in ast, in names, ref resolved);
        if (frameworkAttribute != 0)
        {
            int writtenId = names.Intern(written.AsUtf8());
            int contextId = ast.AddTypeRef(context);
            frameworkUse = ast.AddNode(AstKind.FrameworkAttributeUse, attributeTok, writtenId, contextId, frameworkAttribute, flags);
            ast.FrameworkAttributeUses.Push(frameworkUse);
            ReadOnlySpan<byte> full = resolved.AsUtf8();
            int lastDot = full.Length - 1;
            while (lastDot >= 0 && full[lastDot] != (byte)'.') lastDot -= 1;
            lastName = names.Intern(full.Slice(lastDot + 1));
        }
        return lastName;
    }

    // Metadata-only identities. They never become runtime TypeIds, and using
    // alias targets do not get attribute suffix fallback.
    public static bool TryResolveFrameworkAttributeAlias(int targetNodeId, in AstArena ast, in Interner names, out int identity)
    {
        identity = 0;
        AstNode target = ast.Nodes[targetNodeId];
        if (target.Kind != AstKind.TypeNamed || target.C != 0) return false;
        TypeRefContext context = ast.TypeRefs[target.D];
        var path = new StringBuf();
        int flags = AppendAttributeTypePath(in target, in context, in names, ref path);
        var resolved = new StringBuf();
        identity = ResolveFrameworkAttributeName(path.AsUtf8(), flags, in context, allowSuffix: false, in ast, in names, ref resolved);
        return identity != 0;
    }

    private static void ValidateFrameworkAttributeUses(in AstArena ast, in TokenArena ta, in Interner names, ref DiagnosticBag diags)
    {
        for (int i = 0; i < ast.FrameworkAttributeUses.Count; i++)
        {
            AstNode use = ast.Nodes[ast.FrameworkAttributeUses[i]];
            TypeRefContext context = ast.TypeRefs[use.B];
            var resolved = new StringBuf();
            int identity = ResolveFrameworkAttributeName(names.Text(use.A), use.D & 3, in context,
                allowSuffix: true, in ast, in names, ref resolved);
            if (identity != use.C)
            {
                // A source MethodImplAttribute is not the framework attribute; do not
                // impose framework options on its constructor. UnscopedRef affects
                // ref-return safety, so never leave a superseded grant in a clean AST.
                if (use.C == 1 || AttributeAbsolutePathKind(resolved.AsUtf8(), in ast, in names) != 2)
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[use.TokIdx], "The attribute name does not resolve to the supported framework attribute in this scope."u8);
            }
            else if ((use.D & 4) != 0)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[use.TokIdx], "Invalid MethodImpl options: the low two bits must be zero (CS0591)."u8);
            else if ((use.D & 8) != 0)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[use.TokIdx], "MethodImplOptions.Async cannot be applied manually (CS9330)."u8);
        }
    }

    private static int AppendAttributeTypePath(in AstNode target, in TypeRefContext context, in Interner names, ref StringBuf path)
    {
        int flags = 0;
        if (context.PrefixNsId >= 0)
        {
            ReadOnlySpan<byte> prefix = names.Text(context.PrefixNsId);
            if (prefix.StartsWith("global::"u8)) { prefix = prefix.Slice(8); flags = 1; }
            path.Append(prefix);
            if (prefix.Length > 0) path.AppendAscii((byte)'.');
        }
        path.Append(names.Text(target.A));
        return flags;
    }

    private static int FrameworkAttributeIdentity(ReadOnlySpan<byte> path)
    {
        if (Bytes.Eq(path, "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute"u8)) return 1;
        if (Bytes.Eq(path, "System.Runtime.CompilerServices.MethodImplAttribute"u8)) return 2;
        return 0;
    }

    private static int ResolveFrameworkAttributeName(ReadOnlySpan<byte> written, int flags, in TypeRefContext context,
        bool allowSuffix, in AstArena ast, in Interner names, ref StringBuf resolved)
    {
        int kind = ResolveAttributePath(written, flags, in context, 0, in ast, in names, ref resolved);
        if (kind == 3) return FrameworkAttributeIdentity(resolved.AsUtf8());
        if (kind != 0 || !allowSuffix) return 0;
        var suffixed = new StringBuf();
        suffixed.Append(written);
        suffixed.Append("Attribute"u8);
        resolved.Clear();
        return ResolveAttributePath(suffixed.AsUtf8(), flags, in context, 0, in ast, in names, ref resolved) == 3
            ? FrameworkAttributeIdentity(resolved.AsUtf8()) : 0;
    }

    // 0=unresolved, 1=namespace, 2=source type, 3=supported framework attribute.
    // Resolve the first segment at one lexical tier, then stay in that binding:
    // a nearer namespace/type must not fall through to an outer System name.
    private static int ResolveAttributePath(ReadOnlySpan<byte> path, int flags, in TypeRefContext context, int depth,
        in AstArena ast, in Interner names, ref StringBuf resolved)
    {
        if (depth >= 64 || path.Length == 0) return 0;
        int dot = path.IndexOf((byte)'.');
        ReadOnlySpan<byte> first = dot < 0 ? path : path.Slice(0, dot);
        ReadOnlySpan<byte> rest = dot < 0 ? default : path.Slice(dot + 1);
        if (flags == 1)
        {
            int kind = AttributeAbsolutePathKind(first, in ast, in names);
            if (kind == 0) return 0;
            return FinishAttributePath(first, kind, rest, in ast, in names, ref resolved);
        }
        if (flags != 2)
        {
            for (int owner = context.EnclosingOwnerDeclId; owner >= 0; owner = ast.Types[owner].OwnerDeclId)
            {
                for (int i = 0; i < ast.Types.Count; i++)
                {
                    TypeDecl nested = ast.Types[i];
                    if (nested.OwnerDeclId != owner || nested.TypeParamCount != 0 || !Bytes.Eq(names.Text(nested.NameId), first)) continue;
                    var nestedPath = new StringBuf();
                    AppendAttributeSourcePath(i, in ast, in names, ref nestedPath);
                    return FinishAttributePath(nestedPath.AsUtf8(), 2, rest, in ast, in names, ref resolved);
                }
            }
        }
        ReadOnlySpan<byte> current = context.CurrentNsId >= 0 ? names.Text(context.CurrentNsId) : default;
        int prefixLength = current.Length;
        while (true)
        {
            ReadOnlySpan<byte> scope = current.Slice(0, prefixLength);
            if (flags != 2)
            {
                var candidate = new StringBuf();
                if (scope.Length > 0) { candidate.Append(scope); candidate.AppendAscii((byte)'.'); }
                candidate.Append(first);
                int kind = AttributeAbsolutePathKind(candidate.AsUtf8(), in ast, in names);
                if (kind != 0) return FinishAttributePath(candidate.AsUtf8(), kind, rest, in ast, in names, ref resolved);
            }
            int alias = FindAttributeAliasAtScope(first, scope, context.UsingExtraStart, context.UsingCount, in ast, in names);
            if (alias >= 0)
            {
                AstNode target = ast.Nodes[ast.Usings[alias].TargetNode];
                if (target.Kind != AstKind.TypeNamed || target.C != 0) return 0;
                TypeRefContext targetContext = ast.TypeRefs[target.D];
                var targetPath = new StringBuf();
                int targetFlags = AppendAttributeTypePath(in target, in targetContext, in names, ref targetPath);
                var aliasPath = new StringBuf();
                int kind = ResolveAttributePath(targetPath.AsUtf8(), targetFlags, in targetContext, depth + 1, in ast, in names, ref aliasPath);
                if (kind == 0) return 0;
                return FinishAttributePath(aliasPath.AsUtf8(), kind, rest, in ast, in names, ref resolved);
            }
            if (flags != 2)
            {
                var imported = new StringBuf();
                int importedKind = 0;
                for (int i = 0; i < context.UsingCount; i++)
                {
                    UsingEntry entry = ast.Usings[ast.Extra[context.UsingExtraStart + i]];
                    if (entry.Kind != UsingKinds.Namespace || !AttributeScopeEquals(entry.EnclosingNsId, scope, in names)) continue;
                    AstNode target = ast.Nodes[entry.TargetNode];
                    if (target.Kind != AstKind.TypeNamed || target.C != 0) continue;
                    TypeRefContext targetContext = ast.TypeRefs[target.D];
                    var targetPath = new StringBuf();
                    int targetFlags = AppendAttributeTypePath(in target, in targetContext, in names, ref targetPath);
                    var namespacePath = new StringBuf();
                    if (ResolveAttributePath(targetPath.AsUtf8(), targetFlags, in targetContext, depth + 1, in ast, in names, ref namespacePath) != 1) continue;
                    namespacePath.AppendAscii((byte)'.');
                    namespacePath.Append(first);
                    int kind = AttributeAbsolutePathKind(namespacePath.AsUtf8(), in ast, in names);
                    if (kind != 2 && kind != 3) continue; // namespace usings import types, not child namespaces
                    if (importedKind != 0 && !Bytes.Eq(imported.AsUtf8(), namespacePath.AsUtf8())) return 0;
                    imported.Clear();
                    imported.Append(namespacePath.AsUtf8());
                    importedKind = kind;
                }
                if (importedKind != 0) return FinishAttributePath(imported.AsUtf8(), importedKind, rest, in ast, in names, ref resolved);
            }
            if (prefixLength == 0) break;
            prefixLength -= 1;
            while (prefixLength > 0 && current[prefixLength] != (byte)'.') prefixLength -= 1;
        }
        return 0;
    }

    private static int FinishAttributePath(ReadOnlySpan<byte> start, int kind, ReadOnlySpan<byte> rest,
        in AstArena ast, in Interner names, ref StringBuf resolved)
    {
        var path = new StringBuf();
        path.Append(start);
        while (rest.Length > 0)
        {
            if (kind == 3) return 0;
            int dot = rest.IndexOf((byte)'.');
            ReadOnlySpan<byte> segment = dot < 0 ? rest : rest.Slice(0, dot);
            path.AppendAscii((byte)'.');
            path.Append(segment);
            int next = AttributeAbsolutePathKind(path.AsUtf8(), in ast, in names);
            if (next == 0 || (kind == 2 && next != 2)) return 0;
            kind = next;
            rest = dot < 0 ? default : rest.Slice(dot + 1);
        }
        resolved.Clear();
        resolved.Append(path.AsUtf8());
        return kind;
    }

    private static bool AttributeScopeEquals(int namespaceId, ReadOnlySpan<byte> scope, in Interner names) =>
        namespaceId < 0 ? scope.Length == 0 : Bytes.Eq(names.Text(namespaceId), scope);

    private static int FindAttributeAlias(ReadOnlySpan<byte> name, int currentNs, int usingStart, int usingCount,
        in AstArena ast, in Interner names)
    {
        ReadOnlySpan<byte> current = currentNs >= 0 ? names.Text(currentNs) : default;
        int length = current.Length;
        while (true)
        {
            int found = FindAttributeAliasAtScope(name, current.Slice(0, length), usingStart, usingCount, in ast, in names);
            if (found >= 0) return found;
            if (length == 0) return -1;
            length -= 1;
            while (length > 0 && current[length] != (byte)'.') length -= 1;
        }
    }

    private static int FindAttributeAliasAtScope(ReadOnlySpan<byte> name, ReadOnlySpan<byte> scope, int usingStart, int usingCount,
        in AstArena ast, in Interner names)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < usingCount; i++)
            {
                int index = ast.Extra[usingStart + i];
                UsingEntry entry = ast.Usings[index];
                if (entry.Kind != UsingKinds.Alias || !AttributeScopeEquals(entry.EnclosingNsId, scope, in names) ||
                    !Bytes.Eq(names.Text(entry.AliasOrNsId), name)) continue;
                if (scope.Length == 0 && ((pass == 0 && entry.ScopeLevel != 1) || (pass == 1 && entry.ScopeLevel != 0))) continue;
                return index;
            }
            if (scope.Length != 0) break;
        }
        return -1;
    }

    private static void AppendAttributeSourcePath(int typeId, in AstArena ast, in Interner names, ref StringBuf path)
    {
        TypeDecl type = ast.Types[typeId];
        if (type.NsId >= 0) { path.Append(names.Text(type.NsId)); path.AppendAscii((byte)'.'); }
        var owners = new Vec<int>();
        for (int owner = type.OwnerDeclId; owner >= 0; owner = ast.Types[owner].OwnerDeclId) owners.Push(owner);
        for (int j = owners.Count - 1; j >= 0; j--) { path.Append(names.Text(ast.Types[owners[j]].NameId)); path.AppendAscii((byte)'.'); }
        path.Append(names.Text(type.NameId));
    }

    private static int AttributeAbsolutePathKind(ReadOnlySpan<byte> path, in AstArena ast, in Interner names)
    {
        int start = path.Length;
        while (start > 0 && path[start - 1] != (byte)'.') start -= 1;
        ReadOnlySpan<byte> leaf = path.Slice(start);
        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl type = ast.Types[i];
            if (type.TypeParamCount != 0 || !Bytes.Eq(names.Text(type.NameId), leaf)) continue;
            var qualified = new StringBuf();
            AppendAttributeSourcePath(i, in ast, in names, ref qualified);
            if (Bytes.Eq(qualified.AsUtf8(), path)) return 2;
        }
        for (int i = 0; i < ast.DeclaredNamespaces.Count; i++)
            if (Bytes.Eq(names.Text(ast.DeclaredNamespaces[i]), path)) return 1;
        if (Bytes.Eq(path, "System"u8) || Bytes.Eq(path, "System.Diagnostics"u8) ||
            Bytes.Eq(path, "System.Diagnostics.CodeAnalysis"u8) || Bytes.Eq(path, "System.Runtime"u8) ||
            Bytes.Eq(path, "System.Runtime.CompilerServices"u8)) return 1;
        return FrameworkAttributeIdentity(path) != 0 ? 3 : 0;
    }

    private static void SkipOptionalAttrArgs(ref ParserCtx ctx, in TokenArena ta, in Interner names, ref DiagnosticBag diags)
    {
        if (!Match(ref ctx, in ta, TokKind.LParen)) return;
        int depth = 1;
        while (depth > 0 && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            Token token = Peek(in ctx, in ta, 0);
            if (token.Kind == TokKind.Ident && PeekKind(in ctx, in ta, 1) == TokKind.Dot &&
                PeekKind(in ctx, in ta, 2) == TokKind.Ident && !ctx.KnownTypes.Get(token.AuxId).IsSome)
            {
                // Runtime implementation attributes do not affect native code,
                // but their enum member references must still bind. A source
                // declaration/alias of the same name is not this framework enum.
                ReadOnlySpan<byte> typeName = names.Text(token.AuxId);
                Token member = Peek(in ctx, in ta, 2);
                if (!IsKnownFrameworkAttributeEnumMember(typeName, names.Text(member.AuxId)))
                    diags.ReportTok(VibCodes.Unsupported, member, "Unknown framework attribute enum member (CS0117)."u8);
            }
            TokKind kind = Advance(ref ctx, in ta).Kind;
            if (kind == TokKind.LParen) depth += 1;
            else if (kind == TokKind.RParen) depth -= 1;
        }
    }

    private static bool IsKnownFrameworkAttributeEnumMember(ReadOnlySpan<byte> typeName, ReadOnlySpan<byte> memberName)
    {
        if (Bytes.Eq(typeName, "MethodImplOptions"u8))
            return Bytes.Eq(memberName, "Unmanaged"u8) || Bytes.Eq(memberName, "NoInlining"u8) ||
                Bytes.Eq(memberName, "ForwardRef"u8) || Bytes.Eq(memberName, "Synchronized"u8) ||
                Bytes.Eq(memberName, "NoOptimization"u8) || Bytes.Eq(memberName, "PreserveSig"u8) ||
                Bytes.Eq(memberName, "AggressiveInlining"u8) || Bytes.Eq(memberName, "AggressiveOptimization"u8) ||
                Bytes.Eq(memberName, "Async"u8) || Bytes.Eq(memberName, "InternalCall"u8);
        if (Bytes.Eq(typeName, "MethodCodeType"u8))
            return Bytes.Eq(memberName, "IL"u8) || Bytes.Eq(memberName, "Native"u8) ||
                Bytes.Eq(memberName, "OPTIL"u8) || Bytes.Eq(memberName, "Runtime"u8);
        return true;
    }

    private static void SkipUntilRParen(ref ParserCtx ctx, in TokenArena ta)
    {
        int depth = 1;
        while (depth > 0 && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            TokKind k = Advance(ref ctx, in ta).Kind;
            if (k == TokKind.LParen) depth += 1;
            else if (k == TokKind.RParen) depth -= 1;
        }
    }

    private static int ParseModifiers(ref ParserCtx ctx, in TokenArena ta, ref DiagnosticBag diags)
    {
        int flags = 0;
        while (true)
        {
            Token t = Peek(in ctx, in ta, 0);
            int modifierFlag = ModifierFlag(t.Kind);
            if (modifierFlag != 0 && (flags & modifierFlag) != 0)
                diags.ReportTok(VibCodes.Unsupported, t, "Duplicate modifier (CS1004)."u8);
            else if ((modifierFlag & AccessModifierFlags) != 0 && (flags & AccessModifierFlags) != 0)
                diags.ReportTok(VibCodes.Unsupported, t, "More than one protection modifier (CS0107)."u8);
            switch (t.Kind)
            {
                case TokKind.KwPublic: flags |= DeclFlags.Public; Advance(ref ctx, in ta); break;
                case TokKind.KwInternal: flags |= DeclFlags.Internal; Advance(ref ctx, in ta); break;
                case TokKind.KwPrivate: flags |= DeclFlags.Private; Advance(ref ctx, in ta); break;
                case TokKind.KwStatic: flags |= DeclFlags.Static; Advance(ref ctx, in ta); break;
                case TokKind.KwSealed: flags |= DeclFlags.Sealed; Advance(ref ctx, in ta); break;
                case TokKind.KwPartial: flags |= DeclFlags.Partial; Advance(ref ctx, in ta); break;
                case TokKind.KwReadonly: flags |= DeclFlags.Readonly; Advance(ref ctx, in ta); break;
                case TokKind.KwProtected:
                    diags.ReportTok(VibCodes.Unsupported, t, "'protected' modifier is banned in Vibranium."u8);
                    Advance(ref ctx, in ta);
                    break;
                case TokKind.KwUnsafe:
                    diags.ReportTok(VibCodes.Unsupported, t, "'unsafe' modifier is banned in Vibranium."u8);
                    Advance(ref ctx, in ta);
                    break;
                case TokKind.KwAsync:
                    diags.ReportTok(VibCodes.Unsupported, t, "'async' modifier is banned in Vibranium."u8);
                    Advance(ref ctx, in ta);
                    break;
                case TokKind.KwAbstract:
                case TokKind.KwVirtual:
                case TokKind.KwOverride:
                    diags.ReportTok(VibCodes.Unsupported, t, "Virtual, abstract, and override modifiers are banned in Vibranium."u8);
                    Advance(ref ctx, in ta);
                    break;
                default:
                    return flags;
            }
        }
    }

    private const int AccessModifierFlags = DeclFlags.Public | DeclFlags.Internal | DeclFlags.Private;

    private static int ModifierFlag(TokKind kind) => kind switch
    {
        TokKind.KwPublic => DeclFlags.Public,
        TokKind.KwInternal => DeclFlags.Internal,
        TokKind.KwPrivate => DeclFlags.Private,
        TokKind.KwStatic => DeclFlags.Static,
        TokKind.KwSealed => DeclFlags.Sealed,
        TokKind.KwPartial => DeclFlags.Partial,
        TokKind.KwReadonly => DeclFlags.Readonly,
        _ => 0,
    };

    // Position of the first `kind` modifier of the declaration that starts at
    // `start` (after its attribute sections), or -1.
    private static int FindDeclarationModifier(int start, TokKind kind, in ParserCtx ctx, in TokenArena ta, in Interner names)
    {
        int p = start;
        while (p < ctx.EndPos && ta.Tokens[p].Kind == TokKind.LBracket)
        {
            int depth = 0;
            while (p < ctx.EndPos)
            {
                TokKind k = ta.Tokens[p].Kind;
                p += 1;
                if (k == TokKind.LBracket) depth += 1;
                else if (k == TokKind.RBracket)
                {
                    depth -= 1;
                    if (depth == 0) break;
                }
            }
        }
        while (p < ctx.EndPos && (IsModifierKeyword(ta.Tokens[p].Kind) || IsFileModifier(ta.Tokens[p], in names)))
        {
            if (ta.Tokens[p].Kind == kind) return p;
            p += 1;
        }
        return -1;
    }

    private static void ReportInvalidModifier(int start, TokKind kind, ReadOnlySpan<byte> message, in ParserCtx ctx, in TokenArena ta, in Interner names, ref DiagnosticBag diags)
    {
        int tok = FindDeclarationModifier(start, kind, in ctx, in ta, in names);
        if (tok >= 0) diags.ReportTok(VibCodes.Unsupported, ta.Tokens[tok], message);
    }

    // C# modifier validity for a type declaration (CS0106, CS0441, CS1527).
    private static void ValidateTypeModifiers(int start, int enclosingOwnerDeclId, in ParserCtx ctx, in TokenArena ta, in Interner names, ref DiagnosticBag diags)
    {
        int kindPos = start;
        while (kindPos < ctx.EndPos && ta.Tokens[kindPos].Kind == TokKind.LBracket)
        {
            int depth = 0;
            while (kindPos < ctx.EndPos)
            {
                TokKind k = ta.Tokens[kindPos].Kind;
                kindPos += 1;
                if (k == TokKind.LBracket) depth += 1;
                else if (k == TokKind.RBracket)
                {
                    depth -= 1;
                    if (depth == 0) break;
                }
            }
        }
        while (kindPos < ctx.EndPos && (IsModifierKeyword(ta.Tokens[kindPos].Kind) || IsFileModifier(ta.Tokens[kindPos], in names))) kindPos += 1;
        if (kindPos >= ctx.EndPos) return;
        TokKind declKind = ta.Tokens[kindPos].Kind;
        if (enclosingOwnerDeclId < 0)
            ReportInvalidModifier(start, TokKind.KwPrivate, "Elements defined in a namespace cannot be explicitly declared as private (CS1527)."u8, in ctx, in ta, in names, ref diags);
        if (declKind == TokKind.KwClass)
        {
            ReportInvalidModifier(start, TokKind.KwReadonly, "The modifier 'readonly' is not valid for a class (CS0106)."u8, in ctx, in ta, in names, ref diags);
            int staticTok = FindDeclarationModifier(start, TokKind.KwStatic, in ctx, in ta, in names);
            int sealedTok = FindDeclarationModifier(start, TokKind.KwSealed, in ctx, in ta, in names);
            if (staticTok >= 0 && sealedTok >= 0)
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[staticTok > sealedTok ? staticTok : sealedTok], "A static class cannot be sealed (CS0441)."u8);
        }
        else if (declKind == TokKind.KwStruct)
        {
            ReportInvalidModifier(start, TokKind.KwStatic, "The modifier 'static' is not valid for a struct (CS0106)."u8, in ctx, in ta, in names, ref diags);
            ReportInvalidModifier(start, TokKind.KwSealed, "The modifier 'sealed' is not valid for a struct (CS0106)."u8, in ctx, in ta, in names, ref diags);
        }
        else if (declKind == TokKind.KwEnum)
        {
            ReportInvalidModifier(start, TokKind.KwStatic, "The modifier 'static' is not valid for an enum (CS0106)."u8, in ctx, in ta, in names, ref diags);
            ReportInvalidModifier(start, TokKind.KwSealed, "The modifier 'sealed' is not valid for an enum (CS0106)."u8, in ctx, in ta, in names, ref diags);
            ReportInvalidModifier(start, TokKind.KwReadonly, "The modifier 'readonly' is not valid for an enum (CS0106)."u8, in ctx, in ta, in names, ref diags);
        }
    }

    // C# modifier validity for the member just parsed from `start` (CS0106,
    // CS0238, CS0504, CS8657). `sealed` is only valid with `override`, which
    // Vibranium bans, and `readonly` on a function member only in a struct.
    private static void ValidateMemberModifiers(int start, int ownerDeclId, in Vec<FieldDecl> localFields, int fieldsBefore,
        in Vec<FuncDecl> localFuncs, int funcsBefore, in ParserCtx ctx, in TokenArena ta, in Interner names, in AstArena ast, ref DiagnosticBag diags)
    {
        bool classOwner = ast.Types[ownerDeclId].Kind == TypeDeclKinds.Class;
        if (localFuncs.Count > funcsBefore)
        {
            FuncDecl fd = localFuncs[funcsBefore];
            if ((fd.Flags & DeclFlags.IsCtor) != 0)
            {
                ReportInvalidModifier(start, TokKind.KwReadonly, "The modifier 'readonly' is not valid for a constructor (CS0106)."u8, in ctx, in ta, in names, ref diags);
                ReportInvalidModifier(start, TokKind.KwSealed, "The modifier 'sealed' is not valid for a constructor (CS0106)."u8, in ctx, in ta, in names, ref diags);
                return;
            }
            bool indexer = (fd.Flags & DeclFlags.IsIndexer) != 0;
            bool op = (fd.Flags & DeclFlags.IsOpAddAssign) != 0;
            if (indexer)
                ReportInvalidModifier(start, TokKind.KwStatic, "The modifier 'static' is not valid for an indexer (CS0106)."u8, in ctx, in ta, in names, ref diags);
            if (op)
                ReportInvalidModifier(start, TokKind.KwSealed, "The modifier 'sealed' is not valid for an operator (CS0106)."u8, in ctx, in ta, in names, ref diags);
            else
                ReportInvalidModifier(start, TokKind.KwSealed, "A member cannot be sealed because it is not an override (CS0238)."u8, in ctx, in ta, in names, ref diags);
            if (classOwner)
                ReportInvalidModifier(start, TokKind.KwReadonly, "The modifier 'readonly' is only valid on struct members (CS0106)."u8, in ctx, in ta, in names, ref diags);
            else if (FindDeclarationModifier(start, TokKind.KwStatic, in ctx, in ta, in names) >= 0)
                ReportInvalidModifier(start, TokKind.KwReadonly, "A static member cannot be marked 'readonly' (CS8657)."u8, in ctx, in ta, in names, ref diags);
            return;
        }
        if (localFields.Count > fieldsBefore)
        {
            if ((localFields[fieldsBefore].Flags & DeclFlags.IsConst) != 0)
            {
                ReportInvalidModifier(start, TokKind.KwStatic, "A constant cannot be marked static (CS0504)."u8, in ctx, in ta, in names, ref diags);
                ReportInvalidModifier(start, TokKind.KwReadonly, "The modifier 'readonly' is not valid for a constant (CS0106)."u8, in ctx, in ta, in names, ref diags);
            }
            ReportInvalidModifier(start, TokKind.KwSealed, "The modifier 'sealed' is not valid for a field (CS0106)."u8, in ctx, in ta, in names, ref diags);
        }
    }

    private static void ParseTypeDeclaration(ref ParserCtx ctx, in TokenArena ta, ref Interner names, ref AstArena ast, ref DiagnosticBag diags) => ParseTypeDeclaration(ref ctx, in ta, ref names, ref ast, ref diags, -1);

    private static void ParseTypeDeclaration(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int enclosingOwnerDeclId)
    {
        int firstDiagnostic = diags.Items.Count;
        int declarationStart = ctx.Pos;
        ParseTypeDeclarationCore(ref ctx, in ta, ref names, ref ast, ref diags, enclosingOwnerDeclId, out bool rejected);
        if (rejected) KeepOnlySubsetCutsSince(firstDiagnostic, ref diags);
        else ValidateTypeModifiers(declarationStart, enclosingOwnerDeclId, in ctx, in ta, in names, ref diags);
    }

    private static void ParseTypeDeclarationCore(
        ref ParserCtx ctx, in TokenArena ta, ref Interner names, ref AstArena ast,
        ref DiagnosticBag diags, int enclosingOwnerDeclId, out bool rejected)
    {
        rejected = false;
        int partialToken = FindModifierToken(in ctx, in ta, in names, TokKind.KwPartial);
        int flags = 0;
        int unusedNat = -1;
        var unusedLt = new Vec<int>();
        ParseAttributes(ref ctx, in ta, ref names, ref ast, ref diags, 0, ref flags, ref unusedNat, ref unusedLt);
        while (IsModifierKeyword(PeekKind(in ctx, in ta, 0)) || IsFileModifier(Peek(in ctx, in ta, 0), in names))
        {
            if (IsFileModifier(Peek(in ctx, in ta, 0), in names))
            {
                Token fileToken = Advance(ref ctx, in ta);
                ReportSubsetCut(ref ctx, 1, fileToken, ref diags);
                rejected = true;
            }
            else flags |= ParseModifiers(ref ctx, in ta, ref diags);
        }

        bool isRefStruct = false;
        if (PeekKind(in ctx, in ta, 0) == TokKind.KwRef && PeekKind(in ctx, in ta, 1) == TokKind.KwStruct)
        {
            isRefStruct = true;
            Advance(ref ctx, in ta);
        }

        Token kindTok = Advance(ref ctx, in ta);
        bool isRejectedRecord = IsRecordKeyword(kindTok, in names);
        if (isRejectedRecord && (PeekKind(in ctx, in ta, 0) == TokKind.KwClass || PeekKind(in ctx, in ta, 0) == TokKind.KwStruct))
        {
            kindTok = Advance(ref ctx, in ta);
        }
        Token declaredName = Peek(in ctx, in ta, 0);
        if (kindTok.Kind == TokKind.KwDelegate)
        {
            int returnType = ctx.Pos;
            if (returnType < ctx.EndPos && ta.Tokens[returnType].Kind == TokKind.KwRef) returnType += 1;
            if (returnType < ctx.EndPos && ta.Tokens[returnType].Kind == TokKind.KwReadonly) returnType += 1;
            if (TryScanTypeSyntax(in ta, returnType, ctx.EndPos, out int afterReturnType) && afterReturnType < ctx.EndPos)
                declaredName = ta.Tokens[afterReturnType];
        }
        int declaredNameId = LookupTokenKnownId(declaredName, in names);
        if (declaredNameId >= 0 && Bytes.Eq(names.Text(declaredNameId), "_"u8))
            SubsetCuts.Report(11, declaredName, ref diags);
        if (partialToken >= 0 && (isRejectedRecord || !IsTrustedSysType(in ctx, declaredNameId, enclosingOwnerDeclId, in names)))
        {
            ReportSubsetCut(ref ctx, 2, ta.Tokens[partialToken], ref diags);
            rejected = true;
        }
        if (HasGenericTypeAncestor(enclosingOwnerDeclId, in ast))
        {
            ReportSubsetCut(ref ctx, 4, declaredName, ref diags);
            rejected = true;
        }
        if (kindTok.Kind == TokKind.KwInterface || kindTok.Kind == TokKind.KwDelegate)
        {
            diags.ReportTok(VibCodes.Unsupported, kindTok, "User-defined interfaces and delegates are not supported in Vibranium."u8);
            SkipDeclarationBody(ref ctx, in ta);
            return;
        }

        int declKind = TypeDeclKinds.Class;
        if (kindTok.Kind == TokKind.KwStruct)
        {
            declKind = TypeDeclKinds.Struct;
            if (isRefStruct)
            {
                diags.ReportTok(VibCodes.Unsupported, kindTok, "User-defined ref struct is not supported in Vibranium."u8);
            }
        }
        else if (kindTok.Kind == TokKind.KwEnum)
        {
            declKind = TypeDeclKinds.Enum;
        }

        int declTokIdx = ctx.Pos;
        int rawTypeNameId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected type name."u8, out Token nameTok);
        if (nameTok.Kind == TokKind.KwVar || nameTok.Kind == TokKind.KwDynamic)
        {
            diags.ReportTok(VibCodes.Unsupported, nameTok, "The contextual keyword 'var' or 'dynamic' cannot be used as a declared type name in Vibranium."u8);
        }
        int nameId = rawTypeNameId >= 0 ? rawTypeNameId : names.Intern("_ErrType"u8);

        if (enclosingOwnerDeclId < 0 && rawTypeNameId >= 0)
        {
            int curScopeLevel = 1 + ctx.NamespaceDepth;
            for (int u = 0; u < ctx.CurrentUsings.Count; u++)
            {
                UsingEntry ue = ast.Usings[ctx.CurrentUsings[u]];
                if (ue.ScopeLevel == curScopeLevel && ue.Kind == UsingKinds.Alias && ue.AliasOrNsId == nameId)
                {
                    diags.ReportTok(VibCodes.Unsupported, nameTok, "Declared type name conflicts with a 'using' alias in the same scope."u8);
                    break;
                }
            }
        }

        if (declKind == TypeDeclKinds.Enum)
        {
            ParseEnumBody(ref ctx, in ta, ref names, ref ast, ref diags, nameId, flags, declTokIdx, enclosingOwnerDeclId);
            return;
        }

        var tpNames = new Vec<int>();
        var tpFlags = new Vec<int>();
        ParseOptionalTypeParamList(ref ctx, in ta, ref names, ref diags, ref tpNames, ref tpFlags);

        var primaryParams = new Vec<ParamDecl>();
        bool hasPrimaryCtor = false;
        if (PeekKind(in ctx, in ta, 0) == TokKind.LParen)
        {
            hasPrimaryCtor = true;
            ParseParamList(ref ctx, in ta, ref names, ref ast, ref diags, ref primaryParams);
        }

        var baseTypeNodes = new Vec<int>();
        if (Match(ref ctx, in ta, TokKind.Colon))
        {
            while (PeekKind(in ctx, in ta, 0) != TokKind.LBrace &&
                   PeekKind(in ctx, in ta, 0) != TokKind.Semi &&
                   PeekKind(in ctx, in ta, 0) != TokKind.KwWhere &&
                   PeekKind(in ctx, in ta, 0) != TokKind.Eof)
            {
                int baseTypeNode = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
                baseTypeNodes.Push(baseTypeNode);
                if (!Match(ref ctx, in ta, TokKind.Comma)) break;
            }
        }

        ParseWhereClauses(ref ctx, in ta, ref names, ref diags, in tpNames, ref tpFlags);

        if (ctx.UnitKind != UnitKinds.Std &&
            ctx.CurrentNsId >= 0 &&
            Bytes.Eq(names.Text(ctx.CurrentNsId), "Vibranium.Std"u8) &&
            Bytes.Eq(names.Text(nameId), "Sys"u8))
        {
            diags.ReportTok(VibCodes.Unsupported, nameTok, "Declaring or extending 'Vibranium.Std.Sys' outside Vibranium.Std is banned."u8);
        }

        int tpStart = ast.PushExtraSpan(tpNames.AsReadOnlySpan());
        int tpFlagsStart = ast.PushExtraSpan(tpFlags.AsReadOnlySpan());

        int declId = FindExistingPartialType(in ast, ctx.UnitKind, ctx.CurrentNsId, enclosingOwnerDeclId, nameId, tpNames.Count, flags);
        if (declId < 0)
        {
            declId = ast.Types.Count;
            var td = new TypeDecl(declKind, ctx.UnitKind, ctx.CurrentNsId, nameId, tpStart, tpNames.Count, tpFlagsStart, flags, ctx.SourceId, declTokIdx, enclosingOwnerDeclId, ctx.UsingExtraStart, ctx.UsingCount);
            if (baseTypeNodes.Count > 0)
            {
                td.BaseTypeExtraStart = ast.PushExtraSpan(baseTypeNodes.AsReadOnlySpan());
                td.BaseTypeCount = baseTypeNodes.Count;
            }
            ast.Types.Push(td);
        }
        else
        {
            TypeDecl existing = ast.Types[declId];
            existing.Flags |= flags;
            if (baseTypeNodes.Count > 0)
            {
                if (existing.BaseTypeCount == 0)
                {
                    existing.BaseTypeExtraStart = ast.PushExtraSpan(baseTypeNodes.AsReadOnlySpan());
                    existing.BaseTypeCount = baseTypeNodes.Count;
                }
                else
                {
                    var mergedBases = new Vec<int>(existing.BaseTypeCount + baseTypeNodes.Count);
                    for (int b = 0; b < existing.BaseTypeCount; b++) mergedBases.Push(ast.Extra[existing.BaseTypeExtraStart + b]);
                    for (int b = 0; b < baseTypeNodes.Count; b++) mergedBases.Push(baseTypeNodes[b]);
                    existing.BaseTypeExtraStart = ast.PushExtraSpan(mergedBases.AsReadOnlySpan());
                    existing.BaseTypeCount = mergedBases.Count;
                }
            }
            ast.Types[declId] = existing;
        }

        int prevOwnerDecl = ctx.CurrentOwnerDeclId;
        ctx.CurrentOwnerDeclId = declId;
        var localFields = new Vec<FieldDecl>();
        var localFuncs = new Vec<FuncDecl>();

        if (Match(ref ctx, in ta, TokKind.Semi))
        {
            SynthesizePrimaryCtorIfNeeded(hasPrimaryCtor, in primaryParams, declId, flags, declTokIdx, ctx.SourceId, ctx.UsingExtraStart, ctx.UsingCount, in ta, ref names, ref ast, ref localFields, ref localFuncs, ref diags);
            CommitTypeMembers(declId, in localFields, in localFuncs, ref ast);
            ctx.CurrentOwnerDeclId = prevOwnerDecl;
            return;
        }

        Expect(ref ctx, in ta, TokKind.LBrace, ref diags, "Expected '{' for type body."u8);
        while (PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int memberSave = ctx.Pos;
            if (LooksLikeTypeDeclaration(in ctx, in ta, in names, in ast))
            {
                ParseTypeDeclaration(ref ctx, in ta, ref names, ref ast, ref diags, declId);
                continue;
            }
            ParseTypeMember(ref ctx, in ta, ref names, ref ast, ref diags, declId, nameId, ref localFields, ref localFuncs);
            if (ctx.Pos == memberSave && PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
            {
                Advance(ref ctx, in ta);
            }
        }
        Expect(ref ctx, in ta, TokKind.RBrace, ref diags, "Expected '}' after type body."u8);
        Match(ref ctx, in ta, TokKind.Semi);

        SynthesizePrimaryCtorIfNeeded(hasPrimaryCtor, in primaryParams, declId, flags, declTokIdx, ctx.SourceId, ctx.UsingExtraStart, ctx.UsingCount, in ta, ref names, ref ast, ref localFields, ref localFuncs, ref diags);
        CommitTypeMembers(declId, in localFields, in localFuncs, ref ast);
        ctx.CurrentOwnerDeclId = prevOwnerDecl;
    }

    private static int FindExistingPartialType(in AstArena ast, int unitKind, int nsId, int ownerDeclId, int nameId, int tpCount, int newFlags)
    {
        if ((newFlags & DeclFlags.Partial) == 0) return -1;
        for (int i = 0; i < ast.Types.Count; i++)
        {
            TypeDecl td = ast.Types[i];
            if ((td.Flags & DeclFlags.Partial) != 0 && td.UnitKind == unitKind && td.NsId == nsId && td.OwnerDeclId == ownerDeclId && td.NameId == nameId && td.TypeParamCount == tpCount)
            {
                return i;
            }
        }
        return -1;
    }

    private static void SynthesizePrimaryCtorIfNeeded(
        bool hasPrimaryCtor,
        in Vec<ParamDecl> primaryParams,
        int declId,
        int typeFlags,
        int tokIdx,
        int sourceId,
        int usingExtraStart,
        int usingCount,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref Vec<FieldDecl> localFields,
        ref Vec<FuncDecl> localFuncs,
        ref DiagnosticBag diags)
    {
        if (!hasPrimaryCtor) return;
        var bodyStmts = new Vec<int>();

        // 1. Explicit instance field initializers run FIRST in primary .ctor
        for (int i = 0; i < localFields.Count; i++)
        {
            FieldDecl fd = localFields[i];
            if ((fd.Flags & DeclFlags.Static) == 0 && fd.InitExpr >= 0)
            {
                int thisExpr = ast.AddNode(AstKind.ExprThis, fd.TokIdx, 0, 0, 0, 0);
                int lhs = ast.AddNode(AstKind.ExprMember, fd.TokIdx, thisExpr, fd.NameId, 0, 0);
                int assign = ast.AddNode(AstKind.ExprAssign, fd.TokIdx, (int)TokKind.Eq, lhs, fd.InitExpr, 0);
                bodyStmts.Push(ast.AddNode(AstKind.StmtExpr, fd.TokIdx, assign, 0, 0, 0));
                localFields[i] = new FieldDecl(fd.NameId, fd.TypeNode, -1, fd.Flags, fd.ConstVal, fd.HasConstVal, fd.TokIdx);
            }
        }

        // 2. Synthesize backing fields `__pctor_<name>` for any primary-ctor parameter captured by an instance member body
        var shadowed = new Vec<int>(16);
        for (int p = 0; p < primaryParams.Count; p++)
        {
            ParamDecl pd = primaryParams[p];
            if (!IsPrimaryParamCapturedInInstanceMembers(pd.NameId, in localFuncs, in ast, ref shadowed))
            {
                continue;
            }
            if (pd.Mode != PassMode.ByValue)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[pd.TokIdx], "By-reference primary-constructor parameters cannot be captured by instance members."u8);
            }
            var sb = new StringBuf(32);
            sb.Append("__pctor_"u8);
            sb.Append(names.Text(pd.NameId));
            int synthNameId = names.Intern(sb.AsUtf8());
            int synthFlags = DeclFlags.Private | DeclFlags.IsCapturedPrimaryParam | ((typeFlags & DeclFlags.Readonly) != 0 ? DeclFlags.Readonly : 0);
            localFields.Push(new FieldDecl(synthNameId, pd.TypeNode, -1, synthFlags, pd.NameId, 0, pd.TokIdx));

            int thisExpr = ast.AddNode(AstKind.ExprThis, pd.TokIdx, 0, 0, 0, 0);
            int lhs = ast.AddNode(AstKind.ExprMember, pd.TokIdx, thisExpr, synthNameId, 0, 0);
            int rhs = ast.AddNode(AstKind.ExprIdent, pd.TokIdx, pd.NameId, 0, 0, 0);
            int assign = ast.AddNode(AstKind.ExprAssign, pd.TokIdx, (int)TokKind.Eq, lhs, rhs, 0);
            bodyStmts.Push(ast.AddNode(AstKind.StmtExpr, pd.TokIdx, assign, 0, 0, 0));
        }

        int pStart = ast.Params.Count;
        for (int i = 0; i < primaryParams.Count; i++) ast.Params.Push(primaryParams[i]);
        int bodyStart = ast.PushExtraSpan(bodyStmts.AsReadOnlySpan());
        int blockNode = ast.AddNode(AstKind.StmtBlock, tokIdx, bodyStart, bodyStmts.Count, 0, 0);
        int voidNode = ast.AddNode(AstKind.TypePrim, tokIdx, (int)TokKind.KwVoid, 0, 0, 0);
        localFuncs.Push(new FuncDecl(declId, names.Intern(".ctor"u8), voidNode, 0, 0, 0, pStart, primaryParams.Count, blockNode, DeclFlags.Public | DeclFlags.IsCtor, -1, 0, 0, sourceId, tokIdx, usingExtraStart, usingCount));
    }

    private static bool IsPrimaryParamCapturedInInstanceMembers(
        int paramNameId,
        in Vec<FuncDecl> localFuncs,
        in AstArena ast,
        ref Vec<int> shadowed)
    {
        for (int f = 0; f < localFuncs.Count; f++)
        {
            FuncDecl fd = localFuncs[f];
            if ((fd.Flags & (DeclFlags.Static | DeclFlags.IsCtor)) != 0 || fd.BodyNode < 0)
            {
                continue;
            }
            shadowed.Clear();
            for (int p = 0; p < fd.ParamCount; p++)
            {
                shadowed.Push(ast.Params[fd.ParamStart + p].NameId);
            }
            if (AstSubtreeCapturesIdent(fd.BodyNode, paramNameId, in ast, ref shadowed))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsShadowed(int nameId, in Vec<int> shadowed)
    {
        for (int i = shadowed.Count - 1; i >= 0; i--)
        {
            if (shadowed[i] == nameId) return true;
        }
        return false;
    }

    private static void CollectPatternBindings(int patId, in AstArena ast, ref Vec<int> shadowed)
    {
        if (patId < 0) return;
        AstNode pn = ast.Nodes[patId];
        if ((pn.Kind == AstKind.PatUnion || pn.Kind == AstKind.PatDecl) && pn.B >= 0)
        {
            shadowed.Push(pn.B);
        }
        if (pn.Kind == AstKind.PatUnion && pn.D >= 0)
        {
            CollectPatternBindings(pn.D, in ast, ref shadowed);
        }
        else if (pn.Kind == AstKind.PatNot)
        {
            CollectPatternBindings(pn.A, in ast, ref shadowed);
        }
        else if (pn.Kind == AstKind.PatLogic)
        {
            CollectPatternBindings(pn.B, in ast, ref shadowed);
            CollectPatternBindings(pn.C, in ast, ref shadowed);
        }
    }

    private static bool AstSubtreeCapturesIdent(int nodeId, int targetNameId, in AstArena ast, ref Vec<int> shadowed)
    {
        if (nodeId < 0) return false;
        AstNode n = ast.Nodes[nodeId];
        switch (n.Kind)
        {
            case AstKind.ExprIdent:
                return n.A == targetNameId && !IsShadowed(targetNameId, in shadowed);

            case AstKind.ExprMember:
                return AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed);

            case AstKind.ExprIndex:
            {
                if (AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed)) return true;
                for (int i = 0; i < n.C; i++)
                {
                    AstNode ca = ast.Nodes[ast.Extra[n.B + i]];
                    if (ca.C == -2 && AstSubtreeCapturesIdent(ca.B, targetNameId, in ast, ref shadowed)) return true;
                }
                return false;
            }

            case AstKind.ExprRange:
                return AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed) ||
                       AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed);

            case AstKind.ExprBinary:
            case AstKind.ExprAssign:
                return AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed) ||
                       AstSubtreeCapturesIdent(n.C, targetNameId, in ast, ref shadowed);

            case AstKind.ExprUnary:
            case AstKind.ExprCast:
                return AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed);

            case AstKind.ExprFromEnd:
            case AstKind.StmtExpr:
            case AstKind.StmtReturn:
            case AstKind.StmtUncheckedBlock:
                return AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed);

            case AstKind.ExprCond:
                return AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed) ||
                       AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed) ||
                       AstSubtreeCapturesIdent(n.C, targetNameId, in ast, ref shadowed);

            case AstKind.ExprCall:
            {
                if (AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed)) return true;
                for (int i = 0; i < n.C; i++)
                {
                    AstNode ca = ast.Nodes[ast.Extra[n.B + i]];
                    if (ca.C == -2)
                    {
                        if (AstSubtreeCapturesIdent(ca.B, targetNameId, in ast, ref shadowed)) return true;
                    }
                    else if (ca.D >= 0)
                    {
                        shadowed.Push(ca.D);
                    }
                }
                return false;
            }

            case AstKind.ExprNew:
            {
                for (int i = 0; i < n.C; i++)
                {
                    AstNode ca = ast.Nodes[ast.Extra[n.B + i]];
                    if (ca.C == -2 && AstSubtreeCapturesIdent(ca.B, targetNameId, in ast, ref shadowed)) return true;
                    if (ca.C != -2 && ca.D >= 0) shadowed.Push(ca.D);
                }
                return false;
            }

            case AstKind.ExprCollLit:
            {
                for (int i = 0; i < n.B; i++)
                {
                    if (AstSubtreeCapturesIdent(ast.Extra[n.A + i], targetNameId, in ast, ref shadowed)) return true;
                }
                return false;
            }

            case AstKind.ExprIsPattern:
            {
                if (AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed)) return true;
                if (AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed)) return true;
                CollectPatternBindings(n.B, in ast, ref shadowed);
                return false;
            }

            case AstKind.PatConst:
                return AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed);
            case AstKind.PatRel:
                return AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed);
            case AstKind.PatNot:
                return AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed);
            case AstKind.PatLogic:
                return AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed) ||
                       AstSubtreeCapturesIdent(n.C, targetNameId, in ast, ref shadowed);

            case AstKind.ExprSwitch:
            {
                if (AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed)) return true;
                for (int i = 0; i < n.C; i++)
                {
                    AstNode arm = ast.Nodes[ast.Extra[n.B + i]];
                    int mark = shadowed.Count;
                    if (AstSubtreeCapturesIdent(arm.A, targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                    CollectPatternBindings(arm.A, in ast, ref shadowed);
                    if (arm.C >= 0 && AstSubtreeCapturesIdent(arm.C, targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                    if (AstSubtreeCapturesIdent(arm.B, targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                    shadowed.Truncate(mark);
                }
                return false;
            }

            case AstKind.StmtVarDecl:
            {
                if (n.C >= 0 && AstSubtreeCapturesIdent(n.C, targetNameId, in ast, ref shadowed)) return true;
                shadowed.Push(n.A);
                return false;
            }

            case AstKind.StmtMulti:
            {
                for (int i = 0; i < n.B; i++)
                {
                    if (AstSubtreeCapturesIdent(ast.Extra[n.A + i], targetNameId, in ast, ref shadowed)) return true;
                }
                return false;
            }

            case AstKind.StmtBlock:
            {
                int mark = shadowed.Count;
                for (int i = 0; i < n.B; i++)
                {
                    if (AstSubtreeCapturesIdent(ast.Extra[n.A + i], targetNameId, in ast, ref shadowed))
                    {
                        shadowed.Truncate(mark);
                        return true;
                    }
                }
                shadowed.Truncate(mark);
                return false;
            }

            case AstKind.StmtIf:
            {
                int mark = shadowed.Count;
                if (AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                if (AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                shadowed.Truncate(mark);
                if (n.C >= 0 && AstSubtreeCapturesIdent(n.C, targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                shadowed.Truncate(mark);
                return false;
            }

            case AstKind.StmtWhile:
            case AstKind.StmtDoWhile:
            {
                int mark = shadowed.Count;
                if (AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed) ||
                    AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed))
                {
                    shadowed.Truncate(mark);
                    return true;
                }
                shadowed.Truncate(mark);
                return false;
            }

            case AstKind.StmtFor:
            {
                int mark = shadowed.Count;
                if (AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed) ||
                    AstSubtreeCapturesIdent(n.B, targetNameId, in ast, ref shadowed) ||
                    AstSubtreeCapturesIdent(n.C, targetNameId, in ast, ref shadowed) ||
                    AstSubtreeCapturesIdent(n.D, targetNameId, in ast, ref shadowed))
                {
                    shadowed.Truncate(mark);
                    return true;
                }
                shadowed.Truncate(mark);
                return false;
            }

            case AstKind.StmtForeach:
            {
                if (AstSubtreeCapturesIdent(n.C, targetNameId, in ast, ref shadowed)) return true;
                int mark = shadowed.Count;
                shadowed.Push(n.A);
                bool hit = AstSubtreeCapturesIdent(n.D, targetNameId, in ast, ref shadowed);
                shadowed.Truncate(mark);
                return hit;
            }

            case AstKind.StmtSwitch:
            {
                if (AstSubtreeCapturesIdent(n.A, targetNameId, in ast, ref shadowed)) return true;
                for (int i = 0; i < n.C; i++)
                {
                    AstNode sec = ast.Nodes[ast.Extra[n.B + i]];
                    int mark = shadowed.Count;
                    for (int l = 0; l < sec.B; l++)
                    {
                        int lblId = ast.Extra[sec.A + l];
                        if (lblId >= 0)
                        {
                            AstNode lbl = ast.Nodes[lblId];
                            if (lbl.A >= 0)
                            {
                                if (AstSubtreeCapturesIdent(lbl.A, targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                                CollectPatternBindings(lbl.A, in ast, ref shadowed);
                            }
                            if (lbl.B >= 0 && AstSubtreeCapturesIdent(lbl.B, targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                        }
                    }
                    for (int st = 0; st < sec.D; st++)
                    {
                        if (AstSubtreeCapturesIdent(ast.Extra[sec.C + st], targetNameId, in ast, ref shadowed)) { shadowed.Truncate(mark); return true; }
                    }
                    shadowed.Truncate(mark);
                }
                return false;
            }
        }
        return false;
    }

    private static void CommitTypeMembers(int declId, in Vec<FieldDecl> localFields, in Vec<FuncDecl> localFuncs, ref AstArena ast)
    {
        TypeDecl td = ast.Types[declId];
        if (localFields.Count > 0)
        {
            if (td.FieldCount == 0)
            {
                td.FieldStart = ast.Fields.Count;
                td.FieldCount = localFields.Count;
                for (int i = 0; i < localFields.Count; i++) ast.Fields.Push(localFields[i]);
            }
            else
            {
                int oldStart = td.FieldStart;
                int oldCount = td.FieldCount;
                td.FieldStart = ast.Fields.Count;
                td.FieldCount = oldCount + localFields.Count;
                for (int i = 0; i < oldCount; i++) ast.Fields.Push(ast.Fields[oldStart + i]);
                for (int i = 0; i < localFields.Count; i++) ast.Fields.Push(localFields[i]);
            }
        }
        if (localFuncs.Count > 0)
        {
            if (td.FuncCount == 0)
            {
                td.FuncStart = ast.Funcs.Count;
                td.FuncCount = localFuncs.Count;
                for (int i = 0; i < localFuncs.Count; i++) ast.Funcs.Push(localFuncs[i]);
            }
            else
            {
                int oldStart = td.FuncStart;
                int oldCount = td.FuncCount;
                td.FuncStart = ast.Funcs.Count;
                td.FuncCount = oldCount + localFuncs.Count;
                for (int i = 0; i < oldCount; i++) ast.Funcs.Push(ast.Funcs[oldStart + i]);
                for (int i = 0; i < localFuncs.Count; i++) ast.Funcs.Push(localFuncs[i]);
            }
        }
        ast.Types[declId] = td;
    }

    private static void ParseEnumBody(ref ParserCtx ctx, in TokenArena ta, ref Interner names, ref AstArena ast, ref DiagnosticBag diags, int nameId, int flags, int declTokIdx) => ParseEnumBody(ref ctx, in ta, ref names, ref ast, ref diags, nameId, flags, declTokIdx, -1);

    private static void ParseEnumBody(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int nameId,
        int flags,
        int declTokIdx,
        int enclosingOwnerDeclId)
    {
        if (Match(ref ctx, in ta, TokKind.Colon))
        {
            Token ut = Advance(ref ctx, in ta);
            if (ut.Kind != TokKind.KwInt)
            {
                diags.ReportTok(VibCodes.Unsupported, ut, "Enum must use default 'int' underlying type in Vibranium."u8);
            }
        }
        int declId = ast.Types.Count;
        ast.Types.Push(new TypeDecl(TypeDeclKinds.Enum, ctx.UnitKind, ctx.CurrentNsId, nameId, 0, 0, 0, flags, ctx.SourceId, declTokIdx, enclosingOwnerDeclId, ctx.UsingExtraStart, ctx.UsingCount));

        Expect(ref ctx, in ta, TokKind.LBrace, ref diags, "Expected '{' for enum body."u8);
        int fStart = ast.Fields.Count;
        int fCount = 0;
        long nextVal = 0;
        int intTypeNode = ast.AddNode(AstKind.TypePrim, declTokIdx, (int)TokKind.KwInt, 0, 0, 0);

        while (PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int mTokIdx = ctx.Pos;
            int rawMName = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected enum member name."u8, out _);
            int mName = rawMName >= 0 ? rawMName : names.Intern("_ErrEnum"u8);
            int initExpr = -1;
            long val = nextVal;
            if (Match(ref ctx, in ta, TokKind.Eq))
            {
                initExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                if (TryEvalSimpleIntExpr(in ast, in ta, initExpr, out long explicitVal))
                {
                    val = explicitVal;
                }
            }
            nextVal = val + 1;
            ast.Fields.Push(new FieldDecl(mName, intTypeNode, initExpr, DeclFlags.Public | DeclFlags.Static | DeclFlags.IsConst, val, 1, mTokIdx));
            fCount += 1;
            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
        }
        Expect(ref ctx, in ta, TokKind.RBrace, ref diags, "Expected '}' after enum body."u8);
        Match(ref ctx, in ta, TokKind.Semi);

        TypeDecl updated = ast.Types[declId];
        updated.FieldStart = fStart;
        updated.FieldCount = fCount;
        ast.Types[declId] = updated;
    }

    public static bool TryEvalSimpleIntExpr(in AstArena ast, in TokenArena ta, int exprId, out long result)
    {
        if (exprId < 0) { result = 0; return false; }
        AstNode n = ast.Nodes[exprId];
        if (n.Kind == AstKind.ExprIntLit)
        {
            result = ta.Tokens[n.A].IntVal;
            return true;
        }
        if (n.Kind == AstKind.ExprCharLit)
        {
            result = n.A;
            return true;
        }
        if (n.Kind == AstKind.ExprUnary && n.C == 0 && TryEvalSimpleIntExpr(in ast, in ta, n.B, out long inner))
        {
            TokKind op = (TokKind)n.A;
            if (op == TokKind.Minus) { result = -inner; return true; }
            if (op == TokKind.Plus || op == TokKind.KwUnchecked) { result = inner; return true; }
            if (op == TokKind.Tilde) { result = ~inner; return true; }
        }
        if (n.Kind == AstKind.ExprBinary &&
            TryEvalSimpleIntExpr(in ast, in ta, n.B, out long l) &&
            TryEvalSimpleIntExpr(in ast, in ta, n.C, out long r))
        {
            TokKind op = (TokKind)n.A;
            if (op == TokKind.LtLt) { result = l << (int)r; return true; }
            if (op == TokKind.GtGt) { result = l >> (int)r; return true; }
            if (op == TokKind.GtGtGt) { result = l >>> (int)r; return true; }
            if (op == TokKind.Pipe) { result = l | r; return true; }
            if (op == TokKind.Amp) { result = l & r; return true; }
            if (op == TokKind.Caret) { result = l ^ r; return true; }
            if (op == TokKind.Plus) { result = l + r; return true; }
            if (op == TokKind.Minus) { result = l - r; return true; }
            if (op == TokKind.Star) { result = l * r; return true; }
            if (op == TokKind.Slash && r != 0 && !(l == long.MinValue && r == -1)) { result = l / r; return true; }
            if (op == TokKind.Percent && r != 0 && !(l == long.MinValue && r == -1)) { result = l % r; return true; }
        }
        result = 0;
        return false;
    }

    private static void ParseTypeMember(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int ownerDeclId,
        int ownerNameId,
        ref Vec<FieldDecl> localFields,
        ref Vec<FuncDecl> localFuncs)
    {
        int firstDiagnostic = diags.Items.Count;
        int memberStart = ctx.Pos;
        int fieldsBefore = localFields.Count;
        int funcsBefore = localFuncs.Count;
        ParseTypeMemberCore(ref ctx, in ta, ref names, ref ast, ref diags, ownerDeclId, ownerNameId, ref localFields, ref localFuncs, out bool rejected);
        if (rejected) KeepOnlySubsetCutsSince(firstDiagnostic, ref diags);
        else ValidateMemberModifiers(memberStart, ownerDeclId, in localFields, fieldsBefore, in localFuncs, funcsBefore, in ctx, in ta, in names, in ast, ref diags);
    }

    private static void ParseTypeMemberCore(
        ref ParserCtx ctx, in TokenArena ta, ref Interner names, ref AstArena ast,
        ref DiagnosticBag diags, int ownerDeclId, int ownerNameId,
        ref Vec<FieldDecl> localFields, ref Vec<FuncDecl> localFuncs, out bool rejected)
    {
        rejected = false;
        int partialToken = FindModifierToken(in ctx, in ta, in names, TokKind.KwPartial);
        int mFlags = 0;
        int nativeSymLit = -1;
        var retLifetimes = new Vec<int>();
        ParseAttributes(ref ctx, in ta, ref names, ref ast, ref diags, 1, ref mFlags, ref nativeSymLit, ref retLifetimes);
        mFlags |= ParseModifiers(ref ctx, in ta, ref diags);
        TypeDecl owner = ast.Types[ownerDeclId];
        if (partialToken >= 0 && !((mFlags & DeclFlags.HasNativeAttr) != 0 && IsTrustedSysType(in ctx, ownerNameId, owner.OwnerDeclId, in names)))
        {
            ReportSubsetCut(ref ctx, 2, ta.Tokens[partialToken], ref diags);
            rejected = true;
        }

        if (PeekKind(in ctx, in ta, 0) == TokKind.Tilde)
        {
            Token tilde = Advance(ref ctx, in ta);
            diags.ReportTok(VibCodes.Unsupported, tilde, "Finalizers are banned in Vibranium."u8);
            SkipDeclarationBody(ref ctx, in ta);
            return;
        }

        if (Match(ref ctx, in ta, TokKind.KwConst))
        {
            if ((mFlags & DeclFlags.HasNativeAttr) != 0)
            {
                diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "'[Native]' is only valid on Vibranium.Std.Sys catalog methods."u8);
            }
            mFlags |= DeclFlags.Static | DeclFlags.IsConst;
            int cType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
            while (true)
            {
                int fTokIdx = ctx.Pos;
                int rawFNameId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected const name."u8, out _);
                int fNameId = rawFNameId >= 0 ? rawFNameId : names.Intern("_ErrConst"u8);
                Expect(ref ctx, in ta, TokKind.Eq, ref diags, "Expected '=' in const declaration."u8);
                int cInit = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                long cVal = 0;
                int hasCVal = TryEvalSimpleIntExpr(in ast, in ta, cInit, out cVal) ? 1 : 0;
                localFields.Push(new FieldDecl(fNameId, cType, cInit, mFlags, cVal, hasCVal, fTokIdx));
                if (!Match(ref ctx, in ta, TokKind.Comma)) break;
            }
            Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after const declaration."u8);
            return;
        }

        if (IsIdentLike(PeekKind(in ctx, in ta, 0)) &&
            LookupTokenKnownId(Peek(in ctx, in ta, 0), in names) == ownerNameId &&
            PeekKind(in ctx, in ta, 1) == TokKind.LParen)
        {
            int ctorTokIdx = ctx.Pos;
            Token cTok = Advance(ref ctx, in ta);
            if ((mFlags & DeclFlags.Static) != 0)
            {
                diags.ReportTok(VibCodes.Unsupported, cTok, "Static constructors are banned in Vibranium."u8);
                SkipDeclarationBody(ref ctx, in ta);
                return;
            }
            var cParams = new Vec<ParamDecl>();
            ParseParamList(ref ctx, in ta, ref names, ref ast, ref diags, ref cParams);
            if (Match(ref ctx, in ta, TokKind.Colon))
            {
                Token chainTok = Peek(in ctx, in ta, 0);
                if (chainTok.Kind == TokKind.KwThis)
                {
                    ReportSubsetCut(ref ctx, 8, chainTok, ref diags);
                    rejected = true;
                }
                else if (chainTok.Kind == TokKind.Ident && Bytes.Eq(names.Text(chainTok.AuxId), "base"u8))
                {
                    ReportSubsetCut(ref ctx, 16, chainTok, ref diags);
                    rejected = true;
                }
                else diags.ReportTok(VibCodes.Unsupported, chainTok, "Expected 'this' or 'base' constructor initializer."u8);
                if (PeekKind(in ctx, in ta, 0) == TokKind.Ident || PeekKind(in ctx, in ta, 0) == TokKind.KwThis)
                {
                    Advance(ref ctx, in ta);
                }
                if (PeekKind(in ctx, in ta, 0) == TokKind.LParen)
                {
                    var ignoredChainArgs = new Vec<int>();
                    ParseCallArgList(ref ctx, in ta, ref names, ref ast, ref diags, ref ignoredChainArgs);
                }
            }
            int pStart = ast.Params.Count;
            for (int i = 0; i < cParams.Count; i++) ast.Params.Push(cParams[i]);

            int bodyNode = ParseMethodBody(ref ctx, in ta, ref names, ref ast, ref diags);
            int voidNode = ast.AddNode(AstKind.TypePrim, ctorTokIdx, (int)TokKind.KwVoid, 0, 0, 0);
            var fd = new FuncDecl(ownerDeclId, names.Intern(".ctor"u8), voidNode, 0, 0, 0, pStart, cParams.Count, bodyNode, mFlags | DeclFlags.IsCtor, -1, 0, 0, ctx.SourceId, ctorTokIdx, ctx.UsingExtraStart, ctx.UsingCount);
            AttachPendingLocalFuncs(ref ctx, ref ast, ref fd);
            localFuncs.Push(fd);
            return;
        }

        if (Match(ref ctx, in ta, TokKind.KwRef))
        {
            mFlags |= DeclFlags.ReturnsByRef;
            if (!Match(ref ctx, in ta, TokKind.KwReadonly))
            {
                mFlags |= DeclFlags.ReturnRefMut;
            }
        }

        int memberTokIdx = ctx.Pos;
        int retOrFieldType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);

        if (Match(ref ctx, in ta, TokKind.KwThis))
        {
            Expect(ref ctx, in ta, TokKind.LBracket, ref diags, "Expected '[' for indexer."u8);
            var idxParams = new Vec<ParamDecl>();
            ParseParamListUntil(ref ctx, in ta, ref names, ref ast, ref diags, TokKind.RBracket, ref idxParams);
            Expect(ref ctx, in ta, TokKind.RBracket, ref diags, "Expected ']' after indexer parameters."u8);
            int indexerNameId = names.Intern("Item"u8);
            ParsePropertyOrIndexerAccessors(ref ctx, in ta, ref names, ref ast, ref diags, ownerDeclId, indexerNameId, retOrFieldType, mFlags | DeclFlags.IsIndexer, in idxParams, in retLifetimes, memberTokIdx, ref localFuncs);
            return;
        }

        if (Match(ref ctx, in ta, TokKind.KwOperator))
        {
            Token opTok = Advance(ref ctx, in ta);
            if (opTok.Kind != TokKind.PlusEq)
            {
                diags.ReportTok(VibCodes.Unsupported, opTok, "Only 'void operator +=' is supported in Vibranium."u8);
            }
            var opParams = new Vec<ParamDecl>();
            ParseParamList(ref ctx, in ta, ref names, ref ast, ref diags, ref opParams);
            int pStart = ast.Params.Count;
            for (int i = 0; i < opParams.Count; i++) ast.Params.Push(opParams[i]);
            int opBody = ParseMethodBody(ref ctx, in ta, ref names, ref ast, ref diags);
            int opFlags = opTok.Kind == TokKind.PlusEq ? (mFlags | DeclFlags.IsOpAddAssign) : mFlags;
            var opFd = new FuncDecl(ownerDeclId, names.Intern("op_AdditionAssignment"u8), retOrFieldType, 0, 0, 0, pStart, opParams.Count, opBody, opFlags, -1, 0, 0, ctx.SourceId, memberTokIdx, ctx.UsingExtraStart, ctx.UsingCount);
            AttachPendingLocalFuncs(ref ctx, ref ast, ref opFd);
            localFuncs.Push(opFd);
            return;
        }

        int rawMemberNameId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected member name."u8, out Token memberNameTok);
        int memberNameId = rawMemberNameId >= 0 ? rawMemberNameId : names.Intern("_ErrMember"u8);

        if (PeekKind(in ctx, in ta, 0) == TokKind.LAngle || PeekKind(in ctx, in ta, 0) == TokKind.LParen)
        {
            var mTpNames = new Vec<int>();
            var mTpFlags = new Vec<int>();
            ParseOptionalTypeParamList(ref ctx, in ta, ref names, ref diags, ref mTpNames, ref mTpFlags);

            var mParams = new Vec<ParamDecl>();
            ParseParamList(ref ctx, in ta, ref names, ref ast, ref diags, ref mParams);
            ParseWhereClauses(ref ctx, in ta, ref names, ref diags, in mTpNames, ref mTpFlags);

            for (int pi = 0; pi < mParams.Count; pi++)
            {
                if ((mParams[pi].Flags & DeclFlags.IsExtension) != 0)
                {
                    if (pi == 0 &&
                        (mFlags & DeclFlags.Static) != 0 &&
                        (ast.Types[ownerDeclId].Flags & DeclFlags.Static) != 0 &&
                        ast.Types[ownerDeclId].TypeParamCount == 0)
                    {
                        mFlags |= DeclFlags.IsExtension;
                    }
                    else
                    {
                        diags.ReportTok(VibCodes.Unsupported, ta.Tokens[mParams[pi].TokIdx], "Extension modifier 'this' is only valid on the first parameter of a static method in a non-generic static class."u8);
                    }
                }
            }

            int mTpStart = ast.PushExtraSpan(mTpNames.AsReadOnlySpan());
            int mTpFlagsStart = ast.PushExtraSpan(mTpFlags.AsReadOnlySpan());
            int mParamStart = ast.Params.Count;
            for (int i = 0; i < mParams.Count; i++) ast.Params.Push(mParams[i]);
            int ltStart = ast.PushExtraSpan(retLifetimes.AsReadOnlySpan());

            int mBody = -1;
            if (Match(ref ctx, in ta, TokKind.Semi))
            {
                if (ctx.UnitKind != UnitKinds.Std || (mFlags & DeclFlags.HasNativeAttr) == 0)
                {
                    diags.ReportTok(VibCodes.Unsupported, memberNameTok, "Bodiless or partial/extern method declarations are banned outside Vibranium.Std.Sys."u8);
                }
            }
            else
            {
                mBody = ParseMethodBody(ref ctx, in ta, ref names, ref ast, ref diags);
            }
            var mFd = new FuncDecl(ownerDeclId, memberNameId, retOrFieldType, mTpStart, mTpNames.Count, mTpFlagsStart, mParamStart, mParams.Count, mBody, mFlags, nativeSymLit, ltStart, retLifetimes.Count, ctx.SourceId, memberTokIdx, ctx.UsingExtraStart, ctx.UsingCount);
            AttachPendingLocalFuncs(ref ctx, ref ast, ref mFd);
            localFuncs.Push(mFd);
            return;
        }

        if (PeekKind(in ctx, in ta, 0) == TokKind.FatArrow || PeekKind(in ctx, in ta, 0) == TokKind.LBrace)
        {
            if ((mFlags & DeclFlags.HasNativeAttr) != 0)
            {
                diags.ReportTok(VibCodes.Unsupported, memberNameTok, "'[Native]' is only valid on Vibranium.Std.Sys catalog methods."u8);
            }
            var emptyIdxParams = new Vec<ParamDecl>();
            ParsePropertyOrIndexerAccessors(ref ctx, in ta, ref names, ref ast, ref diags, ownerDeclId, memberNameId, retOrFieldType, mFlags, in emptyIdxParams, in retLifetimes, memberTokIdx, ref localFuncs);
            return;
        }

        if ((mFlags & DeclFlags.HasNativeAttr) != 0)
        {
            diags.ReportTok(VibCodes.Unsupported, memberNameTok, "'[Native]' is only valid on Vibranium.Std.Sys catalog methods."u8);
        }
        if ((mFlags & DeclFlags.Static) != 0)
        {
            diags.ReportTok(VibCodes.Unsupported, memberNameTok, "Non-const static field is banned in Vibranium."u8);
        }

        int curFieldName = memberNameId;
        int curFieldTok = memberTokIdx;
        while (true)
        {
            int fInit = -1;
            if (Match(ref ctx, in ta, TokKind.Eq))
            {
                fInit = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
            }
            localFields.Push(new FieldDecl(curFieldName, retOrFieldType, fInit, mFlags, 0, 0, curFieldTok));
            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
            curFieldTok = ctx.Pos;
            int rawNextField = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected field name."u8, out _);
            curFieldName = rawNextField >= 0 ? rawNextField : names.Intern("_ErrField"u8);
        }
        Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after field declaration."u8);
    }

    private static void ParsePropertyOrIndexerAccessors(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int ownerDeclId,
        int propNameId,
        int propTypeNode,
        int mFlags,
        in Vec<ParamDecl> idxParams,
        in Vec<int> retLifetimes,
        int memberTokIdx,
        ref Vec<FuncDecl> localFuncs)
    {
        int ltStart = ast.PushExtraSpan(retLifetimes.AsReadOnlySpan());

        if (Match(ref ctx, in ta, TokKind.FatArrow))
        {
            int exprTok = ctx.Pos;
            int expr = ParseExpressionAllowingRef(ref ctx, in ta, ref names, ref ast, ref diags);
            Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after expression-bodied property."u8);
            int isRefRet = (mFlags & DeclFlags.ReturnsByRef) != 0 ? 1 : 0;
            if (isRefRet != 0 && ast.Nodes[expr].Kind == AstKind.ExprUnary && ast.Nodes[expr].A == (int)TokKind.KwRef)
            {
                expr = ast.Nodes[expr].B;
            }
            int retStmt = ast.AddNode(AstKind.StmtReturn, exprTok, expr, isRefRet, 0, 0);
            var one = new Vec<int>(1);
            one.Push(retStmt);
            int oneStart = ast.PushExtraSpan(one.AsReadOnlySpan());
            int block = ast.AddNode(AstKind.StmtBlock, exprTok, oneStart, 1, 0, 0);

            int pStart = ast.Params.Count;
            for (int i = 0; i < idxParams.Count; i++) ast.Params.Push(idxParams[i]);

            int getterNameId = MakeAccessorName(ref names, "get_"u8, propNameId);
            int exprGetFlags = (mFlags & ~DeclFlags.MutAttr) | DeclFlags.IsPropGet;
            var gFd = new FuncDecl(ownerDeclId, getterNameId, propTypeNode, 0, 0, 0, pStart, idxParams.Count, block, exprGetFlags, -1, ltStart, retLifetimes.Count, ctx.SourceId, memberTokIdx, ctx.UsingExtraStart, ctx.UsingCount);
            AttachPendingLocalFuncs(ref ctx, ref ast, ref gFd);
            localFuncs.Push(gFd);
            return;
        }

        Expect(ref ctx, in ta, TokKind.LBrace, ref diags, "Expected '{' for property accessors."u8);
        while (PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int accOwnFlags = 0;
            int unusedNat = -1;
            var accLt = new Vec<int>();
            ParseAttributes(ref ctx, in ta, ref names, ref ast, ref diags, 1, ref accOwnFlags, ref unusedNat, ref accLt);
            int accessorModifierStart = ctx.Pos;
            accOwnFlags |= ParseModifiers(ref ctx, in ta, ref diags);
            for (int am = accessorModifierStart; am < ctx.Pos; am++)
            {
                TokKind amKind = ta.Tokens[am].Kind;
                if (amKind == TokKind.KwStatic || amKind == TokKind.KwSealed ||
                    (amKind == TokKind.KwReadonly && ast.Types[ownerDeclId].Kind == TypeDeclKinds.Class))
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[am], "The modifier is not valid for this accessor (CS0106)."u8);
            }

            Token accTok = Expect(ref ctx, in ta, TokKind.Ident, ref diags, "Expected 'get' or 'set'."u8);
            ReadOnlySpan<byte> accWord = accTok.Kind == TokKind.Ident ? names.Text(accTok.AuxId) : ""u8;
            bool isGet = Bytes.Eq(accWord, "get"u8);
            bool isSet = Bytes.Eq(accWord, "set"u8);

            if (Match(ref ctx, in ta, TokKind.Semi))
            {
                diags.ReportTok(VibCodes.Unsupported, accTok, "Auto-properties are banned in Vibranium; declare an explicit field."u8);
                continue;
            }

            int accBody = ParseMethodBody(ref ctx, in ta, ref names, ref ast, ref diags);
            if (isGet)
            {
                int pStart = ast.Params.Count;
                for (int i = 0; i < idxParams.Count; i++) ast.Params.Push(idxParams[i]);
                int getterNameId = MakeAccessorName(ref names, "get_"u8, propNameId);
                // Property-level [Mut] propagates ONLY to set_P/set_Item; get_P/get_Item is shared unless [Mut] is explicitly on get.
                int gFlags = (mFlags & ~DeclFlags.MutAttr) | accOwnFlags | DeclFlags.IsPropGet;
                int gLtStart = ltStart;
                int gLtCount = retLifetimes.Count;
                if (accLt.Count > 0)
                {
                    var mergedLt = new Vec<int>(retLifetimes.Count + accLt.Count);
                    for (int i = 0; i < retLifetimes.Count; i++) mergedLt.Push(retLifetimes[i]);
                    for (int i = 0; i < accLt.Count; i++) mergedLt.Push(accLt[i]);
                    gLtStart = ast.PushExtraSpan(mergedLt.AsReadOnlySpan());
                    gLtCount = mergedLt.Count;
                    gFlags |= DeclFlags.HasReturnLifetimeAttr;
                }
                var gFd = new FuncDecl(ownerDeclId, getterNameId, propTypeNode, 0, 0, 0, pStart, idxParams.Count, accBody, gFlags, -1, gLtStart, gLtCount, ctx.SourceId, memberTokIdx, ctx.UsingExtraStart, ctx.UsingCount);
                AttachPendingLocalFuncs(ref ctx, ref ast, ref gFd);
                localFuncs.Push(gFd);
            }
            else if (isSet)
            {
                int pStart = ast.Params.Count;
                for (int i = 0; i < idxParams.Count; i++) ast.Params.Push(idxParams[i]);
                ast.Params.Push(new ParamDecl(names.Intern("value"u8), propTypeNode, PassMode.ByValue, 0, memberTokIdx, -1));
                int voidNode = ast.AddNode(AstKind.TypePrim, memberTokIdx, (int)TokKind.KwVoid, 0, 0, 0);
                int setterNameId = MakeAccessorName(ref names, "set_"u8, propNameId);
                int sFlags = ((mFlags | accOwnFlags) & ~(DeclFlags.ReturnsByRef | DeclFlags.ReturnRefMut)) | DeclFlags.IsPropSet;
                var sFd = new FuncDecl(ownerDeclId, setterNameId, voidNode, 0, 0, 0, pStart, idxParams.Count + 1, accBody, sFlags, -1, 0, 0, ctx.SourceId, memberTokIdx, ctx.UsingExtraStart, ctx.UsingCount);
                AttachPendingLocalFuncs(ref ctx, ref ast, ref sFd);
                localFuncs.Push(sFd);
            }
        }
        Expect(ref ctx, in ta, TokKind.RBrace, ref diags, "Expected '}' after property accessors."u8);
    }

    private static int MakeAccessorName(ref Interner names, ReadOnlySpan<byte> prefix, int propNameId)
    {
        var sb = new StringBuf(32);
        sb.Append(prefix);
        sb.Append(names.Text(propNameId));
        return names.Intern(sb.AsUtf8());
    }

    private static void AttachPendingLocalFuncs(ref ParserCtx ctx, ref AstArena ast, ref FuncDecl fd)
    {
        if (ctx.PendingLocalFuncs.Count > 0)
        {
            fd.LocalFuncExtraStart = ast.PushExtraSpan(ctx.PendingLocalFuncs.AsReadOnlySpan());
            fd.LocalFuncCount = ctx.PendingLocalFuncs.Count;
            ctx.PendingLocalFuncs.Clear();
        }
    }

    private static int ParseMethodBody(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        if (Match(ref ctx, in ta, TokKind.FatArrow))
        {
            int exprTok = ctx.Pos;
            bool isRef = Match(ref ctx, in ta, TokKind.KwRef);
            int expr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
            Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after expression body."u8);
            int retNode = ast.AddNode(AstKind.StmtReturn, exprTok, expr, isRef ? 1 : 0, 0, 0);
            var single = new Vec<int>(1);
            single.Push(retNode);
            int singleStart = ast.PushExtraSpan(single.AsReadOnlySpan());
            return ast.AddNode(AstKind.StmtBlock, exprTok, singleStart, 1, 0, 0);
        }

        return ParseBlockStatement(ref ctx, in ta, ref names, ref ast, ref diags);
    }

    private static void ParseOptionalTypeParamList(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref DiagnosticBag diags,
        ref Vec<int> tpNames,
        ref Vec<int> tpFlags)
    {
        if (!Match(ref ctx, in ta, TokKind.LAngle)) return;
        while (!IsAtTypeArgClose(in ctx, in ta) && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int rawTpId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected type parameter name."u8, out _);
            tpNames.Push(rawTpId >= 0 ? rawTpId : 0);
            tpFlags.Push(0);
            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
        }
        ExpectTypeArgClose(ref ctx, in ta, ref diags, "Expected '>' after type parameter list."u8);
    }

    private static void ParseWhereClauses(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref DiagnosticBag diags,
        in Vec<int> tpNames,
        ref Vec<int> tpFlags)
    {
        while (Match(ref ctx, in ta, TokKind.KwWhere))
        {
            int tpNameId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected type parameter after 'where'."u8, out _);
            int tpIdx = -1;
            for (int i = 0; i < tpNames.Count; i++)
            {
                if (tpNames[i] == tpNameId) { tpIdx = i; break; }
            }
            Expect(ref ctx, in ta, TokKind.Colon, ref diags, "Expected ':' in where clause."u8);
            while (true)
            {
                Token cTok = Peek(in ctx, in ta, 0);
                if (cTok.Kind == TokKind.Ident && Bytes.Eq(names.Text(cTok.AuxId), "allows"u8))
                {
                    Advance(ref ctx, in ta);
                    Match(ref ctx, in ta, TokKind.KwRef);
                    Match(ref ctx, in ta, TokKind.KwStruct);
                    if (ctx.UnitKind != UnitKinds.Core)
                    {
                        diags.ReportTok(VibCodes.Unsupported, cTok, "Type parameter cannot use 'allows ref struct' in Vibranium."u8);
                    }
                }
                else if (cTok.Kind == TokKind.Ident && Bytes.Eq(names.Text(cTok.AuxId), "unmanaged"u8))
                {
                    Advance(ref ctx, in ta);
                    if (tpIdx >= 0) tpFlags[tpIdx] |= DeclFlags.UnmanagedConstraint;
                }
                else if (cTok.Kind == TokKind.KwStruct)
                {
                    Advance(ref ctx, in ta);
                    if (tpIdx >= 0) tpFlags[tpIdx] |= DeclFlags.StructConstraint;
                }
                else if (cTok.Kind == TokKind.KwClass)
                {
                    Advance(ref ctx, in ta);
                    if (tpIdx >= 0) tpFlags[tpIdx] |= DeclFlags.ClassConstraint;
                }
                else if (cTok.Kind == TokKind.KwNew)
                {
                    Advance(ref ctx, in ta);
                    Match(ref ctx, in ta, TokKind.LParen);
                    Match(ref ctx, in ta, TokKind.RParen);
                    diags.ReportTok(VibCodes.Unsupported, cTok, "Type parameter cannot use 'new()' constraint in Vibranium."u8);
                }
                else
                {
                    Advance(ref ctx, in ta);
                    diags.ReportTok(VibCodes.Unsupported, cTok, "Interface and base-class type constraints are banned in Vibranium."u8);
                }
                if (!Match(ref ctx, in ta, TokKind.Comma)) break;
            }
        }
    }

    private static void ParseParamList(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        ref Vec<ParamDecl> outParams)
    {
        Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '('."u8);
        ParseParamListUntil(ref ctx, in ta, ref names, ref ast, ref diags, TokKind.RParen, ref outParams);
        Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')'."u8);
    }

    private static void ParseParamListUntil(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        TokKind endTok,
        ref Vec<ParamDecl> outParams)
    {
        while (PeekKind(in ctx, in ta, 0) != endTok && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int parameterStart = ctx.Pos;
            int pFlags = 0;
            int unusedNat = -1;
            var unusedLt = new Vec<int>();
            ParseAttributes(ref ctx, in ta, ref names, ref ast, ref diags, 3, ref pFlags, ref unusedNat, ref unusedLt);
            for (int attributeToken = parameterStart; attributeToken < ctx.Pos; attributeToken++)
            {
                Token attribute = ta.Tokens[attributeToken];
                bool qualified = attributeToken > parameterStart &&
                    (ta.Tokens[attributeToken - 1].Kind == TokKind.Dot || ta.Tokens[attributeToken - 1].Kind == TokKind.ColonColon);
                if (attribute.Kind == TokKind.Ident && SubsetCutChecker.IsOptionalAttribute(attribute, qualified, in ast, in names))
                    SubsetCuts.Report(12, ta.Tokens[parameterStart], ref diags);
            }

            while (true)
            {
                if (PeekKind(in ctx, in ta, 0) == TokKind.KwThis)
                {
                    Token thisTok = Advance(ref ctx, in ta);
                    if ((pFlags & DeclFlags.IsExtension) != 0)
                        diags.ReportTok(VibCodes.Unsupported, thisTok, "A parameter can only have one 'this' modifier (CS1107)."u8);
                    pFlags |= DeclFlags.IsExtension;
                    continue;
                }
                if (PeekKind(in ctx, in ta, 0) == TokKind.KwScoped && IsScopedModifierAt(in ta, ctx.Pos, ctx.EndPos, isParam: true))
                {
                    Token scopedTok = Advance(ref ctx, in ta);
                    if ((pFlags & DeclFlags.IsScoped) != 0)
                        diags.ReportTok(VibCodes.Unsupported, scopedTok, "A parameter can only have one 'scoped' modifier (CS1107)."u8);
                    pFlags |= DeclFlags.IsScoped;
                    continue;
                }
                break;
            }

            PassMode mode = PassMode.ByValue;
            if (Match(ref ctx, in ta, TokKind.KwIn)) mode = PassMode.In;
            else if (Match(ref ctx, in ta, TokKind.KwOut))
            {
                mode = PassMode.Out;
                if ((pFlags & DeclFlags.IsExtension) != 0)
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[ctx.Pos - 1], "The parameter modifier 'out' cannot be used with 'this' (CS8328)."u8);
            }
            else if (Match(ref ctx, in ta, TokKind.KwRef))
            {
                if (Match(ref ctx, in ta, TokKind.KwReadonly))
                {
                    SubsetCuts.Report(10, ta.Tokens[ctx.Pos - 2], ref diags);
                    mode = PassMode.In;
                    // Keep the source-variable requirement distinct from plain 'in'.
                    pFlags |= DeclFlags.Readonly;
                }
                else mode = PassMode.Ref;
            }

            if (PeekKind(in ctx, in ta, 0) == TokKind.Ident && Bytes.Eq(names.Text(Peek(in ctx, in ta, 0).AuxId), "params"u8))
            {
                Token pt = Advance(ref ctx, in ta);
                if (ctx.UnitKind != UnitKinds.Core)
                {
                    diags.ReportTok(VibCodes.Unsupported, pt, "'params' parameters are not supported in Vibranium."u8);
                }
            }

            int pTokIdx = ctx.Pos;
            int pType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
            int rawPNameId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected parameter name."u8, out _);
            int pNameId = rawPNameId >= 0 ? rawPNameId : names.Intern("_param"u8);

            int defExpr = -1;
            if (Match(ref ctx, in ta, TokKind.Eq))
            {
                SubsetCuts.Report(12, ta.Tokens[parameterStart], ref diags);
                defExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
            }

            outParams.Push(new ParamDecl(pNameId, pType, mode, pFlags, pTokIdx, defExpr));
            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
        }
    }

    private static int ParseTypeSyntax(ref ParserCtx ctx, in TokenArena ta, ref Interner names, ref AstArena ast, ref DiagnosticBag diags) => ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags, true);

    private static int ParseTypeSyntax(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        bool allowNullableSuffix)
    {
        int tokIdx = ctx.Pos;
        Token t = Peek(in ctx, in ta, 0);

        if (t.Kind == TokKind.KwDynamic)
        {
            Advance(ref ctx, in ta);
            diags.ReportTok(VibCodes.Unsupported, t, "'dynamic' type is banned in Vibranium."u8);
            return ast.AddNode(AstKind.TypePrim, tokIdx, (int)TokKind.KwInt, 0, 0, 0);
        }
        if (t.Kind == TokKind.KwDecimal)
        {
            Advance(ref ctx, in ta);
            diags.ReportTok(VibCodes.Unsupported, t, "'decimal' is not supported in Vibranium."u8);
            return ast.AddNode(AstKind.TypePrim, tokIdx, (int)TokKind.KwInt, 0, 0, 0);
        }
        if (t.Kind == TokKind.KwDelegate && PeekKind(in ctx, in ta, 1) == TokKind.Star)
        {
            Advance(ref ctx, in ta);
            Advance(ref ctx, in ta);
            if (Match(ref ctx, in ta, TokKind.LAngle))
            {
                int d = 1;
                while (d > 0 && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                {
                    TokKind k = Advance(ref ctx, in ta).Kind;
                    if (k == TokKind.LAngle) d += 1;
                    else if (k == TokKind.RAngle) d -= 1;
                }
            }
            diags.ReportTok(VibCodes.Unsupported, t, "Function pointer types are banned in Vibranium."u8);
            return ast.AddNode(AstKind.TypePrim, tokIdx, (int)TokKind.KwInt, 0, 0, 0);
        }

        int baseNode;
        if (IsPrimitiveTypeKeyword(t.Kind))
        {
            Advance(ref ctx, in ta);
            baseNode = ast.AddNode(AstKind.TypePrim, tokIdx, (int)t.Kind, 0, 0, 0);
        }
        else
        {
            bool hasGlobal = false;
            if (PeekKind(in ctx, in ta, 0) == TokKind.KwGlobal && PeekKind(in ctx, in ta, 1) == TokKind.ColonColon)
            {
                hasGlobal = true;
                Advance(ref ctx, in ta);
                Advance(ref ctx, in ta);
            }

            var prefixBuf = new StringBuf(32);
            bool hasDotPrefix = false;
            if (hasGlobal)
            {
                prefixBuf.Append("global::"u8);
            }
            int rawFirstId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected type name."u8, out _);
            int lastId = rawFirstId >= 0 ? rawFirstId : names.Intern("_ErrT"u8);

            var typeArgs = new Vec<int>();
            while (true)
            {
                typeArgs.Clear();
                if (Match(ref ctx, in ta, TokKind.LAngle))
                {
                    while (!IsAtTypeArgClose(in ctx, in ta) && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                    {
                        typeArgs.Push(ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags));
                        if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                    }
                    ExpectTypeArgClose(ref ctx, in ta, ref diags, "Expected '>' in generic type."u8);
                }
                if (Match(ref ctx, in ta, TokKind.Dot))
                {
                    if (hasDotPrefix) prefixBuf.AppendAscii((byte)'.');
                    prefixBuf.Append(names.Text(lastId));
                    hasDotPrefix = true;
                    int rawNextId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected type name after '.'."u8, out _);
                    if (rawNextId >= 0) lastId = rawNextId;
                    continue;
                }
                break;
            }

            int prefixNsId = (hasGlobal || hasDotPrefix) ? names.Intern(prefixBuf.AsUtf8()) : -1;
            int typeRefIdx = ast.AddTypeRef(new TypeRefContext(prefixNsId, ctx.UnitKind, ctx.CurrentNsId, ctx.CurrentOwnerDeclId, ctx.UsingExtraStart, ctx.UsingCount));
            int taStart = ast.PushExtraSpan(typeArgs.AsReadOnlySpan());
            baseNode = ast.AddNode(AstKind.TypeNamed, tokIdx, lastId, taStart, typeArgs.Count, typeRefIdx);
        }

        while (true)
        {
            if (allowNullableSuffix && Match(ref ctx, in ta, TokKind.Question))
            {
                diags.ReportTok(VibCodes.NullOrZeroUnsafe, Peek(in ctx, in ta, -1), "Nullable '?' type is banned in Vibranium; use Option<T>."u8);
                baseNode = ast.AddNode(AstKind.TypeNullable, tokIdx, baseNode, 0, 0, 0);
            }
            else if (Match(ref ctx, in ta, TokKind.Star))
            {
                diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, -1), "Raw pointer types are banned in Vibranium."u8);
            }
            else if (PeekKind(in ctx, in ta, 0) == TokKind.LBracket && PeekKind(in ctx, in ta, 1) == TokKind.RBracket)
            {
                Advance(ref ctx, in ta);
                Advance(ref ctx, in ta);
                baseNode = ast.AddNode(AstKind.TypeArray, tokIdx, baseNode, 0, 0, 0);
            }
            else
            {
                break;
            }
        }

        return baseNode;
    }

    public static bool IsPrimitiveTypeKeyword(TokKind k) =>
        k >= TokKind.KwVoid && k <= TokKind.KwObject;

    private static bool LooksLikeLocalFunction(in ParserCtx ctx, in TokenArena ta)
    {
        int p = ctx.Pos;
        while (p < ctx.EndPos && ta.Tokens[p].Kind == TokKind.LBracket)
        {
            p = SkipBracketBalanced(in ta, p, ctx.EndPos);
        }
        bool hasStaticOrUnsafe = false;
        while (p < ctx.EndPos)
        {
            TokKind mk = ta.Tokens[p].Kind;
            if (mk == TokKind.KwStatic)
            {
                hasStaticOrUnsafe = true;
                p += 1;
            }
            else if (mk == TokKind.KwUnsafe && p + 1 < ctx.EndPos && ta.Tokens[p + 1].Kind != TokKind.LBrace)
            {
                hasStaticOrUnsafe = true;
                p += 1;
            }
            else if (mk == TokKind.KwAsync)
            {
                p += 1;
            }
            else
            {
                break;
            }
        }
        if (hasStaticOrUnsafe) return true;

        if (p < ctx.EndPos && ta.Tokens[p].Kind == TokKind.KwRef)
        {
            p += 1;
            if (p < ctx.EndPos && ta.Tokens[p].Kind == TokKind.KwReadonly) p += 1;
        }
        if (!TryScanTypeSyntax(in ta, p, ctx.EndPos, out int afterType)) return false;
        if (afterType >= ctx.EndPos || !IsIdentLike(ta.Tokens[afterType].Kind)) return false;
        int afterName = afterType + 1;
        if (afterName < ctx.EndPos && ta.Tokens[afterName].Kind == TokKind.LAngle)
        {
            if (!CanScanTypeArgList(in ta, afterName, ctx.EndPos, out afterName)) return false;
        }
        if (afterName >= ctx.EndPos || ta.Tokens[afterName].Kind != TokKind.LParen) return false;
        int afterParen = SkipParenBalanced(in ta, afterName, ctx.EndPos);
        if (afterParen < ctx.EndPos)
        {
            TokKind post = ta.Tokens[afterParen].Kind;
            return post == TokKind.LBrace || post == TokKind.FatArrow || post == TokKind.KwWhere;
        }
        return false;
    }

    private static int ParseLocalFunction(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        int lfFlags = DeclFlags.IsLocalFunc;
        int nativeSym = -1;
        var retLt = new Vec<int>();
        ParseAttributes(ref ctx, in ta, ref names, ref ast, ref diags, 1, ref lfFlags, ref nativeSym, ref retLt);
        lfFlags |= ParseModifiers(ref ctx, in ta, ref diags);

        if ((lfFlags & DeclFlags.HasNativeAttr) != 0)
        {
            diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "'[Native]' is only valid on Vibranium.Std.Sys catalog methods."u8);
            nativeSym = -1;
        }
        if (Match(ref ctx, in ta, TokKind.KwRef))
        {
            lfFlags |= DeclFlags.ReturnsByRef;
            if (!Match(ref ctx, in ta, TokKind.KwReadonly)) lfFlags |= DeclFlags.ReturnRefMut;
        }

        int retType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
        int nameTokIdx = ctx.Pos;
        int rawLfNameId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected local function name."u8, out Token nameTok);
        int nameId = rawLfNameId >= 0 ? rawLfNameId : names.Intern("_localFn"u8);

        if ((lfFlags & DeclFlags.Static) == 0)
        {
            diags.ReportTok(VibCodes.Unsupported, nameTok, "Non-static (capturing) local functions are banned in Vibranium; declare local functions 'static'."u8);
        }

        var tpNames = new Vec<int>();
        var tpFlags = new Vec<int>();
        ParseOptionalTypeParamList(ref ctx, in ta, ref names, ref diags, ref tpNames, ref tpFlags);

        var lfParams = new Vec<ParamDecl>();
        ParseParamList(ref ctx, in ta, ref names, ref ast, ref diags, ref lfParams);
        ParseWhereClauses(ref ctx, in ta, ref names, ref diags, in tpNames, ref tpFlags);

        var savedPending = new Vec<int>();
        for (int i = 0; i < ctx.PendingLocalFuncs.Count; i++) savedPending.Push(ctx.PendingLocalFuncs[i]);
        ctx.PendingLocalFuncs.Clear();

        int lfBody = ParseMethodBody(ref ctx, in ta, ref names, ref ast, ref diags);

        int tpStart = ast.PushExtraSpan(tpNames.AsReadOnlySpan());
        int tpFlagsStart = ast.PushExtraSpan(tpFlags.AsReadOnlySpan());
        for (int i = 0; i < lfParams.Count; i++)
        {
            if ((lfParams[i].Flags & DeclFlags.IsExtension) != 0)
            {
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[lfParams[i].TokIdx], "Local functions cannot declare 'this' extension parameters."u8);
            }
        }
        int pStart = ast.Params.Count;
        for (int i = 0; i < lfParams.Count; i++) ast.Params.Push(lfParams[i]);
        int ltStart = ast.PushExtraSpan(retLt.AsReadOnlySpan());

        var fd = new FuncDecl(-1, nameId, retType, tpStart, tpNames.Count, tpFlagsStart, pStart, lfParams.Count, lfBody, lfFlags, -1, ltStart, retLt.Count, ctx.SourceId, nameTokIdx, ctx.UsingExtraStart, ctx.UsingCount);
        AttachPendingLocalFuncs(ref ctx, ref ast, ref fd);

        for (int i = 0; i < savedPending.Count; i++) ctx.PendingLocalFuncs.Push(savedPending[i]);

        int funcIdx = ast.Funcs.Count;
        ast.Funcs.Push(fd);
        return funcIdx;
    }

    private static int ParseBlockStatement(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        int tokIdx = ctx.Pos;
        Expect(ref ctx, in ta, TokKind.LBrace, ref diags, "Expected '{'."u8);
        var stmts = new Vec<int>();
        var blockLocalFuncs = new Vec<int>();
        while (PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int save = ctx.Pos;
            if (LooksLikeLocalFunction(in ctx, in ta))
            {
                int lf = ParseLocalFunction(ref ctx, in ta, ref names, ref ast, ref diags);
                if (lf >= 0) blockLocalFuncs.Push(lf);
                continue;
            }
            int s = ParseStatement(ref ctx, in ta, ref names, ref ast, ref diags);
            PushStmtFlattened(s, in ast.Nodes, in ast.Extra, ref stmts);
            if (ctx.Pos == save && PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
            {
                Advance(ref ctx, in ta);
            }
        }
        Expect(ref ctx, in ta, TokKind.RBrace, ref diags, "Expected '}'."u8);
        int stmtsStart = ast.PushExtraSpan(stmts.AsReadOnlySpan());
        int lfStart = blockLocalFuncs.Count > 0 ? ast.PushExtraSpan(blockLocalFuncs.AsReadOnlySpan()) : 0;
        return ast.AddNode(AstKind.StmtBlock, tokIdx, stmtsStart, stmts.Count, lfStart, blockLocalFuncs.Count);
    }

    private static void PushStmtFlattened(int stmtId, in Vec<AstNode> nodes, in Vec<int> extra, ref Vec<int> dst)
    {
        if (stmtId < 0) return;
        AstNode n = nodes[stmtId];
        if (n.Kind == AstKind.StmtMulti)
        {
            for (int i = 0; i < n.B; i++)
            {
                dst.Push(extra[n.A + i]);
            }
            return;
        }
        dst.Push(stmtId);
    }

    private static int ParseStatement(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        if (ctx.Nesting >= MaxNesting)
        {
            diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "Maximum AST nesting depth exceeded."u8);
            Advance(ref ctx, in ta);
            return -1;
        }
        ctx.Nesting += 1;
        int res = ParseStatementInner(ref ctx, in ta, ref names, ref ast, ref diags);
        ctx.Nesting -= 1;
        return res;
    }

    // C# embedded_statement excludes declaration_statement, even though declarations
    // remain legal in blocks, switch sections, and for/using headers.
    private static int ParseEmbeddedStatement(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        Token start = Peek(in ctx, in ta, 0);
        int statement = ParseStatement(ref ctx, in ta, ref names, ref ast, ref diags);
        if (statement >= 0 && (ast.Nodes[statement].Kind == AstKind.StmtVarDecl || ast.Nodes[statement].Kind == AstKind.StmtMulti))
        {
            diags.ReportTok(VibCodes.Unsupported, start, "An embedded statement cannot be a declaration; use a braced block."u8);
        }
        return statement;
    }

    private static int ParseStatementInner(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        int tokIdx = ctx.Pos;
        Token t = Peek(in ctx, in ta, 0);
        if (t.Kind == TokKind.Ident && Bytes.Eq(names.Text(t.AuxId), "_"u8) && PeekKind(in ctx, in ta, 1) == TokKind.Colon)
            SubsetCuts.Report(11, t, ref diags);

        switch (t.Kind)
        {
            case TokKind.LBrace:
                return ParseBlockStatement(ref ctx, in ta, ref names, ref ast, ref diags);

            case TokKind.Semi:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);

            case TokKind.KwIf:
            {
                Advance(ref ctx, in ta);
                Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '(' after 'if'."u8);
                int cond = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' after if condition."u8);
                int thenStmt = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                int elseStmt = -1;
                if (Match(ref ctx, in ta, TokKind.KwElse))
                {
                    elseStmt = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                }
                return ast.AddNode(AstKind.StmtIf, tokIdx, cond, thenStmt, elseStmt, 0);
            }

            case TokKind.KwWhile:
            {
                Advance(ref ctx, in ta);
                Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '(' after 'while'."u8);
                int cond = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' after while condition."u8);
                int body = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                return ast.AddNode(AstKind.StmtWhile, tokIdx, cond, body, 0, 0);
            }

            case TokKind.KwDo:
            {
                Advance(ref ctx, in ta);
                int body = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.KwWhile, ref diags, "Expected 'while' after 'do' body."u8);
                Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '(' after 'while'."u8);
                int cond = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' after do-while condition."u8);
                Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after do-while statement."u8);
                return ast.AddNode(AstKind.StmtDoWhile, tokIdx, body, cond, 0, 0);
            }

            case TokKind.KwFor:
            {
                Advance(ref ctx, in ta);
                Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '(' after 'for'."u8);
                int initStmt = -1;
                if (!Match(ref ctx, in ta, TokKind.Semi))
                {
                    if (LooksLikeVarDecl(in ctx, in ta))
                    {
                        initStmt = ParseVarDeclStatement(ref ctx, in ta, ref names, ref ast, ref diags, varFlags: 0);
                    }
                    else
                    {
                        var initExprs = new Vec<int>();
                        while (true)
                        {
                            int eTok = ctx.Pos;
                            int e = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                            initExprs.Push(ast.AddNode(AstKind.StmtExpr, eTok, e, 0, 0, 0));
                            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                        }
                        Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after for initializer."u8);
                        int initStart = ast.PushExtraSpan(initExprs.AsReadOnlySpan());
                        initStmt = ast.AddNode(AstKind.StmtBlock, tokIdx, initStart, initExprs.Count, 0, 0);
                    }
                }
                int condExpr = -1;
                if (!Match(ref ctx, in ta, TokKind.Semi))
                {
                    condExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                    Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after for condition."u8);
                }
                int stepStmt = -1;
                if (!Match(ref ctx, in ta, TokKind.RParen))
                {
                    var stepExprs = new Vec<int>();
                    while (true)
                    {
                        int sTok = ctx.Pos;
                        int se = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                        stepExprs.Push(ast.AddNode(AstKind.StmtExpr, sTok, se, 0, 0, 0));
                        if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                    }
                    Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' after for iterator."u8);
                    int stepStart = ast.PushExtraSpan(stepExprs.AsReadOnlySpan());
                    stepStmt = ast.AddNode(AstKind.StmtBlock, tokIdx, stepStart, stepExprs.Count, 0, 0);
                }
                int bodyStmt = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                return ast.AddNode(AstKind.StmtFor, tokIdx, initStmt, condExpr, stepStmt, bodyStmt);
            }

            case TokKind.KwForeach:
            {
                Advance(ref ctx, in ta);
                Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '(' after 'foreach'."u8);
                int feFlags = 0;
                if (Match(ref ctx, in ta, TokKind.KwRef))
                {
                    feFlags = Match(ref ctx, in ta, TokKind.KwReadonly) ? 4 : 2;
                }
                int itemTypeNode = -1;
                if (PeekKind(in ctx, in ta, 0) == TokKind.KwVar && IsIdentLike(PeekKind(in ctx, in ta, 1)))
                {
                    Advance(ref ctx, in ta);
                }
                else
                {
                    itemTypeNode = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
                }
                int rawItemNameId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected loop variable in 'foreach'."u8, out _);
                int itemNameId = rawItemNameId >= 0 ? rawItemNameId : names.Intern("_item"u8);
                Expect(ref ctx, in ta, TokKind.KwIn, ref diags, "Expected 'in' in 'foreach'."u8);
                int collExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' after foreach header."u8);
                int body = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                int packedFe = feFlags | ((itemTypeNode + 1) << 4);
                return ast.AddNode(AstKind.StmtForeach, tokIdx, itemNameId, packedFe, collExpr, body);
            }

            case TokKind.KwSwitch:
            {
                Advance(ref ctx, in ta);
                Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '(' after 'switch'."u8);
                int scrut = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' after switch expression."u8);
                Expect(ref ctx, in ta, TokKind.LBrace, ref diags, "Expected '{' for switch body."u8);
                var sections = new Vec<int>();
                while (PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                {
                    int secTok = ctx.Pos;
                    var labels = new Vec<int>();
                    while (true)
                    {
                        if (Match(ref ctx, in ta, TokKind.KwCase))
                        {
                            int lblTok = ctx.Pos;
                            int pat = ParsePattern(ref ctx, in ta, ref names, ref ast, ref diags);
                            // A bare `_` case label names a constant in C#, not a discard
                            // (`case (_):` and `case var _:` are discards; use `default:`).
                            if (pat >= 0 && ast.Nodes[pat].Kind == AstKind.PatWildcard && ast.Nodes[pat].TokIdx == lblTok)
                                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[lblTok], "The name '_' does not exist in the current context; a bare '_' case label is not a discard, use 'default:' (CS0103)."u8);
                            int whenExpr = -1;
                            if (PeekKind(in ctx, in ta, 0) == TokKind.Ident &&
                                Bytes.Eq(names.Text(Peek(in ctx, in ta, 0).AuxId), "when"u8))
                            {
                                Advance(ref ctx, in ta);
                                whenExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                            }
                            Expect(ref ctx, in ta, TokKind.Colon, ref diags, "Expected ':' after case label."u8);
                            labels.Push(ast.AddNode(AstKind.SwitchCaseLabel, lblTok, pat, whenExpr, 0, 0));
                        }
                        else if (Match(ref ctx, in ta, TokKind.KwDefault))
                        {
                            int defTok = ctx.Pos - 1;
                            Expect(ref ctx, in ta, TokKind.Colon, ref diags, "Expected ':' after default."u8);
                            labels.Push(ast.AddNode(AstKind.SwitchCaseLabel, defTok, -1, -1, 0, 0));
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (labels.Count == 0)
                    {
                        Advance(ref ctx, in ta);
                        continue;
                    }
                    var secStmts = new Vec<int>();
                    while (PeekKind(in ctx, in ta, 0) != TokKind.KwCase &&
                           PeekKind(in ctx, in ta, 0) != TokKind.KwDefault &&
                           PeekKind(in ctx, in ta, 0) != TokKind.RBrace &&
                           PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                    {
                        if (LooksLikeLocalFunction(in ctx, in ta))
                        {
                            Token declaration = Peek(in ctx, in ta, 0);
                            SubsetCuts.Report(18, declaration, ref diags);
                            // The cut form is already rejected. Consume it without
                            // cascading unrelated local-function subset diagnostics.
                            var recoveryDiags = new DiagnosticBag(0);
                            int lf = ParseLocalFunction(ref ctx, in ta, ref names, ref ast, ref recoveryDiags);
                            for (int recoveryIndex = 0; recoveryIndex < recoveryDiags.Items.Count; recoveryIndex++)
                            {
                                DiagEntry entry = recoveryDiags.Items[recoveryIndex];
                                ReadOnlySpan<byte> message = recoveryDiags.MsgPool.Get(entry.Message);
                                if (SubsetCuts.IsMessage(message))
                                    diags.Report(entry.Code, entry.SourceId, entry.ByteOffset, entry.Length, message);
                            }
                            if (lf >= 0) declaration = ta.Tokens[ast.Funcs[lf].TokIdx];
                            diags.ReportTok(VibCodes.Unsupported, declaration, "Local functions must be declared inside a block; declaring one directly in a switch section is not supported in Vibranium v0.2."u8);
                            continue;
                        }
                        int statementStart = ctx.Pos;
                        if (PeekKind(in ctx, in ta, 0) == TokKind.KwVar && PeekKind(in ctx, in ta, 1) == TokKind.LParen)
                            SubsetCuts.Report(18, ta.Tokens[statementStart], ref diags);
                        int ss = ParseStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                        if (SubsetCutChecker.DeclaresSwitchLocal(ss, in ast))
                            SubsetCuts.Report(18, ta.Tokens[statementStart], ref diags);
                        PushStmtFlattened(ss, in ast.Nodes, in ast.Extra, ref secStmts);
                    }
                    int lStart = ast.PushExtraSpan(labels.AsReadOnlySpan());
                    int sStart = ast.PushExtraSpan(secStmts.AsReadOnlySpan());
                    sections.Push(ast.AddNode(AstKind.SwitchSection, secTok, lStart, labels.Count, sStart, secStmts.Count));
                }
                Expect(ref ctx, in ta, TokKind.RBrace, ref diags, "Expected '}' after switch body."u8);
                int secStart = ast.PushExtraSpan(sections.AsReadOnlySpan());
                return ast.AddNode(AstKind.StmtSwitch, tokIdx, scrut, secStart, sections.Count, 0);
            }

            case TokKind.KwReturn:
            {
                Advance(ref ctx, in ta);
                if (Match(ref ctx, in ta, TokKind.Semi))
                {
                    return ast.AddNode(AstKind.StmtReturn, tokIdx, -1, 0, 0, 0);
                }
                int isRef = Match(ref ctx, in ta, TokKind.KwRef) ? 1 : 0;
                int val = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after return."u8);
                return ast.AddNode(AstKind.StmtReturn, tokIdx, val, isRef, 0, 0);
            }

            case TokKind.KwBreak:
                Advance(ref ctx, in ta);
                Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after break."u8);
                return ast.AddNode(AstKind.StmtBreak, tokIdx, 0, 0, 0, 0);

            case TokKind.KwContinue:
                Advance(ref ctx, in ta);
                Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after continue."u8);
                return ast.AddNode(AstKind.StmtContinue, tokIdx, 0, 0, 0, 0);

            case TokKind.KwUsing:
            {
                Advance(ref ctx, in ta);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LParen)
                {
                    Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '('."u8);
                    int innerDecl = ParseVarDeclCore(ref ctx, in ta, ref names, ref ast, ref diags, varFlags: 1, consumeSemi: false);
                    Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')'."u8);
                    int body = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                    var items = new Vec<int>(2);
                    items.Push(innerDecl);
                    items.Push(body);
                    int itemsStart = ast.PushExtraSpan(items.AsReadOnlySpan());
                    return ast.AddNode(AstKind.StmtBlock, tokIdx, itemsStart, 2, 0, 0);
                }
                return ParseVarDeclStatement(ref ctx, in ta, ref names, ref ast, ref diags, varFlags: 1);
            }

            case TokKind.KwConst:
                Advance(ref ctx, in ta);
                return ParseVarDeclStatement(ref ctx, in ta, ref names, ref ast, ref diags, varFlags: 8);

            case TokKind.KwRef:
            {
                if (PeekKind(in ctx, in ta, 1) == TokKind.KwReadonly ||
                    (PeekKind(in ctx, in ta, 1) == TokKind.KwVar && IsIdentLike(PeekKind(in ctx, in ta, 2))) ||
                    LooksLikeRefLocalDecl(in ctx, in ta))
                {
                    Advance(ref ctx, in ta);
                    int rFlags = Match(ref ctx, in ta, TokKind.KwReadonly) ? 4 : 2;
                    return ParseVarDeclStatement(ref ctx, in ta, ref names, ref ast, ref diags, varFlags: rFlags);
                }
                break;
            }

            case TokKind.KwGoto:
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.Unsupported, t, "Unstructured 'goto' statements are banned in Vibranium."u8);
                SkipToSemi(ref ctx, in ta);
                return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);

            case TokKind.KwThrow:
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.Unsupported, t, "Exceptions ('throw') are banned in Vibranium; use Result<T, E> or VibraniumRuntime.Panic."u8);
                SkipToSemi(ref ctx, in ta);
                return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);

            case TokKind.KwTry:
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.Unsupported, t, "'try'/'catch'/'finally' blocks are banned in Vibranium; use Result<T, E> and 'using var'."u8);
                SkipTryBlocks(ref ctx, in ta);
                return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);

            case TokKind.KwLock:
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.Unsupported, t, "'lock' statements are not supported in Vibranium."u8);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LParen) ctx.Pos = SkipParenBalanced(in ta, ctx.Pos, ctx.EndPos);
                _ = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);

            case TokKind.KwYield:
                if (PeekKind(in ctx, in ta, 1) == TokKind.KwReturn || PeekKind(in ctx, in ta, 1) == TokKind.KwBreak)
                {
                    Advance(ref ctx, in ta);
                    diags.ReportTok(VibCodes.Unsupported, t, "Iterator 'yield' statements are not supported in Vibranium."u8);
                    SkipToSemi(ref ctx, in ta);
                    return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);
                }
                break;

            case TokKind.KwUnsafe:
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.Unsupported, t, "'unsafe' blocks are banned in Vibranium."u8);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) _ = ParseBlockStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);

            case TokKind.KwChecked:
                if (PeekKind(in ctx, in ta, 1) == TokKind.LBrace)
                {
                    Advance(ref ctx, in ta);
                    diags.ReportTok(VibCodes.Unsupported, t, "'checked' blocks are banned in Vibranium."u8);
                    _ = ParseBlockStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                    return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);
                }
                break;

            case TokKind.KwUnchecked:
                if (PeekKind(in ctx, in ta, 1) == TokKind.LBrace)
                {
                    Advance(ref ctx, in ta);
                    int innerBlk = ParseBlockStatement(ref ctx, in ta, ref names, ref ast, ref diags);
                    return ast.AddNode(AstKind.StmtUncheckedBlock, tokIdx, innerBlk, 0, 0, 0);
                }
                break;
        }

        if (t.Kind == TokKind.Ident && PeekKind(in ctx, in ta, 1) == TokKind.LParen && Bytes.Eq(names.Text(t.AuxId), "fixed"u8))
        {
            Advance(ref ctx, in ta);
            diags.ReportTok(VibCodes.Unsupported, t, "'fixed' statements are banned in Vibranium."u8);
            ctx.Pos = SkipParenBalanced(in ta, ctx.Pos, ctx.EndPos);
            _ = ParseEmbeddedStatement(ref ctx, in ta, ref names, ref ast, ref diags);
            return ast.AddNode(AstKind.StmtEmpty, tokIdx, 0, 0, 0, 0);
        }

        if (LooksLikeVarDecl(in ctx, in ta))
        {
            return ParseVarDeclStatement(ref ctx, in ta, ref names, ref ast, ref diags, varFlags: 0);
        }

        int expr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
        Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after expression statement."u8);
        return ast.AddNode(AstKind.StmtExpr, tokIdx, expr, 0, 0, 0);
    }

    private static bool IsScopedModifierAt(in TokenArena ta, int p, int endPos, bool isParam)
    {
        if (p + 1 >= endPos || ta.Tokens[p].Kind != TokKind.KwScoped) return false;
        TokKind next = ta.Tokens[p + 1].Kind;
        if (next == TokKind.KwRef) return true;
        if (isParam && (next == TokKind.KwIn || next == TokKind.KwOut)) return true;
        if (!isParam && next == TokKind.KwVar)
        {
            return p + 2 < endPos && IsIdentLike(ta.Tokens[p + 2].Kind);
        }
        if (!TryScanTypeSyntax(in ta, p + 1, endPos, out int afterType)) return false;
        return afterType < endPos && IsIdentLike(ta.Tokens[afterType].Kind);
    }

    private static bool LooksLikeRefLocalDecl(in ParserCtx ctx, in TokenArena ta)
    {
        int p = ctx.Pos + 1;
        if (p < ctx.EndPos && ta.Tokens[p].Kind == TokKind.KwReadonly) p += 1;
        if (p + 1 < ctx.EndPos && ta.Tokens[p].Kind == TokKind.KwVar && IsIdentLike(ta.Tokens[p + 1].Kind)) return true;
        if (!TryScanTypeSyntax(in ta, p, ctx.EndPos, out int afterType)) return false;
        return afterType < ctx.EndPos && IsIdentLike(ta.Tokens[afterType].Kind);
    }

    private static bool LooksLikeVarDecl(in ParserCtx ctx, in TokenArena ta)
    {
        TokKind k0 = PeekKind(in ctx, in ta, 0);
        if (k0 == TokKind.KwVar && IsIdentLike(PeekKind(in ctx, in ta, 1))) return true;
        if (k0 == TokKind.KwScoped && IsScopedModifierAt(in ta, ctx.Pos, ctx.EndPos, isParam: false)) return true;
        if (IsPrimitiveTypeKeyword(k0) || k0 == TokKind.KwDecimal || k0 == TokKind.KwDynamic)
        {
            if (!TryScanTypeSyntax(in ta, ctx.Pos, ctx.EndPos, out int afterPrim)) return false;
            return afterPrim < ctx.EndPos && IsIdentLike(ta.Tokens[afterPrim].Kind);
        }
        if (IsTypeIdentHead(k0))
        {
            if (!TryScanTypeSyntax(in ta, ctx.Pos, ctx.EndPos, out int afterNamed)) return false;
            if (afterNamed < ctx.EndPos && IsIdentLike(ta.Tokens[afterNamed].Kind))
            {
                TokKind follow = afterNamed + 1 < ctx.EndPos ? ta.Tokens[afterNamed + 1].Kind : TokKind.Eof;
                return follow == TokKind.Eq || follow == TokKind.Semi || follow == TokKind.Comma;
            }
        }
        return false;
    }

    private static int ParseVarDeclStatement(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int varFlags) =>
        ParseVarDeclCore(ref ctx, in ta, ref names, ref ast, ref diags, varFlags, consumeSemi: true);

    private static int ParseVarDeclCore(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int varFlags,
        bool consumeSemi)
    {
        if (PeekKind(in ctx, in ta, 0) == TokKind.KwScoped && IsScopedModifierAt(in ta, ctx.Pos, ctx.EndPos, isParam: false))
        {
            Advance(ref ctx, in ta);
            varFlags |= 16;
            if (Match(ref ctx, in ta, TokKind.KwRef))
            {
                varFlags |= Match(ref ctx, in ta, TokKind.KwReadonly) ? 4 : 2;
            }
        }

        int typeNode = -1;
        if (PeekKind(in ctx, in ta, 0) == TokKind.KwVar && IsIdentLike(PeekKind(in ctx, in ta, 1)))
        {
            Token varKwTok = Advance(ref ctx, in ta);
            if ((varFlags & 8) != 0)
            {
                diags.ReportTok(VibCodes.Unsupported, varKwTok, "Implicitly-typed variables cannot be declared 'const'."u8);
            }
        }
        else
        {
            typeNode = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
        }

        var declNodes = new Vec<int>();
        bool reportedMultiVar = false;
        while (true)
        {
            int dTok = ctx.Pos;
            int rawVarNameId = ExpectDeclaredIdent(ref ctx, in ta, ref names, ref diags, "Expected variable name."u8, out _);
            int nameId = rawVarNameId >= 0 ? rawVarNameId : names.Intern("_var"u8);
            int initExpr = -1;
            if (Match(ref ctx, in ta, TokKind.Eq))
            {
                // Keep the ref wrapper so source validity can distinguish ref and value initializers.
                initExpr = ParseExpressionAllowingRef(ref ctx, in ta, ref names, ref ast, ref diags);
            }
            declNodes.Push(ast.AddNode(AstKind.StmtVarDecl, dTok, nameId, typeNode, initExpr, varFlags));
            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
            if (typeNode < 0 && !reportedMultiVar)
            {
                reportedMultiVar = true;
                diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, -1), "Implicitly-typed variables cannot have multiple declarators."u8);
            }
        }

        if (consumeSemi)
        {
            Expect(ref ctx, in ta, TokKind.Semi, ref diags, "Expected ';' after variable declaration."u8);
        }

        if (declNodes.Count == 1) return declNodes[0];
        int declsStart = ast.PushExtraSpan(declNodes.AsReadOnlySpan());
        return ast.AddNode(AstKind.StmtMulti, ast.Nodes[declNodes[0]].TokIdx, declsStart, declNodes.Count, 0, 0);
    }

    private static int ParseExpression(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        if (ctx.Nesting >= MaxNesting)
        {
            diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "Maximum expression nesting depth exceeded."u8);
            Advance(ref ctx, in ta);
            return ast.AddNode(AstKind.ExprIntLit, ctx.Pos, 0, 0, 0, 0);
        }
        ctx.Nesting += 1;
        int expressionStart = ctx.Pos;
        int e = ParseAssignment(ref ctx, in ta, ref names, ref ast, ref diags);
        if (e >= 0) ctx.ExpressionStarts.Set(e, expressionStart);
        ctx.Nesting -= 1;
        return e;
    }

    // C# parses `ref e` as a ref expression only where a whole expression may
    // be a reference: here, a variable initializer or an expression-bodied
    // property/indexer. `ref` wraps the full expression that follows it.
    private static int ParseExpressionAllowingRef(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        if (PeekKind(in ctx, in ta, 0) != TokKind.KwRef) return ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
        int refTok = ctx.Pos;
        Token t = Advance(ref ctx, in ta);
        if (PeekKind(in ctx, in ta, 0) == TokKind.KwRef)
        {
            diags.ReportTok(VibCodes.Unsupported, t, "A ref expression cannot contain another ref modifier (CS1525)."u8);
            Advance(ref ctx, in ta);
        }
        int sub = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
        int wrapped = ast.AddNode(AstKind.ExprUnary, refTok, (int)TokKind.KwRef, sub, 0, 0);
        ctx.ExpressionStarts.Set(wrapped, refTok);
        return wrapped;
    }

    private static int ParseAssignment(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        if (ctx.InSwitchWhenGuard == 0 && IsIdentLike(PeekKind(in ctx, in ta, 0)) && PeekKind(in ctx, in ta, 1) == TokKind.FatArrow)
        {
            Token lt = Advance(ref ctx, in ta);
            int lambdaName = TokenIdentId(lt, ref names);
            if (Bytes.Eq(names.Text(lambdaName), "_"u8)) SubsetCuts.Report(11, lt, ref diags);
            Advance(ref ctx, in ta);
            diags.ReportTok(VibCodes.Unsupported, lt, "Lambdas and closures are banned in Vibranium."u8);
            _ = ParseAssignment(ref ctx, in ta, ref names, ref ast, ref diags);
            return ast.AddNode(AstKind.ExprIntLit, ctx.Pos, 0, 0, 0, 0);
        }

        int left = ParseConditional(ref ctx, in ta, ref names, ref ast, ref diags);
        Token opTok = Peek(in ctx, in ta, 0);
        if (IsAssignOp(opTok.Kind))
        {
            int opTokIdx = ctx.Pos;
            Advance(ref ctx, in ta);
            if (opTok.Kind == TokKind.QuestionQuestionEq)
            {
                diags.ReportTok(VibCodes.NullOrZeroUnsafe, opTok, "Null-coalescing '??=' operator is banned in Vibranium."u8);
            }
            int isRefAssign = (opTok.Kind == TokKind.Eq && Match(ref ctx, in ta, TokKind.KwRef)) ? 1 : 0;
            int right = ParseAssignment(ref ctx, in ta, ref names, ref ast, ref diags);
            return ast.AddNode(AstKind.ExprAssign, opTokIdx, (int)opTok.Kind, left, right, isRefAssign);
        }
        return left;
    }

    private static bool IsAssignOp(TokKind k) =>
        k == TokKind.Eq || k == TokKind.PlusEq || k == TokKind.MinusEq ||
        k == TokKind.StarEq || k == TokKind.SlashEq || k == TokKind.PercentEq ||
        k == TokKind.AmpEq || k == TokKind.PipeEq || k == TokKind.CaretEq ||
        k == TokKind.LtLtEq || k == TokKind.GtGtEq || k == TokKind.GtGtGtEq ||
        k == TokKind.QuestionQuestionEq;

    private static int ParseConditional(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        int cond = ParseBinary(ref ctx, in ta, ref names, ref ast, ref diags, minPrec: 1);
        if (Match(ref ctx, in ta, TokKind.Question))
        {
            int qTok = ctx.Pos - 1;
            bool trueIsRef = PeekKind(in ctx, in ta, 0) == TokKind.KwRef;
            if (trueIsRef)
            {
                SubsetCuts.Report(15, ta.Tokens[qTok], ref diags);
                Advance(ref ctx, in ta);
            }
            int trueStart = ctx.Pos;
            int savedPendingGt = ctx.PendingGt;
            int savedNesting = ctx.Nesting;
            int savedWhenGuard = ctx.InSwitchWhenGuard;
            int savedForce = ctx.ForceConditionalAccess;
            int nodeCount = ast.Nodes.Count;
            int extraCount = ast.Extra.Count;
            int typeRefCount = ast.TypeRefs.Count;
            int diagCount = diags.Items.Count;
            int tExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
            // In a ? b?[c] : d, the tentative collection conditional consumes the
            // enclosing colon. Retry the true arm as conditional access only when
            // doing so recovers that colon, as in the C# collection grammar.
            if (PeekKind(in ctx, in ta, 0) != TokKind.Colon && savedForce == 0 &&
                HasConditionalCollection(in ast, in ta, nodeCount) &&
                ExpressionEndsAtColon(ref ctx, in ta, ref names, trueStart, 1))
            {
                ast.Nodes.Truncate(nodeCount);
                ast.Extra.Truncate(extraCount);
                ast.TypeRefs.Truncate(typeRefCount);
                RestoreConditionalDiagnostics(ref diags, diagCount);
                ctx.Pos = trueStart;
                ctx.PendingGt = savedPendingGt;
                ctx.Nesting = savedNesting;
                ctx.InSwitchWhenGuard = savedWhenGuard;
                ctx.ForceConditionalAccess = 1;
                tExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                ctx.ForceConditionalAccess = savedForce;
            }
            Expect(ref ctx, in ta, TokKind.Colon, ref diags, "Expected ':' in conditional expression."u8);
            int falseRefTok = ctx.Pos;
            if (Match(ref ctx, in ta, TokKind.KwRef) && !trueIsRef)
            {
                // C# requires both branches or neither to be ref (CS8155); a lone
                // ref branch is not the cut conditional ref form.
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[falseRefTok], "Both conditional operator branches must be ref values or neither may be (CS8155)."u8);
            }
            int fExpr = ParseConditional(ref ctx, in ta, ref names, ref ast, ref diags);
            return ast.AddNode(AstKind.ExprCond, qTok, cond, tExpr, fExpr, 0);
        }
        return cond;
    }

    private static bool HasConditionalCollection(in AstArena ast, in TokenArena ta, int firstNode)
    {
        for (int i = firstNode; i < ast.Nodes.Count; i++)
        {
            AstNode node = ast.Nodes[i];
            if (node.Kind == AstKind.ExprCond && node.TokIdx + 1 < ta.Tokens.Count &&
                ta.Tokens[node.TokIdx + 1].Kind == TokKind.LBracket) return true;
        }
        return false;
    }

    private static void RestoreConditionalDiagnostics(ref DiagnosticBag diags, int count)
    {
        if (diags.Items.Count == count) return;
        // BytePool has no truncate operation. Rebuild the saved prefix so rejected
        // speculative diagnostics leave neither entries nor message bytes behind.
        var restored = new DiagnosticBag(count);
        for (int i = 0; i < count; i++)
        {
            DiagEntry entry = diags.Items[i];
            restored.Report(entry.Code, entry.SourceId, entry.ByteOffset, entry.Length, diags.MsgPool.Get(entry.Message));
        }
        diags = restored;
    }

    private static bool ExpressionEndsAtColon(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        int start,
        int forceConditionalAccess)
    {
        int savedPos = ctx.Pos;
        int savedPendingGt = ctx.PendingGt;
        int savedNesting = ctx.Nesting;
        int savedWhenGuard = ctx.InSwitchWhenGuard;
        int savedForce = ctx.ForceConditionalAccess;
        // Expression parsing appends only Nodes, Extra and TypeRefs. A separate
        // arena and diagnostic bag keep all speculative writes out of the caller.
        var probeAst = new AstArena(16);
        var probeDiags = new DiagnosticBag(0);
        ctx.Pos = start;
        ctx.ForceConditionalAccess = forceConditionalAccess;
        _ = ParseExpression(ref ctx, in ta, ref names, ref probeAst, ref probeDiags);
        bool endsAtColon = PeekKind(in ctx, in ta, 0) == TokKind.Colon;
        ctx.Pos = savedPos;
        ctx.PendingGt = savedPendingGt;
        ctx.Nesting = savedNesting;
        ctx.InSwitchWhenGuard = savedWhenGuard;
        ctx.ForceConditionalAccess = savedForce;
        return endsAtColon;
    }

    private static bool IsConditionalCollection(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names)
    {
        if (ctx.ForceConditionalAccess != 0) return false;
        int bracketStart = ctx.Pos + 1;
        int afterBracket = SkipBracketBalanced(in ta, bracketStart, ctx.EndPos);
        if (afterBracket >= ctx.EndPos) return false;
        TokKind next = ta.Tokens[afterBracket].Kind;
        // The common collection arm needs no speculative recursion, including
        // deeply nested collection elements. Other postfix/binary tails use the
        // full expression grammar rather than a colon-token heuristic.
        if (next == TokKind.Colon) return true;
        if (next == TokKind.Semi || next == TokKind.Comma || next == TokKind.RParen ||
            next == TokKind.RBracket || next == TokKind.RBrace || next == TokKind.Eof) return false;
        return ExpressionEndsAtColon(ref ctx, in ta, ref names, bracketStart, 0);
    }

    private static int GetBinaryPrec(TokKind k)
    {
        switch (k)
        {
            case TokKind.QuestionQuestion: return 1;
            case TokKind.PipePipe: return 2;
            case TokKind.AmpAmp: return 3;
            case TokKind.Pipe: return 4;
            case TokKind.Caret: return 5;
            case TokKind.Amp: return 6;
            case TokKind.EqEq:
            case TokKind.BangEq: return 7;
            case TokKind.LAngle:
            case TokKind.RAngle:
            case TokKind.LtEq:
            case TokKind.GtEq:
            case TokKind.KwIs: return 8;
            case TokKind.LtLt:
            case TokKind.GtGt:
            case TokKind.GtGtGt: return 11;
            case TokKind.Plus:
            case TokKind.Minus: return 12;
            case TokKind.Star:
            case TokKind.Slash:
            case TokKind.Percent: return 13;
            // C# multiplicative operands are switch expressions; switch operands
            // are ranges, whose endpoints are unary expressions.
            case TokKind.KwSwitch: return 14;
            case TokKind.DotDot: return 15;
            default: return 0;
        }
    }

    private static bool IsRangeTerminatorToken(TokKind k) =>
        k == TokKind.RBracket || k == TokKind.RParen || k == TokKind.RBrace ||
        k == TokKind.Comma || k == TokKind.Semi || k == TokKind.Colon ||
        k == TokKind.KwSwitch || k == TokKind.Eof;

    private static int ParseBinary(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int minPrec)
    {
        int left = ParseUnary(ref ctx, in ta, ref names, ref ast, ref diags);
        int leftPrec = 16; // Explicitly parenthesized operands are primary expressions.
        while (true)
        {
            Token opTok = Peek(in ctx, in ta, 0);
            int prec = GetBinaryPrec(opTok.Kind);
            if (prec < minPrec) break;
            if (prec > leftPrec)
            {
                // A completed is-type/pattern does not have an expression RHS
                // that can absorb tighter operators. Require explicit grouping.
                diags.ReportTok(VibCodes.Unsupported, opTok, "Operator precedence requires parentheses around the left operand."u8);
            }
            leftPrec = prec;

            int opIdx = ctx.Pos;
            Advance(ref ctx, in ta);

            if (opTok.Kind == TokKind.QuestionQuestion)
            {
                diags.ReportTok(VibCodes.NullOrZeroUnsafe, opTok, "Null-coalescing '??' operator is banned in Vibranium; use Option<T>.UnwrapOr."u8);
                int rhs = ParseBinary(ref ctx, in ta, ref names, ref ast, ref diags, prec);
                left = ast.AddNode(AstKind.ExprBinary, opIdx, (int)opTok.Kind, left, rhs, 0);
                continue;
            }
            if (opTok.Kind == TokKind.KwIs)
            {
                int pat = ParsePattern(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr: true, allowBareIsTypeFlag: true);
                // A bare `_` right after `is` names a type in C#, not a discard
                // (`x is (_)` and `x is var _` are discards).
                if (pat >= 0 && ast.Nodes[pat].Kind == AstKind.PatWildcard && ast.Nodes[pat].TokIdx == opIdx + 1)
                    diags.ReportTok(VibCodes.Unsupported, ta.Tokens[opIdx + 1], "The type or namespace name '_' could not be found; a top-level '_' after 'is' is not a discard (CS0246)."u8);
                left = ast.AddNode(AstKind.ExprIsPattern, opIdx, left, pat, 0, 0);
                continue;
            }
            if (opTok.Kind == TokKind.KwSwitch)
            {
                left = ParseSwitchExpressionArms(ref ctx, in ta, ref names, ref ast, ref diags, opIdx, left);
                continue;
            }
            if (opTok.Kind == TokKind.DotDot)
            {
                int rhs = IsRangeTerminatorToken(PeekKind(in ctx, in ta, 0))
                    ? -1
                    : ParseBinary(ref ctx, in ta, ref names, ref ast, ref diags, 16);
                left = ast.AddNode(AstKind.ExprRange, opIdx, left, rhs, 0, 0);
                continue;
            }

            int right = ParseBinary(ref ctx, in ta, ref names, ref ast, ref diags, prec + 1);
            left = ast.AddNode(AstKind.ExprBinary, opIdx, (int)opTok.Kind, left, right, 0);
        }
        return left;
    }

    private static int ParseSwitchExpressionArms(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int switchTokIdx,
        int scrutineeExpr)
    {
        Expect(ref ctx, in ta, TokKind.LBrace, ref diags, "Expected '{' in switch expression."u8);
        var arms = new Vec<int>();
        int outerWhen = ctx.InSwitchWhenGuard;
        ctx.InSwitchWhenGuard = 0;
        while (PeekKind(in ctx, in ta, 0) != TokKind.RBrace && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int armTok = ctx.Pos;
            int pat = ParsePattern(ref ctx, in ta, ref names, ref ast, ref diags);
            int whenExpr = -1;
            if (PeekKind(in ctx, in ta, 0) == TokKind.Ident &&
                Bytes.Eq(names.Text(Peek(in ctx, in ta, 0).AuxId), "when"u8))
            {
                Advance(ref ctx, in ta);
                ctx.InSwitchWhenGuard = 1;
                whenExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                ctx.InSwitchWhenGuard = 0;
            }
            Expect(ref ctx, in ta, TokKind.FatArrow, ref diags, "Expected '=>' in switch expression arm."u8);
            int valExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
            arms.Push(ast.AddNode(AstKind.SwitchArm, armTok, pat, valExpr, whenExpr, 0));
            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
        }
        ctx.InSwitchWhenGuard = outerWhen;
        Expect(ref ctx, in ta, TokKind.RBrace, ref diags, "Expected '}' after switch expression."u8);
        int armsStart = ast.PushExtraSpan(arms.AsReadOnlySpan());
        return ast.AddNode(AstKind.ExprSwitch, switchTokIdx, scrutineeExpr, armsStart, arms.Count, 0);
    }

    private static void ClearBareIsTypeFlag(int patId, ref AstArena ast)
    {
        if (patId < 0) return;
        AstNode pn = ast.Nodes[patId];
        if (pn.Kind == AstKind.PatDecl && pn.B < 0 && pn.C != 0)
        {
            ast.Nodes[patId] = new AstNode(AstKind.PatDecl, pn.TokIdx, pn.A, pn.B, 0, pn.D);
        }
        else if (pn.Kind == AstKind.PatConst && pn.B != 0)
        {
            ast.Nodes[patId] = new AstNode(AstKind.PatConst, pn.TokIdx, pn.A, 0, pn.C, pn.D);
        }
    }

    private static int ParsePattern(ref ParserCtx ctx, in TokenArena ta, ref Interner names, ref AstArena ast, ref DiagnosticBag diags) => ParsePattern(ref ctx, in ta, ref names, ref ast, ref diags, false, false);

    private static int ParsePattern(ref ParserCtx ctx, in TokenArena ta, ref Interner names, ref AstArena ast, ref DiagnosticBag diags, bool inIsExpr) => ParsePattern(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr, false);

    private static int ParsePattern(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        bool inIsExpr,
        bool allowBareIsTypeFlag)
    {
        int patternStart = ctx.Pos;
        int pattern = ParsePatternOr(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr, allowBareIsTypeFlag);
        SubsetCutChecker.CheckPatternTokens(patternStart, ctx.Pos, in ta, in names, ref diags);
        return pattern;
    }

    private static int ParsePatternOr(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        bool inIsExpr,
        bool allowBareIsTypeFlag)
    {
        int left = ParsePatternAnd(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr, allowBareIsTypeFlag);
        while (PeekKind(in ctx, in ta, 0) == TokKind.Ident &&
               Bytes.Eq(names.Text(Peek(in ctx, in ta, 0).AuxId), "or"u8))
        {
            ClearBareIsTypeFlag(left, ref ast);
            int opTok = ctx.Pos;
            Advance(ref ctx, in ta);
            int right = ParsePatternAnd(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr, allowBareIsTypeFlag: false);
            left = ast.AddNode(AstKind.PatLogic, opTok, 1, left, right, 0);
        }
        return left;
    }

    private static int ParsePatternAnd(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        bool inIsExpr,
        bool allowBareIsTypeFlag)
    {
        int left = ParsePatternNot(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr, allowBareIsTypeFlag);
        while (PeekKind(in ctx, in ta, 0) == TokKind.Ident &&
               Bytes.Eq(names.Text(Peek(in ctx, in ta, 0).AuxId), "and"u8))
        {
            ClearBareIsTypeFlag(left, ref ast);
            int opTok = ctx.Pos;
            SubsetCuts.Report(20, ta.Tokens[opTok], ref diags);
            Advance(ref ctx, in ta);
            int right = ParsePatternNot(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr, allowBareIsTypeFlag: false);
            left = ast.AddNode(AstKind.PatLogic, opTok, 0, left, right, 0);
        }
        return left;
    }

    private static int ParsePatternNot(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        bool inIsExpr,
        bool allowBareIsTypeFlag)
    {
        if (PeekKind(in ctx, in ta, 0) == TokKind.Ident &&
            Bytes.Eq(names.Text(Peek(in ctx, in ta, 0).AuxId), "not"u8))
        {
            int opTok = ctx.Pos;
            Advance(ref ctx, in ta);
            int sub = ParsePatternNot(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr, allowBareIsTypeFlag: false);
            return ast.AddNode(AstKind.PatNot, opTok, sub, 0, 0, 0);
        }
        return ParsePatternPrimary(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr, allowBareIsTypeFlag);
    }

    private static bool IsPatternCombinatorKeyword(ReadOnlySpan<byte> w) =>
        Bytes.Eq(w, "and"u8) || Bytes.Eq(w, "or"u8) || Bytes.Eq(w, "when"u8);

    private static bool IsPatternCombinatorToken(Token t, in Interner names) =>
        t.Kind == TokKind.Ident && IsPatternCombinatorKeyword(names.Text(t.AuxId));

    private static bool IsNullableSuffixInPattern(in ParserCtx ctx, in TokenArena ta, in Interner names, bool inIsExpr)
    {
        if (PeekKind(in ctx, in ta, 0) != TokKind.Question) return false;
        if (!inIsExpr) return true;
        TokKind k1 = PeekKind(in ctx, in ta, 1);
        if (k1 == TokKind.RParen || k1 == TokKind.Semi || k1 == TokKind.Comma ||
            k1 == TokKind.RBracket || k1 == TokKind.RBrace ||
            k1 == TokKind.AmpAmp || k1 == TokKind.PipePipe)
        {
            return true;
        }
        if (IsIdentLike(k1) && !IsPatternCombinatorToken(Peek(in ctx, in ta, 1), in names))
        {
            TokKind k2 = PeekKind(in ctx, in ta, 2);
            if (k2 == TokKind.RParen || k2 == TokKind.Semi || k2 == TokKind.Comma ||
                k2 == TokKind.RBracket || k2 == TokKind.RBrace ||
                k2 == TokKind.AmpAmp || k2 == TokKind.PipePipe ||
                IsPatternCombinatorToken(Peek(in ctx, in ta, 2), in names))
            {
                return true;
            }
        }
        return false;
    }

    private static int ParsePatternPrimary(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        bool inIsExpr,
        bool allowBareIsTypeFlag)
    {
        int tokIdx = ctx.Pos;
        Token t = Peek(in ctx, in ta, 0);

        if (t.Kind == TokKind.Ident && Bytes.Eq(names.Text(t.AuxId), "_"u8))
        {
            Advance(ref ctx, in ta);
            return ast.AddNode(AstKind.PatWildcard, tokIdx, 0, 0, 0, 0);
        }

        if (t.Kind == TokKind.LAngle || t.Kind == TokKind.LtEq || t.Kind == TokKind.RAngle || t.Kind == TokKind.GtEq)
        {
            SubsetCuts.Report(20, t, ref diags);
            Advance(ref ctx, in ta);
            int boundExpr = ParseBinary(ref ctx, in ta, ref names, ref ast, ref diags, 11);
            return ast.AddNode(AstKind.PatRel, tokIdx, (int)t.Kind, boundExpr, 0, 0);
        }

        if (t.Kind == TokKind.LParen && !IsCastExpression(in ctx, in ta, in names, inPatternContext: true))
        {
            Advance(ref ctx, in ta);
            int innerPat = ParsePattern(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr: false, allowBareIsTypeFlag: false);
            Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' after pattern."u8);
            return innerPat;
        }

        if (t.Kind == TokKind.LBrace)
        {
            SkipBraceBalanced(ref ctx, in ta);
            if (IsIdentLike(PeekKind(in ctx, in ta, 0)) &&
                !IsPatternCombinatorToken(Peek(in ctx, in ta, 0), in names))
            {
                Advance(ref ctx, in ta);
            }
            return ast.AddNode(AstKind.None, tokIdx, -1, 0, 0, 0);
        }

        if (t.Kind == TokKind.KwVar &&
            IsIdentLike(PeekKind(in ctx, in ta, 1)) &&
            !IsPatternCombinatorToken(Peek(in ctx, in ta, 1), in names))
        {
            Advance(ref ctx, in ta);
            int bindId = -1;
            int rawVarPatId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected identifier after 'var' in pattern."u8, out Token designation);
            if (rawVarPatId >= 0 && Bytes.Eq(names.Text(rawVarPatId), "_"u8) && designation.Length != 1)
                SubsetCuts.Report(11, designation, ref diags);
            if (rawVarPatId >= 0 && (!Bytes.Eq(names.Text(rawVarPatId), "_"u8) || designation.Length != 1))
            {
                bindId = rawVarPatId;
            }
            return ast.AddNode(AstKind.PatDecl, tokIdx, -1, bindId, 0, 0);
        }

        if (t.Kind == TokKind.Ident)
        {
            ReadOnlySpan<byte> w = names.Text(t.AuxId);
            if (Bytes.Eq(w, "None"u8) && PeekKind(in ctx, in ta, 1) != TokKind.Dot &&
                !(IsIdentLike(PeekKind(in ctx, in ta, 1)) && !IsPatternCombinatorToken(Peek(in ctx, in ta, 1), in names)))
            {
                Advance(ref ctx, in ta);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace)
                {
                    diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "'None' is a value, not a type, and cannot use a property pattern."u8);
                    SkipBraceBalanced(ref ctx, in ta);
                }
                int noneTypeRef = ast.AddTypeRef(new TypeRefContext(-1, ctx.UnitKind, ctx.CurrentNsId, ctx.CurrentOwnerDeclId, ctx.UsingExtraStart, ctx.UsingCount));
                int noneTypeNode = ast.AddNode(AstKind.TypeNamed, tokIdx, names.Intern("NoneType"u8), 0, 0, noneTypeRef);
                return ast.AddNode(AstKind.PatUnion, tokIdx, noneTypeNode, -1, 0, -1);
            }
            int uVarKind = ClassifyUnionVariantName(w);
            if (uVarKind != 0)
            {
                return ParseUnionVariantPattern(ref ctx, in ta, ref names, ref ast, ref diags, tokIdx, uVarKind, inIsExpr);
            }
        }

        // Declaration pattern `int n`, `MyType n`, `Color Color`, `T x`, `Alias a` (and bare type pattern `is object` / `is int` / `is int?` / `Type { ... }`)
        int tKnownId = LookupTokenKnownId(t, in names);
        bool isKnownOrQualifiedHead =
            IsPrimitiveTypeKeyword(t.Kind) ||
            (t.Kind == TokKind.KwGlobal && PeekKind(in ctx, in ta, 1) == TokKind.ColonColon) ||
            (IsTypeIdentHead(t.Kind) && ((tKnownId >= 0 && ctx.KnownTypes.Get(tKnownId).IsSome) || PeekKind(in ctx, in ta, 1) == TokKind.Dot));
        if (isKnownOrQualifiedHead || IsTypeIdentHead(t.Kind))
        {
            if (ctx.PendingGt == 0 &&
                TryScanTypeSyntaxNoTrailingQuestion(in ta, ctx.Pos, ctx.EndPos, out int afterBaseType))
            {
                if (IsQualifiedVibraniumUnionVariant(in ta, in names, ctx.Pos, afterBaseType, out int qualVarKind))
                {
                    return ParseUnionVariantPattern(ref ctx, in ta, ref names, ref ast, ref diags, tokIdx, qualVarKind, inIsExpr);
                }

                bool hasNullQ = false;
                int afterDeclType = afterBaseType;
                if (isKnownOrQualifiedHead && afterDeclType < ctx.EndPos && ta.Tokens[afterDeclType].Kind == TokKind.Question)
                {
                    int savedPos = ctx.Pos;
                    ctx.Pos = afterDeclType;
                    hasNullQ = IsNullableSuffixInPattern(in ctx, in ta, in names, inIsExpr);
                    ctx.Pos = savedPos;
                    if (hasNullQ) afterDeclType += 1;
                }
                if (afterDeclType < ctx.EndPos &&
                    IsIdentLike(ta.Tokens[afterDeclType].Kind) &&
                    !IsPatternCombinatorToken(ta.Tokens[afterDeclType], in names))
                {
                    int declType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags, allowNullableSuffix: hasNullQ);
                    Token bTok = Advance(ref ctx, in ta);
                    int bTokId = TokenIdentId(bTok, ref names);
                    if (bTokId >= 0 && Bytes.Eq(names.Text(bTokId), "_"u8) && bTok.Length != 1)
                        SubsetCuts.Report(11, bTok, ref diags);
                    int bindId = (bTokId < 0 || (Bytes.Eq(names.Text(bTokId), "_"u8) && bTok.Length == 1)) ? -1 : bTokId;
                    return ast.AddNode(AstKind.PatDecl, tokIdx, declType, bindId, 0, 1);
                }
                if (afterDeclType < ctx.EndPos && ta.Tokens[afterDeclType].Kind == TokKind.LBrace)
                {
                    int declType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags, allowNullableSuffix: hasNullQ);
                    SkipBraceBalanced(ref ctx, in ta);
                    if (IsIdentLike(PeekKind(in ctx, in ta, 0)) &&
                        !IsPatternCombinatorToken(Peek(in ctx, in ta, 0), in names))
                    {
                        Advance(ref ctx, in ta);
                    }
                    return ast.AddNode(AstKind.None, tokIdx, declType, 0, 0, 0);
                }
                if (isKnownOrQualifiedHead)
                {
                    bool notFollowedByDot = afterBaseType >= ctx.EndPos || ta.Tokens[afterBaseType].Kind != TokKind.Dot;
                    bool isBarePrimOrGenericType =
                        notFollowedByDot &&
                        ((IsPrimitiveTypeKeyword(t.Kind) && afterBaseType == ctx.Pos + 1) ||
                         (afterBaseType > ctx.Pos && ta.Tokens[afterBaseType - 1].Kind == TokKind.RAngle));
                    if (hasNullQ || isBarePrimOrGenericType)
                    {
                        int declType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags, allowNullableSuffix: hasNullQ);
                        int bareIsFlag = (!hasNullQ && allowBareIsTypeFlag) ? 1 : 0;
                        return ast.AddNode(AstKind.PatDecl, tokIdx, declType, -1, bareIsFlag, 0);
                    }
                }
            }
        }

        int cExpr = ParseBinary(ref ctx, in ta, ref names, ref ast, ref diags, 11);
        AstKind ck = ast.Nodes[cExpr].Kind;
        int patConstBareIs = (allowBareIsTypeFlag && (ck == AstKind.ExprIdent || ck == AstKind.ExprMember)) ? 1 : 0;
        return ast.AddNode(AstKind.PatConst, tokIdx, cExpr, patConstBareIs, 0, 0);
    }

    private static int ClassifyUnionVariantName(ReadOnlySpan<byte> leaf)
    {
        if (Bytes.Eq(leaf, "Some"u8)) return 1;
        if (Bytes.Eq(leaf, "NoneType"u8)) return 2;
        if (Bytes.Eq(leaf, "Ok"u8)) return 3;
        if (Bytes.Eq(leaf, "Err"u8)) return 4;
        return 0;
    }

    private static bool IsQualifiedVibraniumUnionVariant(
        in TokenArena ta,
        in Interner names,
        int startPos,
        int afterBaseType,
        out int variantKind)
    {
        variantKind = 0;
        int p = startPos;
        if (p + 1 < afterBaseType && ta.Tokens[p].Kind == TokKind.KwGlobal && ta.Tokens[p + 1].Kind == TokKind.ColonColon)
        {
            p += 2;
        }
        if (p + 2 >= afterBaseType || ta.Tokens[p].Kind != TokKind.Ident || ta.Tokens[p + 1].Kind != TokKind.Dot)
        {
            return false;
        }
        if (!Bytes.Eq(names.Text(ta.Tokens[p].AuxId), "Vibranium"u8))
        {
            return false;
        }
        p += 2;
        if (ta.Tokens[p].Kind != TokKind.Ident) return false;
        int vk = ClassifyUnionVariantName(names.Text(ta.Tokens[p].AuxId));
        if (vk == 0)
        {
            return false;
        }
        if (p + 1 < afterBaseType && ta.Tokens[p + 1].Kind == TokKind.Dot)
        {
            return false;
        }
        variantKind = vk;
        return true;
    }

    private static int ParseUnionVariantPattern(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        int tokIdx,
        int variantKind,
        bool inIsExpr)
    {
        int uType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags, allowNullableSuffix: false);
        if (IsNullableSuffixInPattern(in ctx, in ta, in names, inIsExpr) && Match(ref ctx, in ta, TokKind.Question))
        {
            diags.ReportTok(VibCodes.NullOrZeroUnsafe, Peek(in ctx, in ta, -1), "Nullable '?' type is banned in Vibranium; use Option<T>."u8);
            uType = ast.AddNode(AstKind.TypeNullable, tokIdx, uType, 0, 0, 0);
        }
        int bindNameId = -1;
        int hasExtraSubpattern = 0;
        int innerSubPat = -1;
        bool isNoneType = variantKind == 2;
        bool isErrVariant = variantKind == 4;

        if (Match(ref ctx, in ta, TokKind.LParen))
        {
            if (isNoneType)
            {
                diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, -1), "'NoneType' does not support positional deconstruction patterns."u8);
                while (PeekKind(in ctx, in ta, 0) != TokKind.RParen && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                {
                    Advance(ref ctx, in ta);
                }
            }
            else if (PeekKind(in ctx, in ta, 0) != TokKind.RParen)
            {
                innerSubPat = ParsePattern(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr: false, allowBareIsTypeFlag: false);
                if (Match(ref ctx, in ta, TokKind.Comma))
                {
                    diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, -1), "'[Union]' variants have only a single positional payload."u8);
                    while (PeekKind(in ctx, in ta, 0) != TokKind.RParen && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                    {
                        Advance(ref ctx, in ta);
                    }
                }
            }
            Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' in positional pattern."u8);
        }

        if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace)
        {
            Token braceTok = Advance(ref ctx, in ta); // '{'
            if (Match(ref ctx, in ta, TokKind.RBrace))
            {
                if (!isNoneType)
                {
                    hasExtraSubpattern = 1;
                }
            }
            else if (isNoneType)
            {
                diags.ReportTok(VibCodes.Unsupported, braceTok, "'NoneType' has no properties for property pattern matching."u8);
                SkipRemainderOfOpenBrace(ref ctx, in ta);
            }
            else
            {
                hasExtraSubpattern = 1;
                int propTokIdx = ctx.Pos;
                int propNameId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected property name in '[Union]' property pattern."u8, out Token propTok);
                if (PeekKind(in ctx, in ta, 0) == TokKind.Dot)
                {
                    diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "Nested dotted property paths are not supported in '[Union]' property patterns."u8);
                    SkipRemainderOfOpenBrace(ref ctx, in ta);
                }
                else
                {
                    ReadOnlySpan<byte> expectedProp = isErrVariant ? "Error"u8 : "Value"u8;
                    if (propNameId < 0 || !Bytes.Eq(names.Text(propNameId), expectedProp))
                    {
                        diags.ReportTok(VibCodes.Unsupported, propTok, "Unknown or unsupported property in '[Union]' property pattern."u8);
                        SkipRemainderOfOpenBrace(ref ctx, in ta);
                    }
                    else
                    {
                        Expect(ref ctx, in ta, TokKind.Colon, ref diags, "Expected ':' after property name in pattern."u8);
                        int propSubPat = ParsePattern(ref ctx, in ta, ref names, ref ast, ref diags, inIsExpr: false, allowBareIsTypeFlag: false);
                        if (Match(ref ctx, in ta, TokKind.Comma) && PeekKind(in ctx, in ta, 0) != TokKind.RBrace)
                        {
                            diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "Multiple property subpatterns on a '[Union]' variant are not supported in Vibranium."u8);
                            SkipRemainderOfOpenBrace(ref ctx, in ta);
                        }
                        else
                        {
                            Expect(ref ctx, in ta, TokKind.RBrace, ref diags, "Expected '}' after '[Union]' property pattern."u8);
                        }
                        innerSubPat = innerSubPat < 0
                            ? propSubPat
                            : ast.AddNode(AstKind.PatLogic, propTokIdx, 0, innerSubPat, propSubPat, 0);
                    }
                }
            }
        }

        if (IsIdentLike(PeekKind(in ctx, in ta, 0)) &&
            !IsPatternCombinatorToken(Peek(in ctx, in ta, 0), in names))
        {
            Token trailingVar = Advance(ref ctx, in ta);
            int tvId = TokenIdentId(trailingVar, ref names);
            if (tvId >= 0 && !Bytes.Eq(names.Text(tvId), "_"u8)) bindNameId = tvId;
        }
        return ast.AddNode(AstKind.PatUnion, tokIdx, uType, bindNameId, hasExtraSubpattern, innerSubPat);
    }

    private static void SkipRemainderOfOpenBrace(ref ParserCtx ctx, in TokenArena ta)
    {
        int depth = 1;
        while (ctx.Pos < ctx.EndPos && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            TokKind k = Advance(ref ctx, in ta).Kind;
            if (k == TokKind.LBrace) depth += 1;
            else if (k == TokKind.RBrace)
            {
                depth -= 1;
                if (depth <= 0) return;
            }
        }
    }

    private static int ParseUnary(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        int tokIdx = ctx.Pos;
        Token t = Peek(in ctx, in ta, 0);

        if (t.Kind == TokKind.DotDot)
        {
            Advance(ref ctx, in ta);
            int rhs = IsRangeTerminatorToken(PeekKind(in ctx, in ta, 0))
                ? -1
                : ParseBinary(ref ctx, in ta, ref names, ref ast, ref diags, 16);
            return ast.AddNode(AstKind.ExprRange, tokIdx, -1, rhs, 0, 0);
        }
        if (t.Kind == TokKind.Caret)
        {
            Advance(ref ctx, in ta);
            int sub = ParseUnary(ref ctx, in ta, ref names, ref ast, ref diags);
            return ast.AddNode(AstKind.ExprFromEnd, tokIdx, sub, 0, 0, 0);
        }
        if (t.Kind == TokKind.Minus || t.Kind == TokKind.Plus || t.Kind == TokKind.Bang ||
            t.Kind == TokKind.Tilde || t.Kind == TokKind.PlusPlus || t.Kind == TokKind.MinusMinus)
        {
            Advance(ref ctx, in ta);
            int sub = ParseUnary(ref ctx, in ta, ref names, ref ast, ref diags);
            return ast.AddNode(AstKind.ExprUnary, tokIdx, (int)t.Kind, sub, 0, 0);
        }
        if (t.Kind == TokKind.KwRef)
        {
            // A C# ref expression is a whole initializer, return value, '= ref'
            // operand, ref argument or ref conditional branch; those positions
            // consume 'ref' before reaching here. Anywhere else it is not an
            // expression term (CS1525), so it never becomes an operand.
            if (PeekKind(in ctx, in ta, 1) == TokKind.KwRef)
                diags.ReportTok(VibCodes.Unsupported, t, "A ref expression cannot contain another ref modifier (CS1525)."u8);
            else
                diags.ReportTok(VibCodes.Unsupported, t, "A 'ref' expression is only valid as a whole variable initializer, return value, ref assignment value, ref argument or ref conditional branch (CS1525)."u8);
            Advance(ref ctx, in ta);
            int sub = ParseUnary(ref ctx, in ta, ref names, ref ast, ref diags);
            return ast.AddNode(AstKind.ExprUnary, tokIdx, (int)TokKind.KwRef, sub, 0, 0);
        }
        if (t.Kind == TokKind.KwAwait)
        {
            Advance(ref ctx, in ta);
            diags.ReportTok(VibCodes.Unsupported, t, "'await' / async state machines are not supported in Vibranium."u8);
            return ParseUnary(ref ctx, in ta, ref names, ref ast, ref diags);
        }
        if (t.Kind == TokKind.LParen && IsCastExpression(in ctx, in ta, in names, inPatternContext: false))
        {
            Advance(ref ctx, in ta);
            int targetType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
            Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' in cast."u8);
            int operand = ParseUnary(ref ctx, in ta, ref names, ref ast, ref diags);
            return ast.AddNode(AstKind.ExprCast, tokIdx, targetType, operand, 0, 0);
        }

        return ParsePostfix(ref ctx, in ta, ref names, ref ast, ref diags);
    }

    private static bool IsCastExpression(in ParserCtx ctx, in TokenArena ta, in Interner names, bool inPatternContext)
    {
        int inner = ctx.Pos + 1;
        if (inner >= ctx.EndPos || !TryScanTypeSyntax(in ta, inner, ctx.EndPos, out int afterType)) return false;
        if (afterType >= ctx.EndPos || ta.Tokens[afterType].Kind != TokKind.RParen) return false;
        int followIdx = afterType + 1;
        if (followIdx >= ctx.EndPos) return false;
        Token followTok = ta.Tokens[followIdx];
        if (inPatternContext && IsPatternCombinatorToken(followTok, in names)) return false;

        // Cast disambiguation is syntactic, never a KnownTypes lookup. In
        // particular, (C)-x is subtraction even when C names a declared type;
        // nint, nuint and dynamic are contextual names, not predefined keywords.
        TokKind first = ta.Tokens[inner].Kind;
        TokKind last = ta.Tokens[afterType - 1].Kind;
        bool predefined = (IsPrimitiveTypeKeyword(first) && first != TokKind.KwNInt && first != TokKind.KwNUInt) || first == TokKind.KwDecimal;
        // Only the alias-qualified name itself is unambiguously a type.
        // An additional member (global::System.Int32) is again expression-shaped.
        bool hasGlobalAlias = first == TokKind.KwGlobal && inner + 1 < afterType && ta.Tokens[inner + 1].Kind == TokKind.ColonColon;
        int angleDepth = 0;
        for (int i = inner + 2; i < afterType; i++)
        {
            TokKind part = ta.Tokens[i].Kind;
            if (part == TokKind.LAngle) angleDepth += 1;
            else if (part == TokKind.RAngle) angleDepth -= 1;
            else if (part == TokKind.GtGt) angleDepth -= 2;
            else if (part == TokKind.GtGtGt) angleDepth -= 3;
            else if (part == TokKind.Dot && angleDepth == 0) hasGlobalAlias = false;
        }
        bool mustBeType = predefined || hasGlobalAlias || last == TokKind.Question || last == TokKind.Star || last == TokKind.RBracket;
        TokKind follow = followTok.Kind;
        if (mustBeType)
        {
            // Patterns also admit parenthesized types. Only a following operand
            // disambiguates a cast there; ordinary expressions need no lookahead.
            return !inPatternContext || follow == TokKind.Plus || follow == TokKind.Minus ||
                follow == TokKind.Amp || follow == TokKind.Star || follow == TokKind.DotDot || CanFollowCast(follow);
        }
        if (follow == TokKind.LBracket)
        {
            // Generic names can cast collection expressions. A simple name can
            // only cast [] without extra parentheses: (C)[x] remains indexing.
            bool generic = last == TokKind.RAngle || last == TokKind.GtGt || last == TokKind.GtGtGt;
            return generic || (followIdx + 1 < ctx.EndPos && ta.Tokens[followIdx + 1].Kind == TokKind.RBracket);
        }
        return CanFollowCast(follow);
    }

    private static bool CanFollowCast(TokKind k)
    {
        switch (k)
        {
            case TokKind.KwIs:
            case TokKind.Semi:
            case TokKind.RParen:
            case TokKind.RBracket:
            case TokKind.LBrace:
            case TokKind.RBrace:
            case TokKind.Comma:
            case TokKind.Eq:
            case TokKind.PlusEq:
            case TokKind.MinusEq:
            case TokKind.StarEq:
            case TokKind.SlashEq:
            case TokKind.PercentEq:
            case TokKind.AmpEq:
            case TokKind.CaretEq:
            case TokKind.PipeEq:
            case TokKind.LtLtEq:
            case TokKind.GtGtEq:
            case TokKind.GtGtGtEq:
            case TokKind.QuestionQuestionEq:
            case TokKind.Question:
            case TokKind.Colon:
            case TokKind.PipePipe:
            case TokKind.AmpAmp:
            case TokKind.Pipe:
            case TokKind.Caret:
            case TokKind.Amp:
            case TokKind.EqEq:
            case TokKind.BangEq:
            case TokKind.LAngle:
            case TokKind.LtEq:
            case TokKind.RAngle:
            case TokKind.GtEq:
            case TokKind.LtLt:
            case TokKind.GtGt:
            case TokKind.GtGtGt:
            case TokKind.Plus:
            case TokKind.Minus:
            case TokKind.Star:
            case TokKind.Slash:
            case TokKind.Percent:
            case TokKind.PlusPlus:
            case TokKind.MinusMinus:
            case TokKind.LBracket:
            case TokKind.Dot:
            case TokKind.DotDot:
            case TokKind.QuestionDot:
            case TokKind.QuestionQuestion:
            case TokKind.Eof:
            case TokKind.KwSwitch:
            case TokKind.FatArrow:
                return false;
            default:
                return true;
        }
    }

    private static int ParsePostfix(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        int expr = ParsePrimary(ref ctx, in ta, ref names, ref ast, ref diags);
        while (true)
        {
            int tokIdx = ctx.Pos;
            TokKind k = PeekKind(in ctx, in ta, 0);

            if (k == TokKind.Dot)
            {
                Advance(ref ctx, in ta);
                int rawMId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected member name after '.'."u8, out _);
                int mId = rawMId >= 0 ? rawMId : names.Intern("_err"u8);
                int taStart = 0;
                int taCount = 0;
                if (PeekKind(in ctx, in ta, 0) == TokKind.LAngle &&
                    CanScanTypeArgList(in ta, ctx.Pos, ctx.EndPos, out int afterAngle) &&
                    afterAngle < ctx.EndPos &&
                    IsGenericFollowToken(ta.Tokens[afterAngle].Kind))
                {
                    var targs = new Vec<int>();
                    Advance(ref ctx, in ta);
                    while (!IsAtTypeArgClose(in ctx, in ta) && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                    {
                        targs.Push(ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags));
                        if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                    }
                    ExpectTypeArgClose(ref ctx, in ta, ref diags, "Expected '>'."u8);
                    taStart = ast.PushExtraSpan(targs.AsReadOnlySpan());
                    taCount = targs.Count;
                }
                expr = ast.AddNode(AstKind.ExprMember, tokIdx, expr, mId, taStart, taCount);
            }
            else if (k == TokKind.QuestionDot || (k == TokKind.Question && PeekKind(in ctx, in ta, 1) == TokKind.LBracket &&
                !IsConditionalCollection(ref ctx, in ta, ref names)))
            {
                Token qt = Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.NullOrZeroUnsafe, qt, "Null-conditional '?.' / '?[]' operator is banned in Vibranium."u8);
                if (k == TokKind.QuestionDot && IsIdentLike(PeekKind(in ctx, in ta, 0))) Advance(ref ctx, in ta);
            }
            else if (k == TokKind.LParen)
            {
                var args = new Vec<int>();
                ParseCallArgList(ref ctx, in ta, ref names, ref ast, ref diags, ref args);
                int argsStart = ast.PushExtraSpan(args.AsReadOnlySpan());
                expr = ast.AddNode(AstKind.ExprCall, tokIdx, expr, argsStart, args.Count, 0);
            }
            else if (k == TokKind.LBracket)
            {
                var idxArgs = new Vec<int>(2);
                ParseIndexArgList(ref ctx, in ta, ref names, ref ast, ref diags, ref idxArgs, out int singleSimpleExpr);
                int idxStart = ast.PushExtraSpan(idxArgs.AsReadOnlySpan());
                expr = ast.AddNode(AstKind.ExprIndex, tokIdx, expr, idxStart, idxArgs.Count, singleSimpleExpr);
            }
            else if (k == TokKind.PlusPlus || k == TokKind.MinusMinus)
            {
                Advance(ref ctx, in ta);
                expr = ast.AddNode(AstKind.ExprUnary, tokIdx, (int)k, expr, 1, 0);
            }
            else if (k == TokKind.Bang)
            {
                Token bt = Advance(ref ctx, in ta);
                if (ctx.UnitKind != UnitKinds.Core)
                {
                    diags.ReportTok(VibCodes.NullOrZeroUnsafe, bt, "Null-forgiving postfix '!' operator is banned in Vibranium."u8);
                }
                // Keep the rejected postfix wrapper in the recovery AST. A
                // suppressed collection is not itself a conditional branch;
                // erasing it would move C16's canonical anchor to that operand.
                expr = ast.AddNode(AstKind.ExprUnary, tokIdx, (int)TokKind.Bang, expr, 1, 0);
            }
            else
            {
                break;
            }
        }
        return expr;
    }

    private static void ParseIndexArgList(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        ref Vec<int> outArgs,
        out int singleSimpleExpr)
    {
        singleSimpleExpr = -1;
        Token lb = Expect(ref ctx, in ta, TokKind.LBracket, ref diags, "Expected '['."u8);
        if (PeekKind(in ctx, in ta, 0) == TokKind.RBracket)
        {
            diags.ReportTok(VibCodes.Unsupported, lb, "Indexer access requires at least one argument."u8);
            Advance(ref ctx, in ta);
            return;
        }
        int firstExpr = -1;
        PassMode firstMode = PassMode.ByValue;
        int firstName = -1;
        while (PeekKind(in ctx, in ta, 0) != TokKind.RBracket && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int argTok = ctx.Pos;
            int argNameId = -1;
            if (IsIdentLike(PeekKind(in ctx, in ta, 0)) && PeekKind(in ctx, in ta, 1) == TokKind.Colon)
            {
                argNameId = TokenIdentId(Advance(ref ctx, in ta), ref names);
                Advance(ref ctx, in ta);
            }

            PassMode mode = PassMode.ByValue;
            if (Match(ref ctx, in ta, TokKind.KwIn)) mode = PassMode.In;
            else if (Match(ref ctx, in ta, TokKind.KwRef))
            {
                mode = PassMode.Ref;
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[argTok], "'ref' arguments are not permitted in indexer access."u8);
            }
            else if (Match(ref ctx, in ta, TokKind.KwOut))
            {
                mode = PassMode.Out;
                diags.ReportTok(VibCodes.Unsupported, ta.Tokens[argTok], "'out' arguments are not permitted in indexer access."u8);
            }

            int valExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
            int packedModeName = CallArgBits.Pack(mode, argNameId);
            if (outArgs.Count == 0)
            {
                firstExpr = valExpr;
                firstMode = mode;
                firstName = argNameId;
            }
            outArgs.Push(ast.AddNode(AstKind.CallArg, argTok, packedModeName, valExpr, -2, -1));

            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
        }
        Expect(ref ctx, in ta, TokKind.RBracket, ref diags, "Expected ']'."u8);
        if (outArgs.Count == 1 && firstMode == PassMode.ByValue && firstName < 0)
        {
            singleSimpleExpr = firstExpr;
        }
    }

    private static void ParseCallArgList(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags,
        ref Vec<int> outArgs)
    {
        int prevWhen = ctx.InSwitchWhenGuard;
        ctx.InSwitchWhenGuard = 0;
        Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '('."u8);
        while (PeekKind(in ctx, in ta, 0) != TokKind.RParen && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            int argTok = ctx.Pos;
            int argNameId = -1;
            if (IsIdentLike(PeekKind(in ctx, in ta, 0)) && PeekKind(in ctx, in ta, 1) == TokKind.Colon)
            {
                argNameId = TokenIdentId(Advance(ref ctx, in ta), ref names);
                Advance(ref ctx, in ta);
            }

            PassMode mode = PassMode.ByValue;
            if (Match(ref ctx, in ta, TokKind.KwIn)) mode = PassMode.In;
            else if (Match(ref ctx, in ta, TokKind.KwRef)) mode = PassMode.Ref;
            else if (Match(ref ctx, in ta, TokKind.KwOut)) mode = PassMode.Out;
            if (mode != PassMode.ByValue && PeekKind(in ctx, in ta, 0) == TokKind.KwRef)
                diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "An argument can have only one ref, in, or out modifier (CS1525)."u8);

            int packedModeName = CallArgBits.Pack(mode, argNameId);
            if (mode == PassMode.Out)
            {
                if (PeekKind(in ctx, in ta, 0) == TokKind.KwVar && IsIdentLike(PeekKind(in ctx, in ta, 1)))
                {
                    Advance(ref ctx, in ta);
                    int vNameId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected identifier after 'out var'."u8, out Token designation);
                    if (vNameId >= 0 && Bytes.Eq(names.Text(vNameId), "_"u8) && designation.Length != 1)
                        SubsetCuts.Report(11, designation, ref diags);
                    if (vNameId >= 0 && Bytes.Eq(names.Text(vNameId), "_"u8) && designation.Length == 1)
                    {
                        outArgs.Push(ast.AddNode(AstKind.CallArg, argTok, packedModeName, -1, -3, -1));
                    }
                    else
                    {
                        outArgs.Push(ast.AddNode(AstKind.CallArg, argTok, packedModeName, -1, -1, vNameId >= 0 ? vNameId : 0));
                    }
                }
                else if (LooksLikeInlineOutDecl(in ctx, in ta))
                {
                    int outType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
                    int outVarId = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected variable name in out declaration."u8, out Token designation);
                    if (outVarId >= 0 && Bytes.Eq(names.Text(outVarId), "_"u8) && designation.Length != 1)
                        SubsetCuts.Report(11, designation, ref diags);
                    if (outVarId >= 0 && Bytes.Eq(names.Text(outVarId), "_"u8) && designation.Length == 1)
                    {
                        // A typed discard keeps its type syntax in D for the
                        // out-argument type identity check.
                        outArgs.Push(ast.AddNode(AstKind.CallArg, argTok, packedModeName, -1, -3, outType));
                    }
                    else
                    {
                        outArgs.Push(ast.AddNode(AstKind.CallArg, argTok, packedModeName, -1, outType, outVarId >= 0 ? outVarId : 0));
                    }
                }
                else
                {
                    // A bare '_' may name existing storage. Preserve the expression
                    // so binding, not syntax alone, decides whether it is a discard.
                    int outExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                    outArgs.Push(ast.AddNode(AstKind.CallArg, argTok, packedModeName, outExpr, -2, -1));
                }
            }
            else
            {
                int valExpr = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                outArgs.Push(ast.AddNode(AstKind.CallArg, argTok, packedModeName, valExpr, -2, -1));
            }

            if (!Match(ref ctx, in ta, TokKind.Comma)) break;
        }
        Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')'."u8);
        ctx.InSwitchWhenGuard = prevWhen;
    }

    private static bool LooksLikeInlineOutDecl(in ParserCtx ctx, in TokenArena ta)
    {
        if (!TryScanTypeSyntax(in ta, ctx.Pos, ctx.EndPos, out int afterType)) return false;
        if (afterType >= ctx.EndPos || !IsIdentLike(ta.Tokens[afterType].Kind)) return false;
        TokKind follow = afterType + 1 < ctx.EndPos ? ta.Tokens[afterType + 1].Kind : TokKind.Eof;
        return follow == TokKind.Comma || follow == TokKind.RParen;
    }

    private static int ParsePrimary(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        int tokIdx = ctx.Pos;
        Token t = Peek(in ctx, in ta, 0);

        switch (t.Kind)
        {
            case TokKind.IntLit:
            case TokKind.LongLit:
            case TokKind.UIntLit:
            case TokKind.ULongLit:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprIntLit, tokIdx, tokIdx, 0, 0, 0);

            case TokKind.FloatLit:
            case TokKind.DoubleLit:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprFloatLit, tokIdx, tokIdx, 0, 0, 0);

            case TokKind.KwTrue:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprBoolLit, tokIdx, 1, 0, 0, 0);

            case TokKind.KwFalse:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprBoolLit, tokIdx, 0, 0, 0, 0);

            case TokKind.CharLit:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprCharLit, tokIdx, (int)t.IntVal, 0, 0, 0);

            case TokKind.StrLit:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprStrLit, tokIdx, t.AuxId, 0, 0, 0);

            case TokKind.Utf8Lit:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprUtf8Lit, tokIdx, t.AuxId, 0, 0, 0);

            case TokKind.KwNull:
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.NullOrZeroUnsafe, t, "Null literals are banned in Vibranium; use Option<T> (Some/None)."u8);
                return ast.AddNode(AstKind.ExprNull, tokIdx, 0, 0, 0, 0);

            case TokKind.KwThis:
                Advance(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprThis, tokIdx, 0, 0, 0, 0);

            case TokKind.KwDefault:
            {
                Advance(ref ctx, in ta);
                int defType = -1;
                if (Match(ref ctx, in ta, TokKind.LParen))
                {
                    defType = ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags);
                    Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')' after default(T)."u8);
                }
                return ast.AddNode(AstKind.ExprDefault, tokIdx, defType, 0, 0, 0);
            }

            case TokKind.KwChecked:
            {
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.Unsupported, t, "'checked(...)' expressions are banned in Vibranium."u8);
                Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '('."u8);
                int inner = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')'."u8);
                return inner;
            }

            case TokKind.KwUnchecked:
            {
                Advance(ref ctx, in ta);
                int prevWhen = ctx.InSwitchWhenGuard;
                ctx.InSwitchWhenGuard = 0;
                Expect(ref ctx, in ta, TokKind.LParen, ref diags, "Expected '('."u8);
                int inner = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')'."u8);
                ctx.InSwitchWhenGuard = prevWhen;
                return ast.AddNode(AstKind.ExprUnary, tokIdx, (int)TokKind.KwUnchecked, inner, 0, 0);
            }

            case TokKind.KwThrow:
            {
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.Unsupported, t, "Exceptions ('throw') are banned in Vibranium; use Result<T, E> or VibraniumRuntime.Panic."u8);
                _ = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                return ast.AddNode(AstKind.ExprIntLit, tokIdx, tokIdx, 0, 0, 0);
            }

            case TokKind.KwDelegate:
            {
                Advance(ref ctx, in ta);
                diags.ReportTok(VibCodes.Unsupported, t, "Anonymous methods and delegates are banned in Vibranium."u8);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LParen) ctx.Pos = SkipParenBalanced(in ta, ctx.Pos, ctx.EndPos);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) SkipBraceBalanced(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprIntLit, tokIdx, tokIdx, 0, 0, 0);
            }

            case TokKind.KwStackalloc:
            {
                Advance(ref ctx, in ta);
                int elemType = -1;
                if (PeekKind(in ctx, in ta, 0) != TokKind.LBracket)
                {
                    elemType = ParseTypeSyntaxWithoutTrailingArray(ref ctx, in ta, ref names, ref ast, ref diags);
                }
                if (PeekKind(in ctx, in ta, 0) == TokKind.LBracket) ctx.Pos = SkipBracketBalanced(in ta, ctx.Pos, ctx.EndPos);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) SkipBraceBalanced(ref ctx, in ta);
                return ast.AddNode(AstKind.ExprStackAlloc, tokIdx, elemType, 0, 0, 0);
            }

            case TokKind.KwNew:
            {
                Advance(ref ctx, in ta);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LParen)
                {
                    var args = new Vec<int>();
                    ParseCallArgList(ref ctx, in ta, ref names, ref ast, ref diags, ref args);
                    int nArgsStart = ast.PushExtraSpan(args.AsReadOnlySpan());
                    return ast.AddNode(AstKind.ExprNew, tokIdx, -1, nArgsStart, args.Count, 0);
                }
                if (PeekKind(in ctx, in ta, 0) == TokKind.LBracket)
                {
                    ctx.Pos = SkipBracketBalanced(in ta, ctx.Pos, ctx.EndPos);
                    if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) SkipBraceBalanced(ref ctx, in ta);
                    return ast.AddNode(AstKind.ExprNewArray, tokIdx, -1, 0, 0, 0);
                }

                int newType = ParseTypeSyntaxWithoutTrailingArray(ref ctx, in ta, ref names, ref ast, ref diags);
                if (PeekKind(in ctx, in ta, 0) == TokKind.LBracket)
                {
                    while (PeekKind(in ctx, in ta, 0) == TokKind.LBracket)
                    {
                        ctx.Pos = SkipBracketBalanced(in ta, ctx.Pos, ctx.EndPos);
                    }
                    if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) SkipBraceBalanced(ref ctx, in ta);
                    return ast.AddNode(AstKind.ExprNewArray, tokIdx, newType, 0, 0, 0);
                }

                var cArgs = new Vec<int>();
                if (PeekKind(in ctx, in ta, 0) == TokKind.LParen)
                {
                    ParseCallArgList(ref ctx, in ta, ref names, ref ast, ref diags, ref cArgs);
                }
                if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace)
                {
                    diags.ReportTok(VibCodes.Unsupported, Peek(in ctx, in ta, 0), "Object/collection brace initializers are not supported in Vibranium."u8);
                    SkipBraceBalanced(ref ctx, in ta);
                }
                int cArgsStart = ast.PushExtraSpan(cArgs.AsReadOnlySpan());
                return ast.AddNode(AstKind.ExprNew, tokIdx, newType, cArgsStart, cArgs.Count, 0);
            }

            case TokKind.LBracket:
            {
                Advance(ref ctx, in ta);
                var elems = new Vec<int>();
                while (PeekKind(in ctx, in ta, 0) != TokKind.RBracket && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                {
                    elems.Push(ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags));
                    if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                }
                Expect(ref ctx, in ta, TokKind.RBracket, ref diags, "Expected ']' after collection expression."u8);
                int elemsStart = ast.PushExtraSpan(elems.AsReadOnlySpan());
                return ast.AddNode(AstKind.ExprCollLit, tokIdx, elemsStart, elems.Count, 0, 0);
            }

            case TokKind.LParen:
            {
                int afterParen = SkipParenBalanced(in ta, ctx.Pos, ctx.EndPos);
                if (afterParen < ctx.EndPos &&
                    ta.Tokens[afterParen].Kind == TokKind.FatArrow &&
                    (ctx.InSwitchWhenGuard == 0 || LooksLikeParenLambdaParams(in ta, ctx.Pos, afterParen)))
                {
                    diags.ReportTok(VibCodes.Unsupported, t, "Lambdas and closures are banned in Vibranium."u8);
                    ctx.Pos = afterParen + 1;
                    if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) SkipBraceBalanced(ref ctx, in ta);
                    else _ = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                    return ast.AddNode(AstKind.ExprIntLit, tokIdx, tokIdx, 0, 0, 0);
                }
                Advance(ref ctx, in ta);
                int prevWhen = ctx.InSwitchWhenGuard;
                ctx.InSwitchWhenGuard = 0;
                int inner = ParseExpression(ref ctx, in ta, ref names, ref ast, ref diags);
                Expect(ref ctx, in ta, TokKind.RParen, ref diags, "Expected ')'."u8);
                ctx.InSwitchWhenGuard = prevWhen;
                if (inner >= 0) ctx.ExpressionStarts.Set(inner, tokIdx);
                return inner;
            }

            case TokKind.Ident:
            {
                Advance(ref ctx, in ta);
                ReadOnlySpan<byte> idText = names.Text(t.AuxId);
                if (Bytes.Eq(idText, "from"u8) && IsIdentLike(PeekKind(in ctx, in ta, 0)) && PeekKind(in ctx, in ta, 1) == TokKind.KwIn)
                {
                    diags.ReportTok(VibCodes.Unsupported, t, "LINQ query expressions are banned in Vibranium."u8);
                }
                int taStart = 0;
                int taCount = 0;
                if (PeekKind(in ctx, in ta, 0) == TokKind.LAngle &&
                    CanScanTypeArgList(in ta, ctx.Pos, ctx.EndPos, out int afterAngle) &&
                    afterAngle < ctx.EndPos &&
                    IsGenericFollowToken(ta.Tokens[afterAngle].Kind))
                {
                    var targs = new Vec<int>();
                    Advance(ref ctx, in ta);
                    while (!IsAtTypeArgClose(in ctx, in ta) && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                    {
                        targs.Push(ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags));
                        if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                    }
                    ExpectTypeArgClose(ref ctx, in ta, ref diags, "Expected '>'."u8);
                    taStart = ast.PushExtraSpan(targs.AsReadOnlySpan());
                    taCount = targs.Count;
                }
                return ast.AddNode(AstKind.ExprIdent, tokIdx, t.AuxId, taStart, taCount, 0);
            }
        }

        if (t.Kind == TokKind.KwGlobal && PeekKind(in ctx, in ta, 1) == TokKind.ColonColon && IsIdentLike(PeekKind(in ctx, in ta, 2)))
        {
            Advance(ref ctx, in ta);
            Advance(ref ctx, in ta);
            Token gid = Advance(ref ctx, in ta);
            int taStart = 0;
            int taCount = 0;
            if (PeekKind(in ctx, in ta, 0) == TokKind.LAngle &&
                CanScanTypeArgList(in ta, ctx.Pos, ctx.EndPos, out int afterAngle) &&
                afterAngle < ctx.EndPos &&
                IsGenericFollowToken(ta.Tokens[afterAngle].Kind))
            {
                var targs = new Vec<int>();
                Advance(ref ctx, in ta);
                while (!IsAtTypeArgClose(in ctx, in ta) && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                {
                    targs.Push(ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags));
                    if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                }
                ExpectTypeArgClose(ref ctx, in ta, ref diags, "Expected '>'."u8);
                taStart = ast.PushExtraSpan(targs.AsReadOnlySpan());
                taCount = targs.Count;
            }
            int gidName = TokenIdentId(gid, ref names);
            return ast.AddNode(AstKind.ExprIdent, tokIdx, gidName, taStart, taCount, 1);
        }

        if (IsContextualIdentKeyword(t.Kind) &&
            !(IsPrimitiveTypeKeyword(t.Kind) && PeekKind(in ctx, in ta, 1) == TokKind.Dot))
        {
            Advance(ref ctx, in ta);
            int ctxNameId = InternContextualKeyword(t.Kind, ref names);
            int taStart = 0;
            int taCount = 0;
            if (PeekKind(in ctx, in ta, 0) == TokKind.LAngle &&
                CanScanTypeArgList(in ta, ctx.Pos, ctx.EndPos, out int afterAngle) &&
                afterAngle < ctx.EndPos &&
                IsGenericFollowToken(ta.Tokens[afterAngle].Kind))
            {
                var targs = new Vec<int>();
                Advance(ref ctx, in ta);
                while (!IsAtTypeArgClose(in ctx, in ta) && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                {
                    targs.Push(ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags));
                    if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                }
                ExpectTypeArgClose(ref ctx, in ta, ref diags, "Expected '>'."u8);
                taStart = ast.PushExtraSpan(targs.AsReadOnlySpan());
                taCount = targs.Count;
            }
            return ast.AddNode(AstKind.ExprIdent, tokIdx, ctxNameId, taStart, taCount, 0);
        }

        if (IsPrimitiveTypeKeyword(t.Kind) && PeekKind(in ctx, in ta, 1) == TokKind.Dot)
        {
            Advance(ref ctx, in ta);
            if (t.Kind == TokKind.KwObject)
            {
                diags.ReportTok(VibCodes.Unsupported, t, "'object' type is banned in Vibranium."u8);
            }
            int primName = names.Intern(PrimKeywordText(t.Kind));
            return ast.AddNode(AstKind.ExprIdent, tokIdx, primName, 0, 0, 2);
        }

        Token bad = Advance(ref ctx, in ta);
        diags.ReportTok(VibCodes.Unsupported, bad, "Unexpected token in expression."u8);
        return ast.AddNode(AstKind.ExprIntLit, tokIdx, tokIdx, 0, 0, 0);
    }

    private static bool LooksLikeParenLambdaParams(in TokenArena ta, int lparenIdx, int afterParenIdx)
    {
        // `() =>`
        if (afterParenIdx == lparenIdx + 2) return true;
        int depth = 0;
        for (int i = lparenIdx + 1; i + 1 < afterParenIdx; i++)
        {
            TokKind k = ta.Tokens[i].Kind;
            if (k == TokKind.LParen || k == TokKind.LBracket) depth += 1;
            else if (k == TokKind.RParen || k == TokKind.RBracket) depth -= 1;
            else if (depth == 0 && k == TokKind.Comma) return true;
        }
        // `(int x) =>` or `(MyType x) =>`
        if (TryScanTypeSyntax(in ta, lparenIdx + 1, afterParenIdx - 1, out int afterT) &&
            afterT == afterParenIdx - 2 &&
            IsIdentLike(ta.Tokens[afterT].Kind))
        {
            return true;
        }
        return false;
    }

    private static ReadOnlySpan<byte> PrimKeywordText(TokKind k)
    {
        switch (k)
        {
            case TokKind.KwBool: return "bool"u8;
            case TokKind.KwInt: return "int"u8;
            case TokKind.KwLong: return "long"u8;
            case TokKind.KwByte: return "byte"u8;
            case TokKind.KwSByte: return "sbyte"u8;
            case TokKind.KwShort: return "short"u8;
            case TokKind.KwUShort: return "ushort"u8;
            case TokKind.KwUInt: return "uint"u8;
            case TokKind.KwULong: return "ulong"u8;
            case TokKind.KwNInt: return "nint"u8;
            case TokKind.KwNUInt: return "nuint"u8;
            case TokKind.KwFloat: return "float"u8;
            case TokKind.KwDouble: return "double"u8;
            case TokKind.KwString: return "string"u8;
            case TokKind.KwChar: return "char"u8;
            default: return "object"u8;
        }
    }

    private static int ParseTypeSyntaxWithoutTrailingArray(
        ref ParserCtx ctx,
        in TokenArena ta,
        ref Interner names,
        ref AstArena ast,
        ref DiagnosticBag diags)
    {
        int tokIdx = ctx.Pos;
        Token t = Peek(in ctx, in ta, 0);
        if (IsPrimitiveTypeKeyword(t.Kind))
        {
            Advance(ref ctx, in ta);
            return ast.AddNode(AstKind.TypePrim, tokIdx, (int)t.Kind, 0, 0, 0);
        }
        bool hasGlobal = false;
        if (PeekKind(in ctx, in ta, 0) == TokKind.KwGlobal && PeekKind(in ctx, in ta, 1) == TokKind.ColonColon)
        {
            hasGlobal = true;
            Advance(ref ctx, in ta);
            Advance(ref ctx, in ta);
        }
        var prefixBuf = new StringBuf(32);
        bool hasDotPrefix = false;
        if (hasGlobal)
        {
            prefixBuf.Append("global::"u8);
        }
        int rawId0 = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected type name."u8, out _);
        int lastId = rawId0 >= 0 ? rawId0 : names.Intern("_ErrT"u8);
        var typeArgs = new Vec<int>();
        while (true)
        {
            typeArgs.Clear();
            if (Match(ref ctx, in ta, TokKind.LAngle))
            {
                while (!IsAtTypeArgClose(in ctx, in ta) && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
                {
                    typeArgs.Push(ParseTypeSyntax(ref ctx, in ta, ref names, ref ast, ref diags));
                    if (!Match(ref ctx, in ta, TokKind.Comma)) break;
                }
                ExpectTypeArgClose(ref ctx, in ta, ref diags, "Expected '>'."u8);
            }
            if (Match(ref ctx, in ta, TokKind.Dot))
            {
                if (hasDotPrefix) prefixBuf.AppendAscii((byte)'.');
                prefixBuf.Append(names.Text(lastId));
                hasDotPrefix = true;
                int rawNext = ExpectIdent(ref ctx, in ta, ref names, ref diags, "Expected type name."u8, out _);
                if (rawNext >= 0) lastId = rawNext;
                continue;
            }
            break;
        }
        int prefixNsId = (hasGlobal || hasDotPrefix) ? names.Intern(prefixBuf.AsUtf8()) : -1;
        int typeRefIdx = ast.AddTypeRef(new TypeRefContext(prefixNsId, ctx.UnitKind, ctx.CurrentNsId, ctx.CurrentOwnerDeclId, ctx.UsingExtraStart, ctx.UsingCount));
        int taStart = ast.PushExtraSpan(typeArgs.AsReadOnlySpan());
        return ast.AddNode(AstKind.TypeNamed, tokIdx, lastId, taStart, typeArgs.Count, typeRefIdx);
    }

    private static bool IsGenericFollowToken(TokKind k) =>
        k == TokKind.LParen || k == TokKind.Dot || k == TokKind.RParen ||
        k == TokKind.RBracket || k == TokKind.RBrace || k == TokKind.Comma ||
        k == TokKind.Semi || k == TokKind.Colon || k == TokKind.FatArrow;

    public static bool CanScanTypeArgList(in TokenArena ta, int pos, int endPos, out int afterRAngle)
    {
        afterRAngle = pos;
        int p = pos;
        int pendingGt = 0;
        if (!CanScanTypeArgListCore(in ta, ref p, ref pendingGt, endPos) || pendingGt != 0)
        {
            return false;
        }
        afterRAngle = p;
        return true;
    }

    private static bool CanScanTypeArgListCore(in TokenArena ta, ref int p, ref int pendingGt, int endPos)
    {
        if (pendingGt != 0 || p >= endPos || ta.Tokens[p].Kind != TokKind.LAngle) return false;
        p += 1;
        int count = 0;
        while (p < endPos)
        {
            if (!TryScanTypeSyntaxCore(in ta, ref p, ref pendingGt, endPos)) return false;
            count += 1;
            if (p >= endPos) return false;
            if (pendingGt == 0 && ta.Tokens[p].Kind == TokKind.Comma)
            {
                p += 1;
                continue;
            }
            if (pendingGt > 0)
            {
                pendingGt -= 1;
                if (pendingGt == 0) p += 1;
                return count > 0;
            }
            TokKind ck = ta.Tokens[p].Kind;
            if (ck == TokKind.RAngle)
            {
                p += 1;
                return count > 0;
            }
            if (ck == TokKind.GtGt)
            {
                pendingGt = 1;
                return count > 0;
            }
            if (ck == TokKind.GtGtGt)
            {
                pendingGt = 2;
                return count > 0;
            }
            return false;
        }
        return false;
    }

    public static bool TryScanTypeSyntax(in TokenArena ta, int pos, int endPos, out int afterType)
    {
        afterType = pos;
        int p = pos;
        int pendingGt = 0;
        if (!TryScanTypeSyntaxCore(in ta, ref p, ref pendingGt, endPos, allowTrailingQuestion: true) || pendingGt != 0)
        {
            return false;
        }
        afterType = p;
        return true;
    }

    private static bool TryScanTypeSyntaxNoTrailingQuestion(in TokenArena ta, int pos, int endPos, out int afterType)
    {
        afterType = pos;
        int p = pos;
        int pendingGt = 0;
        if (!TryScanTypeSyntaxCore(in ta, ref p, ref pendingGt, endPos, allowTrailingQuestion: false) || pendingGt != 0)
        {
            return false;
        }
        afterType = p;
        return true;
    }

    private static bool TryScanTypeSyntaxCore(in TokenArena ta, ref int cur, ref int pendingGt, int endPos) => TryScanTypeSyntaxCore(in ta, ref cur, ref pendingGt, endPos, true);

    private static bool TryScanTypeSyntaxCore(in TokenArena ta, ref int cur, ref int pendingGt, int endPos, bool allowTrailingQuestion)
    {
        if (pendingGt != 0 || cur >= endPos) return false;
        TokKind k = ta.Tokens[cur].Kind;

        if (IsPrimitiveTypeKeyword(k) || k == TokKind.KwDecimal || k == TokKind.KwDynamic)
        {
            cur += 1;
            while (cur < endPos)
            {
                if ((allowTrailingQuestion && ta.Tokens[cur].Kind == TokKind.Question) || ta.Tokens[cur].Kind == TokKind.Star) cur += 1;
                else if (ta.Tokens[cur].Kind == TokKind.LBracket && cur + 1 < endPos && ta.Tokens[cur + 1].Kind == TokKind.RBracket) cur += 2;
                else break;
            }
            return true;
        }

        if (cur + 1 < endPos && ta.Tokens[cur].Kind == TokKind.KwGlobal && ta.Tokens[cur + 1].Kind == TokKind.ColonColon)
        {
            cur += 2;
        }
        if (cur >= endPos || !IsTypeIdentHead(ta.Tokens[cur].Kind)) return false;
        cur += 1;

        while (pendingGt == 0)
        {
            if (cur < endPos && ta.Tokens[cur].Kind == TokKind.LAngle)
            {
                if (!CanScanTypeArgListCore(in ta, ref cur, ref pendingGt, endPos)) return false;
                if (pendingGt != 0) break;
            }
            if (cur + 1 < endPos && ta.Tokens[cur].Kind == TokKind.Dot && IsIdentLike(ta.Tokens[cur + 1].Kind))
            {
                cur += 2;
                continue;
            }
            break;
        }

        if (pendingGt == 0)
        {
            while (cur < endPos)
            {
                if ((allowTrailingQuestion && ta.Tokens[cur].Kind == TokKind.Question) || ta.Tokens[cur].Kind == TokKind.Star) cur += 1;
                else if (ta.Tokens[cur].Kind == TokKind.LBracket && cur + 1 < endPos && ta.Tokens[cur + 1].Kind == TokKind.RBracket) cur += 2;
                else break;
            }
        }

        return true;
    }

    private static int SkipParenBalanced(in TokenArena ta, int p, int endPos)
    {
        int depth = 0;
        while (p < endPos)
        {
            TokKind k = ta.Tokens[p].Kind;
            if (k == TokKind.LParen) depth += 1;
            else if (k == TokKind.RParen)
            {
                depth -= 1;
                if (depth == 0) return p + 1;
            }
            else if (k == TokKind.Eof) return p;
            p += 1;
        }
        return p;
    }

    private static void SkipBraceBalanced(ref ParserCtx ctx, in TokenArena ta)
    {
        int depth = 0;
        while (ctx.Pos < ctx.EndPos && PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            TokKind k = Advance(ref ctx, in ta).Kind;
            if (k == TokKind.LBrace) depth += 1;
            else if (k == TokKind.RBrace)
            {
                depth -= 1;
                if (depth <= 0) return;
            }
        }
    }

    private static void SkipDeclarationBody(ref ParserCtx ctx, in TokenArena ta)
    {
        while (PeekKind(in ctx, in ta, 0) != TokKind.LBrace &&
               PeekKind(in ctx, in ta, 0) != TokKind.Semi &&
               PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            Advance(ref ctx, in ta);
        }
        if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) SkipBraceBalanced(ref ctx, in ta);
        else Match(ref ctx, in ta, TokKind.Semi);
    }

    private static void SkipToSemi(ref ParserCtx ctx, in TokenArena ta)
    {
        while (PeekKind(in ctx, in ta, 0) != TokKind.Semi &&
               PeekKind(in ctx, in ta, 0) != TokKind.RBrace &&
               PeekKind(in ctx, in ta, 0) != TokKind.Eof)
        {
            Advance(ref ctx, in ta);
        }
        Match(ref ctx, in ta, TokKind.Semi);
    }

    private static void SkipTryBlocks(ref ParserCtx ctx, in TokenArena ta)
    {
        if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) SkipBraceBalanced(ref ctx, in ta);
        while (PeekKind(in ctx, in ta, 0) == TokKind.KwCatch || PeekKind(in ctx, in ta, 0) == TokKind.KwFinally)
        {
            Advance(ref ctx, in ta);
            if (PeekKind(in ctx, in ta, 0) == TokKind.LParen) ctx.Pos = SkipParenBalanced(in ta, ctx.Pos, ctx.EndPos);
            if (PeekKind(in ctx, in ta, 0) == TokKind.LBrace) SkipBraceBalanced(ref ctx, in ta);
        }
    }
}
