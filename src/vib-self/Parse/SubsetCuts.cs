namespace Vibranium.Self;

// All frontends use one diagnostic per source file per cut. Parsing recovery and
// semantic traversal need not visit occurrences in source order, so retain the
// earliest token rather than the first visit.
public static class SubsetCuts
{
    public static ReadOnlySpan<byte> Message(int cut)
    {
        if (cut == 10) return "ref readonly parameters are not supported in Vibranium v0.2; use an in parameter."u8;
        if (cut == 11) return "Declaring an identifier named _ is not supported in Vibranium v0.2; use _ only as a discard."u8;
        if (cut == 12) return "Optional parameters are not supported in Vibranium v0.2; declare an overload instead."u8;
        if (cut == 13) return "Named arguments out of parameter order are not supported in Vibranium v0.2; pass arguments in parameter order."u8;
        if (cut == 14) return "Assigning to this is not supported in Vibranium v0.2; assign the fields individually."u8;
        if (cut == 15) return "Conditional ref expressions (c ? ref a : ref b) are not supported in Vibranium v0.2; use an if statement."u8;
        if (cut == 16) return "Collection expressions nested in another collection expression or used as a conditional or switch-expression branch are not supported in Vibranium v0.2; assign the collection to a local first."u8;
        if (cut == 17) return "The native-sized integer types nint and nuint are not supported in Vibranium v0.2; use long or ulong."u8;
        if (cut == 18) return "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }."u8;
        if (cut == 19) return "Constant conditions, guards and switch selectors are not supported in Vibranium v0.2; only while (true), do ... while (true) and for (;;) may use a constant condition."u8;
        if (cut == 24) return "Property patterns on Option and Result cases are not supported in Vibranium v0.2; use the positional form, such as Some<T>(var x)."u8;
        if (cut == 25) return "Conditional and short-circuit expressions in a ref reassignment are not supported in Vibranium v0.2; compute the value in a local first, or use an if statement."u8;
        return "Relational patterns and and-patterns are not supported in Vibranium v0.2; use a when guard or an if condition."u8;
    }

    public static bool IsMessage(ReadOnlySpan<byte> message)
    {
        for (int cut = 10; cut <= 20; cut++)
            if (Bytes.Eq(message, Message(cut))) return true;
        return Bytes.Eq(message, Message(24)) || Bytes.Eq(message, Message(25));
    }

    public static void Normalize(ref DiagnosticBag diags)
    {
        diags.Sort();
        var conditionalSources = new IntMap<int>(8);
        for (int i = 0; i < diags.Items.Count; i++)
        {
            DiagEntry entry = diags.Items[i];
            if (Bytes.Eq(diags.MsgPool.Get(entry.Message), "Conditional compilation directives (#if, #elif, #else, #endif, #define, #undef) are not supported in Vibranium v0.2; .NET and native builds must compile the same code."u8))
                conditionalSources.Set(entry.SourceId, 1);
        }
        int write = 0;
        for (int read = 0; read < diags.Items.Count; read++)
        {
            DiagEntry entry = diags.Items[read];
            bool duplicate = false;
            // C5 still suppresses ordinary recovery cascades, but never another
            // canonical subset cut in the active code of the same source file.
            if (conditionalSources.Get(entry.SourceId).IsSome &&
                diags.MsgPool.Get(entry.Message).IndexOf("Vibranium v0.2"u8) < 0) continue;
            if (entry.Code == VibCodes.Unsupported && IsMessage(diags.MsgPool.Get(entry.Message)))
            {
                for (int previous = 0; previous < write; previous++)
                {
                    DiagEntry prior = diags.Items[previous];
                    if (prior.SourceId == entry.SourceId && prior.Code == entry.Code &&
                        Bytes.Eq(diags.MsgPool.Get(prior.Message), diags.MsgPool.Get(entry.Message))) duplicate = true;
                }
            }
            if (duplicate) continue;
            diags.Items[write] = entry;
            write += 1;
        }
        diags.Items.Truncate(write);
    }

    public static void Report(int cut, Token token, ref DiagnosticBag diags)
    {
        ReadOnlySpan<byte> message = Message(cut);
        for (int i = 0; i < diags.Items.Count; i++)
        {
            DiagEntry entry = diags.Items[i];
            if (entry.SourceId != token.SourceId || entry.Code != VibCodes.Unsupported ||
                !Bytes.Eq(diags.MsgPool.Get(entry.Message), message)) continue;
            if (entry.ByteOffset > token.Start)
                diags.Items[i] = new DiagEntry(VibCodes.Unsupported, token.SourceId, token.Start, token.Length, entry.Message);
            return;
        }
        diags.ReportTok(VibCodes.Unsupported, token, message);
    }
}
