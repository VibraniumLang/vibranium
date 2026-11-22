namespace Vibranium.Self;

public static class Lexer
{
    public static void LexAll(
        ref SourceTable sources,
        ref Interner names,
        ref TokenArena arena,
        ref DiagnosticBag diags)
    {
        for (int s = 0; s < sources.Files.Count; s++)
        {
            SourceFile sf = sources.Files[s];
            sf.FirstTokIdx = arena.Tokens.Count;
            LexFile(s, sources.GetContent(s), ref names, ref arena, ref diags);
            sf.TokCount = arena.Tokens.Count - sf.FirstTokIdx;
            sources.Files[s] = sf;
        }
    }

    public static void LexFile(
        int sourceId,
        ReadOnlySpan<byte> src,
        ref Interner names,
        ref TokenArena arena,
        ref DiagnosticBag diags)
    {
        if (!Utf8.IsValid(src))
        {
            diags.Report(VibCodes.Unsupported, sourceId, 0, 1, "Source file is not valid UTF-8."u8);
            arena.Tokens.Push(new Token(TokKind.Eof, sourceId, 0, 0, -1, 0));
            return;
        }

        int n = src.Length;
        bool rejectedConditionalFile = false;
        int i = 0;
        if (n >= 3 && src[0] == 0xEF && src[1] == 0xBB && src[2] == 0xBF)
        {
            i = 3;
        }

        var scratch = new Vec<byte>(128);
        var defines = new StrMap<int>(8);
        var condStack = new Vec<int>(8);
        var condStarts = new Vec<int>(8);
        bool atLineStart = true;
        bool seenRealToken = false;

        while (i < n)
        {
            byte c = src[i];

            // Line terminators (C# §6.3.1: CR, LF, CRLF, NEL U+0085, LS U+2028, PS U+2029)
            if (TryMatchLineTerminator(src, i, n, out int nlStep))
            {
                i += nlStep;
                atLineStart = true;
                continue;
            }

            // Inline whitespace (ASCII space/tab/vtab/ff/sub + Unicode Zs + U+FEFF BOM)
            if (TryMatchInlineWhitespace(src, i, n, out int wsStep))
            {
                i += wsStep;
                continue;
            }

            // Comments
            if (c == (byte)'/' && i + 1 < n)
            {
                if (src[i + 1] == (byte)'/')
                {
                    i += 2;
                    while (i < n && !TryMatchLineTerminator(src, i, n, out _))
                    {
                        i += 1;
                    }
                    continue;
                }
                if (src[i + 1] == (byte)'*')
                {
                    int cStart = i;
                    i += 2;
                    bool closed = false;
                    while (i + 1 < n)
                    {
                        if (src[i] == (byte)'*' && src[i + 1] == (byte)'/')
                        {
                            i += 2;
                            closed = true;
                            break;
                        }
                        if (TryMatchLineTerminator(src, i, n, out int blkNlStep))
                        {
                            atLineStart = true;
                            i += blkNlStep;
                            continue;
                        }
                        i += 1;
                    }
                    if (!closed)
                    {
                        diags.Report(VibCodes.Unsupported, sourceId, cStart, n - cStart, "Unterminated block comment."u8);
                        i = n;
                    }
                    continue;
                }
            }

            // Preprocessor directive (#if, #elif, #else, #endif, #define, #undef, #error, #pragma, #nullable, ...)
            if (c == (byte)'#')
            {
                if (!atLineStart)
                {
                    int badStart = i;
                    while (i < n && !TryMatchLineTerminator(src, i, n, out _)) i += 1;
                    diags.Report(VibCodes.Unsupported, sourceId, badStart, i - badStart, "Preprocessor directives must appear as the first non-whitespace character on a line."u8);
                    continue;
                }
                i = HandleActivePreprocessorDirective(src, sourceId, i, seenRealToken, ref scratch, ref defines, ref condStack, ref condStarts, ref rejectedConditionalFile, ref diags);
                // Continue lexing the active C# branch after C5 so other cuts
                // in that branch remain visible to the per-file cut prepass.
                atLineStart = true;
                if (condStack.Count > 0 && (condStack[condStack.Count - 1] & 2) == 0)
                {
                    i = SkipInactivePreprocessorSection(src, sourceId, i, ref scratch, in defines, ref condStack, ref condStarts, ref diags);
                    atLineStart = true;
                }
                continue;
            }

            atLineStart = false;
            seenRealToken = true;

            // Interpolated strings ($"...", $@"...", @$"...") -> VIB000
            if (c == (byte)'$' || (c == (byte)'@' && i + 1 < n && src[i + 1] == (byte)'$'))
            {
                int start = i;
                i += 1;
                if (i < n && (src[i] == (byte)'@' || src[i] == (byte)'$'))
                {
                    i += 1;
                }
                if (i < n && src[i] == (byte)'"')
                {
                    i = SkipInterpolatedString(src, i);
                    diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "String interpolation is not supported in Vibranium."u8);
                    int litIdx = arena.AddLit(""u8);
                    arena.Tokens.Push(new Token(TokKind.StrLit, sourceId, start, i - start, litIdx, 0));
                    continue;
                }
                diags.Report(VibCodes.Unsupported, sourceId, start, 1, "Unexpected '$' character."u8);
                continue;
            }

            // Verbatim string @"..." or @ident
            if (c == (byte)'@')
            {
                int afterAt = TryLexAtToken(src, sourceId, i, ref scratch, ref names, ref arena, ref diags);
                if (afterAt > i)
                {
                    i = afterAt;
                    continue;
                }
            }

            // String or Raw String ("..." or """...""")
            if (c == (byte)'"')
            {
                i = LexStringToken(src, sourceId, i, ref scratch, ref arena, ref diags);
                continue;
            }

            // Char literal 'x'
            if (c == (byte)'\'')
            {
                i = LexCharToken(src, sourceId, i, ref arena, ref diags);
                continue;
            }

            // Numeric literal (including leading-dot float literal `.5`, `.25f`, `.5e2`)
            if ((c >= (byte)'0' && c <= (byte)'9') ||
                (c == (byte)'.' && i + 1 < n && src[i + 1] >= (byte)'0' && src[i + 1] <= (byte)'9'))
            {
                i = LexNumber(src, sourceId, i, ref scratch, ref arena, ref diags);
                continue;
            }

            // Identifier or Keyword (with C# §6.4.3 Cf stripping & \u/\U escape support)
            int afterIdent = TryLexIdentOrKeywordToken(src, sourceId, i, ref scratch, ref names, ref arena, ref diags);
            if (afterIdent > i)
            {
                i = afterIdent;
                continue;
            }

            if (c == (byte)'\\')
            {
                int badStart = i;
                int escStep = ConsumeStrayBackslashEscape(src, i, n);
                i += escStep;
                diags.Report(VibCodes.Unsupported, sourceId, badStart, escStep, "Unexpected character or escape sequence in source."u8);
                continue;
            }

            // Punctuation & Operators
            int opStart = i;
            byte c1 = i + 1 < n ? src[i + 1] : (byte)0;
            byte c2 = i + 2 < n ? src[i + 2] : (byte)0;
            byte c3 = i + 3 < n ? src[i + 3] : (byte)0;

            if (c == (byte)'>' && c1 == (byte)'>' && c2 == (byte)'>' && c3 == (byte)'=')
            {
                arena.Tokens.Push(new Token(TokKind.GtGtGtEq, sourceId, opStart, 4, -1, 0));
                i += 4;
                continue;
            }

            TokKind threeKind = MatchThreeCharOp(c, c1, c2);
            if (threeKind != TokKind.Eof)
            {
                arena.Tokens.Push(new Token(threeKind, sourceId, opStart, 3, -1, 0));
                i += 3;
                continue;
            }

            TokKind twoKind = MatchTwoCharOp(c, c1);
            // A range or leading-dot numeric literal starts the true branch of
            // a conditional: c?..end:other and c?.5:1 do not contain ?. access.
            if (twoKind == TokKind.QuestionDot &&
                (c2 == (byte)'.' || (c2 >= (byte)'0' && c2 <= (byte)'9')))
            {
                twoKind = TokKind.Eof;
            }
            if (twoKind != TokKind.Eof)
            {
                arena.Tokens.Push(new Token(twoKind, sourceId, opStart, 2, -1, 0));
                i += 2;
                continue;
            }

            TokKind oneKind = MatchOneCharOp(c);
            if (oneKind != TokKind.Eof)
            {
                arena.Tokens.Push(new Token(oneKind, sourceId, opStart, 1, -1, 0));
                i += 1;
                continue;
            }

            int badCharLen = 1;
            if (c >= 0x80 && TryDecodeUtf8ScalarAt(src, opStart, n, out _, out int decodedBadLen) && decodedBadLen > 0)
            {
                badCharLen = decodedBadLen;
            }
            diags.Report(VibCodes.Unsupported, sourceId, opStart, badCharLen, "Unexpected character in source."u8);
            i += badCharLen;
        }

        if (condStack.Count > 0)
        {
            int unclosedPos = condStarts[condStarts.Count - 1];
            diags.Report(VibCodes.Unsupported, sourceId, unclosedPos, 3, "Unterminated '#if' preprocessor directive."u8);
        }

