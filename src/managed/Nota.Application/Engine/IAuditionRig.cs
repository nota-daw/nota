// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>One note of an audition phrase, in beats.</summary>
public readonly record struct AuditionNote(double StartBeat, double LengthBeats, int Pitch, float Velocity);

/// <summary>A standalone chain — MIDI effects → built-in instrument → audio effects — that
/// renders a phrase offline (see <see cref="IAudioEngine.CreateAuditionRig"/>). It touches no
/// engine state, so build and render it on a worker thread; <see cref="Cancel"/> may be called
/// from any thread. Hand the result to <see cref="IAudioEngine.StoreAudition"/> on the UI
/// thread, then dispose it.</summary>
public interface IAuditionRig : IDisposable
{
    /// <summary>Built-in instrument kind (no racks). False if the kind can't be built.</summary>
    bool SetInstrument(int kind);
    /// <summary>Normalized instrument value by plugin-param id; false if there's no such id.</summary>
    bool InstrumentParam(string id, float value);
    /// <summary>Appends a built-in audio effect; its index, or -1.</summary>
    int AddDevice(int kind);
    bool DeviceParam(int index, string name, float value);
    /// <summary>Appends a built-in MIDI effect; its index, or -1.</summary>
    int AddMidiEffect(int kind);
    bool MidiParam(int index, string name, float value);
    /// <summary>The Sampler's or Nota Grain's sample, played at its own pitch on
    /// <paramref name="rootNote"/>; null = the Sampler's procedural keys tone at C4.</summary>
    bool SetSamplerSample(string? path, int rootNote = 60);
    /// <summary>Effect source: the first <paramref name="maxSeconds"/> of an audio file.</summary>
    bool SetSourceFile(string path, double maxSeconds);
    /// <summary>A fresh rig at the same rate, to render one part of a demo track.</summary>
    IAuditionRig CreatePart();
    /// <summary>Effect source: mixes a rendered part rig in (a demo track is built part by part).</summary>
    bool AddSourceFrom(IAuditionRig part, float gain);
    /// <summary>Takes a demo track rendered earlier this session (any rig); false = not cached.</summary>
    bool UseCachedSource(string key);
    /// <summary>Normalizes the mixed source to <paramref name="targetPeak"/> and keeps it under <paramref name="key"/>.</summary>
    bool CacheSource(string key, float targetPeak);

    /// <summary>Kits: a pad instrument (Drum Rack style) in place of a built-in instrument.</summary>
    bool UseKit();
    /// <summary>A one-shot pad on <paramref name="note"/>: gain, pan −1..1, choke group (0 = none).
    /// Returns the pad index, or -1 (the file can't be read).</summary>
    int KitAddPad(int note, string path, float gain, float pan, int choke);
    int KitPadAddDevice(int pad, int kind);
    bool KitPadDeviceParam(int pad, int device, string name, float value);

    /// <summary>Renders the phrase and its tail. Returns the frames rendered, or -1 when
    /// cancelled / there is nothing to play. <paramref name="rolling"/>: the instrument sees a
    /// running transport (arp / sequencer synths).</summary>
    long Render(IReadOnlyList<AuditionNote> notes, double bpm, double phraseBeats, double maxTailSeconds, bool rolling);
    void Cancel();
    /// <summary>(min, max) pairs over the rendered audio; returns the bucket count.</summary>
    int ReadPeaks(float[] outMinMax, int maxPoints);
    double Seconds { get; }
}
