using System.Diagnostics.CodeAnalysis;

namespace Vibranium.Self;

public static class TypeIds
{
    public const int Void = 0;
    public const int Bool = 1;
    public const int I8 = 2;
    public const int U8 = 3;
    public const int I16 = 4;
    public const int U16 = 5;
    public const int Char = 6;
    public const int I32 = 7;
    public const int U32 = 8;
    public const int I64 = 9;
    public const int U64 = 10;
    public const int NInt = 11;
    public const int NUInt = 12;
    public const int F32 = 13;
    public const int F64 = 14;
    public const int Str = 15;
    public const int NoneUnit = 16;
    public const int Error = 17;
    public const int Utf8Lines = 18;
    public const int Utf8ArgEnum = 19;
}

public static class TypeKinds
{
    public const int Prim = 0;
    public const int Enum = 1;
    public const int Class = 2;
    public const int Struct = 3;
    public const int Option = 4;
    public const int OptionRef = 5;
    public const int OptionMut = 6;
    public const int SomeWrap = 7;
    public const int Result = 8;
    public const int OkWrap = 9;
    public const int ErrWrap = 10;
    public const int Vec = 11;
    public const int Span = 12;
    public const int ReadOnlySpan = 13;
    public const int Ref = 14;
    public const int RefReadOnly = 15;
    public const int TypeParam = 16;
    public const int Utf8Lines = 17;
    public const int Utf8ArgEnum = 18;
}

public readonly struct ConcreteField
{
    public readonly int NameId;
    public readonly int TypeId;
    public readonly int Flags;
    public readonly int CapturedParamNameId;

    public ConcreteField(int nameId, int typeId, int flags, int capturedParamNameId)
    {
        NameId = nameId;
        TypeId = typeId;
        Flags = flags;
        CapturedParamNameId = capturedParamNameId;
    }
}

public struct TypeInfo
{
    public int Kind;
    public int A;
    public int B;
    public int C;
    public int FieldStart;
    public int FieldCount;
    public int DisposeFuncInstId;

    public TypeInfo(int kind, int a, int b, int c)
    {
        Kind = kind;
        A = a;
        B = b;
        C = c;
        FieldStart = -1;
        FieldCount = 0;
        DisposeFuncInstId = -1;
    }
}

public struct TypeTable
{
    public Vec<TypeInfo> Items;
    public Vec<int> TypeArgs;
    public Vec<ConcreteField> Fields;
    public Vec<int> ConcreteUserTypes;
    private StrMap<int> _byKey;

    public TypeTable(int capacity)
    {
        Items = new Vec<TypeInfo>(capacity);
        TypeArgs = new Vec<int>(128);
        Fields = new Vec<ConcreteField>(256);
        ConcreteUserTypes = new Vec<int>(64);
        _byKey = new StrMap<int>(capacity);

        RegisterPrim(ref Items, ref _byKey, TypeIds.Void, "Void"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.Bool, "Bool"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.I8, "I8"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.U8, "U8"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.I16, "I16"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.U16, "U16"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.Char, "Char"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.I32, "I32"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.U32, "U32"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.I64, "I64"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.U64, "U64"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.NInt, "NInt"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.NUInt, "NUInt"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.F32, "F32"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.F64, "F64"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.Str, "Str"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.NoneUnit, "None"u8);
        RegisterPrim(ref Items, ref _byKey, TypeIds.Error, "Err"u8);
        RegisterFixedKind(ref Items, ref _byKey, TypeIds.Utf8Lines, TypeKinds.Utf8Lines, "Utf8Lines"u8);
        RegisterFixedKind(ref Items, ref _byKey, TypeIds.Utf8ArgEnum, TypeKinds.Utf8ArgEnum, "Utf8ArgEnum"u8);
    }

    private static void RegisterPrim(ref Vec<TypeInfo> items, ref StrMap<int> byKey, int expectedId, ReadOnlySpan<byte> key)
    {
        int id = items.Count;
        if (id != expectedId)
        {
            VibraniumRuntime.Panic("TypeTable primitive init mismatch");
        }
        items.Push(new TypeInfo(TypeKinds.Prim, id, 0, 0));
        byKey.Set(key, id);
    }

    private static void RegisterFixedKind(ref Vec<TypeInfo> items, ref StrMap<int> byKey, int expectedId, int kind, ReadOnlySpan<byte> key)
    {
        int id = items.Count;
        if (id != expectedId)
        {
            VibraniumRuntime.Panic("TypeTable fixed-kind init mismatch");
        }
        items.Push(new TypeInfo(kind, id, 0, 0));
        byKey.Set(key, id);
    }

