// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The palette as the custom-drawn layer sees it: one long-lived SolidColorBrush per
// colour token, re-tinted in place from the active colour theme (Theme/Themes/*.json —
// the only place a colour value lives, see NotaColourTheme.cs). NotaThemeService also
// publishes these same brushes to XAML under their Brush.* keys (KeyMap), so styles and
// custom-drawn views can never disagree.
//
// Themable by construction: a slot hands out one long-lived SolidColorBrush whose
// *Color* is re-pointed when the theme changes. Call sites (and the private static
// readonly fields that alias them all over the view layer) keep the same brush object,
// so switching themes needs no rebuild — only a repaint, which NotaThemeService
// triggers. Never wrap a slot in `new SolidColorBrush(slot.Color)`: that snapshots the
// colour and freezes the view in one theme.
//
// Three ways to get a colour:
//   • a named token   — NotaPalette.SurfaceCard        (one value per theme)
//   • a tinted wash   — NotaPalette.Wash(Accent, 0x24) (tracks its source slot)
//   • a device hue    — NotaPalette.Ink("#D06FB0")     (identity hue of one visualiser;
//                        follows the theme's kind: its light counterpart is derived, or
//                        pinned in InkOverrides)

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

internal static class NotaPalette
{
    // ---- slot machinery ---------------------------------------------------

    // A token slot: its name in the theme files and the brush every view holds.
    private sealed record Slot(string Name, SolidColorBrush Brush);
    // A device hue (Ink): not a token — it follows the theme's kind, not its values.
    private sealed record InkSlot(Color Dark, Color Light, SolidColorBrush Brush);

    private static readonly List<Slot> Slots = new();
    private static readonly List<InkSlot> InkSlots = new();
    private static readonly List<(SolidColorBrush Src, byte Alpha, SolidColorBrush Out)> Washes = new();
    private static readonly Dictionary<(SolidColorBrush, byte), SolidColorBrush> WashCache = new();
    private static readonly List<(SolidColorBrush[] Slots, LinearGradientBrush Out)> Gradients = new();
    private static readonly List<(Func<Color> Compute, SolidColorBrush Out)> Derivations = new();
    private static readonly Dictionary<string, SolidColorBrush> InkCache = new();

    // Slots start in Studio Dark so anything read before the first Apply has a real value.
    // Read raw: the validated theme list needs this class's token names to exist first.
    private static readonly Dictionary<string, string> Seed = NotaThemes.ReadSeed("studio-dark");

    /// <summary>The theme every slot currently holds.</summary>
    public static NotaColourTheme? Theme { get; private set; }

    /// <summary>Dark or light — the kind of the current theme (Studio Dark before the first Apply).</summary>
    public static NotaThemeKind Kind => Theme?.Kind ?? NotaThemeKind.Dark;

    /// <summary>Raised after <see cref="Apply"/> has re-tinted every slot, so views can
    /// drop colour caches they derived themselves (per-index clip brushes, gradients…).</summary>
    public static event Action? Changed;

    // A named token, seeded with its Studio Dark value until a theme is applied.
    private static SolidColorBrush T(string name)
    {
        var slot = new Slot(name, new SolidColorBrush(Color.Parse(Seed[name])));
        Slots.Add(slot);
        return slot.Brush;
    }

    /// <summary>Every token a palette slot carries, in declaration order — the slot half of
    /// the theme catalogue (NotaThemes.Catalogue adds the non-slot tokens).</summary>
    public static IEnumerable<string> TokenNames { get { foreach (var s in Slots) yield return s.Name; } }

