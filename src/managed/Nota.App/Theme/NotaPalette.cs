// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The single source of truth for palette colours used by C# custom-drawn controls
// (Render overrides can't cheaply resolve a XAML DynamicResource). Every slot here
// MIRRORS a Brush.* key in Theme/NotaTheme.axaml — keep the two in sync.
//
// Themable by construction: a slot hands out one long-lived SolidColorBrush whose
// *Color* is re-pointed when the variant changes. Call sites (and the private static
// readonly fields that alias them all over the view layer) keep the same brush object,
// so switching Ember Graphite ↔ Ember Paper needs no rebuild — only a repaint, which
// NotaThemeService triggers. Never wrap a slot in `new SolidColorBrush(slot.Color)`:
// that snapshots the colour and freezes the view in one theme.
//
// Three ways to get a colour:
//   • a named token   — NotaPalette.SurfaceCard        (explicit dark + light pair)
//   • a tinted wash   — NotaPalette.Wash(Accent, 0x24) (tracks its source slot)
//   • a device hue    — NotaPalette.Ink("#D06FB0")     (identity hue of one visualiser;
//                        its light counterpart is derived, or pinned in InkOverrides)

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

internal enum NotaThemeVariant { Dark, Light }

internal static class NotaPalette
{
    // ---- slot machinery ---------------------------------------------------

    private sealed record Slot(Color Dark, Color Light, SolidColorBrush Brush);

    private static readonly List<Slot> Slots = new();
    private static readonly List<(SolidColorBrush Src, byte Alpha, SolidColorBrush Out)> Washes = new();
    private static readonly Dictionary<(SolidColorBrush, byte), SolidColorBrush> WashCache = new();
    private static readonly List<(SolidColorBrush[] Slots, LinearGradientBrush Out)> Gradients = new();
    private static readonly List<(Func<Color> Compute, SolidColorBrush Out)> Derivations = new();
    private static readonly Dictionary<string, SolidColorBrush> InkCache = new();

    /// <summary>The variant every slot currently holds.</summary>
    public static NotaThemeVariant Variant { get; private set; } = NotaThemeVariant.Dark;

    /// <summary>Raised after <see cref="Apply"/> has re-tinted every slot, so views can
    /// drop colour caches they derived themselves (per-index clip brushes, gradients…).</summary>
    public static event Action? Changed;

    // A named token: the Ember Graphite value and its Ember Paper counterpart.
    private static SolidColorBrush T(string dark, string light)
    {
        var d = Color.Parse(dark);
        var slot = new Slot(d, Color.Parse(light), new SolidColorBrush(Variant == NotaThemeVariant.Dark ? d : Color.Parse(light)));
        Slots.Add(slot);
        return slot.Brush;
    }

    /// <summary>Re-tint every slot for <paramref name="variant"/>. Brush objects are kept;
    /// only their Color changes, so every existing reference follows along.</summary>
    public static void Apply(NotaThemeVariant variant)
    {
        Variant = variant;
        bool dark = variant == NotaThemeVariant.Dark;
        foreach (var s in Slots) s.Brush.Color = dark ? s.Dark : s.Light;
        foreach (var (src, alpha, outB) in Washes)
        {
            var c = src.Color;
            outB.Color = Color.FromArgb(alpha, c.R, c.G, c.B);
        }
        foreach (var (src, g) in Gradients)
            for (int i = 0; i < src.Length; i++) g.GradientStops[i].Color = src[i].Color;
        for (int i = 0; i < TrackSlots.Length; i++) TrackColors[i] = TrackSlots[i].Color;
        for (int i = 0; i < ReturnSlots.Length; i++) ReturnColors[i] = ReturnSlots[i].Color;
        // Derivations read the slots above, so they recompute last.
        foreach (var (compute, outB) in Derivations) outB.Color = compute();
        Changed?.Invoke();
    }

    /// <summary>A translucent tint of a palette slot. Shared per (slot, alpha) and
    /// re-tinted with its source, so washes follow the theme like any other token.</summary>
    public static SolidColorBrush Wash(SolidColorBrush src, byte alpha)
    {
        if (WashCache.TryGetValue((src, alpha), out var b)) return b;
        var c = src.Color;
        b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        WashCache[(src, alpha)] = b;
        Washes.Add((src, alpha, b));
        return b;
    }