    [UnscopedRef]
    public readonly ReadOnlySpan<byte> GetKey(int typeId) => _byKey.KeyAt(typeId);

    private int InternType(ReadOnlySpan<byte> key, TypeInfo info)
    {
        int existing = _byKey.Find(key);
        if (existing >= 0)
        {
            return existing;
        }
        int id = Items.Count;
        Items.Push(info);
        _byKey.Set(key, id);
        if (info.Kind == TypeKinds.Class || info.Kind == TypeKinds.Struct)
        {
            ConcreteUserTypes.Push(id);
        }
        return id;
    }

    public int MakeUnaryType(int kind, ReadOnlySpan<byte> prefix, int innerTypeId)
    {
        var kb = new StringBuf(48);
        kb.Append(prefix);
        kb.AppendAscii((byte)'<');
        kb.Append(GetKey(innerTypeId));
        kb.AppendAscii((byte)'>');
        return InternType(kb.AsUtf8(), new TypeInfo(kind, innerTypeId, 0, 0));
    }

    public int MakeOption(int inner) => MakeUnaryType(TypeKinds.Option, "Opt"u8, inner);
    public int MakeOptionRef(int inner) => MakeUnaryType(TypeKinds.OptionRef, "OptRef"u8, inner);
    public int MakeOptionMut(int inner) => MakeUnaryType(TypeKinds.OptionMut, "OptMut"u8, inner);
    public int MakeSomeWrap(int inner) => MakeUnaryType(TypeKinds.SomeWrap, "Some"u8, inner);
    public int MakeOkWrap(int ok) => MakeUnaryType(TypeKinds.OkWrap, "Ok"u8, ok);
    public int MakeErrWrap(int err) => MakeUnaryType(TypeKinds.ErrWrap, "ErrW"u8, err);
    public int MakeVec(int elem) => MakeUnaryType(TypeKinds.Vec, "Vec"u8, elem);
    public int MakeSpan(int elem, bool isReadOnly) =>
        MakeUnaryType(isReadOnly ? TypeKinds.ReadOnlySpan : TypeKinds.Span, isReadOnly ? "RoSpan"u8 : "Span"u8, elem);
    public int MakeRef(int target, bool isMut) =>
        MakeUnaryType(isMut ? TypeKinds.Ref : TypeKinds.RefReadOnly, isMut ? "RefMut"u8 : "RefIn"u8, target);

    public int MakeResult(int okTypeId, int errTypeId)
    {
        var kb = new StringBuf(64);
        kb.Append("Res<"u8);
        kb.Append(GetKey(okTypeId));
        kb.AppendAscii((byte)',');
        kb.Append(GetKey(errTypeId));
        kb.AppendAscii((byte)'>');
        return InternType(kb.AsUtf8(), new TypeInfo(TypeKinds.Result, okTypeId, errTypeId, 0));
    }

    public int MakeEnum(int declId, ReadOnlySpan<byte> typeName)
    {
        var kb = new StringBuf(48);
        kb.Append("Enum:"u8);
        kb.AppendInt(declId);
        kb.AppendAscii((byte)':');
        kb.Append(typeName);
        return InternType(kb.AsUtf8(), new TypeInfo(TypeKinds.Enum, declId, 0, 0));
    }

    public int MakeTypeParam(int nameId, ReadOnlySpan<byte> nameText, bool isUnmanaged) =>
        MakeTypeParam(nameId, nameText, isUnmanaged ? DeclFlags.UnmanagedConstraint : 0);

    public int MakeTypeParam(int nameId, ReadOnlySpan<byte> nameText, int constraintFlags)
    {
        var kb = new StringBuf(48);
        kb.Append("TP:"u8);
        kb.AppendInt(constraintFlags);
        kb.AppendAscii((byte)':');
        kb.Append(nameText);
        bool isUnmanaged = (constraintFlags & DeclFlags.UnmanagedConstraint) != 0;
        return InternType(kb.AsUtf8(), new TypeInfo(TypeKinds.TypeParam, isUnmanaged ? 1 : 0, nameId, constraintFlags));
    }

