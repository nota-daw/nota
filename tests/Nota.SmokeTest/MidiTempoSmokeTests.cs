// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Linq;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Midi;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MidiTempoSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("-- MIDI, tempo map and Piano Roll event editing --");
        MidiFormatZeroRoundTrip(check);
        TempoOnlyRoundTrip(check);
        VelocityWheelSettings(check);
        NativeTempoMapRoundTrip(check);
    }

    private static void MidiFormatZeroRoundTrip(Action<bool, string> check)
    {
        var original = new MidiFileData { LengthBeats = 8 };
        original.TempoPoints.Add(new MidiTempoPoint(0, 120.0));
        original.TempoPoints.Add(new MidiTempoPoint(4, 96.0));

        var lead = new MidiFilePart { Name = "Lead", Channel = 0 };
        lead.Notes.Add(new NotaNote(60, 0.0, 1.0, 0.8f));
        lead.Controllers[MidiController.PitchWheel] = new() { new AutomationPoint(0.25, 0.25f) };
        lead.Controllers[MidiController.ChannelPressure] = new() { new AutomationPoint(0.5, 0.5f) };

        var bass = new MidiFilePart { Name = "Bass", Channel = 1 };
        bass.Notes.Add(new NotaNote(36, 1.0, 0.5, 0.7f));
        bass.Controllers[MidiController.Sustain] = new() { new AutomationPoint(0.75, 1.0f) };

        original.Parts.Add(lead);
        original.Parts.Add(bass);

        byte[] bytes = MidiFileWriter.Write(original, format: 0);
        var parsed = MidiFileReader.Parse(bytes);
        var parsedLead = parsed.Parts.FirstOrDefault(p => p.Notes.Any(n => n.Pitch == 60));
        var parsedBass = parsed.Parts.FirstOrDefault(p => p.Notes.Any(n => n.Pitch == 36));

        check(parsed.Parts.Count == 2 && parsedLead is not null && parsedBass is not null,
            "MIDI Format 0 preserves distinct note channels when parsed back");
        check(parsedLead is not null
              && parsedLead.Controllers.TryGetValue(MidiController.PitchWheel, out var wheel)
              && wheel.Count == 1
              && Math.Abs(wheel[0].Beat - 0.25) < 0.002
              && Math.Abs(wheel[0].Value - 0.25f) < 0.01,
            "MIDI Format 0 preserves pitch-wheel timing and bipolar values");
        check(parsedLead is not null
              && parsedLead.Controllers.TryGetValue(MidiController.ChannelPressure, out var pressure)
              && pressure.Count == 1
              && Math.Abs(pressure[0].Beat - 0.5) < 0.002
              && Math.Abs(pressure[0].Value - 0.5f) < 0.01,
            "MIDI Format 0 preserves channel-pressure timing and value");
        check(parsedBass is not null
              && parsedBass.Controllers.TryGetValue(MidiController.Sustain, out var sustain)
              && sustain.Count == 1
              && Math.Abs(sustain[0].Beat - 0.75) < 0.002
              && sustain[0].Value > 0.99f,
            "MIDI Format 0 preserves switch-controller events");
        check(parsed.TempoPoints.Count == 2
              && Math.Abs(parsed.TempoPoints[1].Beat - 4.0) < 0.002
              && Math.Abs(parsed.TempoPoints[1].Bpm - 96.0) < 0.02,
            "MIDI Format 0 preserves tempo meta-events");

        byte[] smpte = (byte[])bytes.Clone();
        smpte[12] = 0xE7;
        smpte[13] = 0x28;
        bool rejected = false;
        try { _ = MidiFileReader.Parse(smpte); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "MIDI reader rejects unsupported SMPTE time division");
    }

    private static void TempoOnlyRoundTrip(Action<bool, string> check)
    {
        var original = new MidiFileData();
        original.TempoPoints.Add(new MidiTempoPoint(0, 111.0));
        original.TempoPoints.Add(new MidiTempoPoint(12, 132.0));
        var parsed = MidiFileReader.Parse(MidiFileWriter.Write(original));
        check(parsed.Parts.Count == 0 && parsed.TempoPoints.Count == 2
              && Math.Abs(parsed.TempoPoints[0].Bpm - 111.0) < 0.02
              && Math.Abs(parsed.TempoPoints[1].Beat - 12.0) < 0.002
              && Math.Abs(parsed.TempoPoints[1].Bpm - 132.0) < 0.02,
            "tempo-only MIDI round-trip preserves tempo changes without note parts");
    }

    private static void VelocityWheelSettings(Action<bool, string> check)
    {
        check(PianoRollVelocityWheel.ComputeDelta(1, 1, false, 1.0) == 1,
            "velocity wheel default increases by one step");
        check(PianoRollVelocityWheel.ComputeDelta(-1, 1, false, 1.0) == -1,
            "velocity wheel default decreases by one step");
        check(PianoRollVelocityWheel.ComputeDelta(1, 3, true, 1.0) == -3
              && PianoRollVelocityWheel.ComputeDelta(-1, 3, true, 1.0) == 3,
            "velocity wheel reverse direction flips both signs");
        check(PianoRollVelocityWheel.ComputeDelta(1, 10, false, 0.5) == 5
              && PianoRollVelocityWheel.ComputeDelta(-1, 10, false, 2.0) == -20,
            "velocity wheel multiplier scales increases and decreases");
        check(PianoRollVelocityWheel.ComputeDelta(1, 10, false, 0.0) == 0
              && PianoRollVelocityWheel.ComputeDelta(1, 10, false, double.NaN) == 0
              && PianoRollVelocityWheel.ComputeDelta(0, 10, false, 1.0) == 0,
            "zero multiplier, invalid multiplier and no wheel input are no-ops");
        check(PianoRollVelocityWheel.ComputeDelta(1, 10, false, 3.0) == 20,
            "velocity wheel multiplier is capped at 2×");
    }

    private static void NativeTempoMapRoundTrip(Action<bool, string> check)
    {
        using var engine = new NotaEngine();
        var authored = new[]
        {
            new TempoPoint(0.0, 120.0, TempoInterpolation.Linear),
            new TempoPoint(4.0, 80.0, TempoInterpolation.Smooth),
            new TempoPoint(12.0, 150.0, TempoInterpolation.Square),
        };
        engine.SetTempoMap(authored);
        var actual = engine.GetTempoMap();
        check(actual.Count == authored.Length
              && Math.Abs(actual[1].Beat - authored[1].Beat) < 1e-9
              && Math.Abs(actual[1].Bpm - authored[1].Bpm) < 1e-9
              && actual[1].Interpolation == TempoInterpolation.Smooth
              && actual[2].Interpolation == TempoInterpolation.Square,
            "native tempo map round-trip preserves points, BPM and interpolation curves");
    }
}
