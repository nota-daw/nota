// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- Session view (M5) -------------------------------------------------

    public int SceneCount { get { ThrowIfDisposed(); return NativeMethods.SessionSceneCount(_handle); } }

    /// <summary>Creates an empty MIDI clip in a session slot (instrument track).</summary>
    public void AddSessionMidiClip(int trackId, int scene, double lengthBeats = 4.0)
    { ThrowIfDisposed(); Check(NativeMethods.SessionAddMidiClip(_handle, trackId, scene, lengthBeats)); }

    /// <summary>Slot state: 0 empty, 1 filled, 2 queued, 3 playing, 4 recording.</summary>
    public int SessionSlotState(int trackId, int scene)
    { ThrowIfDisposed(); return NativeMethods.SessionSlotState(_handle, trackId, scene); }

    /// <summary>Loop length of a session slot in beats (0 if empty).</summary>
    public double SessionSlotLength(int trackId, int scene)
    { ThrowIfDisposed(); return NativeMethods.SessionSlotLength(_handle, trackId, scene); }

    /// <summary>Changes a session slot's loop length in beats (M5-5).</summary>
    public void SetSessionSlotLength(int trackId, int scene, double lengthBeats)
    { ThrowIfDisposed(); Check(NativeMethods.SessionSetSlotLength(_handle, trackId, scene, lengthBeats)); }

    /// <summary>Audio-slot playback gain (linear; 1.0 when not an audio take).</summary>
    public float SessionSlotGain(int trackId, int scene)
    { ThrowIfDisposed(); return NativeMethods.SessionSlotGain(_handle, trackId, scene); }

    public void SetSessionSlotGain(int trackId, int scene, float gain)
    { ThrowIfDisposed(); NativeMethods.SessionSetSlotGain(_handle, trackId, scene, gain); }

    /// <summary>Sets the Session launch-quantize grid in beats (0 = immediate).</summary>
    public void SetLaunchQuant(double beats)
    { ThrowIfDisposed(); Check(NativeMethods.SessionSetLaunchQuant(_handle, beats)); }

    /// <summary>Launches a session slot (quantized). Empty slot = stop the track.</summary>
    public void LaunchSlot(int trackId, int scene)
    { ThrowIfDisposed(); Check(NativeMethods.SessionLaunchSlot(_handle, trackId, scene)); }

    /// <summary>Stops the playing session slot on a track (quantized).</summary>
    public void StopSlot(int trackId)
    { ThrowIfDisposed(); Check(NativeMethods.SessionStopSlot(_handle, trackId)); }

    /// <summary>Launches an entire scene (row): every track's slot fires; empty slots stop that track.</summary>
    public void LaunchScene(int scene)
    { ThrowIfDisposed(); Check(NativeMethods.SessionLaunchScene(_handle, scene)); }

    /// <summary>Stops all tracks whose playing or queued slot is in the given scene.</summary>
    public void StopScene(int scene)
    { ThrowIfDisposed(); Check(NativeMethods.SessionStopScene(_handle, scene)); }

    /// <summary>Stops all session slots on all tracks (quantized).</summary>
    public void StopAllSession()
    { ThrowIfDisposed(); Check(NativeMethods.SessionStopAll(_handle)); }

    /// <summary>Stops every session clip and re-activates the Arrangement timeline (M5).</summary>
    public void BackToArrangement()
    { ThrowIfDisposed(); Check(NativeMethods.SessionBackToArrangement(_handle)); }

    /// <summary>True when the Arrangement is playing; false in a session-only jam.</summary>
    public bool ArrangementActive
    { get { ThrowIfDisposed(); return NativeMethods.ArrangementActive(_handle) != 0; } }

    /// <summary>Empties a session slot (stops it first if it's playing). False if already empty.</summary>
    public bool ClearSessionSlot(int trackId, int scene)
    { ThrowIfDisposed(); return NativeMethods.SessionClearSlot(_handle, trackId, scene) == NativeMethods.NotaResult.Ok; }

    /// <summary>Appends a scene row; returns its index.</summary>
    public int AddScene()
    { ThrowIfDisposed(); return NativeMethods.SessionAddScene(_handle); }

    /// <summary>Removes a scene row (always keeps at least one). False if it couldn't.</summary>
    public bool RemoveScene(int scene)
    { ThrowIfDisposed(); return NativeMethods.SessionRemoveScene(_handle, scene) == NativeMethods.NotaResult.Ok; }

    /// <summary>Launches a slot looping and overdub-records live MIDI into it (M5-4).</summary>
    public void RecordSessionSlot(int trackId, int scene)
    { ThrowIfDisposed(); Check(NativeMethods.SessionRecordSlot(_handle, trackId, scene)); }

    /// <summary>Stops session-slot recording; the slot keeps playing.</summary>
    public void StopSessionRecord()
    { ThrowIfDisposed(); Check(NativeMethods.SessionStopRecord(_handle)); }

    /// <summary>Copies a session slot into a new arrangement clip at startBeat; returns its index or -1 (M5-6).</summary>
    public int SessionSlotToArrangement(int trackId, int scene, double startBeat)
    { ThrowIfDisposed(); return NativeMethods.SessionSlotToArrangement(_handle, trackId, scene, startBeat); }

    /// <summary>Copies an arrangement MIDI clip into a session slot (M5-6).</summary>
    public void ArrangementClipToSession(int trackId, int clipIndex, int scene)
    { ThrowIfDisposed(); Check(NativeMethods.SessionFromArrangement(_handle, trackId, clipIndex, scene)); }

    /// <summary>Copies an arrangement audio clip into a session slot (loops the whole sample).</summary>
    public bool ArrangementAudioClipToSession(int trackId, int clipIndex, int scene)
    { ThrowIfDisposed(); return NativeMethods.SessionAudioFromArrangement(_handle, trackId, clipIndex, scene) == NativeMethods.NotaResult.Ok; }

    /// <summary>Audio-input recording self-test (M4-3): synthetic capture → clip, no device.</summary>
    public bool AudioRecordSelfTest()
    { ThrowIfDisposed(); return NativeMethods.AudioRecordSelfTest(_handle) != 0; }

    /// <summary>Device-free self-test of session audio-slot capture + looped playback (M5-4).</summary>
    public bool SessionAudioRecordSelfTest()
    { ThrowIfDisposed(); return NativeMethods.SessionAudioRecordSelfTest(_handle) != 0; }

    public void SetSessionNotes(int trackId, int scene, NotaNote[] notes)
    { ThrowIfDisposed(); Check(NativeMethods.SessionSetNotes(_handle, trackId, scene, notes, notes.Length)); }

    public NotaNote[] GetSessionNotes(int trackId, int scene)
    {
        ThrowIfDisposed();
        int count = NativeMethods.SessionNoteCount(_handle, trackId, scene);
        if (count == 0) return Array.Empty<NotaNote>();
        var buf = new NotaNote[count];
        int written = NativeMethods.SessionGetNotes(_handle, trackId, scene, buf, count);
        return written == count ? buf : buf[..written];
    }

    /// <summary>Captured session audio take. False if the slot holds no audio.</summary>
    public bool TryGetSessionAudioSlot(int trackId, int scene, out NotaSessionAudioSlot info)
    { ThrowIfDisposed(); return NativeMethods.SessionGetAudioSlot(_handle, trackId, scene, out info) != 0; }

    /// <summary>Loads a file into a session audio slot (looped over lengthBeats). False on failure.</summary>
    public bool AddSessionAudioClip(int trackId, int scene, string path, double lengthBeats, double sourceOffsetFrames, long lengthFrames, float gain)
    { ThrowIfDisposed(); return NativeMethods.SessionAddAudioClip(_handle, trackId, scene, path, lengthBeats, sourceOffsetFrames, lengthFrames, gain) != 0; }

    /// <summary>Drops a whole audio file into a session slot, auto-computing loop length (M7-5).</summary>
    public bool AddSessionAudioFile(int trackId, int scene, string path)
    { ThrowIfDisposed(); return NativeMethods.SessionAddAudioFile(_handle, trackId, scene, path) != 0; }
}
