// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which picture each page of each device shows on the LED display (specs/113, FR-012–FR-014,
// FR-025). The pages are the controller's (DevicePages.json, specs/051); every built-in type key
// lists its pages here in the same order, each with a drawing and an optional short readout for
// the header's right side. A page is found by name, then — for pages the user renamed or edited —
// by position; anything else (plug-ins, racks, a page past the list) shows the eight knob values
// as bars. The pictures themselves are in the per-family partial files.

namespace Nota.Application.Displays;

public delegate void PageDraw(DisplayContext c, LedFrame f);

/// <summary>One page's picture and the header readout beside the page name.</summary>
public sealed record PageDisplay(string Page, PageDraw Draw, Func<DisplayContext, string>? Readout = null);

public static partial class DeviceDisplays
{
    private static readonly Lazy<Dictionary<string, PageDisplay[]>> _all = new(() =>
    {
        var d = new Dictionary<string, PageDisplay[]>(StringComparer.Ordinal);
        AddInstruments(d);
        AddEffects(d);
        AddMidi(d);
        return d;
    });

    /// <summary>Every type key with its own pictures.</summary>
    public static IReadOnlyCollection<string> TypeKeys => _all.Value.Keys;

    /// <summary>The pages registered for <paramref name="typeKey"/> (empty when it has none).</summary>
    public static IReadOnlyList<PageDisplay> PagesOf(string typeKey)
        => _all.Value.TryGetValue(typeKey, out var p) ? p : Array.Empty<PageDisplay>();

    /// <summary>The picture for page <paramref name="page"/> named <paramref name="pageName"/>, or null (bars).</summary>
    public static PageDisplay? Find(string typeKey, int page, string pageName)
    {
        if (!_all.Value.TryGetValue(typeKey, out var pages)) return null;
        foreach (var p in pages)
            if (string.Equals(p.Page, pageName, StringComparison.OrdinalIgnoreCase)) return p;
        return page >= 0 && page < pages.Length ? pages[page] : null;
    }

    /// <summary>The fallback: the page's eight knob values as bars.</summary>
    public static void Bars(DisplayContext c, LedFrame f)
    {
        var values = new double?[8];
        for (int k = 0; k < 8; k++)
            values[k] = k < c.Knobs.Count && c.Knobs[k] is { Length: > 0 } n ? c.P.N(n) : null;
        Widgets.Bars(f, values, c.Turning);
    }

    // ---- helpers the pictures share ------------------------------------------------------------

    private static PageDisplay Page(string name, PageDraw draw, Func<DisplayContext, string>? readout = null) => new(name, draw, readout);

    /// <summary>The knob values of the page as bars (for pages whose controls are a set of levels).</summary>
    private static readonly PageDraw KnobBars = Bars;

    /// <summary>Bars of the named parameters, labelled.</summary>
    private static void NamedBars(DisplayContext c, LedFrame f, string[] names, string[] labels, bool bipolar = false)
    {
        var values = new double?[names.Length];
        int hi = -1;
        for (int i = 0; i < names.Length; i++)
        {
            values[i] = c.P.Has(names[i]) ? c.P.N(names[i]) : null;
            if (c.IsTurning(names[i])) hi = i;
        }
        Widgets.Bars(f, values, hi, labels, bipolar);
    }

    /// <summary>Hz as the header prints it: "890HZ", "1.2K".</summary>
    internal static string HzShort(double hz) => hz >= 1000
        ? (hz / 1000).ToString(hz >= 10000 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture) + "K"
        : Math.Round(hz).ToString(System.Globalization.CultureInfo.InvariantCulture) + "HZ";

    internal static string SecShort(double s) => s >= 1
        ? s.ToString(s >= 10 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + "S"
        : Math.Round(s * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + "MS";

    internal static string DbShort(double db) => (db > 0 ? "+" : "") + db.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "DB";

    internal static string PctShort(double v01) => Math.Round(v01 * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
}
