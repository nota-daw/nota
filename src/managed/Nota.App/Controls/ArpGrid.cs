// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Arp window (mockup 1a / 1b) — one custom-drawn well with two faces:
//   • Pattern: rows = the chord over every octave (root rows striped, note names in a left
//     gutter), one cell per step = the note that step plays. Cell width = Gate × Length,
//     odd steps pushed right by Swing, a ratchet splits the cell, a muted step is an empty
//     outline. Played steps are Brass Deep, the sounding one Brass Light, the rest Brass Edge.
//   • Groove: the selected lane's per-step bars (Velocity / Length / Chance / Ratchet as
//     stacked quarters / Transpose bipolar from the centre); drag across the well to draw
//     (⌥ draws a ramp); the step-number strip under it mutes a step.
// A brass head band marks the sounding step; it moves with the engine's telemetry.
// ArpPattern mirrors Arpeggiator.h so the picture is exactly what will play.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

/// <summary>A C# mirror of the Nota Arp note order (Arpeggiator.h buildSequence / emitStep).</summary>
internal static class ArpPattern
{
    public const int OrdUp = 0, OrdDown = 1, OrdUpDown = 2, OrdConverge = 3, OrdAsPlayed = 4, OrdChord = 5, OrdRandom = 6, OrdDownUp = 7, OrdDiverge = 8;
    public const int OctUp = 0, OctDown = 1, OctUpDown = 2, OctRandom = 3;
    public const int LoopFwd = 0, LoopBack = 1, LoopPing = 2, LoopRandom = 3;

    /// <summary>The walked note order: the chord expanded over the octaves, then ordered.</summary>
    public static List<int> Sequence(IReadOnlyList<int> held, int order, int octaves, int octMode)
    {
        var res = new List<int>();
        if (held.Count == 0) return res;
        var baseNotes = order == OrdAsPlayed ? held.ToList() : held.OrderBy(p => p).ToList();
        octaves = Math.Clamp(octaves, 1, 8);
        var oo = new List<int>();
        switch (octMode)
        {
            case OctDown: for (int o = octaves - 1; o >= 0; o--) oo.Add(o); break;
            case OctUpDown: for (int o = 0; o < octaves; o++) oo.Add(o); for (int o = octaves - 2; o >= 1; o--) oo.Add(o); break;
            case OctRandom: oo.Add(0); break;
            default: for (int o = 0; o < octaves; o++) oo.Add(o); break;
        }
        var a = new List<int>();
        foreach (int o in oo) foreach (int p in baseNotes) a.Add(p + 12 * o);
        int n = a.Count;
        switch (order)
        {
            case OrdDown: for (int i = n - 1; i >= 0; i--) res.Add(a[i]); break;
            case OrdUpDown: res.AddRange(a); for (int i = n - 2; i >= 1; i--) res.Add(a[i]); break;
            case OrdDownUp: for (int i = n - 1; i >= 0; i--) res.Add(a[i]); for (int i = 1; i <= n - 2; i++) res.Add(a[i]); break;
            case OrdConverge: case OrdDiverge:
            {
                var c = new List<int>(); int lo = 0, hi = n - 1;
                while (lo <= hi) { c.Add(a[lo]); if (lo != hi) c.Add(a[hi]); lo++; hi--; }
                if (order == OrdDiverge) c.Reverse();
                res.AddRange(c); break;
            }
            default: res.AddRange(a); break;
        }
        return res.Take(256).ToList();
    }

    /// <summary>The pitches pattern step <paramref name="kp"/> plays (all of them for Chord).</summary>
    public static IEnumerable<int> NotesAt(List<int> seq, int order, long kp, int octaves, int octMode)
    {
        if (seq.Count == 0) yield break;
        int oc = octMode == OctRandom ? 12 * (int)(Rnd01(kp, 5) * Math.Clamp(octaves, 1, 8)) : 0;
        if (order == OrdChord) { foreach (int p in seq) yield return p + oc; yield break; }
        long idx = order == OrdRandom ? (long)(Rnd01(kp, 7) * seq.Count) : kp % seq.Count;
        idx = ((idx % seq.Count) + seq.Count) % seq.Count;
        yield return seq[(int)idx] + oc;
    }

