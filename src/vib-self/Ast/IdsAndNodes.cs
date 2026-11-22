using System.Diagnostics.CodeAnalysis;

namespace Vibranium.Self;

public static class UnitKinds
{
    public const int Core = 0;
    public const int Std = 1;
    public const int App = 2;
}

public enum TokKind
{
    Eof = 0,
    Ident = 1,
    IntLit = 2,
    LongLit = 3,
    UIntLit = 4,
    ULongLit = 5,
    FloatLit = 6,
    DoubleLit = 7,
    CharLit = 8,
    StrLit = 9,
    Utf8Lit = 10,

    // Keywords
    KwUsing = 20,
    KwStatic = 21,
    KwGlobal = 22,
    KwNamespace = 23,
    KwPublic = 24,
    KwInternal = 25,
    KwPrivate = 26,
    KwProtected = 27,
    KwSealed = 28,
    KwAbstract = 29,
    KwVirtual = 30,
    KwOverride = 31,
    KwPartial = 32,
    KwReadonly = 33,
    KwUnsafe = 34,
    KwAsync = 35,
    KwClass = 36,
    KwStruct = 37,
    KwEnum = 38,
    KwInterface = 39,
    KwDelegate = 40,
    KwWhere = 41,
    KwNew = 42,
    KwThis = 43,
    KwReturn = 44,
    KwIf = 45,
    KwElse = 46,
    KwWhile = 47,
    KwFor = 48,
    KwForeach = 49,
    KwIn = 50,
    KwRef = 51,
    KwOut = 52,
    KwScoped = 53,
    KwSwitch = 54,
    KwCase = 55,
    KwDefault = 56,
    KwBreak = 57,
    KwContinue = 58,
    KwVar = 59,
    KwConst = 60,
    KwTrue = 61,
    KwFalse = 62,
    KwNull = 63,
    KwIs = 64,
    KwChecked = 65,
    KwUnchecked = 66,
    KwGoto = 67,
    KwThrow = 68,
    KwTry = 69,
    KwCatch = 70,
    KwFinally = 71,
    KwLock = 72,
    KwYield = 73,
    KwAwait = 74,
    KwStackalloc = 75,
    KwDecimal = 76,
    KwDynamic = 77,
    KwOperator = 78,
    KwDo = 79,

    // Built-in type keywords
    KwVoid = 80,
    KwBool = 81,
    KwSByte = 82,
    KwByte = 83,
    KwShort = 84,
    KwUShort = 85,
    KwChar = 86,
    KwInt = 87,
    KwUInt = 88,
    KwLong = 89,
    KwULong = 90,
    KwNInt = 91,
    KwNUInt = 92,
    KwFloat = 93,
    KwDouble = 94,
    KwString = 95,
    KwObject = 96,

    // Punctuation & Operators
    LParen = 110,
    RParen = 111,
    LBrace = 112,
    RBrace = 113,
    LBracket = 114,
    RBracket = 115,
    LAngle = 116,
    RAngle = 117,
    Semi = 118,
    Comma = 119,
    Dot = 120,
    Colon = 121,
    ColonColon = 122,
    Question = 123,
    Bang = 124,
    Tilde = 125,
    Plus = 126,
    Minus = 127,
    Star = 128,
    Slash = 129,
    Percent = 130,
    Amp = 131,
    Pipe = 132,
    Caret = 133,
    Eq = 134,
    EqEq = 135,
    BangEq = 136,
    LtEq = 137,
    GtEq = 138,
    AmpAmp = 139,
    PipePipe = 140,
    LtLt = 141,
    GtGt = 142,
    GtGtGt = 143,
    PlusEq = 144,
    MinusEq = 145,
    StarEq = 146,
    SlashEq = 147,
    PercentEq = 148,
    AmpEq = 149,
    PipeEq = 150,
    CaretEq = 151,
    LtLtEq = 152,
    GtGtEq = 153,
    GtGtGtEq = 154,
    PlusPlus = 155,
    MinusMinus = 156,
    FatArrow = 157,
    QuestionDot = 158,
    QuestionQuestion = 159,
    QuestionQuestionEq = 160,
    DotDot = 161,
}