    /// <summary>Re-tint every slot from <paramref name="theme"/>. Brush objects are kept;
    /// only their Color changes, so every existing reference follows along.</summary>
    public static void Apply(NotaColourTheme theme)
    {
        Theme = theme;
        bool dark = theme.IsDark;
        foreach (var s in Slots) s.Brush.Color = theme[s.Name];
        foreach (var s in InkSlots) s.Brush.Color = dark ? s.Dark : s.Light;
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

    /// <summary>The colour <paramref name="slot"/> holds in <paramref name="theme"/>, whatever
    /// the current one is — for previews of a theme (the theme picker's chips).</summary>
    public static Color ColorIn(SolidColorBrush slot, NotaColourTheme theme)
    {
        foreach (var s in Slots)
            if (ReferenceEquals(s.Brush, slot)) return theme[s.Name];
        foreach (var s in InkSlots)
            if (ReferenceEquals(s.Brush, slot)) return theme.IsDark ? s.Dark : s.Light;
        return slot.Color;
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

    /// <summary>Ink for text or a glyph on a solid <paramref name="fill"/> — dark ink or white,
    /// whichever has the higher contrast on the fill's current colour, recomputed on a variant
    /// change. Fills are tokens that differ per variant, so the ink on them is derived, never
    /// fixed. Cached per fill.</summary>
    public static SolidColorBrush InkOn(SolidColorBrush fill)
    {
        if (InkOnCache.TryGetValue(fill, out var b)) return b;
        b = Derived(() => ReadableInkOn(fill.Color));
        InkOnCache[fill] = b;
        return b;
    }

    private static readonly Dictionary<SolidColorBrush, SolidColorBrush> InkOnCache = new();

    /// <summary>The two inks <see cref="ReadableInkOn"/> chooses between. Nota Remote is sent
    /// them so the phone derives ink the same way (spec 014).</summary>
    public static readonly Color InkDark = Color.FromRgb(0x17, 0x18, 0x1A), InkLight = Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>Dark ink (#17181A) or white, whichever reads better on <paramref name="fill"/>
    /// (WCAG contrast ratio). For views that compute a fill at draw time.</summary>
    public static Color ReadableInkOn(Color fill) =>
        Contrast(InkDark, fill) >= Contrast(InkLight, fill) ? InkDark : InkLight;

    /// <summary><paramref name="ink"/> moved toward dark ink or white (away from
    /// <paramref name="ground"/>) just far enough to reach <paramref name="min"/> contrast;
    /// unchanged when it already does. Keeps a hue while making it legible on its ground.</summary>
    public static Color Legible(Color ink, Color ground, double min = 3.0)
    {
        if (Contrast(ink, ground) >= min) return ink;
        var to = ReadableInkOn(ground);
        for (double t = 0.05; t < 1; t += 0.05)
        {
            var c = Color.FromRgb((byte)(ink.R + (to.R - ink.R) * t), (byte)(ink.G + (to.G - ink.G) * t), (byte)(ink.B + (to.B - ink.B) * t));
            if (Contrast(c, ground) >= min) return c;
        }
        return to;
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Ch(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
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
    /// dark value; the light counterpart is derived (same hue, darkened so it reads on the
    /// light grey ground) unless <see cref="InkOverrides"/> pins one.</summary>
    public static SolidColorBrush Ink(string darkHex)
    {
        var key = darkHex.ToUpperInvariant();
        if (InkCache.TryGetValue(key, out var b)) return b;
        // A hex that already is a role token resolves to that token, so it gets the designed
        // light value instead of a derived one. Hexes that were Ember role tokens (views still
        // name a few) resolve to the role's current slot, so no Ember hue survives the switch.
        if (LegacyRole(key) is { } role) { InkCache[key] = role; return role; }
        // "Already a token" means the hex is that token's Studio Dark value — the palette the
        // views' literal hues were written against.
        foreach (var slot in Slots)
            if (string.Equals(Seed[slot.Name], key, StringComparison.OrdinalIgnoreCase)) { InkCache[key] = slot.Brush; return slot.Brush; }
        var dark = Color.Parse(darkHex);
        var light = Color.Parse(InkOverrides.TryGetValue(key, out var pin) ? pin : DeriveLight(dark));
        var ink = new InkSlot(dark, light, new SolidColorBrush(Kind == NotaThemeKind.Dark ? dark : light));
        InkSlots.Add(ink);
        InkCache[key] = ink.Brush;
        return ink.Brush;
    }

    // Ember role hexes that views pass to Ink() → the slot that plays that role now. A method,
    // not a table: the slots it names are initialised further down this file.
    private static SolidColorBrush? LegacyRole(string hex) => hex switch
    {
        "#D8A03D" => Accent,      // brass
        "#5B9E9C" => Teal,
        "#B57286" => Rose,
        "#6D8FB5" => Steel,
        "#B8862E" => Threshold,   // brass-adjacent data fill → amber
        "#2A2317" => AccentSubtle, // brass wash band
        _ => null,
    };

    /// <summary>The Color of an <see cref="Ink"/> hue, for the call sites that mix or
    /// interpolate rather than fill. Read it at draw time — it changes with the theme.</summary>
    public static Color InkColor(string darkHex) => Ink(darkHex).Color;

    // Light counterparts that the generic derivation gets wrong — a hue whose job
    // is to be *pale* on the dark ground (a highlight) or whose saturation collapses when darkened.
    private static readonly Dictionary<string, string> InkOverrides = new()
    {
        ["#8A8F98"] = "#5A6068",   // tag "slate": derivation leaves it near-neutral mud
        ["#CDEEF8"] = "#0E5470",   // resize-edge / bracket highlight: hottest ink on light is darkest
        ["#12181B"] = "#DCE6E8",   // BPM chip ground — a surface, so it lightens rather than darkens
        ["#1A2B31"] = "#C8DEE4",   // …and its hot state
    };

    // Same hue and saturation, lightness reflected onto the light ground: the brighter a
    // hue reads on dark, the darker it must read on light to carry the same emphasis.
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
    // Tokens — the Studio palette: neutral greys, one orange accent.
    // dark = Studio Dark, light = Studio Light (same roles).
    // =======================================================================

    // ---- Surfaces: the grey ladder, far to near -----------------------
    // Well (recess) · Ground (bg-base) · Panel · Raised · Raised hover. Ember's nine
    // steps collapse onto these; the extra slots sit between two of them. Depth is
    // tone plus a 1 px seam; only floating layers cast a shadow.
    public static readonly SolidColorBrush SurfaceAbyss  = T(nameof(SurfaceAbyss)); // Brush.SurfaceAbyss — well: the transport console recess
    public static readonly SolidColorBrush BgApp         = T(nameof(BgApp)); // Brush.BgApp — ground: window, arrangement, plugin body
    public static readonly SolidColorBrush Gutter        = T(nameof(Gutter)); // Brush.Gutter — the seam the docked panels meet on
    public static readonly SolidColorBrush BgSunken      = T(nameof(BgSunken)); // Brush.BgSunken / ChromeBg — Well
    public static readonly SolidColorBrush CanvasBg      = T(nameof(CanvasBg)); // modular canvas, just above the well
    public static readonly SolidColorBrush SurfaceDeep   = T(nameof(SurfaceDeep)); // step-sequencer ground
    public static readonly SolidColorBrush LaneB         = T(nameof(LaneB)); // Brush.LaneB (alternating lane)
    public static readonly SolidColorBrush Panel         = T(nameof(Panel)); // Brush.Panel — browser, headers, inspector
    public static readonly SolidColorBrush SurfaceCard   = T(nameof(SurfaceCard)); // Brush.SurfaceCard — a section inside a device (panel tone; divided by rules)
    public static readonly SolidColorBrush SurfaceInset  = T(nameof(SurfaceInset)); // Brush.SurfaceInset — rail, sunken chip, hovered well
    public static readonly SolidColorBrush SurfaceRaised = T(nameof(SurfaceRaised)); // Brush.SurfaceRaised — button at rest
    public static readonly SolidColorBrush SurfaceHover  = T(nameof(SurfaceHover)); // Brush.SurfaceHover
    public static readonly SolidColorBrush TrackOff      = T(nameof(TrackOff)); // Brush.TrackOff — track of an off toggle
    // Pressed goes *into* the recess rather than to a fourth raised step.
    public static readonly SolidColorBrush SurfaceActive = T(nameof(SurfaceActive)); // Brush.SurfaceActive

    // ---- Grid + graph lines (kept under ~12% contrast against their lane) -
    public static readonly SolidColorBrush GridSubBeat = T(nameof(GridSubBeat)); // finer than a beat
    public static readonly SolidColorBrush GridBeat    = T(nameof(GridBeat)); // Brush.GridBeat — grid inside graphs, zero axis
    public static readonly SolidColorBrush GridRow     = T(nameof(GridRow)); // piano-roll row divider
    public static readonly SolidColorBrush GridBar     = T(nameof(GridBar)); // Brush.GridBar
    public static readonly SolidColorBrush GraphBorder = T(nameof(GraphBorder)); // inner border of a visualiser well
    public static readonly SolidColorBrush WellGrid    = T(nameof(WellGrid)); // grid drawn inside a well
    public static readonly SolidColorBrush PadGrid     = T(nameof(PadGrid)); // XY-pad grid

    // ---- Text: eight steps, strictly by descending importance ------------
    // Cool off-white on dark, near-black on light. Muted text takes the
    // next Ink step down — never opacity, which makes contrast unpredictable.
    public static readonly SolidColorBrush TextHeading   = T(nameof(TextHeading)); // Ink 0 — page titles, project name. Rare.
    public static readonly SolidColorBrush TextPrimary   = T(nameof(TextPrimary)); // Ink 1 — device/track/preset names, values
    public static readonly SolidColorBrush TextStrong    = T(nameof(TextStrong)); // Ink 2 — button text, transport readouts
    public static readonly SolidColorBrush TextSecondary = T(nameof(TextSecondary)); // Ink 3 — inactive but readable, metadata
    public static readonly SolidColorBrush TextMuted     = T(nameof(TextMuted)); // Ink 4 — explanations, units, hints
    public static readonly SolidColorBrush TextTertiary  = T(nameof(TextTertiary)); // Ink 5 — caps labels above parameters
    public static readonly SolidColorBrush TextDisabled  = T(nameof(TextDisabled)); // Ink 6 — placeholder, disabled action
    public static readonly SolidColorBrush TextAxis      = T(nameof(TextAxis)); // Ink 7 — axis labels inside graphs only
    public static readonly SolidColorBrush TextOnAccent  = T(nameof(TextOnAccent)); // Brush.TextOnAccent
    public static readonly SolidColorBrush TextFaint     = T(nameof(TextFaint)); // Brush.TextFaint — disabled controls only
    // The neutral ink a value is drawn in at rest: knob arcs, fader fills, graph curves.
    // The accent replaces it only while the control is live (dragged, hovered) or chosen.
    public static readonly SolidColorBrush VizInk        = T(nameof(VizInk)); // Brush.VizInk
    public static readonly SolidColorBrush VizGrid       = T(nameof(VizGrid)); // Brush.VizGrid — grid inside a well

    // ---- Borders ----------------------------------------------------------
    public static readonly SolidColorBrush BorderDefault = T(nameof(BorderDefault)); // Brush.BorderDefault
    public static readonly SolidColorBrush BorderStrong  = T(nameof(BorderStrong)); // Brush.BorderStrong — knob cap, hovered border
    public static readonly SolidColorBrush BorderBrass   = T(nameof(BorderBrass)); // Brush.BorderBrass — accent line: 70% accent over panel
    // Two lines: the dark seam between panels and around controls, and the
    // lighter rule that divides inside a panel (toolbar separators, field edges).
    public static readonly SolidColorBrush Seam          = T(nameof(Seam)); // Brush.Seam
    public static readonly SolidColorBrush Rule          = T(nameof(Rule)); // Brush.Rule

    // ---- Accent: Studio orange. Live or chosen only — playing, selected, looping,
    //      toggled on, or a control while it is dragged. Never a resting value. ---
    public static readonly SolidColorBrush Accent       = T(nameof(Accent)); // Brush.Accent — playhead, selection, toggled-on fill
    public static readonly SolidColorBrush AccentBright = T(nameof(AccentBright)); // Brush.AccentBright — accent glow: the line/ink variant
    public static readonly SolidColorBrush AccentHover  = T(nameof(AccentHover)); // Brush.AccentHover — accent text on hover
    public static readonly SolidColorBrush AccentDeep   = T(nameof(AccentDeep)); // Brush.AccentDeep — press, left half of the loop
    public static readonly SolidColorBrush AccentDim    = T(nameof(AccentDim)); // Brush.AccentDim — the soft/pressed accent
    public static readonly SolidColorBrush AccentEdge   = T(nameof(AccentEdge)); // Brush.AccentEdge — chip border, link underline
    public static readonly SolidColorBrush AccentMute   = T(nameof(AccentMute)); // a muted accent; nothing at rest uses it any more
    public static readonly SolidColorBrush AccentPale   = T(nameof(AccentPale)); // hot edge highlight
    // Accent-soft: 16 % accent over the panel, opaque so it reads the same on any ground.
    public static readonly SolidColorBrush AccentSubtle = T(nameof(AccentSubtle)); // Brush.AccentSubtle — accent-soft wash

    // ---- Status -----------------------------------------------------------
    // Red belongs to recording. Everything else red is an alert about level.
    public static readonly SolidColorBrush Record      = T(nameof(Record)); // Brush.Record — active recording, the only claim on red
    public static readonly SolidColorBrush RecordInk   = T(nameof(RecordInk)); // Brush.RecordInk — disc on an engaged record button
    public static readonly SolidColorBrush Success     = T(nameof(Success)); // Brush.Success — Signal, working meter zone
    public static readonly SolidColorBrush SuccessInk  = T(nameof(SuccessInk)); // Brush.SuccessInk — ink on a Success fill (Session play)
    public static readonly SolidColorBrush SuccessDim  = T(nameof(SuccessDim)); // Brush.SuccessDim — transport input, metronome
    public static readonly SolidColorBrush Warning     = T(nameof(Warning)); // Brush.Warning — Caution, −6..0 dB
    // Track toggles: mute is amber, solo is blue, arm is Record. Each fills solid when on.
    public static readonly SolidColorBrush Mute        = T(nameof(Mute)); // Brush.Mute
    public static readonly SolidColorBrush Solo        = T(nameof(Solo)); // Brush.Solo
    public static readonly SolidColorBrush Danger      = T(nameof(Danger)); // Brush.Danger — Alert, overload / clipping
    public static readonly SolidColorBrush DangerHover = T(nameof(DangerHover)); // Brush.DangerHover
    public static readonly SolidColorBrush DangerBright= T(nameof(DangerBright)); // Brush.DangerBright — Alert Light, readout text
    public static readonly SolidColorBrush DangerDeep  = T(nameof(DangerDeep)); // over-threshold fill, below Alert
    public static readonly SolidColorBrush DangerPale  = T(nameof(DangerPale)); // live-take waveform / label

    // ---- Data + modulation ------------------------------------------------
    // Role chromas live inside visualisers only. When two or three sources share
    // one graph, VizInk takes the primary and these follow in order. They never
    // reach a button and they never mean state.
    public static readonly SolidColorBrush Teal       = T(nameof(Teal)); // Brush.ChromaTeal — 2nd layer; also the modulation accent
    public static readonly SolidColorBrush TealBright = T(nameof(TealBright)); // Brush.ChromaTealLight
    public static readonly SolidColorBrush Rose       = T(nameof(Rose)); // Brush.ChromaRose — 3rd layer: right channel, highs, tail
    public static readonly SolidColorBrush RoseBright = T(nameof(RoseBright)); // Brush.ChromaRoseLight
    public static readonly SolidColorBrush Steel      = T(nameof(Steel)); // Brush.ChromaSteel — 4th layer, used rarely
    public static readonly SolidColorBrush Sage       = T(nameof(Sage)); // standalone hue (no longer a track slot)
    public static readonly SolidColorBrush Threshold  = T(nameof(Threshold)); // threshold markers
    public static readonly SolidColorBrush Marker     = T(nameof(Marker)); // clip markers / brackets
    public static readonly SolidColorBrush MarkerHot  = T(nameof(MarkerHot)); // resize-edge affordance
    public static readonly SolidColorBrush Frozen     = T(nameof(Frozen)); // frozen-track ice
    public static readonly SolidColorBrush SignalIn   = T(nameof(SignalIn)); // ghost "input" trace label
    public static readonly SolidColorBrush SignalInFill = T(nameof(SignalInFill));
    public static readonly SolidColorBrush HandleSel  = T(nameof(HandleSel)); // selected graph handle

    // ---- Tinted chrome ----------------------------------------------------
    public static readonly SolidColorBrush SelHeaderBg    = T(nameof(SelHeaderBg)); // selected track header — raised hover (the accent tick marks it)
    public static readonly SolidColorBrush FrozenHeaderBg = T(nameof(FrozenHeaderBg)); // frozen track header
    public static readonly SolidColorBrush MuteTint       = T(nameof(MuteTint)); // muted band
    public static readonly SolidColorBrush Veil           = T(nameof(Veil)); // deactivated-clip scrim base
    public static readonly SolidColorBrush TooltipBg      = T(nameof(TooltipBg)); // tooltip: a raised popover
    public static readonly SolidColorBrush TooltipText    = T(nameof(TooltipText));

    // ---- Wells and depth (style guide, spec 038) ----------------------------
    // The shell is flat: a button, field or menu at rest sits on a faint well instead of
    // a border. Depth lives only inside device cards: the card is raised (DeviceHeader,
    // Bevel), each section is pressed in (DeviceSection + its edge), and controls sit in
    // a Groove. The wells are see-through, so they read on any ground.
    public static readonly SolidColorBrush Well              = T(nameof(Well)); // Brush.Well — behind a button, field or menu at rest
    public static readonly SolidColorBrush WellTrack         = T(nameof(WellTrack)); // Brush.WellTrack — switch and slider track
    public static readonly SolidColorBrush KnobTrack         = T(nameof(KnobTrack)); // Brush.KnobTrack — the arc a knob's value runs on
    public static readonly SolidColorBrush Groove            = T(nameof(Groove)); // Brush.Groove — device controls sit in one
    public static readonly SolidColorBrush DeviceSection     = T(nameof(DeviceSection)); // Brush.DeviceSection — a pressed-in section inside a device
    public static readonly SolidColorBrush DeviceSectionEdge = T(nameof(DeviceSectionEdge)); // Brush.DeviceSectionEdge — that section's inner edge
    public static readonly SolidColorBrush DeviceHeader      = T(nameof(DeviceHeader)); // Brush.DeviceHeader — device card header
    public static readonly SolidColorBrush Bevel             = T(nameof(Bevel)); // Brush.Bevel — light top edge on raised parts
    public static readonly SolidColorBrush KnobCap           = T(nameof(KnobCap)); // Brush.KnobCap — knob centre cap (devices only)
    // Picked, not live: a pale accent fill with accent text (AccentBright on it).
    public static readonly SolidColorBrush AccentSoft        = T(nameof(AccentSoft)); // Brush.AccentSoft — behind something picked
    // Track meters: green to 70 %, amber to 88 %, red above.
    public static readonly SolidColorBrush MeterSafe         = T(nameof(MeterSafe)); // Brush.MeterSafe
    public static readonly SolidColorBrush MeterHot          = T(nameof(MeterHot)); // Brush.MeterHot
    public static readonly SolidColorBrush MeterOver         = T(nameof(MeterOver)); // Brush.MeterOver
    // A selected track: its header and its lane get a slightly lighter ground.
    public static readonly SolidColorBrush TrackHeaderSelected = T(nameof(TrackHeaderSelected)); // Brush.TrackHeaderSelected
    public static readonly SolidColorBrush LaneSelected      = T(nameof(LaneSelected)); // Brush.LaneSelected

    // ---- Piano keys (the clip editor keyboard is dark: keys recede, notes lead) ---
    public static readonly SolidColorBrush KeyWhite     = T(nameof(KeyWhite)); // piano-roll keys: dark, never the brightest thing
    public static readonly SolidColorBrush KeyBlack     = T(nameof(KeyBlack)); // black keys and their lanes
    public static readonly SolidColorBrush MiniKeyWhite = T(nameof(MiniKeyWhite)); // device-card mini keyboards
    public static readonly SolidColorBrush MiniKeyBlack = T(nameof(MiniKeyBlack));

    // ---- Track-shade mix targets (TrackColorForIndex) ---------------------
    public static readonly SolidColorBrush ShadeUp   = T(nameof(ShadeUp)); // "lighter" shade
    public static readonly SolidColorBrush ShadeDown = T(nameof(ShadeDown)); // "darker" shade

    // ---- Track palette (Brush.Track1..8) + return buses --------------------
    // Nine muted clip-colour roles: five core hues (teal, blue,
    // amber, violet, rose) plus four at matching chroma. A group sets the hue and its
    // tracks take neighbouring shades of it. Light values are the Studio Light rendition.
    //
    // Saved projects are NOT migrated: a stored colour index keeps working but now
    // points at the hue that sits at that place in this table.
    private static readonly SolidColorBrush[] TrackSlots =
    {
        T("Track1"), // Drums
        T("Track2"), // Perc
        T("Track3"), // Bass
        T("Track4"), // Keys
        T("Track5"), // Texture
        T("Track6"), // FX
        T("Track7"), // Brass
        T("Track8"), // Vox
        T("Track9"), // Return
    };
    // The master bus: a muted grey, not a hue — it is the sum, not a track.
    public static readonly SolidColorBrush Master = T(nameof(Master)); // Brush.Master
    // Return buses draw from the ninth role rather than a palette of their own.
    private static readonly SolidColorBrush[] ReturnSlots = { T("ReturnA"), T("ReturnB") };

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
    // brush follows the theme. (Bind-style reads — ControlExtensions.BindResource —
    // are already live and should keep using the resource observable.)
    private static readonly Dictionary<string, SolidColorBrush> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brush.SurfaceAbyss"] = SurfaceAbyss,   ["Brush.Gutter"] = Gutter,
        ["Brush.BgSunken"] = BgSunken,           ["Brush.ChromeBg"] = BgSunken,
        ["Brush.BgApp"] = BgApp,                 ["Brush.LaneB"] = LaneB,
        ["Brush.Panel"] = Panel,                 ["Brush.TrackOff"] = TrackOff,
        ["Brush.SurfaceCard"] = SurfaceCard,     ["Brush.SurfaceRaised"] = SurfaceRaised,
        ["Brush.SurfaceInset"] = SurfaceInset,   ["Brush.SurfaceDeep"] = SurfaceDeep,
        ["Brush.SurfaceHover"] = SurfaceHover,   ["Brush.SurfaceActive"] = SurfaceActive,
        ["Brush.SurfaceSelected"] = SelHeaderBg,
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
        ["Brush.Success"] = Success, ["Brush.SuccessInk"] = SuccessInk,
        ["Brush.SuccessDim"] = SuccessDim,       ["Brush.Warning"] = Warning,
        ["Brush.Danger"] = Danger,               ["Brush.DangerHover"] = DangerHover,
        ["Brush.DangerBright"] = DangerBright,
        ["Brush.ChromaTeal"] = Teal,             ["Brush.ChromaTealLight"] = TealBright,
        ["Brush.ChromaRose"] = Rose,             ["Brush.ChromaRoseLight"] = RoseBright,
        ["Brush.ChromaSteel"] = Steel,
        ["Brush.Seam"] = Seam,                   ["Brush.Rule"] = Rule,
        ["Brush.TextFaint"] = TextFaint,         ["Brush.Mute"] = Mute,
        ["Brush.Solo"] = Solo,                   ["Brush.VizInk"] = VizInk,
        ["Brush.VizGrid"] = VizGrid,
        ["Brush.Track1"] = TrackSlots[0],        ["Brush.Track2"] = TrackSlots[1],
        ["Brush.Track3"] = TrackSlots[2],        ["Brush.Track4"] = TrackSlots[3],
        ["Brush.Track5"] = TrackSlots[4],        ["Brush.Track6"] = TrackSlots[5],
        ["Brush.Track7"] = TrackSlots[6],        ["Brush.Track8"] = TrackSlots[7],
        ["Brush.Track9"] = TrackSlots[8],
        ["Brush.ReturnA"] = ReturnSlots[0],      ["Brush.ReturnB"] = ReturnSlots[1],
        ["Brush.Master"] = Master,
        ["Brush.Well"] = Well,                   ["Brush.WellTrack"] = WellTrack,
        ["Brush.KnobTrack"] = KnobTrack,         ["Brush.Groove"] = Groove,
        ["Brush.DeviceSection"] = DeviceSection, ["Brush.DeviceSectionEdge"] = DeviceSectionEdge,
        ["Brush.DeviceHeader"] = DeviceHeader,   ["Brush.Bevel"] = Bevel,
        ["Brush.KnobCap"] = KnobCap,             ["Brush.AccentSoft"] = AccentSoft,
        ["Brush.MeterSafe"] = MeterSafe,         ["Brush.MeterHot"] = MeterHot,
        ["Brush.MeterOver"] = MeterOver,
        ["Brush.TrackHeaderSelected"] = TrackHeaderSelected, ["Brush.LaneSelected"] = LaneSelected,
        // Washes, not tokens: they follow their source slot.
        ["Brush.SurfaceHoverClear"] = Wash(SurfaceHover, 0x00),
        ["Brush.DangerSubtle"] = Wash(Danger, 0x33),
    };

    /// <summary>Every <c>Brush.*</c> key with its slot — what NotaThemeService publishes to
    /// XAML, so <c>{DynamicResource Brush.*}</c> resolves to the very brush the theme re-tints.</summary>
    public static IEnumerable<KeyValuePair<string, SolidColorBrush>> AllKeys => KeyMap;

    /// <summary>The slot behind a <c>Brush.*</c> XAML key, or null if the key is unknown.</summary>
    public static SolidColorBrush? ByKey(string key) => KeyMap.GetValueOrDefault(key);

    // ---- Raw Colors — properties, never cached: they move with the theme --
    public static Color AccentColor => Accent.Color;
    public static Color AccentBrightColor => AccentBright.Color;
    public static Color SuccessColor => Success.Color;
    public static Color TealColor => Teal.Color;
}
