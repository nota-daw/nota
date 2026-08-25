// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Domain;

/// <summary>A MIDI clip placed on the arrangement timeline. Notes whose start
/// falls outside [0, Length) are gated (not sounded) by the engine — the
/// authoring model keeps them so trimming is reversible.</summary>
public sealed class MidiClip
{
    public Beats StartBeat { get; set; }
    public Beats LengthBeats { get; set; }
    public List<Note> Notes { get; } = new();

    public MidiClip() { }

    public MidiClip(Beats startBeat, Beats lengthBeats)
    {
        StartBeat = startBeat;
        LengthBeats = lengthBeats;
    }
}

/// <summary>An audio clip: a window into a decoded sample placed on the timeline
/// (arrangement) or captured into a session slot.</summary>
public sealed class AudioClip
{
    /// <summary>Bundle-relative path into <c>samples/</c> once persisted; an
    /// absolute path while still only in memory.</summary>
    public string SamplePath { get; set; } = "";
    public Beats StartBeat { get; set; }
    public long SourceOffsetFrames { get; set; }
    /// <summary>0 = play to the end of the sample.</summary>
    public long LengthFrames { get; set; }
    public Gain Gain { get; set; } = Gain.Unity;

    public AudioClip() { }

    public AudioClip(string samplePath)
    {
        SamplePath = samplePath ?? throw new ArgumentNullException(nameof(samplePath));
    }
}

/// <summary>A clip cell in the Session view, addressed by scene index. Either a
/// MIDI take (<see cref="Notes"/>) or an audio take (<see cref="Audio"/>), never
/// both.</summary>
public sealed class SessionSlot
{
    public int Scene { get; }
    public Beats LengthBeats { get; set; } = new(4.0);
    public List<Note> Notes { get; } = new();
    public AudioClip? Audio { get; set; }

    public SessionSlot(int scene)
    {
        if (scene < 0)
            throw new ArgumentOutOfRangeException(nameof(scene), scene, "Scene index must be >= 0.");
        Scene = scene;
    }

    public bool IsEmpty => Notes.Count == 0 && Audio is null;
}