public enum PassMode
{
    ByValue = 0,
    In = 1,
    Ref = 2,
    Out = 3,
    ThisShared = 4,
    ThisMut = 5,
}

public static class DeclFlags
{
    public const int Public = 1 << 0;
    public const int Internal = 1 << 1;
    public const int Private = 1 << 2;
    public const int Static = 1 << 3;
    public const int Sealed = 1 << 4;
    public const int Readonly = 1 << 5;
    public const int Partial = 1 << 6;
    public const int MutAttr = 1 << 7;
    public const int UnscopedRefAttr = 1 << 8;
    public const int HasNativeAttr = 1 << 9;
    public const int HasReturnLifetimeAttr = 1 << 10;
    public const int IsDisposable = 1 << 11;
    public const int IsCtor = 1 << 12;
    public const int IsPropGet = 1 << 13;
    public const int IsPropSet = 1 << 14;
    public const int IsIndexer = 1 << 15;
    public const int IsOpAddAssign = 1 << 16;
    public const int IsTopLevelMain = 1 << 17;
    public const int IsLocalFunc = 1 << 18;
    public const int ReturnsByRef = 1 << 19;
    public const int ReturnRefMut = 1 << 20;
    public const int IsConst = 1 << 21;
    public const int IsScoped = 1 << 22;
    public const int HasObsoleteAttr = 1 << 23;
    public const int UnmanagedConstraint = 1 << 24;
    public const int ClassConstraint = 1 << 25;
    public const int IsCapturedPrimaryParam = 1 << 26;
    public const int IsExtension = 1 << 27;
    public const int IsUnqualifiedDisposable = 1 << 28;
    public const int StructConstraint = 1 << 29;
}

public enum AstKind
{
    None = 0,

    // Type Syntax Nodes
    TypePrim = 1,       // A = TokKind of built-in primitive
    TypeNamed = 2,      // A = NameId (or qualified last), B = ExtraStart (type args), C = TypeArgCount, D = TypeRefIdx in AstArena.TypeRefs
    TypeArray = 3,      // A = ElemTypeNode (always rejected by gatekeeper, kept for accurate VIB006 vs VIB000)
    TypeNullable = 4,   // A = InnerTypeNode (rejected with VIB008)
    FrameworkAttributeUse = 5, // A = written path NameId, B = TypeRefIdx, C = framework identity, D = rooted/alias-qualified flags and pending invalid option bit

    // Statements
    StmtBlock = 10,     // A = ExtraStart, B = StmtCount
    StmtVarDecl = 11,   // A = NameId, B = TypeNode (-1 for var), C = InitExpr (-1 if none), D = Flags (1=using, 2=ref, 4=ref readonly, 8=const)
    StmtIf = 12,        // A = CondExpr, B = ThenStmt, C = ElseStmt (-1 if none)
    StmtWhile = 13,     // A = CondExpr, B = BodyStmt
    StmtFor = 14,       // A = InitStmt (-1 if none), B = CondExpr (-1 if none), C = StepExtraStart (count in D), D = (StepCount << 16) | BodyStmtIdx? -> store BodyStmt in Extra[StepStart]
    StmtForeach = 15,   // A = ItemNameId, B = ItemTypeNode (-1 for var), C = CollectionExpr, D = BodyStmt (low) + RefFlags in Extra -> A=NameId, B=Flags(0=val,2=ref,4=refro), C=CollExpr, D=BodyStmt
    StmtSwitch = 16,    // A = ScrutineeExpr, B = SectionsExtraStart, C = SectionCount
    SwitchSection = 17, // A = LabelsExtraStart, B = LabelCount, C = StmtsExtraStart, D = StmtCount (label = -1 means default, else ExprNode)
    StmtReturn = 18,    // A = ExprNode (-1 if void), B = IsRefReturn (0 or 1)
    StmtBreak = 19,
    StmtContinue = 20,
    StmtExpr = 21,      // A = ExprNode
    StmtEmpty = 22,
    StmtDoWhile = 23,   // A = BodyStmt, B = CondExpr
    StmtMulti = 24,     // A = ExtraStart, B = StmtCount (flattened into enclosing block)
    SwitchCaseLabel = 25, // A = PatternNode (-1 for default), B = WhenGuardExpr (-1 if none)
    StmtUncheckedBlock = 26, // A = InnerBlockStmt

