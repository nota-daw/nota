// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Audio-clip ADSR handles, shared by the arrangement lane and the audio clip editor so the
// two read and behave the same. Three handles, in played time over the clip:
//   Attack  — a square on the top edge at A (drag sideways);
//   D · S   — a dot at (A+D, sustain) (drag both ways: decay time + sustain level);
//   Release — a square on the sustain line at len−R (drag sideways).
// At the identity (0, 0, 1, 0) the attack square and the D·S dot share the top-left corner:
// the first move decides — sideways grabs the attack, up/down grabs the sustain.
// Double-click on a handle resets its stage.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal enum AdsrHandle { None, Attack, DecaySustain, Release, AttackOrSustain }

/// <summary>Maps the clip's played time (beats 0..Len) and level (0..1) onto a view.</summary>
internal readonly record struct AdsrFrame(double Left, double Right, double Top, double Bottom, double Len)
{
    public double X(double beat) => Left + (Len > 0 ? beat / Len : 0) * (Right - Left);
    public double Beat(double x) => Right > Left ? (x - Left) / (Right - Left) * Len : 0;
    public double Y(double level) => Bottom - Math.Clamp(level, 0, 1) * (Bottom - Top);
    public double Level(double y) => Bottom > Top ? (Bottom - y) / (Bottom - Top) : 1;
}

internal static class ClipAdsrKit
{
    public const double HitPx = 6;
    private const double Square = 7, Dot = 4;

    public static Point AttackPt(in AdsrFrame f, ClipAdsr a) => new(f.X(a.AttackBeats), f.Top);
    public static Point DecayPt(in AdsrFrame f, ClipAdsr a) => new(f.X(a.AttackBeats + a.DecayBeats), f.Y(a.Sustain));
    public static Point ReleasePt(in AdsrFrame f, ClipAdsr a) => new(f.X(f.Len - a.ReleaseBeats), f.Y(a.Sustain));

    private static bool Near(Point p, Point q) => Math.Abs(p.X - q.X) <= HitPx && Math.Abs(p.Y - q.Y) <= HitPx;

    /// <summary>The handle under <paramref name="p"/>. The stages are fitted to the clip first so a
    /// clip shortened below A+D+R still shows grabbable handles.</summary>
    public static AdsrHandle Hit(Point p, in AdsrFrame f, ClipAdsr a)
    {
        if (f.Right - f.Left < 12) return AdsrHandle.None;
        a = a.Fit(f.Len);
        bool att = Near(p, AttackPt(f, a)), dec = Near(p, DecayPt(f, a));
        if (att && dec) return AdsrHandle.AttackOrSustain;
        if (dec) return AdsrHandle.DecaySustain;
        if (att) return AdsrHandle.Attack;
        if (Near(p, ReleasePt(f, a))) return AdsrHandle.Release;
        return AdsrHandle.None;
    }

    /// <summary>Settles a shared-corner grab by the first move: sideways = attack, up/down = sustain.</summary>
    public static AdsrHandle Resolve(AdsrHandle h, double dx, double dy)
        => h != AdsrHandle.AttackOrSustain ? h : Math.Abs(dy) > Math.Abs(dx) ? AdsrHandle.DecaySustain : AdsrHandle.Attack;

    /// <summary>Where a handle sits (clip-local beat, level) — the drag reference at press.</summary>
    public static (double beat, double level) Anchor(AdsrHandle h, ClipAdsr a, double len)
    {
        a = a.Fit(len);
        return h switch
        {
            AdsrHandle.DecaySustain => (a.AttackBeats + a.DecayBeats, a.Sustain),
            AdsrHandle.Release => (len - a.ReleaseBeats, a.Sustain),
            _ => (a.AttackBeats, 1.0),
        };
    }

