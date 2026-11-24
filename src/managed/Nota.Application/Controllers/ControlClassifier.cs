// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What a physical control sends, from one deliberate movement (specs/051 FR-030, research R13):
// the Controller setup wizard captures the messages of one step and asks this to tell a note
// from a CC from pitch bend, a 14-bit CC pair (n, then n + 32) or an NRPN (99 / 98, then 6 / 38)
// from a plain CC, and — for an encoder turned right, then left — which relative encoding it
// uses. When the messages fit more than one reading it says so, and the wizard asks the user.

namespace Nota.Application.Controllers;

/// <summary>A classified control (channel 0..15), or <see cref="Ambiguous"/> with no result.</summary>
public readonly record struct ClassifiedControl(ControlMessage Message, int Channel, int Number, EncoderMode Encoder, bool Ambiguous = false)
{
    public static ClassifiedControl Unclear => new(ControlMessage.Cc, 0, 0, EncoderMode.Absolute, true);
}

public static class ControlClassifier
{
    /// <summary>Classify a captured run of channel messages (status nibble, channel 0..15, d1, d2),
    /// in arrival order. Null when nothing usable was captured.</summary>
    public static ClassifiedControl? Classify(IReadOnlyList<(int Status, int Channel, int D1, int D2)> events)
    {
        if (events.Count == 0) return null;
        var bend = events.FirstOrDefault(e => e.Status == 0xE0);
        if (bend.Status == 0xE0) return new ClassifiedControl(ControlMessage.PitchBend, bend.Channel, 0, EncoderMode.Absolute);

        var notes = events.Where(e => e.Status is 0x90 or 0x80).ToList();
        var ccs = events.Where(e => e.Status == 0xB0).ToList();
        if (notes.Count > 0 && notes.Count >= ccs.Count)
            return new ClassifiedControl(ControlMessage.Note, notes[0].Channel, notes[0].D1, EncoderMode.Absolute);
        if (ccs.Count == 0) return null;
        int ch = ccs[0].Channel;
        ccs = ccs.Where(e => e.Channel == ch).ToList();

        // NRPN: parameter number in 99 (MSB) / 98 (LSB), data in 6 / 38.
        int msb = ccs.FirstOrDefault(e => e.D1 == 99).D2, lsb = ccs.FirstOrDefault(e => e.D1 == 98).D2;
        if (ccs.Any(e => e.D1 == 99) && ccs.Any(e => e.D1 == 98) && ccs.Any(e => e.D1 is 6 or 38 or 96 or 97))
            return new ClassifiedControl(ControlMessage.Nrpn, ch, (msb << 7) | lsb, EncoderMode.Absolute);

        // The control: the CC number moved most (an LSB partner aside).
        int number = ccs.GroupBy(e => e.D1).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        if (number >= 32 && number < 64 && ccs.Any(e => e.D1 == number - 32)) number -= 32;
        if (number < 32 && ccs.Any(e => e.D1 == number + 32))
            return new ClassifiedControl(ControlMessage.Cc14, ch, number, EncoderMode.Absolute);

        var values = ccs.Where(e => e.D1 == number).Select(e => e.D2).ToList();
        return new ClassifiedControl(ControlMessage.Cc, ch, number, Encoder(values) is { } enc ? enc : EncoderMode.Absolute,
            Encoder(values) is null && LooksRelative(values));
    }

    private static bool In(int v, int lo, int hi) => v >= lo && v <= hi;

    // Small steps around a centre: what relative encoders send. An absolute sweep moves through
    // the range instead.
    private static bool LooksRelative(List<int> v)
        => v.Count > 0 && v.All(x => In(x, 1, 15) || In(x, 49, 79) || In(x, 113, 127)) && v.Distinct().Count() <= 8
           && !IsSweep(v);

    private static bool IsSweep(List<int> v)
    {
        if (v.Distinct().Count() < 6) return false;
        int ups = 0, downs = 0;
        for (int i = 1; i < v.Count; i++) { if (v[i] > v[i - 1]) ups++; else if (v[i] < v[i - 1]) downs++; }
        return ups >= v.Count / 2 || downs >= v.Count / 2;
    }

    // Turned right, then left: the first values are the clockwise ones.
    private static EncoderMode? Encoder(List<int> v)
    {
        if (!LooksRelative(v)) return null;
        int split = v.FindIndex(x => x != v[0] && !SameSide(v[0], x));
        if (split < 0) return null;   // one direction only: can't tell the encodings apart
        var right = v.Take(split).ToList();
        var left = v.Skip(split).ToList();
        if (right.All(x => In(x, 1, 15)) && left.All(x => In(x, 113, 127))) return EncoderMode.RelativeTwos;
        if (right.All(x => In(x, 1, 15)) && left.All(x => In(x, 65, 79))) return EncoderMode.RelativeSignBit;
        if (right.All(x => In(x, 65, 79)) && left.All(x => In(x, 49, 63))) return EncoderMode.RelativeOffset;
        return null;
    }

    private static int Band(int x) => x <= 15 ? 0 : x <= 63 ? 1 : x <= 79 ? 2 : 3;
    private static bool SameSide(int a, int b) => Band(a) == Band(b);
}