    // Expressions
    ExprIntLit = 30,    // A = TokIdx (value in Token.IntVal, kind in Token.Kind)
    ExprFloatLit = 31,  // A = TokIdx (slice in Token.AuxId, kind FloatLit/DoubleLit)
    ExprBoolLit = 32,   // A = 0 or 1
    ExprCharLit = 33,   // A = Unicode scalar value
    ExprStrLit = 34,    // A = LitSliceIdx
    ExprUtf8Lit = 35,   // A = LitSliceIdx
    ExprNull = 36,      // Banned VIB008
    ExprDefault = 37,   // A = TypeNode (-1 if target-typed `default`)
    ExprThis = 38,
    ExprIdent = 39,     // A = NameId, B = TypeArgExtraStart, C = TypeArgCount
    ExprMember = 40,    // A = TargetExpr, B = MemberNameId, C = TypeArgExtraStart, D = TypeArgCount
    ExprIndex = 41,     // A = TargetExpr, B = ArgExtraStart, C = ArgCount, D = SingleUnnamedValExpr (-1 if multi/named/ref)
    ExprCall = 42,      // A = CalleeExpr, B = ArgExtraStart, C = ArgCount
    CallArg = 43,       // A = ((ArgNameId + 1) << 4) | PassMode, B = ExprOrOutVar, C = OutVarTypeNode (-1=var, -2=existing expr, -3=discard `_`), D = OutVarNameId (typed discard: its TypeNode, else -1)
    ExprNew = 44,       // A = TypeNode (-1 if target-typed `new(...)`), B = ArgExtraStart, C = ArgCount
    ExprNewArray = 45,  // A = ElemTypeNode (rejected VIB000/VIB006/VIB008)
    ExprStackAlloc = 46,// A = ElemTypeNode (rejected VIB000/VIB006)
    ExprCollLit = 47,   // A = ElemExtraStart, B = ElemCount (`[a, b, c]` or `[]`)
    ExprUnary = 48,     // A = TokKind op, B = OperandExpr, C = IsPostfix (0 or 1)
    ExprCast = 49,      // A = TypeNode, B = OperandExpr
    ExprBinary = 50,    // A = TokKind op, B = LeftExpr, C = RightExpr
    ExprAssign = 51,    // A = TokKind op (=, +=, ...), B = LeftExpr, C = RightExpr
    ExprCond = 52,      // A = CondExpr, B = TrueExpr, C = FalseExpr
    ExprIsPattern = 53, // A = ScrutineeExpr, B = PatternNode
    ExprSwitch = 54,    // A = ScrutineeExpr, B = ArmsExtraStart, C = ArmCount
    SwitchArm = 55,     // A = PatternNode, B = ValueExpr
    ExprDiscard = 56,   // `_`
    ExprRange = 57,     // A = StartExpr (-1 if omitted), B = EndExpr (-1 if omitted)
    ExprFromEnd = 58,   // A = InnerExpr (`^k`)

    // Patterns
    PatWildcard = 70,   // `_`
    PatConst = 71,      // A = ConstExpr
    PatUnion = 72,      // A = TypeNode (Some<T>, NoneType, Ok<T>, Err<E>), B = BindingNameId (-1 if none or `_`), C = HasBraceSubpattern, D = InnerSubPatternNode (-1 if none)
    PatRel = 73,        // A = TokKind op, B = BoundExpr
    PatNot = 74,        // A = SubPattern
    PatLogic = 75,      // A = 0 (and) or 1 (or), B = LeftPat, C = RightPat
    PatDecl = 76,       // A = TypeNode (-1 for var), B = BindingNameId (-1 for `_`)
}

public readonly struct ByteSlice
{
    public readonly int Offset;
    public readonly int Length;

    public ByteSlice(int offset, int length)
    {
        Offset = offset;
        Length = length;
    }
}

