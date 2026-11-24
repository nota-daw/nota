// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The mini LED display a device page draws on (specs/114): a fixed 128 × 48 grid of dots, each
// lit at one of 16 levels (0 = off). Nota draws the picture here and sends the dots — the phone,
// and later other clients, only light them, so every client shows the same pixels. The
// primitives are what the device pictures need and nothing more: dots, lines, a curve joined
// column to column with an optional fill, rectangles and a 3 × 5 upper-case font. Drawing is
// max-blend (a dimmer layer never darkens a brighter one) unless a call says it overwrites.
// RLE4 is the wire format: one byte per run of up to 16 equal dots, (count − 1) << 4 | level.

using System.Globalization;

namespace Nota.Application.Displays;

public sealed class LedFrame : IEquatable<LedFrame>
{
    public const int W = 128, H = 48, Levels = 16, Max = 15;
    /// <summary>The graph area under the header row and its rule.</summary>
    public const int Top = 9, Bottom = 47;

    public byte[] Dots { get; } = new byte[W * H];

    public void Clear() => Array.Clear(Dots);

    public int this[int x, int y] => x >= 0 && y >= 0 && x < W && y < H ? Dots[y * W + x] : 0;

    /// <summary>Light a dot at least to <paramref name="level"/>.</summary>
    public void Dot(double x, double y, int level)
    {
        int xi = (int)Math.Round(x), yi = (int)Math.Round(y);
        if ((uint)xi >= W || (uint)yi >= H || level <= 0) return;
        int i = yi * W + xi;
        byte l = (byte)Math.Min(Max, level);
        if (l > Dots[i]) Dots[i] = l;
    }

    /// <summary>Set a dot to exactly <paramref name="level"/> (0 turns it off).</summary>
    public void Put(double x, double y, int level)
    {
        int xi = (int)Math.Round(x), yi = (int)Math.Round(y);
        if ((uint)xi >= W || (uint)yi >= H) return;
        Dots[yi * W + xi] = (byte)Math.Clamp(level, 0, Max);
    }