    private static uint Hash32(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
    public static double Rnd01(long stepAbs, int salt)
        => (Hash32(unchecked((uint)(stepAbs * 2654435761u) + (uint)salt * 40503u)) & 0xffffff) / (double)0x1000000;
}

internal sealed class ArpGrid : Control
{
    // Param layout (mirrors Arpeggiator.h): 13 globals, 7 lanes × 16 steps, then VelAmt / View.
    internal const int GRate = 0, GSync = 1, GFreeRate = 2, GGate = 3, GOctaves = 4, GOctaveMode = 5,
                       GOrder = 6, GSwing = 7, GHold = 8, GRetrig = 9, GTranspose = 10, GLoop = 11, GLoopMode = 12;
    internal const int LVel = 13, LLen = 29, LChance = 45, LRatchet = 61, LTransp = 77, LOn = 93, LCC = 109;
    internal const int PVelAmt = 125, PView = 126;
    internal const int Steps = 16;

    /// <summary>A Groove lane: its first param, the engine range, and the range the editor draws.</summary>
    internal sealed record Lane(string Label, int Base, double Min, double Max, Func<double, double> ToDisp, Func<double, double> FromDisp, Func<double, string> Fmt, bool Bipolar = false);

    internal static readonly Lane[] Lanes =
    {
        new("Velocity", LVel, 1, 127, v => Math.Round(v * 127), d => d / 127.0, d => $"{d:0}"),
        new("Length", LLen, 5, 100, v => Math.Round(v * 100), d => d / 100.0, d => $"{d:0} %"),
        new("Chance", LChance, 0, 100, v => Math.Round(v * 100), d => d / 100.0, d => $"{d:0} %"),
        new("Ratchet", LRatchet, 1, 4, v => Math.Round(v), d => d, d => $"×{d:0}"),
        new("Transpose", LTransp, -12, 12, v => Math.Round(v), d => d, d => d == 0 ? "0\u2009st" : $"{(d > 0 ? "+" : "−")}{Math.Abs(d):0} st", Bipolar: true),
    };

    private static readonly IBrush Stripe = NotaPalette.Panel;
    private static readonly IBrush ColMinor = NotaPalette.GridSubBeat;
    private static readonly IBrush ColMajor = NotaPalette.GraphBorder;
    private static readonly IBrush Head = NotaPalette.Wash(NotaPalette.Accent, 0x14);
    private static readonly IBrush Zero = NotaPalette.BorderDefault;
    private static readonly Typeface Mono = NotaFonts.Mono;
    private const double GutterW = 20, NumsH = 13;

    private readonly IAudioEngine _e; private readonly int _t, _mi;
    private readonly float[] _scope = new float[40];
    private bool _groove;
    private int _lane;
    private int _dragStep = -1; private double _dragDisp;
    private int _pos = -1; private long _k = -1; private int _heldLive;
    private List<int> _chord = new();

    /// <summary>Mini (S) face: no key gutter and no step-number strip; the lowest note sits in the well.</summary>
    public bool Mini { get; init; }
    /// <summary>Last Groove edit (step, display value) for the header readout; cleared on lane/tab change.</summary>
    public (int Step, double Value)? LastEdit { get; private set; }
    public event Action? Edited;

    public ArpGrid(IAudioEngine e, int t, int mi) { _e = e; _t = t; _mi = mi; MinHeight = 60; ClipToBounds = true; Poll(); }

    public bool Groove { get => _groove; set { if (_groove == value) return; _groove = value; LastEdit = null; Cursor = value ? new Cursor(StandardCursorType.Cross) : Cursor.Default; InvalidateVisual(); } }
    public int LaneIndex { get => _lane; set { value = Math.Clamp(value, 0, Lanes.Length - 1); if (_lane == value) return; _lane = value; LastEdit = null; InvalidateVisual(); } }

