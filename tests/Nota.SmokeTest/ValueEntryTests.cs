// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Typed value entry (specs/035-typed-value-entry): the parser reads what a user types and the
// search finds the control position whose read-out shows it. Checked against plain read-out
// formatters shaped like the app's (dB with −∞, log Hz → kHz, ms → s, pan L/C/R, % with
// "off"). Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --valueentry`.

using System.Globalization;
using Nota.App;

namespace Nota.SmokeTest;

internal static class ValueEntryTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // −60 … +12 dB, 0.1 dB steps, −∞ at the bottom.
    private static string Db(double n) => n <= 0 ? "-∞ dB" : string.Format(Inv, "{0:0.0} dB", -60 + n * 72);
    // 20 Hz … 20 kHz, log.
    private static string Hz(double n)
    {
        double f = 20 * Math.Pow(1000, n);
        return f < 1000 ? string.Format(Inv, "{0:0} Hz", f) : string.Format(Inv, "{0:0.00} kHz", f / 1000);
    }
    // 1 ms … 10 s, log.
    private static string Time(double n)
    {
        double t = 0.001 * Math.Pow(10000, n);
        return t < 1 ? string.Format(Inv, "{0:0} ms", t * 1000) : string.Format(Inv, "{0:0.00} s", t);
    }
    // 100L … C … 100R.
    private static string Pan(double n)
    {
        double p = n * 2 - 1;
        int pc = (int)Math.Round(Math.Abs(p) * 100);
        return pc == 0 ? "C" : $"{pc}{(p < 0 ? "L" : "R")}";
    }
    // off, then 1 … 100 %.
    private static string Amount(double n) => n < 0.02 ? "off" : string.Format(Inv, "{0:0} %", n * 100);

    private static bool Near(double? a, double b, double tol = 1e-9) => a is { } v && Math.Abs(v - b) <= tol;

    private static string Typed(string typed, Func<double, string> at, double current)
        => ValueText.Solve(typed, at(current), at) is { } n ? at(n) : "(none)";

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // ---- the parser ----
        yield return (Near(ValueText.Read("1.2k", out _, out _), 1200), "value entry: \"1.2k\" reads 1200");
        yield return (Near(ValueText.Read("1.2kHz", out _, out bool u1), 1200) && u1, "value entry: \"1.2kHz\" reads 1200 with a unit");
        yield return (Near(ValueText.Read("12ms", out _, out _), 0.012), "value entry: \"12ms\" reads 0.012 s");
        yield return (Near(ValueText.Read("500us", out _, out _), 500e-6) && Near(ValueText.Read("500µs", out _, out _), 500e-6),
            "value entry: µs / us read as microseconds");
        yield return (Near(ValueText.Read("1/4", out _, out _), 0.25), "value entry: a fraction reads as its quotient");
        yield return (Near(ValueText.Read("-6,5", out _, out _), -6.5) && Near(ValueText.Read("−6.5 dB", out _, out _), -6.5),
            "value entry: decimal comma and the typographic minus");
        yield return (Near(ValueText.Read("30L", out _, out _), -30) && Near(ValueText.Read("L30", out _, out _), -30)
                      && Near(ValueText.Read("30 R", out _, out _), 30) && Near(ValueText.Read("C", out _, out _), 0),
            "value entry: pan written 30L, L30, 30 R and C");
        yield return (ValueText.Read("-∞", out _, out _) is double.NegativeInfinity && ValueText.Read("inf", out _, out _) is double.PositiveInfinity,
            "value entry: ∞ and inf, signed");
        yield return (ValueText.Read("abc", out _, out _) is null && ValueText.Read("", out _, out _) is null,
            "value entry: text with no number reads as nothing");

        // ---- the search against a read-out ----
        string s;
        yield return ((s = Typed("1.2k", Hz, 0.5)) == "1.20 kHz", $"value entry: \"1.2k\" on a frequency lands on 1.20 kHz ({s})");
        yield return ((s = Typed("1.5", Hz, 0.8)) == "1.50 kHz", $"value entry: a bare number takes the read-out's unit — 1.5 on a kHz read-out ({s})");
        yield return ((s = Typed("800", Hz, 0.8)) == "800 Hz", $"value entry: a bare number takes the unit that fits — 800 on a kHz read-out is 800 Hz ({s})");
        yield return ((s = Typed("1,5k", Hz, 0.2)) == "1.50 kHz", $"value entry: \"1,5k\" (decimal comma) ({s})");
        yield return ((s = Typed("50k", Hz, 0.5)) == "20.00 kHz", $"value entry: past the top clamps to the end ({s})");
        yield return ((s = Typed("5 Hz", Hz, 0.5)) == "20 Hz", $"value entry: below the bottom clamps to the start ({s})");
        yield return ((s = Typed("5", Hz, 0.5)) == "5.00 kHz", $"value entry: a bare 5 on a Hz read-out takes kHz, the unit it shows 5 in ({s})");
        yield return ((s = Typed("500", Time, 0.9)) == "500 ms", $"value entry: 500 on a seconds read-out is 500 ms ({s})");
        yield return ((s = Typed("12ms", Time, 0.9)) == "12 ms", $"value entry: \"12ms\" on a time ({s})");
        yield return ((s = Typed("-6", Db, 0.5)) == "-6.0 dB", $"value entry: -6 on a dB read-out ({s})");
        yield return ((s = Typed("-∞", Db, 0.5)) == "-∞ dB", $"value entry: -∞ lands on the bottom ({s})");
        yield return ((s = Typed("30L", Pan, 0.5)) == "30L" && Typed("C", Pan, 0.2) == "C", $"value entry: pan 30L and C ({s})");
        yield return ((s = Typed("off", Amount, 0.5)) == "off", $"value entry: a word the read-out shows ({s})");
        yield return (ValueText.Solve("abc", Db(0.5), Db) is null, "value entry: unreadable text finds nothing");

        // A stepped read-out: "0" lands in the middle of the run of positions showing 0.0 dB.
        double? z = ValueText.Solve("0", Db(0.2), Db);
        double exact = 60.0 / 72, step = 0.1 / 72;
        yield return (z is { } zz && Db(zz) == "0.0 dB" && Math.Abs(zz - exact) < step / 4,
            $"value entry: \"0\" lands mid-step on 0.0 dB (n={z?.ToString("0.00000", Inv)}, exact {exact:0.00000})");
    }
}