public struct BytePool
{
    private Vec<byte> _data;

    public BytePool(int capacity)
    {
        _data = new Vec<byte>(capacity);
    }

    public ByteSlice Add(ReadOnlySpan<byte> bytes)
    {
        int off = _data.Count;
        _data.Extend(bytes);
        return new ByteSlice(off, bytes.Length);
    }

    [UnscopedRef]
    public readonly ReadOnlySpan<byte> Get(ByteSlice slice) =>
        _data.AsReadOnlySpan().Slice(slice.Offset, slice.Length);
}

public readonly struct Token
{
    public readonly TokKind Kind;
    public readonly int SourceId;
    public readonly int Start;
    public readonly int Length;
    public readonly int AuxId;
    public readonly long IntVal;

    public Token(TokKind kind, int sourceId, int start, int length, int auxId, long intVal)
    {
        Kind = kind;
        SourceId = sourceId;
        Start = start;
        Length = length;
        AuxId = auxId;
        IntVal = intVal;
    }
}

public struct TokenArena
{
    public Vec<Token> Tokens;
    public Vec<ByteSlice> LitSlices;
    public BytePool LitPool;

    public TokenArena(int capacity)
    {
        Tokens = new Vec<Token>(capacity);
        LitSlices = new Vec<ByteSlice>(64);
        LitPool = new BytePool(1024);
    }

    public int AddLit(ReadOnlySpan<byte> bytes)
    {
        ByteSlice s = LitPool.Add(bytes);
        int idx = LitSlices.Count;
        LitSlices.Push(s);
        return idx;
    }

    [UnscopedRef]
    public readonly ReadOnlySpan<byte> GetLit(int idx) =>
        LitPool.Get(LitSlices[idx]);
}

public readonly struct AstNode
{
    public readonly AstKind Kind;
    public readonly int TokIdx;
    public readonly int A;
    public readonly int B;
    public readonly int C;
    public readonly int D;

    public AstNode(AstKind kind, int tokIdx, int a, int b, int c, int d)
    {
        Kind = kind;
        TokIdx = tokIdx;
        A = a;
        B = b;
        C = c;
        D = d;
    }
}

public readonly struct ParamDecl
{
    public readonly int NameId;
    public readonly int TypeNode;
    public readonly PassMode Mode;
    public readonly int Flags;
    public readonly int TokIdx;
    public readonly int DefaultExpr;

    public ParamDecl(int nameId, int typeNode, PassMode mode, int flags, int tokIdx, int defaultExpr)
    {
        NameId = nameId;
        TypeNode = typeNode;
        Mode = mode;
        Flags = flags;
        TokIdx = tokIdx;
        DefaultExpr = defaultExpr;
    }
}

public readonly struct FieldDecl
{
    public readonly int NameId;
    public readonly int TypeNode;
    public readonly int InitExpr;
    public readonly int Flags;
    public readonly long ConstVal;
    public readonly int HasConstVal;
    public readonly int TokIdx;

    public FieldDecl(int nameId, int typeNode, int initExpr, int flags, long constVal, int hasConstVal, int tokIdx)
    {
        NameId = nameId;
        TypeNode = typeNode;
        InitExpr = initExpr;
        Flags = flags;
        ConstVal = constVal;
        HasConstVal = hasConstVal;
        TokIdx = tokIdx;
    }
}

public struct FuncDecl
{
    public int OwnerDeclId;
    public int NameId;
    public int RetTypeNode;
    public int TypeParamStart;
    public int TypeParamCount;
    public int TypeParamFlagsStart;
    public int ParamStart;
    public int ParamCount;
    public int BodyNode;
    public int Flags;
    public int NativeSymLitIdx;
    public int LifetimeExtraStart;
    public int LifetimeCount;
    public int LocalFuncExtraStart;
    public int LocalFuncCount;
    public int UsingExtraStart;
    public int UsingCount;
    public int SourceId;
    public int TokIdx;