    public void Line(double x0, double y0, double x1, double y1, int level, bool dotted = false)
    {
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1)) return;
        int ax = Clip(x0), ay = Clip(y0), bx = Clip(x1), by = Clip(y1);
        int dx = Math.Abs(bx - ax), dy = -Math.Abs(by - ay), sx = ax < bx ? 1 : -1, sy = ay < by ? 1 : -1, e = dx + dy, n = 0;
        for (int guard = 0; guard < 4096; guard++)
        {
            if (!dotted || (n & 1) == 0) Dot(ax, ay, level);
            n++;
            if (ax == bx && ay == by) break;
            int e2 = 2 * e;
            if (e2 >= dy) { e += dy; ax += sx; }
            if (e2 <= dx) { e += dx; ay += sy; }
        }
    }

    private static int Clip(double v) => (int)Math.Round(Math.Clamp(v, -1000, 1000));

    public void HLine(double x0, double x1, double y, int level, bool dotted = false)
    {
        int a = (int)Math.Round(Math.Min(x0, x1)), b = (int)Math.Round(Math.Max(x0, x1));
        for (int x = Math.Max(0, a); x <= Math.Min(W - 1, b); x++)
            if (!dotted || (x & 1) == 0) Dot(x, y, level);
    }

    public void VLine(double x, double y0, double y1, int level, bool dotted = false)
    {
        int a = (int)Math.Round(Math.Min(y0, y1)), b = (int)Math.Round(Math.Max(y0, y1));
        for (int y = Math.Max(0, a); y <= Math.Min(H - 1, b); y++)
            if (!dotted || (y & 1) == 0) Dot(x, y, level);
    }

    public void Rect(double x, double y, double w, double h, int level)
    {
        int x0 = (int)Math.Round(x), y0 = (int)Math.Round(y), x1 = (int)Math.Round(x + w), y1 = (int)Math.Round(y + h);
        for (int j = Math.Max(0, y0); j < Math.Min(H, y1); j++)
            for (int i = Math.Max(0, x0); i < Math.Min(W, x1); i++) Dot(i, j, level);
    }

    /// <summary>An outline.</summary>
    public void Box(double x, double y, double w, double h, int level)
    {
        HLine(x, x + w - 1, y, level);
        HLine(x, x + w - 1, y + h - 1, level);
        VLine(x, y, y + h - 1, level);
        VLine(x + w - 1, y, y + h - 1, level);
    }

    /// <summary>The function <paramref name="y"/> across columns <paramref name="x0"/>..<paramref name="x1"/>,
    /// each column joined to the previous one, with dots below it down to <paramref name="bottom"/>
    /// lit at <paramref name="fill"/> (0 = no fill). Off-screen values are clamped just outside.</summary>
    public void Curve(Func<int, double> y, int x0, int x1, int level, int fill = 0, int bottom = Bottom)
    {
        double? prev = null;
        for (int x = Math.Max(0, x0); x <= Math.Min(W - 1, x1); x++)
        {
            double v = y(x);
            if (!double.IsFinite(v)) { prev = null; continue; }
            v = Math.Clamp(v, -2, H + 1);
            if (fill > 0) for (int j = (int)Math.Round(v) + 1; j <= bottom; j++) Dot(x, j, fill);
            if (prev is { } p) Line(x - 1, p, x, v, level); else Dot(x, v, level);
            prev = v;
        }
    }

    // ---- text ---------------------------------------------------------------------------------

    /// <summary>Width in dots of <paramref name="s"/> in the 3 × 5 font.</summary>
    public static int TextWidth(string s) => s.Length == 0 ? 0 : Normalize(s).Length * 4 - 1;

    /// <summary>Draw <paramref name="s"/> with its top-left at (x, y); returns the x after it.
    /// <paramref name="overwrite"/> sets the glyph dots exactly (dark text on a lit bar).</summary>
    public int Text(string s, int x, int y, int level, bool overwrite = false)
    {
        foreach (char ch in Normalize(s))
        {
            ushort g = Glyph(ch);
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 3; c++)
                    if ((g >> (14 - (r * 3 + c)) & 1) != 0)
                    {
                        if (overwrite) Put(x + c, y + r, level); else Dot(x + c, y + r, level);
                    }
            x += 4;
        }
        return x;
    }

    /// <summary><paramref name="s"/> cut to fit <paramref name="width"/> dots.</summary>
    public static string Fit(string s, int width)
    {
        s = Normalize(s);
        int max = Math.Max(0, (width + 1) / 4);
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>Upper case, the characters the font has; anything else becomes a space.</summary>
    public static string Normalize(string s)
    {
        var b = new System.Text.StringBuilder(s.Length);
        foreach (char raw in s.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
            char ch = char.ToUpperInvariant(raw) switch
            {
                '−' or '–' or '—' => '-',
                '›' or '→' => '>',
                '‹' or '←' => '<',
                '×' => 'X',
                '·' or '•' => '.',
                '♯' => '#',
                '±' => '+',
                char c => c,
            };
            b.Append(Glyph(ch) == 0 && ch != ' ' ? ' ' : ch);
        }
        return b.ToString();
    }

    // 15 bits per glyph, rows top to bottom, three columns each, the top-left dot the high bit.
    private static ushort Glyph(char c) => c switch
    {
        'A' => 0b010_101_111_101_101, 'B' => 0b110_101_110_101_110, 'C' => 0b011_100_100_100_011,
        'D' => 0b110_101_101_101_110, 'E' => 0b111_100_110_100_111, 'F' => 0b111_100_110_100_100,
        'G' => 0b011_100_101_101_011, 'H' => 0b101_101_111_101_101, 'I' => 0b111_010_010_010_111,
        'J' => 0b001_001_001_101_010, 'K' => 0b101_101_110_101_101, 'L' => 0b100_100_100_100_111,
        'M' => 0b101_111_111_101_101, 'N' => 0b110_101_101_101_101, 'O' => 0b010_101_101_101_010,
        'P' => 0b110_101_110_100_100, 'Q' => 0b010_101_101_110_011, 'R' => 0b110_101_110_101_101,
        'S' => 0b011_100_010_001_110, 'T' => 0b111_010_010_010_010, 'U' => 0b101_101_101_101_111,
        'V' => 0b101_101_101_101_010, 'W' => 0b101_101_111_111_101, 'X' => 0b101_101_010_101_101,
        'Y' => 0b101_101_010_010_010, 'Z' => 0b111_001_010_100_111,
        '0' => 0b111_101_101_101_111, '1' => 0b010_110_010_010_111, '2' => 0b110_001_010_100_111,
        '3' => 0b110_001_010_001_110, '4' => 0b101_101_111_001_001, '5' => 0b111_100_110_001_110,
        '6' => 0b011_100_111_101_111, '7' => 0b111_001_010_010_010, '8' => 0b111_101_111_101_111,
        '9' => 0b111_101_111_001_110,
        '.' => 0b000_000_000_000_010, ',' => 0b000_000_000_010_100, '-' => 0b000_000_111_000_000,
        '+' => 0b000_010_111_010_000, '%' => 0b101_001_010_100_101, '/' => 0b001_001_010_100_100,
        ':' => 0b000_010_000_010_000, '>' => 0b100_010_001_010_100, '<' => 0b001_010_100_010_001,
        '&' => 0b010_101_010_101_011, '#' => 0b101_111_101_111_101, '(' => 0b010_100_100_100_010,
        ')' => 0b010_001_001_001_010, '=' => 0b000_111_000_111_000, '\'' => 0b010_010_000_000_000,
        '_' => 0b000_000_000_000_111, '!' => 0b010_010_010_000_010, '?' => 0b110_001_010_000_010,
        '*' => 0b000_101_010_101_000, '^' => 0b010_101_000_000_000, '~' => 0b000_011_110_000_000,
        _ => 0,
    };

    // ---- wire ---------------------------------------------------------------------------------

    /// <summary>The dots as RLE4: one byte per run of 1–16 equal dots, (count − 1) &lt;&lt; 4 | level.</summary>
    public byte[] Rle4()
    {
        var o = new List<byte>(512);
        var d = Dots;
        int i = 0;
        while (i < d.Length)
        {
            byte v = d[i];
            int n = 1;
            while (i + n < d.Length && d[i + n] == v && n < 16) n++;
            o.Add((byte)(((n - 1) << 4) | v));
            i += n;
        }
        return o.ToArray();
    }

    /// <summary>A frame from RLE4 bytes; null when they don't make exactly W × H dots.</summary>
    public static LedFrame? FromRle4(ReadOnlySpan<byte> rle)
    {
        var f = new LedFrame();
        int at = 0;
        foreach (byte b in rle)
        {
            int n = (b >> 4) + 1;
            if (at + n > f.Dots.Length) return null;
            Array.Fill(f.Dots, (byte)(b & 15), at, n);
            at += n;
        }
        return at == f.Dots.Length ? f : null;
    }

    public bool Equals(LedFrame? other) => other is not null && Dots.AsSpan().SequenceEqual(other.Dots);
    public override bool Equals(object? obj) => Equals(obj as LedFrame);
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.AddBytes(Dots);
        return h.ToHashCode();
    }

    /// <summary>The frame as text (tests, MCP): ' ' off, then .:-=+*#%@ by level.</summary>
    public string ToAscii()
    {
        const string ramp = " ..::--==++**##%@";
        var b = new System.Text.StringBuilder((W + 1) * H);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++) b.Append(ramp[Dots[y * W + x]]);
            b.Append('\n');
        }
        return b.ToString();
    }
}
