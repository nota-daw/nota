// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The accent colour the user chose (spec 084, #311) and how a theme wears it. Pure: colours
// are 0xRRGGBB, no UI, so the smoke test can run every preset in every theme. A theme keeps
// its own accent family (orange); a choice re-colours that family in OKLCH — each token keeps
// its lightness relation to the theme's orange and takes the new hue — and, only when the
// readability bar would fail, moves the accent's lightness by the smallest step that passes
// (specs/084 research R2–R3). Orange is the theme file itself, untouched.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nota.Application;

/// <summary>A preset id, or <see cref="Accent.Custom"/> with the user's colour (0xRRGGBB).</summary>
public sealed record AccentChoice(string Preset, uint? Custom = null)
{
    public static readonly AccentChoice Default = new(Accent.Orange);
    /// <summary>Orange: the theme files as they are.</summary>
    public bool IsDefault => Preset == Accent.Orange;
}

/// <summary>A curated accent: its OKLCH chroma and hue; lightness comes from the theme.
/// Orange carries none — it is the theme's own accent.</summary>
public sealed record AccentPreset(string Id, double Chroma, double Hue);

public static class Accent
{
    public const string Orange = "orange";
    public const string Custom = "custom";

    /// <summary>The presets in the order the Accent row shows them (research R4): spread round
    /// the wheel, clear of the mute amber, record red, meter green and solo blue.</summary>
    public static readonly IReadOnlyList<AccentPreset> Presets = new AccentPreset[]
    {
        new(Orange, 0, 0),
        new("lime", 0.15, 125),
        new("aqua", 0.12, 185),
        new("sky", 0.13, 224),
        new("violet", 0.15, 295),
        new("magenta", 0.16, 330),
        new("pink", 0.15, 355),
    };

    public static AccentPreset? Find(string? id) => Presets.FirstOrDefault(p => p.Id == id);

    /// <summary>What settings.json says, or Orange: an empty, unknown or malformed value
    /// falls back without throwing, and nothing is written.</summary>
    public static AccentChoice Resolve(Settings s)
    {
        var id = s.Accent;
        if (id == Custom)
            return TryParseHex(s.CustomAccent, out var rgb) ? new AccentChoice(Custom, rgb) : AccentChoice.Default;
        return Find(id) is { } p ? new AccentChoice(p.Id) : AccentChoice.Default;
    }

    /// <summary>Write a choice. A custom colour is also remembered in <see cref="Settings.CustomAccent"/>,
    /// which picking a preset keeps, so Custom offers it again.</summary>
    public static void Store(Settings s, AccentChoice c)
    {
        s.Accent = c.IsDefault ? "" : c.Preset;
        if (c.Preset == Custom && c.Custom is { } rgb) s.CustomAccent = Hex(rgb);
    }

    /// <summary>The last custom colour, if one was ever chosen.</summary>
    public static uint? RememberedCustom(Settings s) => TryParseHex(s.CustomAccent, out var rgb) ? rgb : null;