    /// <summary>A brush whose colour is computed from other slots — a track hue mixed to a
    /// shade, an alpha-blended clip fill. Recomputed on a variant change, so the brush can be
    /// handed to a control that outlives one. Registration is permanent: cache the result per
    /// key (a palette index, say), never build one per repaint.</summary>
    public static SolidColorBrush Derived(Func<Color> compute)
    {
        var b = new SolidColorBrush(compute());
        Derivations.Add((compute, b));
        return b;
    }

    /// <summary>A top-to-bottom gradient over palette slots. A LinearGradientBrush snapshots
    /// its stop Colors, so unlike a fill it cannot ride a slot on its own — this registers it
    /// for re-tinting. Registration is permanent: only ever assign the result to a static
    /// field, never build one per control instance.</summary>
    public static LinearGradientBrush VGradient(params (SolidColorBrush Slot, double Offset)[] stops)
    {
        var g = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        foreach (var (slot, off) in stops) g.GradientStops.Add(new GradientStop(slot.Color, off));
        Gradients.Add((Array.ConvertAll(stops, t => t.Slot), g));
        return g;
    }

    /// <summary>The identity hue of one visualiser or picker swatch — a colour that means
    /// "this band / this quadrant / this tag", not a role in the design system. Give the
    /// Ember Graphite value; the Ember Paper counterpart is derived (same hue, darkened so
    /// it reads on paper) unless <see cref="InkOverrides"/> pins one.</summary>
    public static SolidColorBrush Ink(string darkHex)
    {
        var key = darkHex.ToUpperInvariant();
        if (InkCache.TryGetValue(key, out var b)) return b;
        // A hex that already is a role token (brass, teal, rose, steel…) resolves to that token,
        // so it gets the designed light value instead of a derived one — never a second brass.
        var dark = Color.Parse(darkHex);
        foreach (var slot in Slots)
            if (slot.Dark == dark) { InkCache[key] = slot.Brush; return slot.Brush; }
        b = T(darkHex, InkOverrides.TryGetValue(key, out var pin) ? pin : DeriveLight(Color.Parse(darkHex)));
        InkCache[key] = b;
        return b;
    }

    /// <summary>The Color of an <see cref="Ink"/> hue, for the call sites that mix or
    /// interpolate rather than fill. Read it at draw time — it changes with the theme.</summary>
    public static Color InkColor(string darkHex) => Ink(darkHex).Color;

    // Ember Paper counterparts that the generic derivation gets wrong — a hue whose job
    // is to be *pale* on graphite (a highlight) or whose saturation collapses when darkened.
    private static readonly Dictionary<string, string> InkOverrides = new()
    {
        ["#8A8F98"] = "#5A6068",   // tag "slate": derivation leaves it near-neutral mud
        ["#CDEEF8"] = "#0E5470",   // resize-edge / bracket highlight: hottest ink on paper is darkest
        ["#12181B"] = "#DCE6E8",   // BPM chip ground — a surface, so it lightens rather than darkens
        ["#1A2B31"] = "#C8DEE4",   // …and its hot state
    };

