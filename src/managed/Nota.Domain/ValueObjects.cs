// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Immutable value objects for the project domain. Each validates its own
// invariants at construction so an invalid value can never exist. These carry no
// engine or serialization concerns — just meaning and range.

namespace Nota.Domain;

/// <summary>Tempo in beats per minute. Clamped to a musically sane range.</summary>
public readonly record struct Bpm
{
    public const double Min = 20.0;
    public const double Max = 999.0;

    public double Value { get; }

    public Bpm(double value)
    {
        if (double.IsNaN(value) || value < Min || value > Max)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"BPM must be in [{Min}, {Max}].");
        Value = value;
    }

    public static implicit operator double(Bpm b) => b.Value;
    public override string ToString() => Value.ToString("0.##");
}

/// <summary>A position or duration measured in beats. Non-negative.</summary>
public readonly record struct Beats
{
    public double Value { get; }

    public Beats(double value)
    {
        if (double.IsNaN(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Beats must be >= 0.");
        Value = value;
    }

    public static readonly Beats Zero = new(0.0);
    public static implicit operator double(Beats b) => b.Value;
    public static Beats operator +(Beats a, Beats b) => new(a.Value + b.Value);
    public override string ToString() => Value.ToString("0.###");
}

/// <summary>MIDI note number, 0..127.</summary>
public readonly record struct Pitch
{
    public int Value { get; }

    public Pitch(int value)
    {
        if (value < 0 || value > 127)
            throw new ArgumentOutOfRangeException(nameof(value), value, "MIDI pitch must be in [0, 127].");
        Value = value;
    }

    public static implicit operator int(Pitch p) => p.Value;
}

/// <summary>Note velocity, normalized 0..1.</summary>
public readonly record struct Velocity
{
    public float Value { get; }

    public Velocity(float value)
    {
        if (float.IsNaN(value) || value < 0f || value > 1f)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Velocity must be in [0, 1].");
        Value = value;
    }

    public static readonly Velocity Full = new(1f);
    public static implicit operator float(Velocity v) => v.Value;
}

/// <summary>Stereo pan, -1 (hard left) .. +1 (hard right).</summary>
public readonly record struct Pan
{
    public float Value { get; }

    public Pan(float value)
    {
        if (float.IsNaN(value) || value < -1f || value > 1f)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Pan must be in [-1, 1].");
        Value = value;
    }

    public static readonly Pan Center = new(0f);
    public static implicit operator float(Pan p) => p.Value;
}

/// <summary>A linear gain multiplier, non-negative (1.0 = unity). Used for track
/// volume, master volume and clip gain.</summary>
public readonly record struct Gain
{
    public float Value { get; }

    public Gain(float value)
    {
        if (float.IsNaN(value) || value < 0f)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Gain must be >= 0.");
        Value = value;
    }

    public static readonly Gain Unity = new(1f);
    public static implicit operator float(Gain g) => g.Value;
}

/// <summary>A track colour as a <c>#RRGGBB</c> hex string. The concrete palette
/// (round-robin assignment, HANDOFF §1) lives in the presentation/theme layer;
/// Domain only carries the chosen colour so it stays UI-framework-free.</summary>
public readonly record struct TrackColor
{
    public string Hex { get; }

    public TrackColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException("Colour hex must be non-empty.", nameof(hex));
        var h = hex.Trim();
        if (h[0] != '#' || (h.Length != 7 && h.Length != 9))
            throw new ArgumentException($"Colour must be #RRGGBB or #AARRGGBB, got '{hex}'.", nameof(hex));
        Hex = h.ToUpperInvariant();
    }

    public override string ToString() => Hex;
}
