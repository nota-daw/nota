// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RecordPreview — what a MIDI take looks like while it records (#56, specs/017): the notes
// held into it (read from the engine once per recording tick, so the arrangement, the Session
// grid and the piano roll draw the same frame) and the appear animation each new note plays.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Nota.Application;

namespace Nota.App;

internal sealed class RecordPreview
{
    /// <summary>How long a new note takes to settle from the accent into its own colour.</summary>
    public const double AppearMs = 150;

    private readonly NotaRecordHeldNote[] _buf = new NotaRecordHeldNote[256];
    private int _count;
    // Appear animation: when each take note was first seen (held, or newly stored), keyed by
    // pitch + start (absolute beat for an arrangement take, slot-local for Session).
    private readonly Dictionary<(int pitch, long start), long> _appear = new();
    private readonly List<(int, long)> _expired = new();
    // Every take note seen so far — held or stored — so a note animates once: not again when
    // its key is still down after the animation, nor when it lands in the clip on release.
    // A note pressed and released between two ticks (never held in a snapshot) is first seen
    // stored, and animates then. The take's notes from before the first tick don't animate.
    private readonly HashSet<(int, long)> _seen = new();
    private bool _primed;

    public int TrackId { get; private set; }
    /// <summary>Session scene being recorded, or -1 for an arrangement take.</summary>
    public int SessionScene { get; private set; } = -1;
    /// <summary>The capture clock: the held notes' right edge (runs through the count-in).</summary>
    public double NowBeat { get; private set; }
    /// <summary>The arrangement take's clip (-1 when unknown or a Session take) and its start.</summary>
    public int TakeClipIndex { get; private set; } = -1;
    public double TakeClipStart { get; private set; }
    public bool Active => TrackId > 0;
    public ReadOnlySpan<NotaRecordHeldNote> Held => _buf.AsSpan(0, _count);
    /// <summary>True while any note is still animating (drives the optional fast repaint).</summary>
    public bool Animating => _appear.Count > 0;

    public static long Key(double beat) => (long)Math.Round(beat * 1e4);

    /// <summary>Read the engine's held notes and advance the appear animation. Call once per
    /// recording tick, after Engine.Poll (so a released note is already in its clip).</summary>
    public void Update(IAudioEngine eng)
    {
        int n = eng.RecordHeldNotes(_buf, out int track, out int scene, out double now);
        if (track != TrackId || scene != SessionScene) { _primed = false; _seen.Clear(); _appear.Clear(); }
        _count = Math.Clamp(n, 0, _buf.Length);
        TrackId = track;
        SessionScene = scene;
        NowBeat = now;
        if (track <= 0) { Clear(); return; }

        long t = Stopwatch.GetTimestamp();
        for (int i = 0; i < _count; i++)
        {
            var k = (_buf[i].Pitch, Key(_buf[i].StartBeat));
            if (_seen.Add(k)) _appear[k] = t;   // a key down is always new to the take
        }

        // The take's stored notes: ones never seen before (staccato between two ticks) animate.
        TakeClipIndex = -1;
        if (scene >= 0)
        {
            foreach (var x in eng.GetSessionNotes(track, scene)) Saw((x.Pitch, Key(x.StartBeat)), t);
        }
        else
        {
            // The take is the clip under the capture clock — or, during the count-in, the
            // first clip after it (the one the take opened at the record start).
            double bestStart = double.MaxValue;
            for (int c = 0; c < 4096 && eng.TryGetClipInfo(track, c, out var ci); c++)
            {
                if (!ci.IsMidi) continue;
                if (now >= ci.StartBeat && now < ci.StartBeat + ci.LengthBeats) { TakeClipIndex = c; TakeClipStart = ci.StartBeat; break; }
                if (ci.StartBeat > now && ci.StartBeat < bestStart) { bestStart = ci.StartBeat; TakeClipIndex = c; TakeClipStart = ci.StartBeat; }
            }
            if (TakeClipIndex >= 0)
                foreach (var x in eng.GetClipNotes(track, TakeClipIndex))
                    Saw((x.Pitch, Key(TakeClipStart + x.StartBeat)), t);
        }
        _primed = true;

        Expire(t);
    }

    private void Saw((int, long) key, long t)
    {
        if (_seen.Add(key) && _primed) _appear[key] = t;
    }

    public void Clear()
    {
        _count = 0;
        TrackId = 0;
        SessionScene = -1;
        TakeClipIndex = -1;
        _appear.Clear();
        _seen.Clear();
        _primed = false;
    }

    /// <summary>Appear progress of a take note, 0 → 1 (1 = settled, or not a new note).</summary>
    public double Progress(int pitch, double startBeat)
    {
        if (_appear.Count == 0 || !_appear.TryGetValue((pitch, Key(startBeat)), out long t0)) return 1;
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        return Math.Clamp(ms / AppearMs, 0, 1);
    }

    /// <summary>Ease-out of a progress value: fast start, soft landing.</summary>
    public static double Ease(double p) => 1 - (1 - p) * (1 - p);

    /// <summary>A settling note's fill: the accent fading into the note's own ink (and from
    /// translucent to opaque) as the appear animation runs.</summary>
    public static Avalonia.Media.IBrush AppearBrush(Avalonia.Media.Color ink, double p)
    {
        double e = Ease(p);
        var a = NotaPalette.Accent.Color;
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * e);
        return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(
            Avalonia.Media.Color.FromRgb(L(a.R, ink.R), L(a.G, ink.G), L(a.B, ink.B)), 0.35 + 0.65 * e);
    }

    private void Expire(long now)
    {
        _expired.Clear();
        foreach (var (k, t0) in _appear)
            if (Stopwatch.GetElapsedTime(t0, now).TotalMilliseconds >= AppearMs) _expired.Add(k);
        foreach (var k in _expired) _appear.Remove(k);
    }
}