    /// <summary>Loop position of the sounding step (-1 = idle).</summary>
    public int Position => _pos;
    /// <summary>The chord the Pattern shows (held, or the last one played; empty = none yet).</summary>
    public IReadOnlyList<int> Chord => _chord;
    /// <summary>True while notes are held (or latched by Hold) and the arp runs.</summary>
    public bool Running => _heldLive > 0;
    /// <summary>The preview chord drawn before anything has been played.</summary>
    internal static readonly int[] PreviewChord = { 48, 52, 55, 59 };

    private double G(int p) => _e.MidiEffectGetParam(_t, _mi, p);
    private int GI(int p) => (int)Math.Round(G(p));
    public int StepCount => Math.Clamp(GI(GLoop), 1, Steps);

    /// <summary>Re-read the engine telemetry (60 Hz) and repaint.</summary>
    public void Poll()
    {
        int n = _e.MidiEffectScope(_t, _mi, _scope);
        if (n >= 4)
        {
            _pos = (int)_scope[0]; _k = (long)_scope[1]; _heldLive = (int)_scope[2];
            int cn = Math.Min((int)_scope[3], n - 4);
            if (cn != _chord.Count || Enumerable.Range(0, cn).Any(i => (int)_scope[4 + i] != _chord[i]))
                _chord = Enumerable.Range(0, cn).Select(i => (int)_scope[4 + i]).ToList();
        }
        InvalidateVisual();
    }

    public IReadOnlyList<int> ShownChord => _chord.Count > 0 ? _chord : PreviewChord;
    public List<int> CurrentSequence() => ArpPattern.Sequence(ShownChord, GI(GOrder), GI(GOctaves), GI(GOctaveMode));

    // ---- geometry ---------------------------------------------------------------
    private Rect Well()
    {
        double gx = !Groove && !Mini ? GutterW + 4 : 0;
        double nb = Groove && !Mini ? NumsH + 2 : 0;
        return new Rect(gx, 0, Math.Max(1, Bounds.Width - gx), Math.Max(1, Bounds.Height - nb));
    }

