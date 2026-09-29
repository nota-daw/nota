// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Length GATE window (almanac mockups 1a / 1b): one bar of five demo notes of different
// pitch and velocity, and what the device does to each. Per row: the input note as an
// outline, the output note as a brass fill, and a dark band behind it — the range Random
// spreads that note's length over. The window runs 1¼ bars; the part after the bar line is
// shaded (Clip length limit cuts there). A playhead sweeps the bar; the note under it lights
// Brass Light, and Random re-rolls each pass. The full view adds note labels, the output
// length tags and a ms ruler; the mini view is the window alone.
//
// The model mirrors MidiNoteLength::len/schedule so the picture is what the device plays.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class LengthGateViz : Control
{
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush WellEdge = NotaPalette.GraphBorder;
    private static readonly IBrush BeatLine = NotaPalette.GraphBorder;
    private static readonly IBrush HalfLine = NotaPalette.SurfaceCard;
    private static readonly IBrush PastBar = NotaPalette.Wash(NotaPalette.SurfaceAbyss, 0xA0);   // like the piano roll past the clip end
    private static readonly IBrush Band = NotaPalette.Ink("#2A2317");
    private static readonly IBrush Fill = NotaPalette.Ink("#B8862E");
    private static readonly IBrush FillLit = NotaPalette.AccentBright;
    private static readonly IBrush FillOff = NotaPalette.BorderStrong;
    private static readonly IBrush Outline = NotaPalette.TextMuted;
    private static readonly IBrush Head = NotaPalette.Wash(NotaPalette.AccentBright, 0xB3);
    private static readonly IBrush Label = NotaPalette.TextSecondary;
    private static readonly IBrush VelInk = NotaPalette.TextDisabled;
    private static readonly IBrush TagInk = NotaPalette.TextMuted;
    private static readonly IBrush Axis = NotaPalette.TextAxis;

    public const double Bar = 4.0, Window = 5.0;   // beats: one 4/4 bar, the window 1¼ bars

    /// <summary>The demo phrase, in beats (at 120 BPM: 0 / 500 / 750 / 1250 / 1500 ms).</summary>
    internal static readonly (double T, double Len, int Key, int Vel)[] Input =
    {
        (0.0, 0.76, 48, 112), (1.0, 0.32, 55, 64), (1.5, 1.04, 60, 96), (2.5, 0.40, 67, 44), (3.0, 0.72, 72, 124),
    };

    /// <summary>One note of the model, in beats: where the forced note starts, its output
    /// length and the Random range [lo, hi].</summary>
    internal readonly record struct Note(double Start, double Out, double Lo, double Hi);

    // Param indices — mirror MidiNoteLength.h.
    private const int PGate = 1, PMode = 2, PMs = 3, PPercent = 4, PTrigger = 5, PVel = 6, PKey = 7, PRandom = 8, PLegato = 9, PClip = 10, PDivision = 11;
    internal static readonly double[] DivBeats = { 0.125, 0.25, 0.5, 1.0, 2.0, 4.0, 0.75, 2.0 / 3.0 };

    private static double Hash(int i, int cycle)
    {
        double x = Math.Sin((i + 1) * 12.9898 + cycle * 78.233) * 43758.5453;
        return (x - Math.Floor(x)) * 2 - 1;
    }
    private static double BarEnd(double beat) => (Math.Floor(beat / Bar + 1e-9) + 1) * Bar;

    /// <summary>The five notes as the device would play them at this tempo; <paramref name="cycle"/>
    /// seeds Random (a new roll per bar).</summary>
    internal static Note[] Compute(Func<int, float> g, double bpm, int cycle)
    {
        int mode = Math.Clamp((int)Math.Round(g(PMode)), 0, 2);
        bool onOff = g(PTrigger) >= 0.5f, legato = g(PLegato) >= 0.5f, clip = g(PClip) >= 0.5f;
        double vel = Math.Clamp(g(PVel), -1, 1), key = Math.Clamp(g(PKey), -1, 1), rnd = Math.Clamp(g(PRandom), 0, 1);
        double beatsPerMs = (bpm > 0 ? bpm : 120) / 60000.0;
        double Start(int i) => onOff ? Input[i].T + Input[i].Len : Input[i].T;
        var notes = new Note[Input.Length];
        for (int i = 0; i < Input.Length; i++)
        {
            var n = Input[i];
            double L = mode switch
            {
                1 => Math.Clamp(g(PMs), 1, 2000) * beatsPerMs,
                2 => n.Len * Math.Clamp(g(PPercent), 1, 200) / 100.0,
                _ => DivBeats[Math.Clamp((int)Math.Round(g(PDivision)), 0, DivBeats.Length - 1)] * Math.Clamp(g(PGate), 0.05, 2.0),
            };
            L *= Math.Max(0.05, 1 + vel * (n.Vel - 64) / 64.0);
            L *= Math.Max(0.05, 1 - key * (n.Key - 60) / 24.0);
            double s = Start(i);
            double lo = L * (1 - rnd), hi = L * (1 + rnd), o = L * (1 + rnd * Hash(i, cycle));
            if (legato)
            {
                // Until the next note starts, at most to the bar line.
                double next = double.PositiveInfinity;
                for (int j = 0; j < Input.Length; j++) if (j != i && Start(j) > s && Start(j) < next) next = Start(j);
                lo = hi = o = Math.Max(1.0 / 256, Math.Min(next, BarEnd(s)) - s);
            }
            if (clip) { double lim = BarEnd(s) - s; lo = Math.Min(lo, lim); hi = Math.Min(hi, lim); o = Math.Min(o, lim); }
            notes[i] = new Note(s, Math.Max(1.0 / 256, o), lo, hi);
        }
        return notes;
    }

    internal static string Ms(double ms) => ms >= 1000 ? $"{ms / 1000:0.00} s" : $"{Math.Round(ms):0} ms";

    /// <summary>Full view: note labels, output tags and the ruler.</summary>
    public bool Full { get; init; } = true;

    private Note[] _notes = Array.Empty<Note>();
    private double _now, _bpm = 120;
    private bool _off;

    public LengthGateViz() { MinHeight = 40; ClipToBounds = true; }

    public void Set(Note[] notes, double nowBeats, double bpm, bool bypassed)
    {
        _notes = notes; _now = nowBeats; _bpm = bpm > 0 ? bpm : 120; _off = bypassed;
        InvalidateVisual();
    }

    private static FormattedText Text(string s, Typeface tf, double size, IBrush ink)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, ink);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double labelW = Full ? 43 : 0, rulerH = Full ? 12 : 0;
        var win = new Rect(labelW, 0, Math.Max(10, w - labelW), Math.Max(10, h - rulerH));
        double msPerBeat = 60000.0 / _bpm;
        int n = Math.Max(1, _notes.Length);
        double rowH = win.Height / n;
        double X(double beat) => win.X + Math.Clamp(beat / Window, 0, 1) * win.Width;

        ctx.DrawRectangle(Well, new Pen(WellEdge, 1), new RoundedRect(win.Deflate(0.5), 4));
        using (ctx.PushClip(new RoundedRect(win.Deflate(1), 3)))
        {
            for (double b = 0.5; b < Window - 1e-6; b += 0.5)
                ctx.FillRectangle(Math.Abs(b % 1) < 1e-6 ? BeatLine : HalfLine, new Rect(Math.Round(X(b)), win.Y, 1, win.Height));
            ctx.FillRectangle(PastBar, new Rect(X(Bar), win.Y, win.Right - X(Bar), win.Height));

            double pad = Full ? 5 : Math.Min(5, rowH * 0.18), radius = Full ? 2 : 1;
            for (int i = 0; i < _notes.Length; i++)
            {
                var nt = _notes[i]; var inp = Input[i];
                double top = win.Y + i * rowH + pad, hh = Math.Max(2, rowH - 2 * pad), cy = win.Y + (i + 0.5) * rowH;
                bool lit = !_off && _now >= nt.Start && _now < nt.Start + nt.Out;
                // Random band behind, output fill, input outline.
                if (nt.Hi - nt.Lo > 1e-4)
                    ctx.FillRectangle(Band, new Rect(X(nt.Start + nt.Lo), cy - 1.5, Math.Max(0, X(nt.Start + nt.Hi) - X(nt.Start + nt.Lo)), 3));
                double ox = X(nt.Start), oe = X(nt.Start + nt.Out);
                ctx.DrawRectangle(_off ? FillOff : lit ? FillLit : Fill, null, new RoundedRect(new Rect(ox, top, Math.Max(1.5, oe - ox), hh), radius));
                double ix = X(inp.T), ie = X(inp.T + inp.Len);
                ctx.DrawRectangle(null, new Pen(Outline, 1), new RoundedRect(new Rect(ix + 0.5, top + 0.5, Math.Max(1, ie - ix - 1), Math.Max(1, hh - 1)), radius));
                if (Full)
                {
                    var tag = Text(Ms(nt.Out * msPerBeat), NotaFonts.Mono, 7, lit ? FillLit : TagInk);
                    double tx = Math.Min(Math.Max(Math.Max(oe, ie), X(nt.Start + nt.Hi)), win.X + win.Width * 0.88) + 5;
                    ctx.DrawText(tag, new Point(tx, top + hh / 2 - tag.Height / 2));
                }
            }
            ctx.FillRectangle(Head, new Rect(Math.Round(X(_now)), win.Y, 1, win.Height));
        }

        if (!Full) return;
        // Row labels: note name + velocity.
        for (int i = 0; i < _notes.Length; i++)
        {
            var inp = Input[i];
            bool lit = !_off && _now >= _notes[i].Start && _now < _notes[i].Start + _notes[i].Out;
            var name = Text(DeviceCardKit.NoteName(inp.Key), NotaFonts.Mono, 8, lit ? FillLit : Label);
            var vel = Text($"vel {inp.Vel}", NotaFonts.Mono, 6, VelInk);
            double cy = win.Y + (i + 0.5) * rowH, th = name.Height + vel.Height - 2;
            ctx.DrawText(name, new Point(0, cy - th / 2));
            ctx.DrawText(vel, new Point(0, cy - th / 2 + name.Height - 2));
        }
        // Ruler: one label per beat, in time at the current tempo.
        for (int b = 0; b <= (int)Window; b++)
        {
            string s = b == 0 ? "0" : b * msPerBeat >= 1000 ? $"{b * msPerBeat / 1000:0.0#} s" : $"{b * msPerBeat:0} ms";
            var ft = Text(s, NotaFonts.Mono, 7, Axis);
            double x = X(b) - (b == 0 ? 0 : b == (int)Window ? ft.Width : ft.Width / 2);
            ctx.DrawText(ft, new Point(x, win.Bottom + 2));
        }
    }
}
