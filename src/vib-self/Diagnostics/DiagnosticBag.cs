using System.Diagnostics.CodeAnalysis;

namespace Vibranium.Self;

public static class VibCodes
{
    public const int Unsupported = 0;            // VIB000
    public const int UseAfterMove = 1;           // VIB001
    public const int OverlappingCallBorrows = 2; // VIB002
    public const int ConflictActiveBorrow = 3;   // VIB003
    public const int MutateSharedBorrow = 4;     // VIB004
    public const int IllegalFieldMove = 5;       // VIB005
    public const int LinearViolation = 6;        // VIB006
    public const int EscapingBorrow = 7;         // VIB007
    public const int NullOrZeroUnsafe = 8;       // VIB008

    public static ReadOnlySpan<byte> Text(int code)
    {
        switch (code)
        {
            case 0: return "VIB000"u8;
            case 1: return "VIB001"u8;
            case 2: return "VIB002"u8;
            case 3: return "VIB003"u8;
            case 4: return "VIB004"u8;
            case 5: return "VIB005"u8;
            case 6: return "VIB006"u8;
            case 7: return "VIB007"u8;
            case 8: return "VIB008"u8;
            default: return "VIB009"u8;
        }
    }
}

public struct SourceFile
{
    public ByteSlice DisplayPath;
    public ByteSlice Content;
    public int UnitKind;
    public int FirstTokIdx;
    public int TokCount;

    public SourceFile(ByteSlice displayPath, ByteSlice content, int unitKind)
    {
        DisplayPath = displayPath;
        Content = content;
        UnitKind = unitKind;
        FirstTokIdx = 0;
        TokCount = 0;
    }
}

public struct SourceTable
{
    public Vec<SourceFile> Files;
    public BytePool Pool;

    public SourceTable(int capacity)
    {
        Files = new Vec<SourceFile>(capacity);
        Pool = new BytePool(16384);
    }

    public int AddFile(ReadOnlySpan<byte> displayPath, ReadOnlySpan<byte> content, int unitKind)
    {
        ByteSlice p = Pool.Add(displayPath);
        ByteSlice c = Pool.Add(content);
        int id = Files.Count;
        Files.Push(new SourceFile(p, c, unitKind));
        return id;
    }

    [UnscopedRef]
    public readonly ReadOnlySpan<byte> GetPath(int sourceId) =>
        Pool.Get(Files[sourceId].DisplayPath);

    [UnscopedRef]
    public readonly ReadOnlySpan<byte> GetContent(int sourceId) =>
        Pool.Get(Files[sourceId].Content);

    public readonly void GetLineCol(int sourceId, int byteOffset, out int line, out int col)
    {
        if (sourceId < 0 || sourceId >= Files.Count)
        {
            line = 1;
            col = 1;
            return;
        }
        ReadOnlySpan<byte> text = GetContent(sourceId);
        int limit = byteOffset;
        if (limit < 0) limit = 0;
        if (limit > text.Length) limit = text.Length;

        int ln = 1;
        int lineStart = 0;
        if (text.Length >= 3 && text[0] == 0xEF && text[1] == 0xBB && text[2] == 0xBF)
        {
            lineStart = 3;
        }

        int i = lineStart;
        while (i < limit)
        {
            if (text[i] == (byte)'\n')
            {
                ln += 1;
                lineStart = i + 1;
            }
            i += 1;
        }

        int cl = 1;
        int p = lineStart;
        while (p < limit)
        {
            byte b = text[p];
            if (b == (byte)'\r')
            {
                p += 1;
                continue;
            }
            if (b < 0x80)
            {
                cl += 1;
                p += 1;
            }
            else if ((b & 0xE0) == 0xC0)
            {
                cl += 1;
                p += 2;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                cl += 1;
                p += 3;
            }
            else
            {
                cl += 2;
                p += 4;
            }
        }

        line = ln;
        col = cl;
    }
}

public readonly struct DiagEntry
{
    public readonly int Code;
    public readonly int SourceId;
    public readonly int ByteOffset;
    public readonly int Length;
    public readonly ByteSlice Message;

    public DiagEntry(int code, int sourceId, int byteOffset, int length, ByteSlice message)
    {
        Code = code;
        SourceId = sourceId;
        ByteOffset = byteOffset;
        Length = length;
        Message = message;
    }
}

public struct DiagnosticBag
{
    public Vec<DiagEntry> Items;
    public BytePool MsgPool;

    public DiagnosticBag(int capacity)
    {
        Items = new Vec<DiagEntry>(capacity);
        MsgPool = new BytePool(1024);
    }

    public readonly bool HasErrors => Items.Count > 0;

    public void Report(int code, int sourceId, int byteOffset, int length, ReadOnlySpan<byte> message)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            DiagEntry e = Items[i];
            if (e.Code == code && e.SourceId == sourceId && e.ByteOffset == byteOffset && e.Length == length &&
                (Bytes.Eq(MsgPool.Get(e.Message), message) ||
                 (!SubsetCuts.IsMessage(MsgPool.Get(e.Message)) && !SubsetCuts.IsMessage(message))))
            {
                return;
            }
        }
        ByteSlice msgSlice = MsgPool.Add(message);
        Items.Push(new DiagEntry(code, sourceId, byteOffset, length, msgSlice));
    }

    public void ReportTok(int code, Token tok, ReadOnlySpan<byte> message)
    {
        Report(code, tok.SourceId, tok.Start, tok.Length, message);
    }

    public void Sort()
    {
        int n = Items.Count;
        for (int i = 1; i < n; i++)
        {
            DiagEntry key = Items[i];
            int j = i - 1;
            while (j >= 0 && Compare(Items[j], key) > 0)
            {
                Items[j + 1] = Items[j];
                j -= 1;
            }
            Items[j + 1] = key;
        }
    }

    private static int Compare(DiagEntry a, DiagEntry b)
    {
        if (a.SourceId != b.SourceId) return a.SourceId < b.SourceId ? -1 : 1;
        if (a.ByteOffset != b.ByteOffset) return a.ByteOffset < b.ByteOffset ? -1 : 1;
        if (a.Length != b.Length) return a.Length < b.Length ? -1 : 1;
        if (a.Code != b.Code) return a.Code < b.Code ? -1 : 1;
        return 0;
    }

    public void FormatAll(in SourceTable sources, ref StringBuf outBuf)
    {
        Sort();
        for (int i = 0; i < Items.Count; i++)
        {
            DiagEntry d = Items[i];
            if (d.SourceId >= 0 && d.SourceId < sources.Files.Count)
            {
                outBuf.Append(sources.GetPath(d.SourceId));
                sources.GetLineCol(d.SourceId, d.ByteOffset, out int line, out int col);
                outBuf.AppendAscii((byte)'(');
                outBuf.AppendInt(line);
                outBuf.AppendAscii((byte)',');
                outBuf.AppendInt(col);
                outBuf.Append("): error "u8);
            }
            else
            {
                outBuf.Append("vib: error "u8);
            }
            outBuf.Append(VibCodes.Text(d.Code));
            outBuf.Append(": "u8);
            outBuf.Append(MsgPool.Get(d.Message));
            outBuf.AppendLine();
        }
    }
}