    // CLR unmanaged is stricter than Vibranium Copy: strings and owned buffers
    // may have native value representations, but contain managed references on CLR.
    public readonly bool IsUnmanaged(int typeId)
    {
        if (typeId == TypeIds.Error) return true;
        if (typeId < 0 || typeId >= Items.Count) return false;
        TypeInfo root = Items[typeId];
        if (root.Kind == TypeKinds.Prim) return typeId != TypeIds.Str && typeId != TypeIds.Void;
        if (root.Kind == TypeKinds.Enum) return true;
        if (root.Kind == TypeKinds.TypeParam) return (root.C & DeclFlags.UnmanagedConstraint) != 0;
        var seen = new Vec<byte>(Items.Count);
        for (int i = 0; i < Items.Count; i++) seen.Push(0);
        var pending = new Vec<int>(8);
        pending.Push(typeId);
        while (pending.Count > 0)
        {
            int current = pending.Pop().Unwrap();
            if (current == TypeIds.Error) continue;
            if (current < 0 || current >= Items.Count) return false;
            if (seen[current] != 0) continue;
            seen[current] = 1;
            TypeInfo ti = Items[current];
            if (ti.Kind == TypeKinds.Prim)
            {
                if (current == TypeIds.Str || current == TypeIds.Void) return false;
            }
            else if (ti.Kind == TypeKinds.TypeParam)
            {
                if ((ti.C & DeclFlags.UnmanagedConstraint) == 0) return false;
            }
            else if (ti.Kind == TypeKinds.Struct)
            {
                if (ti.FieldStart < 0) return false;
                for (int i = 0; i < ti.FieldCount; i++) pending.Push(Fields[ti.FieldStart + i].TypeId);
            }
            else if (ti.Kind == TypeKinds.Option || ti.Kind == TypeKinds.SomeWrap ||
                ti.Kind == TypeKinds.OkWrap || ti.Kind == TypeKinds.ErrWrap)
            {
                pending.Push(ti.A);
            }
            else if (ti.Kind == TypeKinds.Result)
            {
                pending.Push(ti.A);
                pending.Push(ti.B);
            }
            else if (ti.Kind != TypeKinds.Enum)
            {
                return false;
            }
        }
        return true;
    }

    public readonly bool SatisfiesTypeConstraint(int typeId, int flags)
    {
        if (typeId == TypeIds.Error) return true;
        if (typeId < 0 || typeId >= Items.Count) return false;
        TypeInfo ti = Items[typeId];
        if ((flags & DeclFlags.UnmanagedConstraint) != 0 && !IsUnmanaged(typeId)) return false;
        if ((flags & DeclFlags.ClassConstraint) != 0)
        {
            if (typeId != TypeIds.Str && ti.Kind != TypeKinds.Class &&
                !(ti.Kind == TypeKinds.TypeParam && (ti.C & DeclFlags.ClassConstraint) != 0)) return false;
        }
        if ((flags & DeclFlags.StructConstraint) != 0)
        {
            if (ti.Kind == TypeKinds.TypeParam)
            {
                if ((ti.C & (DeclFlags.StructConstraint | DeclFlags.UnmanagedConstraint)) == 0) return false;
            }
            else if (typeId == TypeIds.Str || typeId == TypeIds.Void || ti.Kind == TypeKinds.Class || IsView(typeId))
            {
                return false;
            }
        }
        return true;
    }

