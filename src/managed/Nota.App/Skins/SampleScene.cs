// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The fixed little project the skin previews are drawn from (spec 087, research R6): its own
// engine, never started — no audio thread, no device — so a preview never touches the user's
// project and the same theme always gives the same pictures. Six coloured tracks of mixed
// kinds, MIDI and audio clips, an automation lane, built-in instruments and effects, an
// arpeggiator, a return. Built once, on the first preview request.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.App;

internal sealed class SampleScene : IDisposable
{
    private static SampleScene? _shared;

    /// <summary>The scene, built on first use (on the UI thread).</summary>
    public static SampleScene Shared => _shared ??= new SampleScene();

    public NotaEngine Engine { get; }
    public int Drums { get; }
    public int Bass { get; }
    public int Keys { get; }
    public int Lead { get; }
    public int Texture { get; }
    public int Reverb { get; }
    /// <summary>The Keys track's first clip — the piano roll's.</summary>
    public int KeysClip { get; }

    private SampleScene()
    {
        Engine = new NotaEngine();
        var e = Engine;
        e.SetTimeSignature(4, 4);

        Drums = Instrument("Drums", 12, 0);
        Bass = Instrument("Bass", 7, 2);
        Keys = Instrument("Keys", 14, 3);
        Lead = Instrument("Lead", 6, 6);
        Texture = e.AddAudioTrack();
        Name(Texture, "Texture", 4);
        Reverb = e.AddReturnTrack();
        Name(Reverb, "Reverb", 8);

        for (int bar = 0; bar < 16; bar += 4) Clip(Drums, bar, 4, DrumBar(bar >= 8), "Beat");
        for (int bar = 4; bar < 16; bar += 4) Clip(Bass, bar, 4, BassLine(), "Bass");
        KeysClip = Clip(Keys, 0, 4, Chords(), "Chords");
        for (int bar = 4; bar < 16; bar += 4) Clip(Keys, bar, 4, Chords(), "Chords");
        Clip(Lead, 8, 8, Melody(), "Hook");
        var pad = PadSample();
        if (pad is not null) e.SetClipName(Texture, e.AddAudioClip(Texture, pad, 16), "Pad");

        e.AddBuiltinDevice(Bass, 1);       // Compressor
        e.AddBuiltinDevice(Keys, 0);       // EQ-8
        e.AddBuiltinDevice(Keys, 20);      // Chamber
        e.AddBuiltinDevice(Lead, 3);       // Delay
        e.AddMidiEffect(Lead, 0);          // Arpeggiator
        e.AddBuiltinDevice(Reverb, 2);
        e.SetTrackSend(Keys, 0, 0.35f);
        e.SetTrackSend(Lead, 0, 0.25f);
        e.SetTrackVolume(Texture, 0.6f);
        e.SetTrackPan(Lead, 0.15f);

        int lane = e.AddAutomationLane(Lead, AutomationTarget.Volume, -1, -1);
        if (lane >= 0)
            e.SetAutomationPoints(Lead, lane, new[]
            {
                new AutomationPoint(32, 0.25f), new AutomationPoint(44, 0.85f, 0.4f),
                new AutomationPoint(56, 0.85f), new AutomationPoint(60, 0.45f),
            });
    }

    public void Dispose()
    {
        Engine.Dispose();
        if (ReferenceEquals(_shared, this)) _shared = null;
    }

    // ── tracks and clips ────────────────────────────────────────────────────────────────

    private int Instrument(string name, int kind, int color)
    {
        int t = Engine.AddInstrumentTrack();
        Engine.SetTrackBuiltinInstrument(t, kind);
        Name(t, name, color);
        return t;
    }

    private void Name(int track, string name, int color)
    {
        Engine.SetTrackName(track, name);
        Engine.SetTrackColorIndex(track, color);
    }

    private int Clip(int track, int bar, int bars, NotaNote[] notes, string name)
    {
        int c = Engine.AddMidiClip(track, bar * 4, bars * 4);
        Engine.SetClipNotes(track, c, notes.Where(n => n.StartBeat < bars * 4).ToArray());
        Engine.SetClipName(track, c, name);
        return c;
    }

    private static NotaNote N(int pitch, double start, double len, float vel = 0.8f) => new(pitch, start, len, vel);

    private static NotaNote[] DrumBar(bool full)
    {
        var l = new List<NotaNote>();
        for (int b = 0; b < 4; b++)
        {
            double o = b * 4;
            l.Add(N(36, o, 0.25)); l.Add(N(36, o + 2, 0.25));
            if (full) l.Add(N(36, o + 2.75, 0.25, 0.6f));
            l.Add(N(38, o + 1, 0.25)); l.Add(N(38, o + 3, 0.25));
            for (int h = 0; h < 8; h++) l.Add(N(42, o + h * 0.5, 0.2, h % 2 == 0 ? 0.7f : 0.45f));
        }
        return l.ToArray();
    }

    private static readonly int[][] Chords4 = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } };

    private static NotaNote[] Chords() => Chords4.SelectMany((ch, i) => ch.Select(p => N(p, i * 4, 3.75, 0.55f + 0.1f * (i % 3)))).ToArray();

    private static NotaNote[] BassLine()
    {
        int[] roots = { 33, 29, 36, 31 };
        return roots.SelectMany((r, i) => new[] { N(r, i * 4, 0.75), N(r, i * 4 + 1.5, 0.5, 0.6f), N(r + 12, i * 4 + 2.5, 0.5, 0.7f), N(r, i * 4 + 3, 0.75) }).ToArray();
    }

    private static NotaNote[] Melody()
    {
        int[] p = { 69, 72, 76, 74, 72, 69, 67, 69, 72, 74, 76, 79, 76, 74, 72, 71 };
        return p.Select((x, i) => N(x, i * 2, i % 4 == 3 ? 1.75 : 1.0, 0.75f)).ToArray();
    }

    // ── audio: a soft chord pad, synthesised so the scene needs no file from outside ─────

    private static string? PadSample()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "nota-skin-preview");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "pad.wav");
            if (!File.Exists(path)) WriteWav(path, Pad(new[] { 57, 60, 64, 69 }, 8));
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private const int Rate = 48000;

    private static float[] Pad(int[] midi, int beats)
    {
        int n = (int)(Rate * 60 / 120.0 * beats);
        var s = new float[n];
        foreach (var m in midi)
        {
            double f = 440 * Math.Pow(2, (m - 69) / 12.0);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double env = Math.Min(1, t / 0.6) * Math.Min(1, (n - i) / (Rate * 0.8));
                s[i] += (float)(env * Math.Sin(2 * Math.PI * f * t) * (1 + 0.3 * Math.Sin(2 * Math.PI * 0.25 * t)));
            }
        }
        float peak = s.Max(Math.Abs);
        if (peak > 0) for (int i = 0; i < n; i++) s[i] *= 0.5f / peak;
        return s;
    }

    private static void WriteWav(string path, float[] mono)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = mono.Length * 2 * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (var x in mono)
        {
            short v = (short)Math.Clamp(x * 32767f, -32768f, 32767f);
            w.Write(v); w.Write(v);
        }
    }
}