    public FuncDecl(
        int ownerDeclId,
        int nameId,
        int retTypeNode,
        int typeParamStart,
        int typeParamCount,
        int typeParamFlagsStart,
        int paramStart,
        int paramCount,
        int bodyNode,
        int flags,
        int nativeSymLitIdx,
        int lifetimeExtraStart,
        int lifetimeCount,
        int sourceId,
        int tokIdx)
    {
        OwnerDeclId = ownerDeclId;
        NameId = nameId;
        RetTypeNode = retTypeNode;
        TypeParamStart = typeParamStart;
        TypeParamCount = typeParamCount;
        TypeParamFlagsStart = typeParamFlagsStart;
        ParamStart = paramStart;
        ParamCount = paramCount;
        BodyNode = bodyNode;
        Flags = flags;
        NativeSymLitIdx = nativeSymLitIdx;
        LifetimeExtraStart = lifetimeExtraStart;
        LifetimeCount = lifetimeCount;
        LocalFuncExtraStart = 0;
        LocalFuncCount = 0;
        UsingExtraStart = 0;
        UsingCount = 0;
        SourceId = sourceId;
        TokIdx = tokIdx;
    }

    public FuncDecl(
        int ownerDeclId,
        int nameId,
        int retTypeNode,
        int typeParamStart,
        int typeParamCount,
        int typeParamFlagsStart,
        int paramStart,
        int paramCount,
        int bodyNode,
        int flags,
        int nativeSymLitIdx,
        int lifetimeExtraStart,
        int lifetimeCount,
        int sourceId,
        int tokIdx,
        int usingExtraStart)
    {
        OwnerDeclId = ownerDeclId;
        NameId = nameId;
        RetTypeNode = retTypeNode;
        TypeParamStart = typeParamStart;
        TypeParamCount = typeParamCount;
        TypeParamFlagsStart = typeParamFlagsStart;
        ParamStart = paramStart;
        ParamCount = paramCount;
        BodyNode = bodyNode;
        Flags = flags;
        NativeSymLitIdx = nativeSymLitIdx;
        LifetimeExtraStart = lifetimeExtraStart;
        LifetimeCount = lifetimeCount;
        LocalFuncExtraStart = 0;
        LocalFuncCount = 0;
        UsingExtraStart = usingExtraStart;
        UsingCount = 0;
        SourceId = sourceId;
        TokIdx = tokIdx;
    }

    public FuncDecl(
        int ownerDeclId,
        int nameId,
        int retTypeNode,
        int typeParamStart,
        int typeParamCount,
        int typeParamFlagsStart,
        int paramStart,
        int paramCount,
        int bodyNode,
        int flags,
        int nativeSymLitIdx,
        int lifetimeExtraStart,
        int lifetimeCount,
        int sourceId,
        int tokIdx,
        int usingExtraStart,
        int usingCount)
    {
        OwnerDeclId = ownerDeclId;
        NameId = nameId;
        RetTypeNode = retTypeNode;
        TypeParamStart = typeParamStart;
        TypeParamCount = typeParamCount;
        TypeParamFlagsStart = typeParamFlagsStart;
        ParamStart = paramStart;
        ParamCount = paramCount;
        BodyNode = bodyNode;
        Flags = flags;
        NativeSymLitIdx = nativeSymLitIdx;
        LifetimeExtraStart = lifetimeExtraStart;
        LifetimeCount = lifetimeCount;
        LocalFuncExtraStart = 0;
        LocalFuncCount = 0;
        UsingExtraStart = usingExtraStart;
        UsingCount = usingCount;
        SourceId = sourceId;
        TokIdx = tokIdx;
    }
}

public static class TypeDeclKinds
{
    public const int Class = 1;
    public const int Struct = 2;
    public const int Enum = 3;
}

public static class CallArgBits
{
    public static int Pack(PassMode mode, int nameId) => ((nameId + 1) << 4) | (int)mode;
    public static PassMode Mode(in AstNode cArg) => (PassMode)(cArg.A & 0xF);
    public static int NameId(in AstNode cArg) => (cArg.A >> 4) - 1;
}