    public int MakeUserType(int kind, int declId, ReadOnlySpan<byte> typeName, ReadOnlySpan<int> args)
    {
        var kb = new StringBuf(64);
        kb.Append(kind == TypeKinds.Class ? "Class:"u8 : "Struct:"u8);
        kb.AppendInt(declId);
        kb.AppendAscii((byte)':');
        kb.Append(typeName);
        if (args.Length > 0)
        {
            kb.AppendAscii((byte)'<');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) kb.AppendAscii((byte)',');
                kb.Append(GetKey(args[i]));
            }
            kb.AppendAscii((byte)'>');
        }

        int existing = _byKey.Find(kb.AsUtf8());
        if (existing >= 0)
        {
            return existing;
        }

        int argStart = TypeArgs.Count;
        TypeArgs.Extend(args);
        return InternType(kb.AsUtf8(), new TypeInfo(kind, declId, argStart, args.Length));
    }

    public readonly bool ContainsTypeParam(int typeId)
    {
        TypeInfo ti = Items[typeId];
        switch (ti.Kind)
        {
            case TypeKinds.TypeParam:
                return true;
            case TypeKinds.Option:
            case TypeKinds.OptionRef:
            case TypeKinds.OptionMut:
            case TypeKinds.SomeWrap:
            case TypeKinds.OkWrap:
            case TypeKinds.ErrWrap:
            case TypeKinds.Vec:
            case TypeKinds.Span:
            case TypeKinds.ReadOnlySpan:
            case TypeKinds.Ref:
            case TypeKinds.RefReadOnly:
                return ContainsTypeParam(ti.A);
            case TypeKinds.Result:
                return ContainsTypeParam(ti.A) || ContainsTypeParam(ti.B);
            case TypeKinds.Class:
            case TypeKinds.Struct:
                for (int i = 0; i < ti.C; i++)
                {
                    if (ContainsTypeParam(TypeArgs[ti.B + i])) return true;
                }
                return false;
            default:
                return false;
        }
    }

    public readonly int GetNestingDepth(int typeId)
    {
        TypeInfo ti = Items[typeId];
        switch (ti.Kind)
        {
            case TypeKinds.Option:
            case TypeKinds.OptionRef:
            case TypeKinds.OptionMut:
            case TypeKinds.SomeWrap:
            case TypeKinds.OkWrap:
            case TypeKinds.ErrWrap:
            case TypeKinds.Vec:
            case TypeKinds.Span:
            case TypeKinds.ReadOnlySpan:
                return 1 + GetNestingDepth(ti.A);
            case TypeKinds.Result:
            {
                int d1 = GetNestingDepth(ti.A);
                int d2 = GetNestingDepth(ti.B);
                return 1 + (d1 > d2 ? d1 : d2);
            }
            case TypeKinds.Class:
            case TypeKinds.Struct:
            {
                if (ti.C == 0) return 0;
                int max = 0;
                for (int i = 0; i < ti.C; i++)
                {
                    int d = GetNestingDepth(TypeArgs[ti.B + i]);
                    if (d > max) max = d;
                }
                return 1 + max;
            }
            default:
                return 0;
        }
    }

    public readonly bool IsInteger(int typeId)
    {
        if (typeId >= TypeIds.I8 && typeId <= TypeIds.NUInt) return true;
        return Items[typeId].Kind == TypeKinds.Enum;
    }

    public readonly bool IsSignedInteger(int typeId)
    {
        if (typeId == TypeIds.I8 || typeId == TypeIds.I16 || typeId == TypeIds.I32 ||
            typeId == TypeIds.I64 || typeId == TypeIds.NInt)
        {
            return true;
        }
        return Items[typeId].Kind == TypeKinds.Enum;
    }

    public readonly bool IsUnsignedInteger(int typeId) =>
        typeId == TypeIds.U8 || typeId == TypeIds.U16 || typeId == TypeIds.Char ||
        typeId == TypeIds.U32 || typeId == TypeIds.U64 || typeId == TypeIds.NUInt;

    public readonly bool IsFloat(int typeId) =>
        typeId == TypeIds.F32 || typeId == TypeIds.F64;

    public readonly int IntBitWidth(int typeId)
    {
        if (typeId == TypeIds.I8 || typeId == TypeIds.U8) return 8;
        if (typeId == TypeIds.I16 || typeId == TypeIds.U16 || typeId == TypeIds.Char) return 16;
        if (typeId == TypeIds.I32 || typeId == TypeIds.U32 || Items[typeId].Kind == TypeKinds.Enum) return 32;
        if (typeId == TypeIds.I64 || typeId == TypeIds.U64 || typeId == TypeIds.NInt || typeId == TypeIds.NUInt) return 64;
        return 0;
    }

    public readonly bool IsView(int typeId)
    {
        int k = Items[typeId].Kind;
        return k == TypeKinds.Span || k == TypeKinds.ReadOnlySpan ||
               k == TypeKinds.OptionRef || k == TypeKinds.OptionMut ||
               k == TypeKinds.Utf8Lines || k == TypeKinds.Utf8ArgEnum ||
               k == TypeKinds.Ref || k == TypeKinds.RefReadOnly;
    }

    public readonly bool IsExclusiveView(int typeId)
    {
        int k = Items[typeId].Kind;
        return k == TypeKinds.Span || k == TypeKinds.OptionMut || k == TypeKinds.Ref;
    }

    public readonly bool IsCopy(int typeId, in AstArena ast)
    {
        var visited = new Vec<int>();
        return IsCopyInner(typeId, in ast, ref visited);
    }

    private readonly bool IsCopyInner(int typeId, in AstArena ast, ref Vec<int> visited)
    {
        TypeInfo ti = Items[typeId];
        switch (ti.Kind)
        {
            case TypeKinds.Prim:
            case TypeKinds.Enum:
            case TypeKinds.OptionRef:
            case TypeKinds.ReadOnlySpan:
            case TypeKinds.Utf8Lines:
            case TypeKinds.Utf8ArgEnum:
            case TypeKinds.RefReadOnly:
                return true;
            case TypeKinds.TypeParam:
                return ti.A == 1; // unmanaged constraint
            case TypeKinds.Class:
            case TypeKinds.Vec:
            case TypeKinds.OptionMut:
            case TypeKinds.Span:
            case TypeKinds.Ref:
                return false;
            case TypeKinds.Option:
            case TypeKinds.SomeWrap:
            case TypeKinds.OkWrap:
            case TypeKinds.ErrWrap:
                return IsCopyInner(ti.A, in ast, ref visited);
            case TypeKinds.Result:
                return IsCopyInner(ti.A, in ast, ref visited) && IsCopyInner(ti.B, in ast, ref visited);
            case TypeKinds.Struct:
            {
                if ((ast.Types[ti.A].Flags & DeclFlags.IsDisposable) != 0) return false;
                if (ContainsInt(in visited, typeId)) return false;
                visited.Push(typeId);
                for (int i = 0; i < ti.FieldCount; i++)
                {
                    if (!IsCopyInner(Fields[ti.FieldStart + i].TypeId, in ast, ref visited))
                    {
                        _ = visited.Pop();
                        return false;
                    }
                }
                _ = visited.Pop();
                return true;
            }
            default:
                return false;
        }
    }

    public readonly bool IsZeroSafe(int typeId, in AstArena ast)
    {
        var visited = new Vec<int>();
        return IsZeroSafeInner(typeId, in ast, ref visited);
    }

    private readonly bool IsZeroSafeInner(int typeId, in AstArena ast, ref Vec<int> visited)
    {
        if (typeId == TypeIds.Str) return false;
        TypeInfo ti = Items[typeId];
        switch (ti.Kind)
        {
            case TypeKinds.Prim:
            case TypeKinds.Enum:
            case TypeKinds.Option:
            case TypeKinds.Span:
            case TypeKinds.ReadOnlySpan:
                return true;
            case TypeKinds.TypeParam:
                return false;
            case TypeKinds.Class:
            case TypeKinds.Result:
            case TypeKinds.SomeWrap:
            case TypeKinds.OkWrap:
            case TypeKinds.ErrWrap:
            case TypeKinds.OptionRef:
            case TypeKinds.OptionMut:
            case TypeKinds.Utf8Lines:
            case TypeKinds.Utf8ArgEnum:
            case TypeKinds.Vec:
            case TypeKinds.Ref:
            case TypeKinds.RefReadOnly:
                return false;
            case TypeKinds.Struct:
            {
                if ((ast.Types[ti.A].Flags & DeclFlags.IsDisposable) != 0) return false;
                if (ContainsInt(in visited, typeId)) return false;
                visited.Push(typeId);
                for (int i = 0; i < ti.FieldCount; i++)
                {
                    if (!IsZeroSafeInner(Fields[ti.FieldStart + i].TypeId, in ast, ref visited))
                    {
                        _ = visited.Pop();
                        return false;
                    }
                }
                _ = visited.Pop();
                return true;
            }
            default:
                return false;
        }
    }

    public readonly bool IsLinear(int typeId, in AstArena ast)
    {
        var visited = new Vec<int>();
        return IsLinearInner(typeId, in ast, ref visited);
    }

    private readonly bool IsLinearInner(int typeId, in AstArena ast, ref Vec<int> visited)
    {
        TypeInfo ti = Items[typeId];
        switch (ti.Kind)
        {
            case TypeKinds.Class:
                return (ast.Types[ti.A].Flags & DeclFlags.IsDisposable) != 0;
            case TypeKinds.Option:
            case TypeKinds.SomeWrap:
            case TypeKinds.OkWrap:
            case TypeKinds.ErrWrap:
                return IsLinearInner(ti.A, in ast, ref visited);
            case TypeKinds.Result:
                return IsLinearInner(ti.A, in ast, ref visited) || IsLinearInner(ti.B, in ast, ref visited);
            case TypeKinds.Struct:
            {
                if ((ast.Types[ti.A].Flags & DeclFlags.IsDisposable) != 0) return true;
                if (ContainsInt(in visited, typeId)) return false;
                visited.Push(typeId);
                for (int i = 0; i < ti.FieldCount; i++)
                {
                    if (IsLinearInner(Fields[ti.FieldStart + i].TypeId, in ast, ref visited))
                    {
                        _ = visited.Pop();
                        return true;
                    }
                }
                _ = visited.Pop();
                return false;
            }
            default:
                return false;
        }
    }

    public readonly bool NeedsMemoryDrop(int typeId, in AstArena ast) =>
        !IsCopy(typeId, in ast) && !IsLinear(typeId, in ast) && !IsView(typeId);

    private static bool ContainsInt(in Vec<int> list, int val)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == val) return true;
        }
        return false;
    }
}