    // ---- input (Groove) -------------------------------------------------------------
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!Groove || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this); var w = Well();
        int n = StepCount;
        if (!Mini && p.Y > w.Bottom)
        {
            int s = Math.Clamp((int)((p.X - w.X) / w.Width * n), 0, n - 1);
            _e.MidiEffectSetParam(_t, _mi, LOn + s, G(LOn + s) > 0.5 ? 0f : 1f);
            e.Handled = true; InvalidateVisual(); Edited?.Invoke(); return;
        }
        _dragStep = StepAt(p.X); _dragDisp = DispAt(p.Y);
        Apply(_dragStep, _dragDisp);
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragStep < 0) return;
        var p = e.GetPosition(this); int s = StepAt(p.X); double d = DispAt(p.Y);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && s != _dragStep)
        {
            int lo = Math.Min(_dragStep, s), hi = Math.Max(_dragStep, s);
            for (int i = lo; i <= hi; i++) Apply(i, _dragDisp + (d - _dragDisp) * (i - _dragStep) / (double)(s - _dragStep));
        }
        else Apply(s, d);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (_dragStep >= 0) { _dragStep = -1; e.Pointer.Capture(null); } }

    private int StepAt(double x) { var w = Well(); int n = StepCount; return Math.Clamp((int)((x - w.X) / w.Width * n), 0, n - 1); }
    private double DispAt(double y)
    {
        var w = Well(); var l = Lanes[_lane];
        double f = Math.Clamp(1 - (y - w.Y) / w.Height, 0, 1);
        return Math.Round(l.Min + f * (l.Max - l.Min));
    }
    private void Apply(int s, double disp)
    {
        var l = Lanes[_lane];
        disp = Math.Clamp(Math.Round(disp), l.Min, l.Max);
        _e.MidiEffectSetParam(_t, _mi, l.Base + s, (float)l.FromDisp(disp));
        LastEdit = (s, disp);
        InvalidateVisual(); Edited?.Invoke();
    }

    // ---- render ---------------------------------------------------------------------
    private static void Text(DrawingContext ctx, string t, double x, double y, IBrush b, double size = 7, bool right = false, bool centre = false)
    {
        var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b);
        double px = right ? x - ft.Width : centre ? x - ft.Width / 2 : x;
        ctx.DrawText(ft, new Point(px, y - ft.Height / 2));
    }

    private IBrush Tone(int c, bool muted, IBrush future)
        => muted ? NotaPalette.TrackOff : c == _pos ? NotaPalette.AccentBright : _pos >= 0 && c < _pos ? NotaPalette.AccentDeep : future;

    public override void Render(DrawingContext ctx)
    {
        if (Bounds.Width <= 2 || Bounds.Height <= 2) return;
        var w = Well();
        int n = StepCount; double cw = w.Width / n;
        var seq = CurrentSequence();
        var chord = ShownChord;
        int octs = Math.Clamp(GI(GOctaves), 1, 8);
        int rootPc = ((chord.Count > 0 ? chord.Min() : 48) % 12 + 12) % 12;
        // Rows: every pitch the order can reach, high → low.
        var rows = ArpPattern.Sequence(chord, ArpPattern.OrdUp, octs, ArpPattern.OctUp).Distinct().OrderByDescending(p => p).ToList();
        if (rows.Count == 0) rows.Add(60);
        double rh = w.Height / rows.Count;

        // Well.
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.GraphBorder, 1), w.Deflate(0.5), 4, 4);
        using (ctx.PushClip(new RoundedRect(w.Deflate(1), 3)))
        {
            if (!Groove)
                for (int r = 0; r < rows.Count; r++)
                    if (((rows[r] % 12) + 12) % 12 == rootPc) ctx.FillRectangle(Stripe, new Rect(w.X, w.Y + r * rh, w.Width, rh));
            for (int c = 1; c < n; c++)
                ctx.FillRectangle(c % 4 == 0 ? ColMajor : ColMinor, new Rect(w.X + Math.Round(c * cw), w.Y, 1, w.Height));
            var lane = Lanes[_lane];
            if (Groove && lane.Bipolar) ctx.FillRectangle(Zero, new Rect(w.X, w.Y + Math.Round(w.Height / 2), w.Width, 1));

            // Head band on the sounding step.
            if (_pos >= 0 && _pos < n)
            {
                var hr = new Rect(w.X + _pos * cw, w.Y, cw, w.Height);
                ctx.FillRectangle(Head, hr);
                ctx.FillRectangle(NotaPalette.BorderBrass, new Rect(hr.X, hr.Y, 1, hr.Height));
            }

            if (!Groove) DrawPattern(ctx, w, n, cw, seq, rows, rh, octs);
            else DrawGroove(ctx, w, n, cw, lane);
        }

        // Key gutter (L Pattern): note names, root rows brighter; all names while it stays legible.
        if (!Groove && !Mini)
            for (int r = 0; r < rows.Count; r++)
            {
                bool root = ((rows[r] % 12) + 12) % 12 == rootPc;
                if (rows.Count > 8 && !root) continue;
                Text(ctx, DeviceCardKit.NoteName(rows[r]), GutterW, w.Y + (r + 0.5) * rh, root ? NotaPalette.TextMuted : NotaPalette.TextDisabled, 7, right: true);
            }
        if (!Groove && Mini && rows.Count > 0)
            Text(ctx, DeviceCardKit.NoteName(rows[^1]), w.X + 4, w.Bottom - 7, NotaPalette.TextAxis, 7);

        // Step numbers (L Groove): click mutes.
        if (Groove && !Mini)
        {
            double y = w.Bottom + 2 + NumsH / 2;
            for (int c = 0; c < n; c++)
            {
                bool muted = G(LOn + c) < 0.5;
                var ink = muted ? NotaPalette.TextAxis : c == _pos ? NotaPalette.AccentBright : NotaPalette.TextMuted;
                double cx = w.X + (c + 0.5) * cw;
                Text(ctx, (c + 1).ToString(), cx, y, ink, 7, centre: true);
                if (muted) ctx.FillRectangle(ink, new Rect(cx - 4, y, 8, 1));
            }
        }
    }

    private void DrawPattern(DrawingContext ctx, Rect w, int n, double cw, List<int> seq, List<int> rows, double rh, int octs)
    {
        if (seq.Count == 0) return;
        int order = GI(GOrder), octMode = GI(GOctaveMode);
        double gate = Math.Clamp(G(GGate), 0, 2), swing = Math.Clamp(G(GSwing), 0, 1);
        long page = _k >= 0 ? (_k / n) * n : 0;
        var rowOf = new Dictionary<int, int>();
        for (int i = 0; i < rows.Count; i++) rowOf[rows[i]] = i;
        for (int c = 0; c < n; c++)
        {
            bool muted = G(LOn + c) < 0.5;
            double len = Math.Clamp(G(LLen + c), 0.05, 2);
            int rat = Math.Clamp(GI(LRatchet + c), 1, 8);
            double wFrac = Math.Clamp(gate * len, 0.12, 1.0);
            double off = c % 2 == 1 ? swing * 0.5 * cw : 0;
            double cellW = wFrac * cw;
            IBrush fill = muted ? Brushes.Transparent : c == _pos ? NotaPalette.AccentBright : _pos >= 0 && c < _pos ? NotaPalette.AccentDeep : NotaPalette.AccentEdge;
            IBrush border = muted ? NotaPalette.BorderStrong : fill;
            foreach (int p in ArpPattern.NotesAt(seq, order, page + c, octs, octMode))
            {
                if (!rowOf.TryGetValue(p, out int r)) continue;   // a random octave above the drawn range
                for (int k = 0; k < rat; k++)
                {
                    double x = w.X + c * cw + off + k * cellW / rat + 1;
                    var rc = new Rect(x, w.Y + r * rh + 1, Math.Max(1, cellW / rat - 2), Math.Max(1, rh - 2));
                    if (muted) ctx.DrawRectangle(null, new Pen(border, 1), rc.Deflate(0.5), 2, 2);
                    else ctx.DrawRectangle(fill, null, rc, 2, 2);
                }
            }
        }
    }

    private void DrawGroove(DrawingContext ctx, Rect w, int n, double cw, Lane lane)
    {
        for (int c = 0; c < n; c++)
        {
            bool muted = G(LOn + c) < 0.5;
            double d = Math.Clamp(lane.ToDisp(G(lane.Base + c)), lane.Min, lane.Max);
            double f = (d - lane.Min) / (lane.Max - lane.Min);
            var fill = Tone(c, muted, NotaPalette.AccentDim);
            double x = w.X + c * cw + 2, bw = Math.Max(1, cw - 4);
            if (lane.Base == LRatchet)
            {
                double q = w.Height / 4;
                for (int k = 0; k < (int)d; k++) ctx.DrawRectangle(fill, null, new Rect(x, w.Bottom - (k + 1) * q + 2, bw, Math.Max(1, q - 3)), 1, 1);
            }
            else if (lane.Bipolar)
            {
                double h = Math.Abs(f - 0.5) * w.Height, mid = w.Y + w.Height / 2;
                ctx.DrawRectangle(fill, null, new Rect(x, f >= 0.5 ? mid - h : mid, bw, Math.Max(h, 1)), 1, 1);
            }
            else
            {
                double h = Math.Max(f, 0.02) * w.Height;
                ctx.DrawRectangle(fill, null, new Rect(x, w.Bottom - h, bw, h), 1, 1);
            }
        }
        if (!Mini)
        {
            Text(ctx, lane.Fmt(lane.Max), w.Right - 5, w.Y + 7, NotaPalette.TextAxis, 7, right: true);
            Text(ctx, lane.Fmt(lane.Min), w.Right - 5, w.Bottom - 7, NotaPalette.TextAxis, 7, right: true);
        }
    }
}