    // Same hue and saturation, lightness reflected onto the paper ground: the brighter a
    // hue reads on graphite, the darker it must read on paper to carry the same emphasis.
    private static string DeriveLight(Color c)
    {
        var (h, s, l) = ToHsl(c);
        double nl = Math.Clamp(73.3 - 0.545 * l, 18, 58);
        double ns = Math.Clamp(s * 1.12, 0, 100);
        var o = FromHsl(h, ns, nl);
        return $"#{o.R:X2}{o.G:X2}{o.B:X2}";
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double l = (max + min) / 2, s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        double h = d == 0 ? 0
            : max == r ? 60 * (((g - b) / d) % 6)
            : max == g ? 60 * ((b - r) / d + 2)
            : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, s * 100, l * 100);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        s /= 100; l /= 100;
        double cc = (1 - Math.Abs(2 * l - 1)) * s, x = cc * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - cc / 2;
        (double r, double g, double b) = h < 60 ? (cc, x, 0.0) : h < 120 ? (x, cc, 0.0) : h < 180 ? (0.0, cc, x)
            : h < 240 ? (0.0, x, cc) : h < 300 ? (x, 0.0, cc) : (cc, 0.0, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    // =======================================================================
    // Tokens.  dark = "Ember Graphite"        light = "Ember Paper"
    // =======================================================================

    // ---- Surfaces: the nine steps of the almanac, far to near -------------
    // Void · App · Gutter · Well · Panel · Card · Raised · Hover · Track off.
    // Depth is lightness plus a hairline, never a shadow, and three levels of
    // nesting is the limit — a fourth means the layout is wrong, not the palette.
    public static readonly SolidColorBrush SurfaceAbyss  = T("#0A0908", "#D8D1BE"); // Brush.SurfaceAbyss — Void
    public static readonly SolidColorBrush BgApp         = T("#0B0A09", "#DCD6C5"); // Brush.BgApp — window ground, plugin body
    public static readonly SolidColorBrush Gutter        = T("#0C0B09", "#E0DAC9"); // Brush.Gutter — the gaps panels float in
    public static readonly SolidColorBrush BgSunken      = T("#100F0D", "#E4DFD1"); // Brush.BgSunken / ChromeBg — Well
    public static readonly SolidColorBrush CanvasBg      = T("#121110", "#E6E1D3"); // modular canvas, just above the well
    public static readonly SolidColorBrush SurfaceDeep   = T("#131210", "#E7E2D4"); // step-sequencer ground
    public static readonly SolidColorBrush LaneB         = T("#131210", "#E7E2D4"); // Brush.LaneB (alternating lane)
    public static readonly SolidColorBrush Panel         = T("#141310", "#EAE5D7"); // Brush.Panel — browser, headers, inspector
    public static readonly SolidColorBrush SurfaceCard   = T("#171613", "#EFEADE"); // Brush.SurfaceCard — a section inside a device
    public static readonly SolidColorBrush SurfaceInset  = T("#1A1815", "#E9E4D6"); // rail / sunken chip
    public static readonly SolidColorBrush SurfaceRaised = T("#1C1A16", "#F8F5EC"); // Brush.SurfaceRaised — button at rest
    public static readonly SolidColorBrush SurfaceHover  = T("#252219", "#E7E1D0"); // Brush.SurfaceHover
    public static readonly SolidColorBrush TrackOff      = T("#26231E", "#DBD4C0"); // Brush.TrackOff — track of an off toggle
    // Pressed goes *into* the recess rather than to a fourth raised step.
    public static readonly SolidColorBrush SurfaceActive = T("#100F0D", "#D5CEBB"); // Brush.SurfaceActive

    // ---- Grid + graph lines (kept under ~12% contrast against their lane) -
    public static readonly SolidColorBrush GridSubBeat = T("#191712", "#E5DFCE"); // finer than a beat
    public static readonly SolidColorBrush GridBeat    = T("#1E1C18", "#DED8C8"); // Brush.GridBeat — grid inside graphs, zero axis
    public static readonly SolidColorBrush GridRow     = T("#141311", "#E6E1D2"); // piano-roll row divider
    public static readonly SolidColorBrush GridBar     = T("#2A2721", "#CFC8B4"); // Brush.GridBar
    public static readonly SolidColorBrush GraphBorder = T("#221F1A", "#D5CFBE"); // inner border of a visualiser well
    public static readonly SolidColorBrush WellGrid    = T("#232019", "#DDD7C6"); // grid drawn inside a well
    public static readonly SolidColorBrush PadGrid     = T("#1C1A16", "#DED8C7"); // XY-pad grid

    // ---- Text: eight steps, strictly by descending importance ------------
    // Warm off-white on graphite, warm near-black on paper. Muted text takes the
    // next Ink step down — never opacity, which makes contrast unpredictable.
    public static readonly SolidColorBrush TextHeading   = T("#F2EDE1", "#1A150E"); // Ink 0 — page titles, project name. Rare.
    public static readonly SolidColorBrush TextPrimary   = T("#E9E4D8", "#241F17"); // Ink 1 — device/track/preset names, values
    public static readonly SolidColorBrush TextStrong    = T("#C7C0B0", "#3E382C"); // Ink 2 — button text, transport readouts
    public static readonly SolidColorBrush TextSecondary = T("#A39D8F", "#5E5849"); // Ink 3 — inactive but readable, metadata
    public static readonly SolidColorBrush TextMuted     = T("#8D8779", "#746D5C"); // Ink 4 — explanations, units, hints
    public static readonly SolidColorBrush TextTertiary  = T("#6E6A5E", "#8A8474"); // Ink 5 — caps labels above parameters
    public static readonly SolidColorBrush TextDisabled  = T("#55514A", "#9E9786"); // Ink 6 — placeholder, disabled action
    public static readonly SolidColorBrush TextAxis      = T("#4A463D", "#ADA694"); // Ink 7 — axis labels inside graphs only
    public static readonly SolidColorBrush TextOnAccent  = T("#171613", "#FFFBF2"); // Brush.TextOnAccent

    // ---- Borders ----------------------------------------------------------
    public static readonly SolidColorBrush BorderDefault = T("#2C2923", "#D5CEBB"); // Brush.BorderDefault
    public static readonly SolidColorBrush BorderStrong  = T("#3A362D", "#BDB5A0"); // Brush.BorderStrong — knob cap, hovered border
    public static readonly SolidColorBrush BorderBrass   = T("#6B5326", "#C9A254"); // Brush.BorderBrass — engaged button: brass at 40%

    // ---- Accent (brass on graphite, bronze on paper: darker carries the
    //      same emphasis against a light ground) ----------------------------
    public static readonly SolidColorBrush Accent       = T("#D8A03D", "#A87415"); // Brush.Accent — arc, fill, playhead, solid button
    public static readonly SolidColorBrush AccentBright = T("#F0C060", "#744D07"); // Brush.AccentBright — Brass Light, the hottest mark
    public static readonly SolidColorBrush AccentHover  = T("#E9BE6A", "#8F6110"); // Brush.AccentHover — hover on brass text
    public static readonly SolidColorBrush AccentDeep   = T("#B08536", "#7A5008"); // Brush.AccentDeep — press, left half of the loop
    public static readonly SolidColorBrush AccentDim    = T("#8A6B2E", "#9C8034"); // Brush.AccentDim — eyebrows, mono section marks
    public static readonly SolidColorBrush AccentEdge   = T("#5E4A22", "#D3B173"); // Brush.AccentEdge — chip border, link underline
    public static readonly SolidColorBrush AccentPale   = T("#FCE5B8", "#5C3C04"); // hot edge highlight
    // Opaque, not an alpha tint: the almanac wants one predictable colour, and
    // alpha over two different grounds is not one.
    public static readonly SolidColorBrush AccentSubtle = T("#241F17", "#F2E4C4"); // Brush.AccentSubtle — Brass Wash, selected row

    // ---- Status -----------------------------------------------------------
    // Red belongs to recording. Everything else red is an alert about level.
    public static readonly SolidColorBrush Record      = T("#C25B44", "#B54A32"); // Brush.Record — active recording, the only claim on red
    public static readonly SolidColorBrush RecordInk   = T("#F4E3DC", "#FFF4EE"); // Brush.RecordInk — disc on an engaged record button
    public static readonly SolidColorBrush Success     = T("#58B368", "#2C7A3E"); // Brush.Success — Signal, working meter zone
    public static readonly SolidColorBrush SuccessDim  = T("#7FB069", "#609548"); // Brush.SuccessDim — transport input, metronome
    public static readonly SolidColorBrush Warning     = T("#D9C34C", "#837010"); // Brush.Warning — Caution, −6..0 dB
    public static readonly SolidColorBrush Danger      = T("#C2554A", "#B13E33"); // Brush.Danger — Alert, overload / clipping
    public static readonly SolidColorBrush DangerHover = T("#D0685C", "#C64A36"); // Brush.DangerHover
    public static readonly SolidColorBrush DangerBright= T("#E08A72", "#A3391B"); // Brush.DangerBright — Alert Light, readout text
    public static readonly SolidColorBrush DangerDeep  = T("#A8453B", "#9C3221"); // over-threshold fill, below Alert
    public static readonly SolidColorBrush DangerPale  = T("#F0928A", "#8E2416"); // live-take waveform / label

    // ---- Data + modulation ------------------------------------------------
    // Role chromas live inside visualisers only. When two or three sources share
    // one graph, brass takes the primary and these follow in order. They never
    // reach a button and they never mean state.
    public static readonly SolidColorBrush Teal       = T("#5B9E9C", "#2F7472"); // Brush.ChromaTeal — 2nd layer; also the modulation accent
    public static readonly SolidColorBrush TealBright = T("#7FC0BE", "#3E8A88"); // Brush.ChromaTealLight
    public static readonly SolidColorBrush Rose       = T("#B57286", "#86465B"); // Brush.ChromaRose — 3rd layer: right channel, highs, tail
    public static readonly SolidColorBrush RoseBright = T("#D79BAB", "#7F2D43"); // Brush.ChromaRoseLight
    public static readonly SolidColorBrush Steel      = T("#6D8FB5", "#3E6288"); // Brush.ChromaSteel — 4th layer, used rarely
    public static readonly SolidColorBrush Sage       = T("#6FA383", "#42765A"); // standalone hue (no longer a track slot)
    public static readonly SolidColorBrush Threshold  = T("#C9884F", "#8A5218"); // threshold markers
    public static readonly SolidColorBrush Marker     = T("#7FCCE1", "#1B6E8C"); // clip markers / brackets
    public static readonly SolidColorBrush MarkerHot  = T("#CDEEF8", "#0E5470"); // resize-edge affordance
    public static readonly SolidColorBrush Frozen     = T("#7FC7EC", "#1C6E96"); // frozen-track ice
    public static readonly SolidColorBrush SignalIn   = T("#52604F", "#6E7C68"); // ghost "input" trace label
    public static readonly SolidColorBrush SignalInFill = T("#3E4A3C", "#D3DCCF");
    public static readonly SolidColorBrush HandleSel  = T("#FFFFFF", "#1E1A14"); // selected graph handle

    // ---- Tinted chrome ----------------------------------------------------
    public static readonly SolidColorBrush SelHeaderBg    = T("#241F17", "#F2E4C4"); // selected track header — Brass Wash
    public static readonly SolidColorBrush FrozenHeaderBg = T("#1B2A33", "#D8E8F0"); // frozen track header
    public static readonly SolidColorBrush MuteTint       = T("#3A2320", "#F0D9D3"); // muted band
    public static readonly SolidColorBrush Veil           = T("#121418", "#F2EFE6"); // deactivated-clip scrim base
    public static readonly SolidColorBrush TooltipBg      = T("#18181E", "#2A251C"); // tooltips stay a dark pill
    public static readonly SolidColorBrush TooltipText    = T("#F0E4C8", "#F5EEDC");

    // ---- Piano keys (the clip editor keyboard is dark: keys recede, notes lead) ---
    public static readonly SolidColorBrush KeyWhite     = T("#1C1A16", "#F3EFE4"); // piano-roll keys: dark, never the brightest thing
    public static readonly SolidColorBrush KeyBlack     = T("#0E0D0B", "#DAD3C2"); // black keys and their lanes
    public static readonly SolidColorBrush MiniKeyWhite = T("#2A2721", "#F5F1E4"); // device-card mini keyboards
    public static readonly SolidColorBrush MiniKeyBlack = T("#151310", "#38332A");

    // ---- Track-shade mix targets (TrackColorForIndex) ---------------------
    public static readonly SolidColorBrush ShadeUp   = T("#FFFFFF", "#FFFCF2"); // "lighter" shade
    public static readonly SolidColorBrush ShadeDown = T("#141210", "#3A3227"); // "darker" shade

    // ---- Track palette (Brush.Track1..8) + return buses --------------------
    // Nine roles at one lightness, so no track is louder than another. A group sets
    // the hue and its tracks take neighbouring shades of it. Paper darkens each by
    // the same ratio the previous eight pairs used (lightness ×0.67, saturation ×1.18).
    //
    // Saved projects are NOT migrated: a stored colour index keeps working but now
    // points at the hue that sits at that place in this table.
    private static readonly SolidColorBrush[] TrackSlots =
    {
        T("#58B368", "#328140"), // Drums
        T("#4E9E7A", "#306F53"), // Perc
        T("#3E8E8E", "#256464"), // Bass
        T("#5AA0B8", "#317187"), // Keys
        T("#C77F55", "#985127"), // Texture
        T("#7A6FB0", "#4B4082"), // FX
        T("#B05A7A", "#7F3450"), // Brass
        T("#9AA64A", "#6C752C"), // Vox
        T("#7FA88E", "#4D7A5D"), // Return
    };
    // Return buses draw from the ninth role rather than a palette of their own.
    private static readonly SolidColorBrush[] ReturnSlots = { T("#7FA88E", "#4D7A5D"), T("#6B907B", "#3E6A4D") };

    /// <summary>Brush form of the 8-track cycle — index it for fills that follow the theme.</summary>
    public static readonly SolidColorBrush[] TrackBrushes = TrackSlots;
    public static readonly SolidColorBrush[] ReturnBrushes = ReturnSlots;

    /// <summary>The 8-track colour cycle + the two return-bus tints, as Colors for the views
    /// that mix or alpha-blend them. Rewritten in place by <see cref="Apply"/> — read at draw
    /// time (the array reference is stable, its contents are not).</summary>
    public static readonly Color[] TrackColors = { TrackSlots[0].Color, TrackSlots[1].Color, TrackSlots[2].Color, TrackSlots[3].Color, TrackSlots[4].Color, TrackSlots[5].Color, TrackSlots[6].Color, TrackSlots[7].Color, TrackSlots[8].Color };
    public static readonly Color[] ReturnColors = { ReturnSlots[0].Color, ReturnSlots[1].Color };

    // ---- Lookup by XAML key ------------------------------------------------

    // Code-built views that read a token by its XAML key resolve it here instead of
    // through TryFindResource: a resource read is a one-time snapshot, while the slot
    // brush follows the variant. (Bind-style reads — ControlExtensions.BindResource —
    // are already live and should keep using the resource observable.)
    private static readonly Dictionary<string, SolidColorBrush> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brush.SurfaceAbyss"] = SurfaceAbyss,   ["Brush.Gutter"] = Gutter,
        ["Brush.BgSunken"] = BgSunken,           ["Brush.ChromeBg"] = BgSunken,
        ["Brush.BgApp"] = BgApp,                 ["Brush.LaneB"] = LaneB,
        ["Brush.Panel"] = Panel,                 ["Brush.TrackOff"] = TrackOff,
        ["Brush.SurfaceCard"] = SurfaceCard,     ["Brush.SurfaceRaised"] = SurfaceRaised,
        ["Brush.SurfaceHover"] = SurfaceHover,   ["Brush.SurfaceActive"] = SurfaceActive,
        ["Brush.SurfaceSelected"] = AccentSubtle,
        ["Brush.GridBeat"] = GridBeat,           ["Brush.GridBar"] = GridBar,
        ["Brush.Hairline"] = GraphBorder,
        ["Brush.TextHeading"] = TextHeading,     ["Brush.TextPrimary"] = TextPrimary,
        ["Brush.TextStrong"] = TextStrong,       ["Brush.TextSecondary"] = TextSecondary,
        ["Brush.TextMuted"] = TextMuted,         ["Brush.TextTertiary"] = TextTertiary,
        ["Brush.TextDisabled"] = TextDisabled,   ["Brush.TextAxis"] = TextAxis,
        ["Brush.TextOnAccent"] = TextOnAccent,
        ["Brush.BorderDefault"] = BorderDefault, ["Brush.BorderStrong"] = BorderStrong,
        ["Brush.BorderBrass"] = BorderBrass,
        ["Brush.Accent"] = Accent,               ["Brush.AccentHover"] = AccentHover,
        ["Brush.AccentBright"] = AccentBright,   ["Brush.AccentDeep"] = AccentDeep,
        ["Brush.AccentDim"] = AccentDim,         ["Brush.AccentEdge"] = AccentEdge,
        ["Brush.AccentSubtle"] = AccentSubtle,
        ["Brush.Record"] = Record,               ["Brush.RecordInk"] = RecordInk,
        ["Brush.Success"] = Success,
        ["Brush.SuccessDim"] = SuccessDim,       ["Brush.Warning"] = Warning,
        ["Brush.Danger"] = Danger,               ["Brush.DangerHover"] = DangerHover,
        ["Brush.DangerBright"] = DangerBright,
        ["Brush.ChromaTeal"] = Teal,             ["Brush.ChromaTealLight"] = TealBright,
        ["Brush.ChromaRose"] = Rose,             ["Brush.ChromaRoseLight"] = RoseBright,
        ["Brush.ChromaSteel"] = Steel,
    };

    /// <summary>The slot behind a <c>Brush.*</c> XAML key, or null if the key is unknown.</summary>
    public static SolidColorBrush? ByKey(string key) => KeyMap.GetValueOrDefault(key);

    // ---- Raw Colors — properties, never cached: they move with the theme --
    public static Color AccentColor => Accent.Color;
    public static Color AccentBrightColor => AccentBright.Color;
    public static Color SuccessColor => Success.Color;
    public static Color TealColor => Teal.Color;
}
