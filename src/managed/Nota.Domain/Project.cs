// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Domain;

/// <summary>Musical time signature, e.g. 4/4. Denominator is a power of two.</summary>
public readonly record struct TimeSignature
{
    public int Numerator { get; }
    public int Denominator { get; }

    public TimeSignature(int numerator, int denominator)
    {
        if (numerator < 1 || numerator > 32)
            throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "Numerator must be in [1, 32].");
        if (denominator < 1 || (denominator & (denominator - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "Denominator must be a power of two.");
        Numerator = numerator;
        Denominator = denominator;
    }

    public static readonly TimeSignature FourFour = new(4, 4);
    public override string ToString() => $"{Numerator}/{Denominator}";
}

/// <summary>Global transport state: tempo, time signature, master volume and the
/// metronome/loop toggles. The single source of truth for musical time lives in
/// the engine; this mirrors the authoring-side values.</summary>
public sealed class Transport
{
    public Bpm Bpm { get; set; } = new(120.0);
    public TimeSignature TimeSignature { get; set; } = TimeSignature.FourFour;
    public Gain MasterVolume { get; set; } = Gain.Unity;
    public bool MetronomeOn { get; set; }
    public bool LoopOn { get; set; }
}

/// <summary>The project aggregate root: transport, the session scene count, and
/// the ordered list of tracks. This is the authoring "score"; the engine holds a
/// real-time-safe projection that commands keep in sync.</summary>
public sealed class Project
{
    public const int DefaultSceneCount = 8;

    public Transport Transport { get; } = new();
    public List<Track> Tracks { get; } = new();

    private int _sceneCount = DefaultSceneCount;

    /// <summary>Number of session scenes (rows in the Session grid).</summary>
    public int SceneCount
    {
        get => _sceneCount;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Scene count must be >= 1.");
            _sceneCount = value;
        }
    }
}