    /// <summary>Moves handle <paramref name="h"/> to (beat, level); the other stages stay put and
    /// the moved one stops where it would cross them or leave the clip.</summary>
    public static ClipAdsr Drag(AdsrHandle h, ClipAdsr start, double beat, double level, double len)
    {
        var a = start.Fit(len);
        switch (h)
        {
            case AdsrHandle.Attack:
                a.AttackBeats = Math.Clamp(beat, 0, Math.Max(0, len - a.DecayBeats - a.ReleaseBeats));
                break;
            case AdsrHandle.DecaySustain:
                a.DecayBeats = Math.Clamp(beat - a.AttackBeats, 0, Math.Max(0, len - a.ReleaseBeats - a.AttackBeats));
                a.Sustain = (float)Math.Clamp(level, 0, 1);
                break;
            case AdsrHandle.Release:
                a.ReleaseBeats = Math.Clamp(len - beat, 0, Math.Max(0, len - a.AttackBeats - a.DecayBeats));
                break;
        }
        return a;
    }

    /// <summary>Resets one stage to its identity value (double-click on a handle).</summary>
    public static ClipAdsr Reset(AdsrHandle h, ClipAdsr a)
    {
        switch (h)
        {
            case AdsrHandle.Attack: a.AttackBeats = 0; break;
            case AdsrHandle.DecaySustain: a.DecayBeats = 0; a.Sustain = 1f; break;
            case AdsrHandle.Release: a.ReleaseBeats = 0; break;
            case AdsrHandle.AttackOrSustain: a.AttackBeats = 0; a.DecayBeats = 0; a.Sustain = 1f; break;
        }
        return a;
    }

    /// <summary>The rendered gain curve (engine semantics, so overlapping stages draw as they sound).</summary>
    public static void DrawCurve(DrawingContext ctx, in AdsrFrame f, ClipAdsr a, IPen pen)
    {
        double len = f.Len;
        if (len <= 0) return;
        var ts = new List<double> { 0, a.AttackBeats, a.AttackBeats + a.DecayBeats, len - a.ReleaseBeats, len };
        if (a.ReleaseBeats > 0)   // the release multiplies the ADS stage: sample it where it may curve
            for (int i = 1; i < 16; i++) ts.Add(len - a.ReleaseBeats * i / 16.0);
        ts.RemoveAll(t => t < 0 || t > len);
        ts.Sort();
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(f.X(0), f.Y(a.GainAt(0, len))), false);
            foreach (double t in ts)
                g.LineTo(new Point(f.X(t), f.Y(a.GainAt(t, len))));
            g.LineTo(new Point(f.X(len), f.Bottom));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    /// <summary>The three handles; <paramref name="hot"/> takes the brass fill.</summary>
    public static void DrawHandles(DrawingContext ctx, in AdsrFrame f, ClipAdsr a, AdsrHandle hot, IBrush fill, IBrush hotFill, IPen? ring)
    {
        if (f.Right - f.Left < 12) return;
        a = a.Fit(f.Len);
        bool hotA = hot is AdsrHandle.Attack or AdsrHandle.AttackOrSustain;
        bool hotD = hot is AdsrHandle.DecaySustain or AdsrHandle.AttackOrSustain;
        var r = ReleasePt(f, a);
        ctx.DrawRectangle(hot == AdsrHandle.Release ? hotFill : fill, ring, new Rect(r.X - Square / 2, r.Y - Square / 2, Square, Square));
        ctx.DrawEllipse(hotD ? hotFill : fill, ring, DecayPt(f, a), Dot, Dot);
        var p = AttackPt(f, a);
        ctx.DrawRectangle(hotA ? hotFill : fill, ring, new Rect(p.X - Square / 2, p.Y - Square / 2, Square, Square));
    }

    /// <summary>"Attack 120 ms" / "Sustain −6.0 dB" — the drag readout for a handle.</summary>
    public static string Readout(AdsrHandle h, ClipAdsr a, double bpm)
    {
        string T(double beats) => NotaNum.Time(beats * 60.0 / Math.Max(1, bpm));
        return h switch
        {
            AdsrHandle.Attack => "Attack " + T(a.AttackBeats),
            AdsrHandle.DecaySustain => "Decay " + T(a.DecayBeats) + " · Sustain " + SustainText(a.Sustain),
            AdsrHandle.Release => "Release " + T(a.ReleaseBeats),
            _ => "",
        };
    }

    public static string SustainText(double s) => NotaNum.Db(s <= 0.0011 ? double.NegativeInfinity : AudioMath.LinToDb(s));
}