public struct TypeDecl
{
    public int Kind;
    public int UnitKind;
    public int NsId;
    public int OwnerDeclId;
    public int NameId;
    public int TypeParamStart;
    public int TypeParamCount;
    public int TypeParamFlagsStart;
    public int FieldStart;
    public int FieldCount;
    public int FuncStart;
    public int FuncCount;
    public int BaseTypeExtraStart;
    public int BaseTypeCount;
    public int UsingExtraStart;
    public int UsingCount;
    public int Flags;
    public int SourceId;
    public int TokIdx;

    public TypeDecl(
        int kind,
        int unitKind,
        int nsId,
        int nameId,
        int typeParamStart,
        int typeParamCount,
        int typeParamFlagsStart,
        int flags,
        int sourceId,
        int tokIdx)
    {
        Kind = kind;
        UnitKind = unitKind;
        NsId = nsId;
        OwnerDeclId = -1;
        NameId = nameId;
        TypeParamStart = typeParamStart;
        TypeParamCount = typeParamCount;
        TypeParamFlagsStart = typeParamFlagsStart;
        FieldStart = 0;
        FieldCount = 0;
        FuncStart = 0;
        FuncCount = 0;
        BaseTypeExtraStart = 0;
        BaseTypeCount = 0;
        UsingExtraStart = 0;
        UsingCount = 0;
        Flags = flags;
        SourceId = sourceId;
        TokIdx = tokIdx;
    }

    public TypeDecl(
        int kind,
        int unitKind,
        int nsId,
        int nameId,
        int typeParamStart,
        int typeParamCount,
        int typeParamFlagsStart,
        int flags,
        int sourceId,
        int tokIdx,
        int ownerDeclId)
    {
        Kind = kind;
        UnitKind = unitKind;
        NsId = nsId;
        OwnerDeclId = ownerDeclId;
        NameId = nameId;
        TypeParamStart = typeParamStart;
        TypeParamCount = typeParamCount;
        TypeParamFlagsStart = typeParamFlagsStart;
        FieldStart = 0;
        FieldCount = 0;
        FuncStart = 0;
        FuncCount = 0;
        BaseTypeExtraStart = 0;
        BaseTypeCount = 0;
        UsingExtraStart = 0;
        UsingCount = 0;
        Flags = flags;
        SourceId = sourceId;
        TokIdx = tokIdx;
    }

    public TypeDecl(
        int kind,
        int unitKind,
        int nsId,
        int nameId,
        int typeParamStart,
        int typeParamCount,
        int typeParamFlagsStart,
        int flags,
        int sourceId,
        int tokIdx,
        int ownerDeclId,
        int usingExtraStart)
    {
        Kind = kind;
        UnitKind = unitKind;
        NsId = nsId;
        OwnerDeclId = ownerDeclId;
        NameId = nameId;
        TypeParamStart = typeParamStart;
        TypeParamCount = typeParamCount;
        TypeParamFlagsStart = typeParamFlagsStart;
        FieldStart = 0;
        FieldCount = 0;
        FuncStart = 0;
        FuncCount = 0;
        BaseTypeExtraStart = 0;
        BaseTypeCount = 0;
        UsingExtraStart = usingExtraStart;
        UsingCount = 0;
        Flags = flags;
        SourceId = sourceId;
        TokIdx = tokIdx;
    }

    public TypeDecl(
        int kind,
        int unitKind,
        int nsId,
        int nameId,
        int typeParamStart,
        int typeParamCount,
        int typeParamFlagsStart,
        int flags,
        int sourceId,
        int tokIdx,
        int ownerDeclId,
        int usingExtraStart,
        int usingCount)
    {
        Kind = kind;
        UnitKind = unitKind;
        NsId = nsId;
        OwnerDeclId = ownerDeclId;
        NameId = nameId;
        TypeParamStart = typeParamStart;
        TypeParamCount = typeParamCount;
        TypeParamFlagsStart = typeParamFlagsStart;
        FieldStart = 0;
        FieldCount = 0;
        FuncStart = 0;
        FuncCount = 0;
        BaseTypeExtraStart = 0;
        BaseTypeCount = 0;
        UsingExtraStart = usingExtraStart;
        UsingCount = usingCount;
        Flags = flags;
        SourceId = sourceId;
        TokIdx = tokIdx;
    }
}

