// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Domain;

/// <summary>A track: audio, instrument or return. Carries its mixer state, an
/// optional instrument (instrument tracks only), a device chain, and its clips
/// across arrangement and session views.</summary>
public sealed class Track
{
    /// <summary>Number of send/return buses (mirrors the engine's kMaxReturns).</summary>
    public const int MaxSends = 4;

    public TrackKind Kind { get; }
    public string Name { get; set; } = "";
    public Gain Volume { get; set; } = Gain.Unity;
    public Pan Pan { get; set; } = Pan.Center;
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public bool Armed { get; set; }
    public TrackColor? Color { get; set; }

    /// <summary>For return tracks: which return bus this track *is* (0-based).
    /// -1 for audio/instrument tracks.</summary>
    public int ReturnIndex { get; set; } = -1;

    /// <summary>Post-fader send levels into each return bus, indexed by bus.</summary>
    public float[] Sends { get; } = new float[MaxSends];

    /// <summary>The sound source; non-null only on instrument tracks.</summary>
    public Instrument? Instrument { get; set; }

    public List<Device> Devices { get; } = new();
    public List<MidiClip> MidiClips { get; } = new();
    public List<AudioClip> AudioClips { get; } = new();
    public List<SessionSlot> SessionSlots { get; } = new();

    public Track(TrackKind kind) => Kind = kind;

    public bool IsReturn => Kind == TrackKind.Return;

    /// <summary>Set the send level into a return bus, guarding the bus index.</summary>
    public void SetSend(int bus, float level)
    {
        if (bus < 0 || bus >= MaxSends)
            throw new ArgumentOutOfRangeException(nameof(bus), bus, $"Send bus must be in [0, {MaxSends}).");
        if (float.IsNaN(level) || level < 0f)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Send level must be >= 0.");
        Sends[bus] = level;
    }
}
