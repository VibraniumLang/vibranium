namespace Vibranium.Std;

public static class Utf8
{
    public static bool IsValid(global::System.ReadOnlySpan<byte> text)
    {
        int n = text.Length;
        int i = 0;
        while (i < n)
        {
            int b = text[i];
            if (b < 0x80)
            {
                i += 1;
                continue;
            }
            int need;
            int min;
            int cp;
            if ((b & 0xE0) == 0xC0) { need = 1; min = 0x80; cp = b & 0x1F; }
            else if ((b & 0xF0) == 0xE0) { need = 2; min = 0x800; cp = b & 0x0F; }
            else if ((b & 0xF8) == 0xF0) { need = 3; min = 0x10000; cp = b & 0x07; }
            else { return false; }
            if (n - i <= need) return false;
            for (int k = 1; k <= need; k++)
            {
                int c = text[i + k];
                if ((c & 0xC0) != 0x80) return false;
                cp = (cp << 6) | (c & 0x3F);
            }
            if (cp < min || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) return false;
            i += need + 1;
        }
        return true;
    }

    public static bool IsValid(string text) => IsValid(text.AsUtf8());

    public static bool IsBoundary(global::System.ReadOnlySpan<byte> text, int index)
    {
        if (index == text.Length) return true;
        return (text[index] & 0xC0) != 0x80;
    }

    public static bool IsBoundary(string text, int index) => IsBoundary(text.AsUtf8(), index);
}
