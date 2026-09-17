// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// How a number is set (almanac § Labels and numbers):
//
//   the unit follows a thin space          4.6 s · −3.3 dB · 72 %
//   the minus is typographic (U+2212)      a plus only where the sign matters: +0.8 dB
//   the precision is fixed per unit        dB one decimal · tempo two · percent whole ·
//                                          Hz whole up to 999, then 3.2 k
//   the width never jumps across 10 or 100
//
// Culture is the display culture the app runs in: invariant (a point, never a comma,
// whatever the OS locale) with U+2212 as the negative sign, so an implicit {v:0.0} already
// prints −3.3. Parsing with it still accepts a typed hyphen. Persistence never goes through
// it — project, preset and settings files are JSON and culture-free.

using System;
using System.Globalization;

namespace Nota.App;

public static class NotaNum
{
    public const string Thin = "\u2009";
    public const string Minus = "\u2212";

    public static readonly CultureInfo Culture = MakeCulture();

    private static CultureInfo MakeCulture()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NegativeSign = Minus;
        return CultureInfo.ReadOnly(c);
    }

    /// <summary>Make the display culture the default for every thread. Call once at start-up.</summary>
    public static void Install()
    {
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.CurrentCulture = Culture;
    }

    /// <summary>Format an interpolated string in the display culture.</summary>
    public static string F(FormattableString s) => s.ToString(Culture);

    public static string Str(double v, string format) => v.ToString(format, Culture);

    /// <summary>Decibels, one decimal: "−3.3 dB"; "+0.8 dB" when <paramref name="signed"/>; "−∞ dB" at silence.</summary>
    public static string Db(double db, bool signed = false, double floor = -99)
    {
        if (db <= floor || double.IsNegativeInfinity(db)) return Minus + "∞" + Thin + "dB";
        return db.ToString(signed ? "+0.0;\u22120.0;0.0" : "0.0", Culture) + Thin + "dB";
    }

    /// <summary>Frequency: whole Hz up to 999, then "3.2 k" (one decimal below 10 k, whole above).</summary>
    public static string Hz(double hz)
    {
        if (hz < 999.5) return hz.ToString("0", Culture) + Thin + "Hz";
        double k = hz / 1000;
        return (k < 9.95 ? k.ToString("0.0", Culture) : k.ToString("0", Culture)) + Thin + "k";
    }

    /// <summary>Percent, whole: "72 %". <paramref name="frac"/> is 0..1.</summary>
    public static string Pct(double frac) => (frac * 100).ToString("0", Culture) + Thin + "%";

    /// <summary>Tempo, two decimals: "120.00".</summary>
    public static string Bpm(double bpm) => bpm.ToString("0.00", Culture);

    /// <summary>Time: "240 ms" below a second, "1.25 s" above.</summary>
    public static string Time(double seconds)
        => seconds < 0.9995 ? (seconds * 1000).ToString("0", Culture) + Thin + "ms" : seconds.ToString("0.00", Culture) + Thin + "s";

    /// <summary>A number followed by its unit, thin-spaced.</summary>
    public static string Unit(double v, string format, string unit) => v.ToString(format, Culture) + Thin + unit;
}