public readonly struct TypeRefContext
{
    public readonly int PrefixNsId;
    public readonly int UnitKind;
    public readonly int CurrentNsId;
    public readonly int EnclosingOwnerDeclId;
    public readonly int UsingExtraStart;
    public readonly int UsingCount;

    public TypeRefContext(
        int prefixNsId,
        int unitKind,
        int currentNsId,
        int enclosingOwnerDeclId,
        int usingExtraStart,
        int usingCount)
    {
        PrefixNsId = prefixNsId;
        UnitKind = unitKind;
        CurrentNsId = currentNsId;
        EnclosingOwnerDeclId = enclosingOwnerDeclId;
        UsingExtraStart = usingExtraStart;
        UsingCount = usingCount;
    }
}

public static class UsingKinds
{
    public const int Namespace = 1;
    public const int Alias = 2;
    public const int Static = 3;
}

public readonly struct UsingEntry
{
    public readonly int Kind;
    public readonly int UnitKind;
    public readonly int ScopeLevel;
    public readonly int EnclosingNsId;
    public readonly int AliasOrNsId;
    public readonly int TargetNode;
    public readonly int SourceId;
    public readonly int TokIdx;

    public UsingEntry(
        int kind,
        int unitKind,
        int scopeLevel,
        int enclosingNsId,
        int aliasOrNsId,
        int targetNode,
        int sourceId,
        int tokIdx)
    {
        Kind = kind;
        UnitKind = unitKind;
        ScopeLevel = scopeLevel;
        EnclosingNsId = enclosingNsId;
        AliasOrNsId = aliasOrNsId;
        TargetNode = targetNode;
        SourceId = sourceId;
        TokIdx = tokIdx;
    }
}

public struct AstArena
{
    public Vec<AstNode> Nodes;
    public Vec<int> Extra;
    public Vec<TypeRefContext> TypeRefs;
    public Vec<TypeDecl> Types;
    public Vec<FieldDecl> Fields;
    public Vec<FuncDecl> Funcs;
    public Vec<ParamDecl> Params;
    public Vec<UsingEntry> Usings;
    public Vec<int> FrameworkAttributeUses;
    public Vec<int> DeclaredNamespaces;
    public Vec<int> TopStmts;
    public Vec<int> TopLocalFuncs;
    public Vec<int> SourceUnitKinds;
    public Vec<int> SourceUsingStarts;
    public Vec<int> SourceUsingCounts;
    public int TopSourceId;
    public int TopTokIdx;

    public AstArena(int capacity)
    {
        Nodes = new Vec<AstNode>(capacity);
        Extra = new Vec<int>(capacity);
        TypeRefs = new Vec<TypeRefContext>(256);
        Types = new Vec<TypeDecl>(64);
        Fields = new Vec<FieldDecl>(256);
        Funcs = new Vec<FuncDecl>(256);
        Params = new Vec<ParamDecl>(512);
        Usings = new Vec<UsingEntry>(64);
        FrameworkAttributeUses = new Vec<int>(32);
        DeclaredNamespaces = new Vec<int>(32);
        TopStmts = new Vec<int>();
        TopLocalFuncs = new Vec<int>();
        SourceUnitKinds = new Vec<int>(32);
        SourceUsingStarts = new Vec<int>(32);
        SourceUsingCounts = new Vec<int>(32);
        TopSourceId = -1;
        TopTokIdx = 0;
    }

    public int AddNode(AstKind kind, int tokIdx, int a, int b, int c, int d)
    {
        int id = Nodes.Count;
        Nodes.Push(new AstNode(kind, tokIdx, a, b, c, d));
        return id;
    }

    public int AddTypeRef(TypeRefContext ctx)
    {
        int id = TypeRefs.Count;
        TypeRefs.Push(ctx);
        return id;
    }

    public int PushExtraSpan(ReadOnlySpan<int> items)
    {
        int start = Extra.Count;
        Extra.Extend(items);
        return start;
    }
}