    /// <summary>#RGB or #RRGGBB, the # optional, any case.</summary>
    public static bool TryParseHex(string? text, out uint rgb)
    {
        rgb = 0;
        var t = (text ?? "").Trim();
        if (t.StartsWith('#')) t = t[1..];
        if (t.Length == 3) t = string.Concat(t.Select(ch => new string(ch, 2)));
        return t.Length == 6 && uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    public static string Hex(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
}

/// <summary>Why a custom colour cannot be the accent in a theme.</summary>
public enum AccentRefusalReason { Grey, Clash, Unreadable }

public sealed record AccentRefusal(AccentRefusalReason Reason, string? ClashWith = null);

/// <summary>The accent family as one theme wears a choice: the tokens to overlay, whether the
/// lightness was moved, or why it was refused (custom colours only).</summary>
public sealed record AccentResult(IReadOnlyDictionary<string, uint>? Tokens, bool Adjusted, AccentRefusal? Refusal);

public static class AccentColours
{
    /// <summary>Tokens that are the accent itself: they move with its lightness.</summary>
    public static readonly string[] SolidTokens =
    {
        "Accent", "AccentBright", "AccentHover", "AccentDeep", "AccentPale",
        "SystemAccent", "SystemAccentLight1", "SystemAccentLight2", "SystemAccentLight3",
        "SystemAccentDark1", "SystemAccentDark2", "SystemAccentDark3",
    };

    /// <summary>Tokens that are the accent washed over the theme's surfaces: they keep the
    /// theme's lightness, or a dark custom colour would sink the "picked" tint into the ground.</summary>
    public static readonly string[] BlendTokens = { "AccentDim", "AccentEdge", "AccentMute", "AccentSoft", "AccentSubtle", "BorderBrass" };

    /// <summary>Every token a choice replaces.</summary>
    public static IEnumerable<string> Tokens => SolidTokens.Concat(BlendTokens).Append("TextOnAccent");

    /// <summary>The state colours an accent must not be mistaken for (T3.8 + the meter green).</summary>
    public static readonly string[] States = { "Record", "Mute", "Solo", "Success" };

    public const uint InkDark = 0x17181A, InkLight = 0xFFFFFF;
    public const double MinChroma = 0.05;     // below this a colour reads as grey
    public const double CustomShift = 0.12;   // most a custom colour's lightness may move
    public const double PresetShift = 0.40;
    private const double Step = 0.005;

    /// <summary>How <paramref name="theme"/> (token → 0xRRGGBB) wears <paramref name="choice"/>.</summary>
    public static AccentResult Wear(IReadOnlyDictionary<string, uint> theme, AccentChoice choice)
    {
        if (choice.IsDefault || (choice.Preset == Accent.Custom ? choice.Custom is null : Accent.Find(choice.Preset) is null))
            return new AccentResult(Tokens.ToDictionary(t => t, t => theme[t]), false, null);

        var (la, _, _) = OkLch(theme["Accent"]);
        double l1, c1, h1, reach;
        bool custom = choice.Preset == Accent.Custom;
        if (custom)
        {
            (l1, c1, h1) = OkLch(choice.Custom!.Value);
            if (c1 < MinChroma) return new AccentResult(null, false, new AccentRefusal(AccentRefusalReason.Grey));
            // Distinctness is judged on the colour itself, so moving its lightness can never
            // talk a copy of the record red past the bar.
            if (Clash(theme, choice.Custom.Value) is { } with)
                return new AccentResult(null, false, new AccentRefusal(AccentRefusalReason.Clash, with));
            reach = CustomShift;
        }
        else
        {
            var p = Accent.Find(choice.Preset)!;
            (l1, c1, h1) = (la, p.Chroma, p.Hue);
            reach = PresetShift;
        }

        int steps = (int)Math.Round(reach / Step);
        for (int n = 0; n <= steps; n++)
            foreach (var sign in n == 0 ? new[] { 0 } : new[] { 1, -1 })
            {
                double l = l1 + sign * n * Step;
                if (l <= 0.2 || l >= 0.95) continue;
                var tokens = Tint(theme, l, c1, h1);
                if (Failures(theme, tokens, includeTracks: !custom).Count == 0)
                    return new AccentResult(tokens, n != 0, null);
            }
        return new AccentResult(null, false, new AccentRefusal(AccentRefusalReason.Unreadable));
    }

    /// <summary>The accent family re-coloured to lightness <paramref name="l1"/> (of the
    /// accent itself), chroma <paramref name="c1"/> and hue <paramref name="h1"/>.</summary>
    public static Dictionary<string, uint> Tint(IReadOnlyDictionary<string, uint> theme, double l1, double c1, double h1)
    {
        var (la, ca, ha) = OkLch(theme["Accent"]);
        var outp = new Dictionary<string, uint>();
        foreach (var k in SolidTokens.Concat(BlendTokens))
        {
            var (lk, ck, hk) = OkLch(theme[k]);
            double l = lk + (Array.IndexOf(SolidTokens, k) >= 0 ? l1 - la : 0);
            outp[k] = InGamut(l, ca > 1e-6 ? ck * (c1 / ca) : c1, hk + (h1 - ha));
        }
        outp["TextOnAccent"] = InkOn(outp["Accent"]);
        return outp;
    }

    /// <summary>The rules a derived accent fails in a theme (research R3), empty when it
    /// passes: T3.4 accent line on the grounds, T3.5 ink on the accent, T3.8 + the meter green
    /// by ΔE and by Δa*b*, and — presets only — every track colour.</summary>
    public static List<string> Failures(IReadOnlyDictionary<string, uint> theme, IReadOnlyDictionary<string, uint> tokens, bool includeTracks)
    {
        var f = new List<string>();
        uint accent = tokens["Accent"], bright = tokens["AccentBright"];
        foreach (var s in new[] { "BgApp", "Panel", "BgSunken" })
            if (Contrast(bright, theme[s]) < 2.3) f.Add($"T3.4 AccentBright/{s}");
        if (Contrast(InkOn(accent), accent) < 4.5) f.Add("T3.5 ink on Accent");
        foreach (var s in States)
        {
            if (DeltaE76(accent, theme[s]) < 15) f.Add($"ΔE Accent/{s}");
            if (DeltaAb(accent, theme[s]) < 15) f.Add($"Δab Accent/{s}");
        }
        if (includeTracks)
            for (int i = 1; i <= 9; i++)
                if (DeltaE76(accent, theme[$"Track{i}"]) < 15) f.Add($"ΔE Accent/Track{i}");
        return f;
    }

    /// <summary>The state colour <paramref name="rgb"/> would be mistaken for, if any.</summary>
    public static string? Clash(IReadOnlyDictionary<string, uint> theme, uint rgb) =>
        States.FirstOrDefault(s => DeltaE76(rgb, theme[s]) < 15 || DeltaAb(rgb, theme[s]) < 15);

    // ---- colour maths ------------------------------------------------------------------

    /// <summary>Dark ink or white, whichever reads better on the fill (as NotaPalette.ReadableInkOn).</summary>
    public static uint InkOn(uint fill) => Contrast(InkDark, fill) >= Contrast(InkLight, fill) ? InkDark : InkLight;

    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint c)
    {
        static double Ch(uint v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch((c >> 16) & 0xFF) + 0.7152 * Ch((c >> 8) & 0xFF) + 0.0722 * Ch(c & 0xFF);
    }

    /// <summary>CIE76 colour difference (as the readability bar measures it).</summary>
    public static double DeltaE76(uint a, uint b)
    {
        var (l1, a1, b1) = Lab(a);
        var (l2, a2, b2) = Lab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    /// <summary>Colour difference with lightness left out: a lighter copy of the mute amber is
    /// still the mute amber.</summary>
    public static double DeltaAb(uint a, uint b)
    {
        var (_, a1, b1) = Lab(a);
        var (_, a2, b2) = Lab(b);
        return Math.Sqrt((a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    private static double Lin(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    private static double Gam(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;

    private static (double R, double G, double B) Linear(uint c) =>
        (Lin(((c >> 16) & 0xFF) / 255.0), Lin(((c >> 8) & 0xFF) / 255.0), Lin((c & 0xFF) / 255.0));

    private static (double L, double A, double B) Lab(uint c)
    {
        var (r, g, b) = Linear(c);
        double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, y = 0.2126 * r + 0.7152 * g + 0.0722 * b,
               z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    /// <summary>OKLCH of an sRGB colour: lightness 0–1, chroma, hue in degrees.</summary>
    public static (double L, double C, double H) OkLch(uint c)
    {
        var (r, g, b) = Linear(c);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        double h = Math.Atan2(B, A) * 180 / Math.PI;
        return (L, Math.Sqrt(A * A + B * B), h < 0 ? h + 360 : h);
    }

    // OKLCH → linear sRGB; false when outside the gamut.
    private static bool FromOkLch(double L, double C, double h, out double r, out double g, out double b)
    {
        double A = C * Math.Cos(h * Math.PI / 180), B = C * Math.Sin(h * Math.PI / 180);
        double l = Math.Pow(L + 0.3963377774 * A + 0.2158037573 * B, 3);
        double m = Math.Pow(L - 0.1055613458 * A - 0.0638541728 * B, 3);
        double s = Math.Pow(L - 0.0894841775 * A - 1.2914855480 * B, 3);
        r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        const double e = 1e-4;
        return r >= -e && r <= 1 + e && g >= -e && g <= 1 + e && b >= -e && b <= 1 + e;
    }

    /// <summary>The sRGB colour nearest to an OKLCH value: chroma is reduced until it fits.</summary>
    public static uint InGamut(double L, double C, double h)
    {
        L = Math.Clamp(L, 0, 1);
        double r = 0, g = 0, b = 0;
        bool ok = false;
        for (int i = 0; i < 60 && !(ok = FromOkLch(L, C, h, out r, out g, out b)); i++) C *= 0.95;
        if (!ok) FromOkLch(L, 0, h, out r, out g, out b);
        static uint Byte(double v) => (uint)Math.Round(Math.Clamp(Gam(Math.Clamp(v, 0, 1)), 0, 1) * 255);
        return (Byte(r) << 16) | (Byte(g) << 8) | Byte(b);
    }
}
