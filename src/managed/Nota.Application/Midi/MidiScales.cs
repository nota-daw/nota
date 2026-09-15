// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The scale vocabulary shared by the piano roll's scale overlay, the Nota Scale
// MIDI device (src/MidiScale.h) and the clip tools. A mask is 12 bits; bit i set
// means "i semitones above the root is in the scale".

namespace Nota.Application.Midi;

public static class MidiScales
{
    public static readonly string[] KeyNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static readonly string[] Names =
        { "Major", "Natural Minor", "Harmonic Minor", "Dorian", "Phrygian",
          "Lydian", "Mixolydian", "Pentatonic Major", "Pentatonic Minor", "Chromatic" };

    public static readonly ushort[] Masks =
        { 2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095 };

    public const ushort Chromatic = 4095;

    public static ushort MaskAt(int index) =>
        index >= 0 && index < Masks.Length ? Masks[index] : Chromatic;

    /// <summary>Semitone offsets of the mask, ascending — the scale's degrees.</summary>
    public static int[] Degrees(ushort mask)
    {
        var d = new List<int>(12);
        for (int i = 0; i < 12; i++) if ((mask & (1 << i)) != 0) d.Add(i);
        if (d.Count == 0) d.Add(0);
        return d.ToArray();
    }

    public static bool Contains(ushort mask, int root, int pitch)
    {
        int pc = (((pitch - root) % 12) + 12) % 12;
        return (mask & (1 << pc)) != 0;
    }

    /// <summary>Nearest in-scale pitch. <paramref name="prefer"/> &gt; 0 searches upward
    /// first, &lt; 0 downward first, 0 takes whichever is closer (ties go up).</summary>
    public static int Snap(ushort mask, int root, int pitch, int prefer = 0)
    {
        if (mask == Chromatic || Contains(mask, root, pitch)) return pitch;
        for (int d = 1; d <= 12; d++)
        {
            int up = pitch + d, down = pitch - d;
            bool upOk = up <= 127 && Contains(mask, root, up);
            bool downOk = down >= 0 && Contains(mask, root, down);
            if (prefer >= 0 && upOk) return up;
            if (downOk && (prefer <= 0 || !upOk)) return down;
            if (upOk) return up;
        }
        return pitch;
    }

    /// <summary>Walks <paramref name="degree"/> scale steps from the root (degree 0 = the
    /// root in octave <paramref name="octave"/>). Negative degrees walk down; the octave
    /// wraps automatically, so degree 7 of a 7-note scale is the root an octave up.</summary>
    public static int PitchAtDegree(int[] degrees, int root, int degree, int octave)
    {
        int n = degrees.Length;
        int oct = (int)Math.Floor(degree / (double)n);
        int idx = degree - oct * n;
        return root + degrees[idx] + 12 * (octave + oct);
    }

    /// <summary>Inverse of <see cref="PitchAtDegree"/>: the scale-step index of a pitch
    /// relative to <paramref name="root"/> in octave 0, snapping off-scale pitches down.</summary>
    public static int DegreeOfPitch(int[] degrees, int root, int pitch)
    {
        int rel = pitch - root;
        int oct = (int)Math.Floor(rel / 12.0);
        int pc = rel - oct * 12;
        int best = 0;
        for (int i = 0; i < degrees.Length; i++) if (degrees[i] <= pc) best = i;
        return oct * degrees.Length + best;
    }
}