        arena.Tokens.Push(new Token(TokKind.Eof, sourceId, n, 0, -1, 0));
    }

    private static int TryLexAtToken(
        ReadOnlySpan<byte> src,
        int sourceId,
        int start,
        ref Vec<byte> scratch,
        ref Interner names,
        ref TokenArena arena,
        ref DiagnosticBag diags)
    {
        int n = src.Length;
        if (start + 1 < n && src[start + 1] == (byte)'"')
        {
            int i = start + 2;
            scratch.Clear();
            while (i < n)
            {
                if (src[i] == (byte)'"')
                {
                    if (i + 1 < n && src[i + 1] == (byte)'"')
                    {
                        scratch.Push((byte)'"');
                        i += 2;
                        continue;
                    }
                    i += 1;
                    break;
                }
                scratch.Push(src[i]);
                i += 1;
            }
            TokKind strKind = TokKind.StrLit;
            if (i + 1 < n && (src[i] == (byte)'u' || src[i] == (byte)'U') && src[i + 1] == (byte)'8')
            {
                strKind = TokKind.Utf8Lit;
                i += 2;
            }
            int litIdx = arena.AddLit(scratch.AsReadOnlySpan());
            arena.Tokens.Push(new Token(strKind, sourceId, start, i - start, litIdx, 0));
            return i;
        }

        if (start + 1 < n && TryLexNormalizedIdent(src, start + 1, n, ref scratch, out int atConsumed, out bool atHadEscOrCf, out bool atBadEsc))
        {
            int idStart = start + 1;
            int end = idStart + atConsumed;
            if (atBadEsc)
            {
                diags.Report(VibCodes.Unsupported, sourceId, start, end - start, "Invalid Unicode escape sequence in identifier."u8);
            }
            int nameId;
            if (atHadEscOrCf)
            {
                ReadOnlySpan<byte> normWord = scratch.AsReadOnlySpan();
                if (normWord.Length == 0)
                {
                    diags.Report(VibCodes.Unsupported, sourceId, start, end - start, "Identifier cannot consist solely of Unicode format characters."u8);
                }
                nameId = names.Intern(normWord);
            }
            else
            {
                nameId = names.Intern(src.Slice(idStart, atConsumed));
            }
            arena.Tokens.Push(new Token(TokKind.Ident, sourceId, start, end - start, nameId, 0));
            return end;
        }

        if (start + 1 < n && src[start + 1] == (byte)'\\')
        {
            int escLen = ConsumeStrayBackslashEscape(src, start + 1, n);
            int end = start + 1 + escLen;
            diags.Report(VibCodes.Unsupported, sourceId, start, end - start, "Invalid verbatim identifier."u8);
            return end;
        }

        return start;
    }

    private static int LexStringToken(
        ReadOnlySpan<byte> src,
        int sourceId,
        int start,
        ref Vec<byte> scratch,
        ref TokenArena arena,
        ref DiagnosticBag diags)
    {
        int n = src.Length;
        if (start + 2 < n && src[start + 1] == (byte)'"' && src[start + 2] == (byte)'"')
        {
            int rawEnd = LexRawString(src, sourceId, start, ref scratch, ref diags);
            TokKind rawKind = TokKind.StrLit;
            if (rawEnd + 1 < n && (src[rawEnd] == (byte)'u' || src[rawEnd] == (byte)'U') && src[rawEnd + 1] == (byte)'8')
            {
                rawKind = TokKind.Utf8Lit;
                rawEnd += 2;
            }
            int litIdx = arena.AddLit(scratch.AsReadOnlySpan());
            arena.Tokens.Push(new Token(rawKind, sourceId, start, rawEnd - start, litIdx, 0));
            return rawEnd;
        }

        int i = start + 1;
        scratch.Clear();
        bool badSurrogate = false;
        bool badEscape = false;
        bool closedStr = false;
        while (i < n && src[i] != (byte)'"' && !TryMatchLineTerminator(src, i, n, out _))
        {
            if (src[i] == (byte)'\\')
            {
                i += 1;
                int scalar = ReadEscapeScalar(src, ref i, allowSurrogatePair: true, out bool surrogateErr, out bool invalidEsc);
                if (invalidEsc)
                {
                    badEscape = true;
                }
                else if (surrogateErr)
                {
                    badSurrogate = true;
                }
                else if (scalar >= 0)
                {
                    AppendScalarUtf8(scalar, ref scratch);
                }
            }
            else
            {
                scratch.Push(src[i]);
                i += 1;
            }
        }
        if (i < n && src[i] == (byte)'"')
        {
            i += 1;
            closedStr = true;
        }
        TokKind sk = TokKind.StrLit;
        if (closedStr && i + 1 < n && (src[i] == (byte)'u' || src[i] == (byte)'U') && src[i + 1] == (byte)'8')
        {
            sk = TokKind.Utf8Lit;
            i += 2;
        }
        if (!closedStr)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "Newline in constant or unterminated string literal."u8);
        }
        else if (badEscape)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "Unrecognized or invalid escape sequence."u8);
        }
        else if (badSurrogate)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "String literal contains a lone UTF-16 surrogate, which is banned in Vibranium."u8);
        }
        int sLitIdx = arena.AddLit(scratch.AsReadOnlySpan());
        arena.Tokens.Push(new Token(sk, sourceId, start, i - start, sLitIdx, 0));
        return i;
    }

    private static int LexCharToken(
        ReadOnlySpan<byte> src,
        int sourceId,
        int start,
        ref TokenArena arena,
        ref DiagnosticBag diags)
    {
        int n = src.Length;
        int i = start + 1;
        int chVal = 0;
        bool surrErr = false;
        bool escErr = false;
        bool badCharLit = false;
        if (i < n && src[i] == (byte)'\\')
        {
            i += 1;
            chVal = ReadEscapeScalar(src, ref i, allowSurrogatePair: false, out surrErr, out escErr);
        }
        else if (i < n && src[i] != (byte)'\'' && !TryMatchLineTerminator(src, i, n, out _))
        {
            chVal = DecodeOneUtf8Scalar(src, ref i);
        }
        else
        {
            badCharLit = true;
        }
        if (i < n && src[i] == (byte)'\'')
        {
            i += 1;
        }
        else
        {
            badCharLit = true;
            while (i < n && src[i] != (byte)'\'' && !TryMatchLineTerminator(src, i, n, out _))
            {
                if (src[i] == (byte)'\\' && i + 1 < n && !TryMatchLineTerminator(src, i + 1, n, out _))
                {
                    i += 2;
                    continue;
                }
                i += 1;
            }
            if (i < n && src[i] == (byte)'\'')
            {
                i += 1;
            }
        }
        if (escErr)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "Unrecognized or invalid escape sequence."u8);
            chVal = 0;
        }
        else if (surrErr || (chVal >= 0xD800 && chVal <= 0xDFFF))
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "Lone UTF-16 surrogate char literal is banned in Vibranium."u8);
            chVal = 0;
        }
        else if (badCharLit || chVal > 0xFFFF)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "Invalid character literal."u8);
            chVal = 0;
        }
        arena.Tokens.Push(new Token(TokKind.CharLit, sourceId, start, i - start, 0, chVal));
        return i;
    }

    private static int TryLexIdentOrKeywordToken(
        ReadOnlySpan<byte> src,
        int sourceId,
        int start,
        ref Vec<byte> scratch,
        ref Interner names,
        ref TokenArena arena,
        ref DiagnosticBag diags)
    {
        int n = src.Length;
        if (!TryLexNormalizedIdent(src, start, n, ref scratch, out int idConsumed, out bool idHadEscOrCf, out bool idBadEsc))
        {
            return start;
        }
        int end = start + idConsumed;
        if (idBadEsc)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, idConsumed, "Invalid Unicode escape sequence in identifier."u8);
        }
        if (!idHadEscOrCf)
        {
            ReadOnlySpan<byte> word = src.Slice(start, idConsumed);
            TokKind kw = ClassifyKeyword(word);
            if (kw != TokKind.Ident)
            {
                arena.Tokens.Push(new Token(kw, sourceId, start, idConsumed, -1, 0));
            }
            else
            {
                int nameId = names.Intern(word);
                arena.Tokens.Push(new Token(TokKind.Ident, sourceId, start, idConsumed, nameId, 0));
            }
        }
        else
        {
            ReadOnlySpan<byte> normWord = scratch.AsReadOnlySpan();
            if (normWord.Length == 0)
            {
                diags.Report(VibCodes.Unsupported, sourceId, start, idConsumed, "Identifier cannot consist solely of Unicode format characters."u8);
            }
            int nameId = names.Intern(normWord);
            arena.Tokens.Push(new Token(TokKind.Ident, sourceId, start, idConsumed, nameId, 0));
        }
        return end;
    }

    private static int LexNumber(
        ReadOnlySpan<byte> src,
        int sourceId,
        int start,
        ref Vec<byte> scratch,
        ref TokenArena arena,
        ref DiagnosticBag diags)
    {
        int n = src.Length;
        int i = start;

        // Hex literal 0x...
        if (src[i] == (byte)'0' && i + 1 < n && (src[i + 1] == (byte)'x' || src[i + 1] == (byte)'X'))
        {
            i += 2;
            long val = 0;
            bool hexOvf = false;
            while (i < n)
            {
                byte b = src[i];
                if (b == (byte)'_')
                {
                    i += 1;
                    continue;
                }
                int hd = HexDigit(b);
                if (hd < 0) break;
                if (((ulong)val >> 60) != 0UL) hexOvf = true;
                val = (val << 4) | (long)hd;
                i += 1;
            }
            TokKind kind = ConsumeIntSuffix(src, ref i, val, isHexOrBin: true, out int hexAux);
            if (hexOvf) hexAux |= 4;
            arena.Tokens.Push(new Token(kind, sourceId, start, i - start, hexAux, val));
            return i;
        }

        // Binary literal 0b...
        if (src[i] == (byte)'0' && i + 1 < n && (src[i + 1] == (byte)'b' || src[i + 1] == (byte)'B'))
        {
            i += 2;
            long val = 0;
            bool binOvf = false;
            while (i < n)
            {
                byte b = src[i];
                if (b == (byte)'_')
                {
                    i += 1;
                    continue;
                }
                if (b != (byte)'0' && b != (byte)'1') break;
                if (((ulong)val >> 63) != 0UL) binOvf = true;
                val = (val << 1) | (long)(uint)(b - (byte)'0');
                i += 1;
            }
            TokKind kind = ConsumeIntSuffix(src, ref i, val, isHexOrBin: true, out int binAux);
            if (binOvf) binAux |= 4;
            arena.Tokens.Push(new Token(kind, sourceId, start, i - start, binAux, val));
            return i;
        }

        // Decimal integer or float
        while (i < n && ((src[i] >= (byte)'0' && src[i] <= (byte)'9') || src[i] == (byte)'_'))
        {
            i += 1;
        }

        bool isFloat = false;
        if (i + 1 < n && src[i] == (byte)'.' && src[i + 1] >= (byte)'0' && src[i + 1] <= (byte)'9')
        {
            isFloat = true;
            i += 1;
            while (i < n && ((src[i] >= (byte)'0' && src[i] <= (byte)'9') || src[i] == (byte)'_'))
            {
                i += 1;
            }
        }

        if (i < n && (src[i] == (byte)'e' || src[i] == (byte)'E'))
        {
            isFloat = true;
            i += 1;
            if (i < n && (src[i] == (byte)'+' || src[i] == (byte)'-'))
            {
                i += 1;
            }
            while (i < n && ((src[i] >= (byte)'0' && src[i] <= (byte)'9') || src[i] == (byte)'_'))
            {
                i += 1;
            }
        }

        if (i < n && (src[i] == (byte)'f' || src[i] == (byte)'F' || src[i] == (byte)'d' || src[i] == (byte)'D' || src[i] == (byte)'m' || src[i] == (byte)'M'))
        {
            byte suf = src[i];
            int numEnd = i;
            i += 1;
            if (suf == (byte)'m' || suf == (byte)'M')
            {
                diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "'decimal' literals are not supported in Vibranium."u8);
                arena.Tokens.Push(new Token(TokKind.IntLit, sourceId, start, i - start, -1, 0));
                return i;
            }
            NormalizeFloatLiteral(src.Slice(start, numEnd - start), ref scratch);
            int fLitIdx = arena.AddLit(scratch.AsReadOnlySpan());
            TokKind fk = (suf == (byte)'f' || suf == (byte)'F') ? TokKind.FloatLit : TokKind.DoubleLit;
            arena.Tokens.Push(new Token(fk, sourceId, start, i - start, fLitIdx, 0));
            return i;
        }

        if (isFloat)
        {
            NormalizeFloatLiteral(src.Slice(start, i - start), ref scratch);
            int dLitIdx = arena.AddLit(scratch.AsReadOnlySpan());
            arena.Tokens.Push(new Token(TokKind.DoubleLit, sourceId, start, i - start, dLitIdx, 0));
            return i;
        }

        long decVal = 0;
        bool exceedsSigned64 = false;
        bool exceedsUInt64 = false;
        for (int p = start; p < i; p++)
        {
            byte d = src[p];
            if (d == (byte)'_') continue;
            int digit = d - (byte)'0';
            // Unsigned multiply by 10 + digit in 64-bit two's complement
            ulong uPrev = (ulong)decVal;
            if (uPrev > 1844674407370955161UL || (uPrev == 1844674407370955161UL && digit > 5))
            {
                exceedsUInt64 = true;
            }
            ulong uNext = uPrev * 10UL + (ulong)digit;
            if (uNext > 9223372036854775807UL)
            {
                exceedsSigned64 = true;
            }
            decVal = (long)uNext;
        }

        TokKind intKind = ConsumeDecimalSuffix(src, ref i, decVal, exceedsSigned64, out int decAux);
        if (exceedsUInt64) decAux |= 4;
        arena.Tokens.Push(new Token(intKind, sourceId, start, i - start, decAux, decVal));
        return i;
    }

    private static void NormalizeFloatLiteral(ReadOnlySpan<byte> raw, ref Vec<byte> outBytes)
    {
        outBytes.Clear();
        bool hasDot = false;
        int expPos = -1;
        for (int i = 0; i < raw.Length; i++)
        {
            byte b = raw[i];
            if (b == (byte)'.') hasDot = true;
            if (b == (byte)'e' || b == (byte)'E')
            {
                expPos = i;
                break;
            }
        }

        int mantEnd = expPos >= 0 ? expPos : raw.Length;
        if (mantEnd > 0 && raw[0] == (byte)'.')
        {
            outBytes.Push((byte)'0');
        }
        for (int i = 0; i < mantEnd; i++)
        {
            if (raw[i] != (byte)'_')
            {
                outBytes.Push(raw[i]);
            }
        }
        if (!hasDot)
        {
            outBytes.Push((byte)'.');
            outBytes.Push((byte)'0');
        }

        if (expPos >= 0)
        {
            outBytes.Push((byte)'e');
            int k = expPos + 1;
            if (k < raw.Length && (raw[k] == (byte)'+' || raw[k] == (byte)'-'))
            {
                outBytes.Push(raw[k]);
                k += 1;
            }
            else
            {
                outBytes.Push((byte)'+');
            }
            while (k < raw.Length)
            {
                if (raw[k] != (byte)'_')
                {
                    outBytes.Push(raw[k]);
                }
                k += 1;
            }
        }
        else
        {
            outBytes.Extend("e+00"u8);
        }
    }

    private static TokKind ConsumeIntSuffix(ReadOnlySpan<byte> src, ref int i, long val, bool isHexOrBin, out int auxFlags)
    {
        bool hasU = false;
        bool hasL = false;
        int n = src.Length;
        for (int s = 0; s < 2 && i < n; s++)
        {
            byte b = src[i];
            if (!hasU && (b == (byte)'u' || b == (byte)'U'))
            {
                hasU = true;
                i += 1;
            }
            else if (!hasL && (b == (byte)'l' || b == (byte)'L'))
            {
                hasL = true;
                i += 1;
            }
            else
            {
                break;
            }
        }

        auxFlags = (hasU ? 1 : 0) | (hasL ? 2 : 0);
        ulong u = (ulong)val;
        if (hasU && hasL) return TokKind.ULongLit;
        if (hasU) return u <= 4294967295UL ? TokKind.UIntLit : TokKind.ULongLit;
        if (hasL) return u <= 9223372036854775807UL ? TokKind.LongLit : TokKind.ULongLit;

        if (isHexOrBin)
        {
            if (u <= 2147483647UL) return TokKind.IntLit;
            if (u <= 4294967295UL) return TokKind.UIntLit;
            if (u <= 9223372036854775807UL) return TokKind.LongLit;
            return TokKind.ULongLit;
        }

        if (u <= 2147483647UL) return TokKind.IntLit;
        if (u <= 4294967295UL) return TokKind.UIntLit;
        if (u <= 9223372036854775807UL) return TokKind.LongLit;
        return TokKind.ULongLit;
    }

    private static TokKind ConsumeDecimalSuffix(ReadOnlySpan<byte> src, ref int i, long val, bool exceedsSigned64, out int auxFlags)
    {
        TokKind k = ConsumeIntSuffix(src, ref i, val, isHexOrBin: false, out auxFlags);
        if (exceedsSigned64 && (k == TokKind.IntLit || k == TokKind.UIntLit || k == TokKind.LongLit))
        {
            return TokKind.ULongLit;
        }
        return k;
    }

    private static int LexRawString(
        ReadOnlySpan<byte> src,
        int sourceId,
        int start,
        ref Vec<byte> outBytes,
        ref DiagnosticBag diags)
    {
        int n = src.Length;
        int quotes = 0;
        int i = start;
        while (i < n && src[i] == (byte)'"')
        {
            quotes += 1;
            i += 1;
        }

        bool multiLine = false;
        int wsAfterOpen = i;
        while (wsAfterOpen < n && TryMatchInlineWhitespace(src, wsAfterOpen, n, out int openWsStep))
        {
            wsAfterOpen += openWsStep;
        }
        if (wsAfterOpen < n && TryMatchLineTerminator(src, wsAfterOpen, n, out int openNlLen))
        {
            multiLine = true;
            i = wsAfterOpen + openNlLen;
        }

        int contentStart = i;
        int contentEnd = n;
        bool closed = false;

        while (i < n)
        {
            if (src[i] == (byte)'"')
            {
                int qRun = 0;
                while (i + qRun < n && src[i + qRun] == (byte)'"')
                {
                    qRun += 1;
                }
                if (qRun == quotes)
                {
                    contentEnd = i;
                    i += quotes;
                    closed = true;
                    break;
                }
                if (qRun > quotes)
                {
                    contentEnd = i;
                    i += qRun;
                    diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "Raw string literal contains too many consecutive quotes for its delimiter."u8);
                    closed = true;
                    break;
                }
                i += qRun;
                continue;
            }
            i += 1;
        }

        if (!closed)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, n - start, "Unterminated raw string literal."u8);
            outBytes.Clear();
            return n;
        }

        outBytes.Clear();
        if (!multiLine)
        {
            int slPos = contentStart;
            while (slPos < contentEnd)
            {
                if (TryMatchLineTerminator(src, slPos, contentEnd, out _))
                {
                    diags.Report(VibCodes.Unsupported, sourceId, start, i - start, "Single-line raw string literal cannot contain newline."u8);
                    break;
                }
                slPos += 1;
            }
            outBytes.Extend(src.Slice(contentStart, contentEnd - contentStart));
            return i;
        }

        DedentMultiLineRawString(src, sourceId, start, i, contentStart, contentEnd, ref outBytes, ref diags);
        return i;
    }

    private static void DedentMultiLineRawString(
        ReadOnlySpan<byte> src,
        int sourceId,
        int start,
        int endAfterQuotes,
        int contentStart,
        int contentEnd,
        ref Vec<byte> outBytes,
        ref DiagnosticBag diags)
    {
        int lastNlStart = -1;
        int lastNlEnd = -1;
        int scan = contentStart;
        while (scan < contentEnd)
        {
            if (TryMatchLineTerminator(src, scan, contentEnd, out int scanNlLen))
            {
                lastNlStart = scan;
                lastNlEnd = scan + scanNlLen;
                scan += scanNlLen;
            }
            else
            {
                scan += 1;
            }
        }

        if (lastNlStart < contentStart)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, endAfterQuotes - start, "Multi-line raw string literals must contain at least one line of content and place closing quotes on their own line."u8);
            return;
        }

        int indentStart = lastNlEnd;
        int indentLen = contentEnd - indentStart;
        int indentVerify = indentStart;
        while (indentVerify < contentEnd && TryMatchInlineWhitespace(src, indentVerify, contentEnd, out int indStep))
        {
            indentVerify += indStep;
        }
        if (indentVerify < contentEnd)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, endAfterQuotes - start, "Closing raw string delimiter must be preceded only by whitespace on its line."u8);
        }

        int cur = contentStart;
        while (true)
        {
            int lineEnd = cur;
            int lineNlLen = 0;
            while (lineEnd < lastNlStart)
            {
                if (TryMatchLineTerminator(src, lineEnd, lastNlStart, out lineNlLen))
                {
                    break;
                }
                lineEnd += 1;
            }

            AppendDedentedRawLine(src, sourceId, start, endAfterQuotes, cur, lineEnd, indentStart, indentLen, ref outBytes, ref diags);

            if (lineEnd < lastNlStart)
            {
                outBytes.Extend(src.Slice(lineEnd, lineNlLen));
                cur = lineEnd + lineNlLen;
            }
            else
            {
                break;
            }
        }
    }

    private static void AppendDedentedRawLine(
        ReadOnlySpan<byte> src,
        int sourceId,
        int start,
        int endAfterQuotes,
        int lineStart,
        int lineEnd,
        int indentStart,
        int indentLen,
        ref Vec<byte> outBytes,
        ref DiagnosticBag diags)
    {
        int lineLen = lineEnd - lineStart;
        if (lineLen <= 0)
        {
            return;
        }
        int matchLen = 0;
        while (matchLen < indentLen && matchLen < lineLen && src[lineStart + matchLen] == src[indentStart + matchLen])
        {
            matchLen += 1;
        }
        int wsRem = lineStart + matchLen;
        while (wsRem < lineEnd && TryMatchInlineWhitespace(src, wsRem, lineEnd, out int remStep))
        {
            wsRem += remStep;
        }
        if (matchLen < indentLen && wsRem < lineEnd)
        {
            diags.Report(VibCodes.Unsupported, sourceId, start, endAfterQuotes - start, "Raw string literal line does not start with the required closing indentation."u8);
        }
        int actualSkip = matchLen < indentLen ? lineLen : indentLen;
        if (actualSkip < lineLen)
        {
            outBytes.Extend(src.Slice(lineStart + actualSkip, lineLen - actualSkip));
        }
    }

    private static int SkipInterpolatedString(ReadOnlySpan<byte> src, int i)
    {
        int n = src.Length;
        i += 1; // skip '"'
        while (i < n && src[i] != (byte)'"' && !TryMatchLineTerminator(src, i, n, out _))
        {
            if (src[i] == (byte)'\\' && i + 1 < n)
            {
                i += 2;
                continue;
            }
            i += 1;
        }
        if (i < n && src[i] == (byte)'"') i += 1;
        return i;
    }

    private static bool TryConsumeLowSurrogateEscape(ReadOnlySpan<byte> src, ref int i, out int lowSurrogate)
    {
        lowSurrogate = 0;
        if (i + 2 > src.Length || src[i] != (byte)'\\')
        {
            return false;
        }
        int save = i;
        byte kind = src[i + 1];
        if (kind == (byte)'u')
        {
            if (i + 6 <= src.Length)
            {
                i += 2;
                int low = ReadFixedHex(src, ref i, 4, out bool ok4);
                if (ok4 && low >= 0xDC00 && low <= 0xDFFF)
                {
                    lowSurrogate = low;
                    return true;
                }
                i = save;
            }
            return false;
        }
        if (kind == (byte)'x')
        {
            i += 2;
            int low = 0;
            int count = 0;
            while (count < 4 && i < src.Length)
            {
                int hd = HexDigit(src[i]);
                if (hd < 0) break;
                low = (low << 4) | hd;
                i += 1;
                count += 1;
            }
            if (count > 0 && low >= 0xDC00 && low <= 0xDFFF)
            {
                lowSurrogate = low;
                return true;
            }
            i = save;
            return false;
        }
        if (kind == (byte)'U')
        {
            if (i + 10 <= src.Length)
            {
                i += 2;
                int low = ReadFixedHex(src, ref i, 8, out bool ok8);
                if (ok8 && low >= 0xDC00 && low <= 0xDFFF)
                {
                    lowSurrogate = low;
                    return true;
                }
                i = save;
            }
            return false;
        }
        return false;
    }

    private static int ReadEscapeScalar(ReadOnlySpan<byte> src, ref int i, bool allowSurrogatePair, out bool surrogateError, out bool invalidEscape)
    {
        surrogateError = false;
        invalidEscape = false;
        if (i >= src.Length)
        {
            invalidEscape = true;
            return 0;
        }
        byte esc = src[i];
        i += 1;
        switch (esc)
        {
            case (byte)'n': return '\n';
            case (byte)'r': return '\r';
            case (byte)'t': return '\t';
            case (byte)'\\': return '\\';
            case (byte)'"': return '"';
            case (byte)'\'': return '\'';
            case (byte)'0': return 0;
            case (byte)'a': return '\a';
            case (byte)'b': return '\b';
            case (byte)'e': return 0x1B;
            case (byte)'f': return '\f';
            case (byte)'v': return '\v';
            case (byte)'x':
            {
                int val = 0;
                int count = 0;
                while (count < 4 && i < src.Length)
                {
                    int hd = HexDigit(src[i]);
                    if (hd < 0) break;
                    val = (val << 4) | hd;
                    i += 1;
                    count += 1;
                }
                if (count == 0)
                {
                    invalidEscape = true;
                    return 0;
                }
                if (val >= 0xD800 && val <= 0xDBFF)
                {
                    if (allowSurrogatePair && TryConsumeLowSurrogateEscape(src, ref i, out int lowX))
                    {
                        return 0x10000 + ((val - 0xD800) << 10) + (lowX - 0xDC00);
                    }
                    surrogateError = true;
                    return 0;
                }
                if (val >= 0xDC00 && val <= 0xDFFF)
                {
                    surrogateError = true;
                    return 0;
                }
                return val;
            }
            case (byte)'u':
            {
                int val = ReadFixedHex(src, ref i, 4, out bool exact4);
                if (!exact4)
                {
                    invalidEscape = true;
                    return 0;
                }
                if (val >= 0xD800 && val <= 0xDBFF)
                {
                    if (allowSurrogatePair && TryConsumeLowSurrogateEscape(src, ref i, out int lowU))
                    {
                        return 0x10000 + ((val - 0xD800) << 10) + (lowU - 0xDC00);
                    }
                    surrogateError = true;
                    return 0;
                }
                if (val >= 0xDC00 && val <= 0xDFFF)
                {
                    surrogateError = true;
                    return 0;
                }
                return val;
            }
            case (byte)'U':
            {
                int val = ReadFixedHex(src, ref i, 8, out bool exact8);
                if (!exact8 || val < 0 || val > 0x10FFFF)
                {
                    invalidEscape = true;
                    return 0;
                }
                if (val >= 0xD800 && val <= 0xDBFF)
                {
                    if (allowSurrogatePair && TryConsumeLowSurrogateEscape(src, ref i, out int lowBigU))
                    {
                        return 0x10000 + ((val - 0xD800) << 10) + (lowBigU - 0xDC00);
                    }
                    surrogateError = true;
                    return 0;
                }
                if (val >= 0xDC00 && val <= 0xDFFF)
                {
                    surrogateError = true;
                    return 0;
                }
                return val;
            }
            default:
                invalidEscape = true;
                return 0;
        }
    }

    private static int ReadFixedHex(ReadOnlySpan<byte> src, ref int i, int digits, out bool ok)
    {
        int val = 0;
        int got = 0;
        while (got < digits && i < src.Length)
        {
            int hd = HexDigit(src[i]);
            if (hd < 0) break;
            val = (val << 4) | hd;
            i += 1;
            got += 1;
        }
        ok = got == digits;
        return val;
    }

    private static void AppendScalarUtf8(int scalar, ref Vec<byte> dst)
    {
        if (scalar < 0x80)
        {
            dst.Push((byte)scalar);
        }
        else if (scalar < 0x800)
        {
            dst.Push((byte)(0xC0 | (scalar >> 6)));
            dst.Push((byte)(0x80 | (scalar & 0x3F)));
        }
        else if (scalar < 0x10000)
        {
            dst.Push((byte)(0xE0 | (scalar >> 12)));
            dst.Push((byte)(0x80 | ((scalar >> 6) & 0x3F)));
            dst.Push((byte)(0x80 | (scalar & 0x3F)));
        }
        else
        {
            dst.Push((byte)(0xF0 | (scalar >> 18)));
            dst.Push((byte)(0x80 | ((scalar >> 12) & 0x3F)));
            dst.Push((byte)(0x80 | ((scalar >> 6) & 0x3F)));
            dst.Push((byte)(0x80 | (scalar & 0x3F)));
        }
    }

    private static int DecodeOneUtf8Scalar(ReadOnlySpan<byte> src, ref int i)
    {
        byte b0 = src[i];
        if (b0 < 0x80)
        {
            i += 1;
            return b0;
        }
        if ((b0 & 0xE0) == 0xC0 && i + 1 < src.Length)
        {
            int v = ((b0 & 0x1F) << 6) | (src[i + 1] & 0x3F);
            i += 2;
            return v;
        }
        if ((b0 & 0xF0) == 0xE0 && i + 2 < src.Length)
        {
            int v = ((b0 & 0x0F) << 12) | ((src[i + 1] & 0x3F) << 6) | (src[i + 2] & 0x3F);
            i += 3;
            return v;
        }
        if ((b0 & 0xF8) == 0xF0 && i + 3 < src.Length)
        {
            int v = ((b0 & 0x07) << 18) | ((src[i + 1] & 0x3F) << 12) | ((src[i + 2] & 0x3F) << 6) | (src[i + 3] & 0x3F);
            i += 4;
            return v;
        }
        i += 1;
        return b0;
    }

    private static int HexDigit(byte b)
    {
        if (b >= (byte)'0' && b <= (byte)'9') return b - (byte)'0';
        if (b >= (byte)'a' && b <= (byte)'f') return 10 + (b - (byte)'a');
        if (b >= (byte)'A' && b <= (byte)'F') return 10 + (b - (byte)'A');
        return -1;
    }

    private static bool IsIdentStart(byte b) =>
        (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z') || b == (byte)'_';

    private static bool IsIdentPart(byte b) =>
        IsIdentStart(b) || (b >= (byte)'0' && b <= (byte)'9');

    private static bool TryDecodeUtf8ScalarAt(ReadOnlySpan<byte> src, int pos, int limit, out int scalar, out int byteLen)
    {
        scalar = 0;
        byteLen = 0;
        if (pos >= limit) return false;
        byte b0 = src[pos];
        if (b0 < 0x80)
        {
            scalar = b0;
            byteLen = 1;
            return true;
        }
        if (b0 >= 0xC2 && b0 <= 0xDF)
        {
            if (pos + 1 >= limit) return false;
            byte b1 = src[pos + 1];
            if ((b1 & 0xC0) != 0x80) return false;
            scalar = ((b0 & 0x1F) << 6) | (b1 & 0x3F);
            byteLen = 2;
            return true;
        }
        if (b0 >= 0xE0 && b0 <= 0xEF)
        {
            if (pos + 2 >= limit) return false;
            byte b1 = src[pos + 1];
            byte b2 = src[pos + 2];
            if ((b1 & 0xC0) != 0x80 || (b2 & 0xC0) != 0x80) return false;
            if (b0 == 0xE0 && b1 < 0xA0) return false;
            if (b0 == 0xED && b1 >= 0xA0) return false;
            scalar = ((b0 & 0x0F) << 12) | ((b1 & 0x3F) << 6) | (b2 & 0x3F);
            byteLen = 3;
            return true;
        }
        if (b0 >= 0xF0 && b0 <= 0xF4)
        {
            if (pos + 3 >= limit) return false;
            byte b1 = src[pos + 1];
            byte b2 = src[pos + 2];
            byte b3 = src[pos + 3];
            if ((b1 & 0xC0) != 0x80 || (b2 & 0xC0) != 0x80 || (b3 & 0xC0) != 0x80) return false;
            if (b0 == 0xF0 && b1 < 0x90) return false;
            if (b0 == 0xF4 && b1 >= 0x90) return false;
            scalar = ((b0 & 0x07) << 18) | ((b1 & 0x3F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F);
            byteLen = 4;
            return true;
        }
        return false;
    }

    private static bool IsExcludedNonAsciiScalar(int s)
    {
        if (s < 0x00AA || s > 0x10FFFF) return true;
        if (s == 0x1680 || s == 0xFEFF) return true;
        if (s >= 0x2000 && s <= 0x200F) return true;
        if (s >= 0x2010 && s <= 0x203E) return true;
        if (s >= 0x2041 && s <= 0x2053) return true;
        if (s >= 0x2055 && s <= 0x206F) return true;
        if (s >= 0x2070 && s <= 0x20CF && s != 0x2071 && s != 0x207F && !(s >= 0x2090 && s <= 0x209C)) return true;
        if (s >= 0x2190 && s <= 0x2BFF) return true;
        if (s >= 0x2E00 && s <= 0x2E7F) return true;
        if (s >= 0x3000 && s <= 0x3004) return true;
        if (s >= 0x3008 && s <= 0x3020) return true;
        if (s == 0x3030 || s == 0x3036 || s == 0x3037 || (s >= 0x303D && s <= 0x303F)) return true;
        if (s >= 0xD800 && s <= 0xF8FF) return true;
        if (s >= 0xFE30 && s <= 0xFE6F && s != 0xFE33 && s != 0xFE34 && !(s >= 0xFE4D && s <= 0xFE4F)) return true;
        if ((s >= 0xFF01 && s <= 0xFF0F) || (s >= 0xFF1A && s <= 0xFF20) ||
            (s >= 0xFF3B && s <= 0xFF40 && s != 0xFF3F) || (s >= 0xFF5B && s <= 0xFF65))
        {
            return true;
        }
        if (s >= 0xFFF0 && s <= 0xFFFF) return true;
        if (s >= 0x1F000 && s <= 0x1FFFF) return true;
        if (s >= 0xE0000) return true;
        return false;
    }

    private static bool IsNonAsciiPartOnlyScalar(int s) =>
        s == 0x00AD ||
        (s >= 0x0300 && s <= 0x036F) ||
        (s >= 0x0483 && s <= 0x0489) ||
        (s >= 0x0660 && s <= 0x0669) ||
        (s >= 0x06F0 && s <= 0x06F9) ||
        (s >= 0x0966 && s <= 0x096F) ||
        s == 0x203F || s == 0x2040 || s == 0x2054 ||
        s == 0xFE33 || s == 0xFE34 || (s >= 0xFE4D && s <= 0xFE4F) ||
        (s >= 0xFF10 && s <= 0xFF19) || s == 0xFF3F;

    private static bool TryMatchLineTerminator(ReadOnlySpan<byte> src, int pos, int limit, out int byteLen)
    {
        if (pos >= limit)
        {
            byteLen = 0;
            return false;
        }
        byte b0 = src[pos];
        if (b0 == (byte)'\n')
        {
            byteLen = 1;
            return true;
        }
        if (b0 == (byte)'\r')
        {
            byteLen = (pos + 1 < limit && src[pos + 1] == (byte)'\n') ? 2 : 1;
            return true;
        }
        if (b0 == 0xC2 && pos + 1 < limit && src[pos + 1] == 0x85)
        {
            byteLen = 2;
            return true;
        }
        if (b0 == 0xE2 && pos + 2 < limit && src[pos + 1] == 0x80 && (src[pos + 2] == 0xA8 || src[pos + 2] == 0xA9))
        {
            byteLen = 3;
            return true;
        }
        byteLen = 0;
        return false;
    }

    private static bool IsUnicodeWhitespaceScalar(int s) =>
        s == 0x00A0 ||
        s == 0x1680 ||
        (s >= 0x2000 && s <= 0x200A) ||
        s == 0x202F ||
        s == 0x205F ||
        s == 0x3000 ||
        s == 0xFEFF;

    private static bool TryMatchInlineWhitespace(ReadOnlySpan<byte> src, int pos, int limit, out int byteLen)
    {
        if (pos >= limit)
        {
            byteLen = 0;
            return false;
        }
        byte b = src[pos];
        if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\v' || b == (byte)'\f' || b == 0x1A)
        {
            byteLen = 1;
            return true;
        }
        if (b >= 0x80 &&
            TryDecodeUtf8ScalarAt(src, pos, limit, out int wsScalar, out int wsLen) &&
            IsUnicodeWhitespaceScalar(wsScalar))
        {
            byteLen = wsLen;
            return true;
        }
        byteLen = 0;
        return false;
    }

    private static bool IsUnicodeFormatScalar(int s) =>
        s == 0x00AD ||
        (s >= 0x0600 && s <= 0x0605) ||
        s == 0x061C ||
        s == 0x06DD ||
        s == 0x070F ||
        (s >= 0x0890 && s <= 0x0891) ||
        s == 0x08E2 ||
        s == 0x180E ||
        (s >= 0x200B && s <= 0x200F) ||
        (s >= 0x202A && s <= 0x202E) ||
        (s >= 0x2060 && s <= 0x2064) ||
        (s >= 0x2066 && s <= 0x206F) ||
        s == 0xFEFF ||
        (s >= 0xFFF9 && s <= 0xFFFB) ||
        s == 0x110BD ||
        s == 0x110CD ||
        (s >= 0x13430 && s <= 0x1343F) ||
        (s >= 0x1BCA0 && s <= 0x1BCA3) ||
        (s >= 0x1D173 && s <= 0x1D17A) ||
        s == 0xE0001 ||
        (s >= 0xE0020 && s <= 0xE007F);

    private static bool IsNonAsciiIdentStartScalar(int s)
    {
        if (s > 0xFFFF || IsUnicodeFormatScalar(s)) return false;
        if (s <= 0x00FF)
        {
            return s == 0x00AA || s == 0x00B5 || s == 0x00BA ||
                   (s >= 0x00C0 && s <= 0x00D6) ||
                   (s >= 0x00D8 && s <= 0x00F6) ||
                   (s >= 0x00F8 && s <= 0x00FF);
        }
        if (IsExcludedNonAsciiScalar(s) || IsNonAsciiPartOnlyScalar(s))
        {
            return false;
        }
        if ((s >= 0x02C2 && s <= 0x02C5) || (s >= 0x02D2 && s <= 0x02DF) ||
            (s >= 0x02E5 && s <= 0x02EB) || s == 0x02ED || (s >= 0x02EF && s <= 0x02FF))
        {
            return false;
        }
        return true;
    }

    private static bool IsNonAsciiIdentPartScalar(int s)
    {
        if (s > 0xFFFF) return false;
        if (IsUnicodeFormatScalar(s)) return true;
        if (IsNonAsciiIdentStartScalar(s)) return true;
        if (s <= 0x00FF) return false;
        if (IsExcludedNonAsciiScalar(s)) return false;
        return IsNonAsciiPartOnlyScalar(s);
    }

    private static bool IsIdentStartScalar(int s)
    {
        if (s < 0x80) return s >= 0 && IsIdentStart((byte)s);
        return IsNonAsciiIdentStartScalar(s);
    }

    private static bool IsIdentPartScalar(int s)
    {
        if (s < 0x80) return s >= 0 && IsIdentPart((byte)s);
        return IsNonAsciiIdentPartScalar(s);
    }

    private static int ConsumeStrayBackslashEscape(ReadOnlySpan<byte> src, int pos, int limit)
    {
        if (pos + 1 >= limit) return 1;
        byte k = src[pos + 1];
        if (k == (byte)'u')
        {
            int p = pos + 2;
            int cnt = 0;
            while (cnt < 4 && p < limit && HexDigit(src[p]) >= 0)
            {
                p += 1;
                cnt += 1;
            }
            return p - pos;
        }
        if (k == (byte)'U')
        {
            int p = pos + 2;
            int cnt = 0;
            while (cnt < 8 && p < limit && HexDigit(src[p]) >= 0)
            {
                p += 1;
                cnt += 1;
            }
            return p - pos;
        }
        return 1;
    }

    private static bool TryPeekIdentChar(
        ReadOnlySpan<byte> src,
        int pos,
        int limit,
        out int scalar,
        out int byteLen,
        out bool wasEscape,
        out bool invalidEscape)
    {
        scalar = 0;
        byteLen = 0;
        wasEscape = false;
        invalidEscape = false;
        if (pos >= limit) return false;

        byte b = src[pos];
        if (b == (byte)'\\')
        {
            if (pos + 1 >= limit) return false;
            byte kind = src[pos + 1];
            if (kind == (byte)'u')
            {
                wasEscape = true;
                int p = pos + 2;
                int val = 0;
                int got = 0;
                while (got < 4 && p < limit)
                {
                    int hd = HexDigit(src[p]);
                    if (hd < 0) break;
                    val = (val << 4) | hd;
                    p += 1;
                    got += 1;
                }
                byteLen = p - pos;
                if (got != 4 || (val >= 0xD800 && val <= 0xDFFF))
                {
                    invalidEscape = true;
                    return true;
                }
                scalar = val;
                return true;
            }
            if (kind == (byte)'U')
            {
                wasEscape = true;
                int p = pos + 2;
                int val = 0;
                int got = 0;
                while (got < 8 && p < limit)
                {
                    int hd = HexDigit(src[p]);
                    if (hd < 0) break;
                    val = (val << 4) | hd;
                    p += 1;
                    got += 1;
                }
                byteLen = p - pos;
                if (got != 8 || val < 0 || val > 0xFFFF || (val >= 0xD800 && val <= 0xDFFF))
                {
                    invalidEscape = true;
                    return true;
                }
                scalar = val;
                return true;
            }
            return false;
        }

        if (b < 0x80)
        {
            scalar = b;
            byteLen = 1;
            return true;
        }

        return TryDecodeUtf8ScalarAt(src, pos, limit, out scalar, out byteLen);
    }

    private static bool TryLexNormalizedIdent(
        ReadOnlySpan<byte> src,
        int pos,
        int limit,
        ref Vec<byte> scratch,
        out int consumedBytes,
        out bool hadEscapeOrCf,
        out bool invalidEscape)
    {
        consumedBytes = 0;
        hadEscapeOrCf = false;
        invalidEscape = false;
        if (pos >= limit) return false;

        // Zero-allocation ASCII fast path
        byte b0 = src[pos];
        if (b0 < 0x80 && IsIdentStart(b0))
        {
            int fast = pos + 1;
            while (fast < limit && src[fast] < 0x80 && IsIdentPart(src[fast]))
            {
                fast += 1;
            }
            if (fast >= limit || (src[fast] < 0x80 && src[fast] != (byte)'\\'))
            {
                consumedBytes = fast - pos;
                return true;
            }
        }

        if (!TryPeekIdentChar(src, pos, limit, out int firstScalar, out int firstLen, out bool firstEsc, out bool firstBad))
        {
            return false;
        }
        if (firstBad || !IsIdentStartScalar(firstScalar))
        {
            return false;
        }

        scratch.Clear();
        AppendScalarUtf8(firstScalar, ref scratch);
        hadEscapeOrCf = firstEsc;
        int p = pos + firstLen;

        while (p < limit)
        {
            byte bp = src[p];
            if (bp < 0x80 && IsIdentPart(bp))
            {
                scratch.Push(bp);
                p += 1;
                continue;
            }
            if (!TryPeekIdentChar(src, p, limit, out int partScalar, out int partLen, out bool partEsc, out bool partBad))
            {
                break;
            }
            if (partBad)
            {
                invalidEscape = true;
                hadEscapeOrCf = true;
                p += partLen;
                break;
            }
            if (!IsIdentPartScalar(partScalar))
            {
                break;
            }
            p += partLen;
            if (partEsc)
            {
                hadEscapeOrCf = true;
            }
            if (IsUnicodeFormatScalar(partScalar))
            {
                hadEscapeOrCf = true;
            }
            else
            {
                AppendScalarUtf8(partScalar, ref scratch);
            }
        }

        consumedBytes = p - pos;
        return true;
    }

    private static TokKind ClassifyKeyword(ReadOnlySpan<byte> w)
    {
        switch (w.Length)
        {
            case 2:
                if (Bytes.Eq(w, "do"u8)) return TokKind.KwDo;
                if (Bytes.Eq(w, "if"u8)) return TokKind.KwIf;
                if (Bytes.Eq(w, "in"u8)) return TokKind.KwIn;
                if (Bytes.Eq(w, "is"u8)) return TokKind.KwIs;
                break;
            case 3:
                if (Bytes.Eq(w, "int"u8)) return TokKind.KwInt;
                if (Bytes.Eq(w, "var"u8)) return TokKind.KwVar;
                if (Bytes.Eq(w, "new"u8)) return TokKind.KwNew;
                if (Bytes.Eq(w, "for"u8)) return TokKind.KwFor;
                if (Bytes.Eq(w, "ref"u8)) return TokKind.KwRef;
                if (Bytes.Eq(w, "out"u8)) return TokKind.KwOut;
                if (Bytes.Eq(w, "try"u8)) return TokKind.KwTry;
                break;
            case 4:
                if (Bytes.Eq(w, "void"u8)) return TokKind.KwVoid;
                if (Bytes.Eq(w, "bool"u8)) return TokKind.KwBool;
                if (Bytes.Eq(w, "byte"u8)) return TokKind.KwByte;
                if (Bytes.Eq(w, "char"u8)) return TokKind.KwChar;
                if (Bytes.Eq(w, "uint"u8)) return TokKind.KwUInt;
                if (Bytes.Eq(w, "long"u8)) return TokKind.KwLong;
                if (Bytes.Eq(w, "nint"u8)) return TokKind.KwNInt;
                if (Bytes.Eq(w, "else"u8)) return TokKind.KwElse;
                if (Bytes.Eq(w, "this"u8)) return TokKind.KwThis;
                if (Bytes.Eq(w, "true"u8)) return TokKind.KwTrue;
                if (Bytes.Eq(w, "null"u8)) return TokKind.KwNull;
                if (Bytes.Eq(w, "enum"u8)) return TokKind.KwEnum;
                if (Bytes.Eq(w, "case"u8)) return TokKind.KwCase;
                if (Bytes.Eq(w, "goto"u8)) return TokKind.KwGoto;
                if (Bytes.Eq(w, "lock"u8)) return TokKind.KwLock;
                break;
            case 5:
                if (Bytes.Eq(w, "using"u8)) return TokKind.KwUsing;
                if (Bytes.Eq(w, "class"u8)) return TokKind.KwClass;
                if (Bytes.Eq(w, "while"u8)) return TokKind.KwWhile;
                if (Bytes.Eq(w, "break"u8)) return TokKind.KwBreak;
                if (Bytes.Eq(w, "const"u8)) return TokKind.KwConst;
                if (Bytes.Eq(w, "false"u8)) return TokKind.KwFalse;
                if (Bytes.Eq(w, "sbyte"u8)) return TokKind.KwSByte;
                if (Bytes.Eq(w, "short"u8)) return TokKind.KwShort;
                if (Bytes.Eq(w, "ulong"u8)) return TokKind.KwULong;
                if (Bytes.Eq(w, "nuint"u8)) return TokKind.KwNUInt;
                if (Bytes.Eq(w, "float"u8)) return TokKind.KwFloat;
                if (Bytes.Eq(w, "where"u8)) return TokKind.KwWhere;
                if (Bytes.Eq(w, "async"u8)) return TokKind.KwAsync;
                if (Bytes.Eq(w, "throw"u8)) return TokKind.KwThrow;
                if (Bytes.Eq(w, "catch"u8)) return TokKind.KwCatch;
                if (Bytes.Eq(w, "yield"u8)) return TokKind.KwYield;
                if (Bytes.Eq(w, "await"u8)) return TokKind.KwAwait;
                break;
            case 6:
                if (Bytes.Eq(w, "public"u8)) return TokKind.KwPublic;
                if (Bytes.Eq(w, "static"u8)) return TokKind.KwStatic;
                if (Bytes.Eq(w, "struct"u8)) return TokKind.KwStruct;
                if (Bytes.Eq(w, "return"u8)) return TokKind.KwReturn;
                if (Bytes.Eq(w, "switch"u8)) return TokKind.KwSwitch;
                if (Bytes.Eq(w, "string"u8)) return TokKind.KwString;
                if (Bytes.Eq(w, "ushort"u8)) return TokKind.KwUShort;
                if (Bytes.Eq(w, "double"u8)) return TokKind.KwDouble;
                if (Bytes.Eq(w, "object"u8)) return TokKind.KwObject;
                if (Bytes.Eq(w, "sealed"u8)) return TokKind.KwSealed;
                if (Bytes.Eq(w, "scoped"u8)) return TokKind.KwScoped;
                if (Bytes.Eq(w, "global"u8)) return TokKind.KwGlobal;
                if (Bytes.Eq(w, "unsafe"u8)) return TokKind.KwUnsafe;
                break;
            case 7:
                if (Bytes.Eq(w, "private"u8)) return TokKind.KwPrivate;
                if (Bytes.Eq(w, "partial"u8)) return TokKind.KwPartial;
                if (Bytes.Eq(w, "foreach"u8)) return TokKind.KwForeach;
                if (Bytes.Eq(w, "default"u8)) return TokKind.KwDefault;
                if (Bytes.Eq(w, "virtual"u8)) return TokKind.KwVirtual;
                if (Bytes.Eq(w, "checked"u8)) return TokKind.KwChecked;
                if (Bytes.Eq(w, "finally"u8)) return TokKind.KwFinally;
                if (Bytes.Eq(w, "decimal"u8)) return TokKind.KwDecimal;
                if (Bytes.Eq(w, "dynamic"u8)) return TokKind.KwDynamic;
                break;
            case 8:
                if (Bytes.Eq(w, "internal"u8)) return TokKind.KwInternal;
                if (Bytes.Eq(w, "readonly"u8)) return TokKind.KwReadonly;
                if (Bytes.Eq(w, "continue"u8)) return TokKind.KwContinue;
                if (Bytes.Eq(w, "abstract"u8)) return TokKind.KwAbstract;
                if (Bytes.Eq(w, "override"u8)) return TokKind.KwOverride;
                if (Bytes.Eq(w, "delegate"u8)) return TokKind.KwDelegate;
                if (Bytes.Eq(w, "operator"u8)) return TokKind.KwOperator;
                break;
            case 9:
                if (Bytes.Eq(w, "namespace"u8)) return TokKind.KwNamespace;
                if (Bytes.Eq(w, "protected"u8)) return TokKind.KwProtected;
                if (Bytes.Eq(w, "interface"u8)) return TokKind.KwInterface;
                if (Bytes.Eq(w, "unchecked"u8)) return TokKind.KwUnchecked;
                break;
            case 10:
                if (Bytes.Eq(w, "stackalloc"u8)) return TokKind.KwStackalloc;
                break;
        }
        return TokKind.Ident;
    }

    private static TokKind MatchThreeCharOp(byte a, byte b, byte c)
    {
        if (a == (byte)'>' && b == (byte)'>' && c == (byte)'>') return TokKind.GtGtGt;
        if (a == (byte)'<' && b == (byte)'<' && c == (byte)'=') return TokKind.LtLtEq;
        if (a == (byte)'>' && b == (byte)'>' && c == (byte)'=') return TokKind.GtGtEq;
        if (a == (byte)'?' && b == (byte)'?' && c == (byte)'=') return TokKind.QuestionQuestionEq;
        return TokKind.Eof;
    }

    private static TokKind MatchTwoCharOp(byte a, byte b)
    {
        switch (a)
        {
            case (byte)'=':
                if (b == (byte)'=') return TokKind.EqEq;
                if (b == (byte)'>') return TokKind.FatArrow;
                break;
            case (byte)'!':
                if (b == (byte)'=') return TokKind.BangEq;
                break;
            case (byte)'<':
                if (b == (byte)'=') return TokKind.LtEq;
                if (b == (byte)'<') return TokKind.LtLt;
                break;
            case (byte)'>':
                if (b == (byte)'=') return TokKind.GtEq;
                if (b == (byte)'>') return TokKind.GtGt;
                break;
            case (byte)'&':
                if (b == (byte)'&') return TokKind.AmpAmp;
                if (b == (byte)'=') return TokKind.AmpEq;
                break;
            case (byte)'|':
                if (b == (byte)'|') return TokKind.PipePipe;
                if (b == (byte)'=') return TokKind.PipeEq;
                break;
            case (byte)'+':
                if (b == (byte)'+') return TokKind.PlusPlus;
                if (b == (byte)'=') return TokKind.PlusEq;
                break;
            case (byte)'-':
                if (b == (byte)'-') return TokKind.MinusMinus;
                if (b == (byte)'=') return TokKind.MinusEq;
                break;
            case (byte)'*':
                if (b == (byte)'=') return TokKind.StarEq;
                break;
            case (byte)'/':
                if (b == (byte)'=') return TokKind.SlashEq;
                break;
            case (byte)'%':
                if (b == (byte)'=') return TokKind.PercentEq;
                break;
            case (byte)'^':
                if (b == (byte)'=') return TokKind.CaretEq;
                break;
            case (byte)'?':
                if (b == (byte)'.') return TokKind.QuestionDot;
                if (b == (byte)'?') return TokKind.QuestionQuestion;
                break;
            case (byte)':':
                if (b == (byte)':') return TokKind.ColonColon;
                break;
            case (byte)'.':
                if (b == (byte)'.') return TokKind.DotDot;
                break;
        }
        return TokKind.Eof;
    }

    private static TokKind MatchOneCharOp(byte a)
    {
        switch (a)
        {
            case (byte)'(': return TokKind.LParen;
            case (byte)')': return TokKind.RParen;
            case (byte)'{': return TokKind.LBrace;
            case (byte)'}': return TokKind.RBrace;
            case (byte)'[': return TokKind.LBracket;
            case (byte)']': return TokKind.RBracket;
            case (byte)'<': return TokKind.LAngle;
            case (byte)'>': return TokKind.RAngle;
            case (byte)';': return TokKind.Semi;
            case (byte)',': return TokKind.Comma;
            case (byte)'.': return TokKind.Dot;
            case (byte)':': return TokKind.Colon;
            case (byte)'?': return TokKind.Question;
            case (byte)'!': return TokKind.Bang;
            case (byte)'~': return TokKind.Tilde;
            case (byte)'+': return TokKind.Plus;
            case (byte)'-': return TokKind.Minus;
            case (byte)'*': return TokKind.Star;
            case (byte)'/': return TokKind.Slash;
            case (byte)'%': return TokKind.Percent;
            case (byte)'&': return TokKind.Amp;
            case (byte)'|': return TokKind.Pipe;
            case (byte)'^': return TokKind.Caret;
            case (byte)'=': return TokKind.Eq;
            default: return TokKind.Eof;
        }
    }

    private static void FindPreprocessorLineBounds(ReadOnlySpan<byte> src, int pos, out int lineEnd, out int nextLine)
    {
        int n = src.Length;
        while (pos < n)
        {
            if (TryMatchLineTerminator(src, pos, n, out int nlLen))
            {
                lineEnd = pos;
                nextLine = pos + nlLen;
                return;
            }
            pos += 1;
        }
        lineEnd = n;
        nextLine = n;
    }

    private static void SkipPpInlineSpace(ReadOnlySpan<byte> src, ref int p, int lineEnd, int sourceId, ref DiagnosticBag diags)
    {
        while (p < lineEnd)
        {
            if (TryMatchInlineWhitespace(src, p, lineEnd, out int wsLen))
            {
                p += wsLen;
                continue;
            }
            byte b = src[p];
            if (b == (byte)'/' && p + 1 < lineEnd && src[p + 1] == (byte)'*')
            {
                int cStart = p;
                p += 2;
                bool closed = false;
                while (p + 1 < lineEnd)
                {
                    if (src[p] == (byte)'*' && src[p + 1] == (byte)'/')
                    {
                        p += 2;
                        closed = true;
                        break;
                    }
                    p += 1;
                }
                if (!closed)
                {
                    diags.Report(VibCodes.Unsupported, sourceId, cStart, lineEnd - cStart, "Unterminated comment in preprocessor directive."u8);
                    p = lineEnd;
                }
                continue;
            }
            break;
        }
    }

    private static bool IsAtPpEndOfDirective(ReadOnlySpan<byte> src, int p, int lineEnd)
    {
        if (p >= lineEnd) return true;
        return src[p] == (byte)'/' && p + 1 < lineEnd && src[p + 1] == (byte)'/';
    }

    private static int HandleActivePreprocessorDirective(
        ReadOnlySpan<byte> src,
        int sourceId,
        int hashPos,
        bool seenRealToken,
        ref Vec<byte> scratch,
        ref StrMap<int> defines,
        ref Vec<int> condStack,
        ref Vec<int> condStarts,
        ref bool rejectedConditionalFile,
        ref DiagnosticBag diags)
    {
        FindPreprocessorLineBounds(src, hashPos, out int lineEnd, out int nextLine);
        int p = hashPos + 1;
        SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);

        int kwStart = p;
        while (p < lineEnd && IsIdentPart(src[p]))
        {
            p += 1;
        }
        ReadOnlySpan<byte> kw = src.Slice(kwStart, p - kwStart);
        if (Bytes.Eq(kw, "if"u8) || Bytes.Eq(kw, "elif"u8) || Bytes.Eq(kw, "else"u8) ||
            Bytes.Eq(kw, "endif"u8) || Bytes.Eq(kw, "define"u8) || Bytes.Eq(kw, "undef"u8))
        {
            if (!rejectedConditionalFile)
                diags.Report(VibCodes.Unsupported, sourceId, kwStart, p - kwStart, "Conditional compilation directives (#if, #elif, #else, #endif, #define, #undef) are not supported in Vibranium v0.2; .NET and native builds must compile the same code."u8);
            rejectedConditionalFile = true;
        }

        if (Bytes.Eq(kw, "if"u8))
        {
            bool cond = EvalPpConditionLine(src, p, lineEnd, sourceId, hashPos, ref scratch, in defines, ref diags);
            int state = 1 | (cond ? 2 : 0) | (cond ? 4 : 0);
            condStack.Push(state);
            condStarts.Push(hashPos);
            return nextLine;
        }

        if (Bytes.Eq(kw, "elif"u8))
        {
            if (condStack.Count == 0)
            {
                diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Unexpected '#elif' without matching '#if'."u8);
                return nextLine;
            }
            int top = condStack[condStack.Count - 1];
            if ((top & 8) != 0)
            {
                diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "'#elif' cannot follow '#else'."u8);
                return nextLine;
            }
            // Validate syntax even though prior branch was active
            _ = EvalPpConditionLine(src, p, lineEnd, sourceId, hashPos, ref scratch, in defines, ref diags);
            // Since we were in an active branch, branchTaken is 1; this #elif becomes inactive
            condStack[condStack.Count - 1] = (top & ~2) | 4;
            return nextLine;
        }

        if (Bytes.Eq(kw, "else"u8))
        {
            if (condStack.Count == 0)
            {
                diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Unexpected '#else' without matching '#if'."u8);
                return nextLine;
            }
            int top = condStack[condStack.Count - 1];
            if ((top & 8) != 0)
            {
                diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Duplicate '#else' in '#if' block."u8);
                return nextLine;
            }
            SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
            if (!IsAtPpEndOfDirective(src, p, lineEnd))
            {
                diags.Report(VibCodes.Unsupported, sourceId, p, lineEnd - p, "Unexpected tokens after '#else'."u8);
            }
            condStack[condStack.Count - 1] = (top & ~2) | 4 | 8;
            return nextLine;
        }

        if (Bytes.Eq(kw, "endif"u8))
        {
            if (condStack.Count == 0)
            {
                diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Unexpected '#endif' without matching '#if'."u8);
                return nextLine;
            }
            SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
            if (!IsAtPpEndOfDirective(src, p, lineEnd))
            {
                diags.Report(VibCodes.Unsupported, sourceId, p, lineEnd - p, "Unexpected tokens after '#endif'."u8);
            }
            condStack.Pop();
            condStarts.Pop();
            return nextLine;
        }

        if (Bytes.Eq(kw, "define"u8) || Bytes.Eq(kw, "undef"u8))
        {
            bool isDef = Bytes.Eq(kw, "define"u8);
            if (seenRealToken)
            {
                diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Cannot define or undefine preprocessor symbols after first token in file."u8);
                return nextLine;
            }
            SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
            int symStart = p;
            if (!TryLexNormalizedIdent(src, p, lineEnd, ref scratch, out int ppSymLen, out bool ppHadEscOrCf, out bool ppBadEsc))
            {
                diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Expected identifier in '#define'/'#undef' directive."u8);
                return nextLine;
            }
            p += ppSymLen;
            if (ppBadEsc)
            {
                diags.Report(VibCodes.Unsupported, sourceId, symStart, ppSymLen, "Invalid Unicode escape sequence in preprocessor symbol."u8);
                return nextLine;
            }
            if (!ppHadEscOrCf)
            {
                ReadOnlySpan<byte> rawSym = src.Slice(symStart, ppSymLen);
                if (Bytes.Eq(rawSym, "true"u8) || Bytes.Eq(rawSym, "false"u8))
                {
                    diags.Report(VibCodes.Unsupported, sourceId, symStart, ppSymLen, "Cannot define or undefine 'true' or 'false'."u8);
                    return nextLine;
                }
            }
            SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
            if (!IsAtPpEndOfDirective(src, p, lineEnd))
            {
                diags.Report(VibCodes.Unsupported, sourceId, p, lineEnd - p, "Unexpected tokens after preprocessor symbol."u8);
            }
            ReadOnlySpan<byte> sym = ppHadEscOrCf ? scratch.AsReadOnlySpan() : src.Slice(symStart, ppSymLen);
            if (isDef)
            {
                defines.Set(sym, 1);
            }
            else
            {
                defines.Remove(sym);
            }
            return nextLine;
        }

        if (Bytes.Eq(kw, "error"u8))
        {
            diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Preprocessor '#error' directive."u8);
            return nextLine;
        }

        if (Bytes.Eq(kw, "nullable"u8) || Bytes.Eq(kw, "pragma"u8) ||
            Bytes.Eq(kw, "region"u8) || Bytes.Eq(kw, "endregion"u8) ||
            Bytes.Eq(kw, "line"u8) || Bytes.Eq(kw, "warning"u8))
        {
            return nextLine;
        }

        diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Unrecognized preprocessor directive."u8);
        return nextLine;
    }

    private static int SkipInactivePreprocessorSection(
        ReadOnlySpan<byte> src,
        int sourceId,
        int pos,
        ref Vec<byte> scratch,
        in StrMap<int> defines,
        ref Vec<int> condStack,
        ref Vec<int> condStarts,
        ref DiagnosticBag diags)
    {
        int n = src.Length;
        int cur = pos;
        while (cur < n && condStack.Count > 0 && (condStack[condStack.Count - 1] & 2) == 0)
        {
            FindPreprocessorLineBounds(src, cur, out int lineEnd, out int nextLine);
            int p = cur;
            while (p < lineEnd && TryMatchInlineWhitespace(src, p, lineEnd, out int leadWsLen))
            {
                p += leadWsLen;
            }
            if (p < lineEnd && src[p] == (byte)'#')
            {
                int hashPos = p;
                p += 1;
                SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
                int kwStart = p;
                while (p < lineEnd && IsIdentPart(src[p])) p += 1;
                ReadOnlySpan<byte> kw = src.Slice(kwStart, p - kwStart);

                if (Bytes.Eq(kw, "if"u8))
                {
                    condStack.Push(4); // parentActive=0, curActive=0, branchTaken=1
                    condStarts.Push(hashPos);
                }
                else if (Bytes.Eq(kw, "elif"u8))
                {
                    int top = condStack[condStack.Count - 1];
                    if ((top & 8) != 0)
                    {
                        diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "'#elif' cannot follow '#else'."u8);
                    }
                    else if ((top & 1) != 0 && (top & 4) == 0)
                    {
                        bool cond = EvalPpConditionLine(src, p, lineEnd, sourceId, hashPos, ref scratch, in defines, ref diags);
                        if (cond)
                        {
                            condStack[condStack.Count - 1] = top | 2 | 4;
                            return nextLine;
                        }
                    }
                }
                else if (Bytes.Eq(kw, "else"u8))
                {
                    int top = condStack[condStack.Count - 1];
                    if ((top & 8) != 0)
                    {
                        diags.Report(VibCodes.Unsupported, sourceId, hashPos, lineEnd - hashPos, "Duplicate '#else' in '#if' block."u8);
                    }
                    else
                    {
                        SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
                        if (!IsAtPpEndOfDirective(src, p, lineEnd))
                        {
                            diags.Report(VibCodes.Unsupported, sourceId, p, lineEnd - p, "Unexpected tokens after '#else'."u8);
                        }
                        if ((top & 1) != 0 && (top & 4) == 0)
                        {
                            condStack[condStack.Count - 1] = top | 2 | 4 | 8;
                            return nextLine;
                        }
                        condStack[condStack.Count - 1] = top | 8;
                    }
                }
                else if (Bytes.Eq(kw, "endif"u8))
                {
                    SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
                    if (!IsAtPpEndOfDirective(src, p, lineEnd))
                    {
                        diags.Report(VibCodes.Unsupported, sourceId, p, lineEnd - p, "Unexpected tokens after '#endif'."u8);
                    }
                    condStack.Pop();
                    condStarts.Pop();
                    if (condStack.Count == 0 || (condStack[condStack.Count - 1] & 2) != 0)
                    {
                        return nextLine;
                    }
                }
            }
            cur = nextLine;
        }
        return cur;
    }

    private static bool EvalPpConditionLine(
        ReadOnlySpan<byte> src,
        int exprStart,
        int lineEnd,
        int sourceId,
        int dirStart,
        ref Vec<byte> scratch,
        in StrMap<int> defines,
        ref DiagnosticBag diags)
    {
        int p = exprStart;
        SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
        if (IsAtPpEndOfDirective(src, p, lineEnd))
        {
            diags.Report(VibCodes.Unsupported, sourceId, dirStart, lineEnd - dirStart, "Expected preprocessor expression."u8);
            return false;
        }
        bool ok = true;
        bool val = EvalPpOr(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
        SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
        if (!ok || !IsAtPpEndOfDirective(src, p, lineEnd))
        {
            diags.Report(VibCodes.Unsupported, sourceId, dirStart, lineEnd - dirStart, "Invalid preprocessor expression."u8);
            return false;
        }
        return val;
    }

    private static bool EvalPpOr(
        ReadOnlySpan<byte> src,
        ref int p,
        int lineEnd,
        int sourceId,
        ref Vec<byte> scratch,
        in StrMap<int> defines,
        ref DiagnosticBag diags,
        ref bool ok)
    {
        bool left = EvalPpAnd(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
        while (ok)
        {
            SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
            if (p + 1 < lineEnd && src[p] == (byte)'|' && src[p + 1] == (byte)'|')
            {
                p += 2;
                bool right = EvalPpAnd(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
                left = left || right;
            }
            else
            {
                break;
            }
        }
        return left;
    }

    private static bool EvalPpAnd(
        ReadOnlySpan<byte> src,
        ref int p,
        int lineEnd,
        int sourceId,
        ref Vec<byte> scratch,
        in StrMap<int> defines,
        ref DiagnosticBag diags,
        ref bool ok)
    {
        bool left = EvalPpEq(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
        while (ok)
        {
            SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
            if (p + 1 < lineEnd && src[p] == (byte)'&' && src[p + 1] == (byte)'&')
            {
                p += 2;
                bool right = EvalPpEq(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
                left = left && right;
            }
            else
            {
                break;
            }
        }
        return left;
    }

    private static bool EvalPpEq(
        ReadOnlySpan<byte> src,
        ref int p,
        int lineEnd,
        int sourceId,
        ref Vec<byte> scratch,
        in StrMap<int> defines,
        ref DiagnosticBag diags,
        ref bool ok)
    {
        bool left = EvalPpUnary(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
        while (ok)
        {
            SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
            if (p + 1 < lineEnd && src[p] == (byte)'=' && src[p + 1] == (byte)'=')
            {
                p += 2;
                bool right = EvalPpUnary(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
                left = left == right;
            }
            else if (p + 1 < lineEnd && src[p] == (byte)'!' && src[p + 1] == (byte)'=')
            {
                p += 2;
                bool right = EvalPpUnary(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
                left = left != right;
            }
            else
            {
                break;
            }
        }
        return left;
    }

    private static bool EvalPpUnary(
        ReadOnlySpan<byte> src,
        ref int p,
        int lineEnd,
        int sourceId,
        ref Vec<byte> scratch,
        in StrMap<int> defines,
        ref DiagnosticBag diags,
        ref bool ok)
    {
        SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
        if (IsAtPpEndOfDirective(src, p, lineEnd))
        {
            ok = false;
            return false;
        }
        if (src[p] == (byte)'!' && !(p + 1 < lineEnd && src[p + 1] == (byte)'='))
        {
            p += 1;
            return !EvalPpUnary(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
        }
        if (src[p] == (byte)'(')
        {
            p += 1;
            bool inner = EvalPpOr(src, ref p, lineEnd, sourceId, ref scratch, in defines, ref diags, ref ok);
            SkipPpInlineSpace(src, ref p, lineEnd, sourceId, ref diags);
            if (p >= lineEnd || src[p] != (byte)')')
            {
                ok = false;
                return false;
            }
            p += 1;
            return inner;
        }
        int start = p;
        if (TryLexNormalizedIdent(src, p, lineEnd, ref scratch, out int ppUnLen, out bool ppHadEscOrCf, out bool ppBadEsc))
        {
            p += ppUnLen;
            if (ppBadEsc)
            {
                ok = false;
                return false;
            }
            if (!ppHadEscOrCf)
            {
                ReadOnlySpan<byte> rawWord = src.Slice(start, ppUnLen);
                if (Bytes.Eq(rawWord, "true"u8)) return true;
                if (Bytes.Eq(rawWord, "false"u8)) return false;
                return defines.Contains(rawWord);
            }
            return defines.Contains(scratch.AsReadOnlySpan());
        }
        ok = false;
        return false;
    }
}
