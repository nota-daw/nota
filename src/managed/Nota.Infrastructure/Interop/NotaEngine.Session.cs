// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application;

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

    // --- Session P0 -------------------------------------------------------------

    public bool TryGetSessionClipProps(int trackId, int scene, out NotaSessionClipProps props)
    { ThrowIfDisposed(); return NativeMethods.SessionGetClipProps(_handle, trackId, scene, out props) != 0; }

    public bool SetSessionClipProps(int trackId, int scene, NotaSessionClipProps props)
    { ThrowIfDisposed(); return NativeMethods.SessionSetClipProps(_handle, trackId, scene, in props) == NativeMethods.NotaResult.Ok; }

    public string GetSessionClipName(int trackId, int scene)
    { ThrowIfDisposed(); return ReadNativeString((b, c) => NativeMethods.SessionGetClipName(_handle, trackId, scene, b, c)); }

    public void SetSessionClipName(int trackId, int scene, string name)
    { ThrowIfDisposed(); NativeMethods.SessionSetClipName(_handle, trackId, scene, name ?? ""); }

    public bool GetSessionSlotStopButton(int trackId, int scene)
    { ThrowIfDisposed(); return NativeMethods.SessionGetSlotStopButton(_handle, trackId, scene) != 0; }

    public void SetSessionSlotStopButton(int trackId, int scene, bool on)
    { ThrowIfDisposed(); NativeMethods.SessionSetSlotStopButton(_handle, trackId, scene, on ? 1 : 0); }

    public bool CopySessionSlot(int srcTrackId, int srcScene, int dstTrackId, int dstScene)
    { ThrowIfDisposed(); return NativeMethods.SessionCopySlot(_handle, srcTrackId, srcScene, dstTrackId, dstScene) == NativeMethods.NotaResult.Ok; }

    public bool TryGetSceneProps(int scene, out NotaSceneProps props)
    { ThrowIfDisposed(); return NativeMethods.SessionGetSceneProps(_handle, scene, out props) != 0; }

    public void SetSceneProps(int scene, NotaSceneProps props)
    { ThrowIfDisposed(); NativeMethods.SessionSetSceneProps(_handle, scene, in props); }

    public string GetSceneName(int scene)
    { ThrowIfDisposed(); return ReadNativeString((b, c) => NativeMethods.SessionGetSceneName(_handle, scene, b, c)); }

    public void SetSceneName(int scene, string name)
    { ThrowIfDisposed(); NativeMethods.SessionSetSceneName(_handle, scene, name ?? ""); }

    public int InsertScene(int at) { ThrowIfDisposed(); return NativeMethods.SessionInsertScene(_handle, at); }
    public int DuplicateScene(int scene) { ThrowIfDisposed(); return NativeMethods.SessionDuplicateScene(_handle, scene); }
    public int CaptureScene(int at) { ThrowIfDisposed(); return NativeMethods.SessionCaptureScene(_handle, at); }

    public void LaunchSlotVelocity(int trackId, int scene, float velocity)
    { ThrowIfDisposed(); Check(NativeMethods.SessionLaunchSlotVel(_handle, trackId, scene, velocity)); }

    public void ReleaseSlot(int trackId, int scene)
    { ThrowIfDisposed(); Check(NativeMethods.SessionReleaseSlot(_handle, trackId, scene)); }

    public void TrackBackToArrangement(int trackId)
    { ThrowIfDisposed(); Check(NativeMethods.SessionTrackBackToArrangement(_handle, trackId)); }

    public int SessionPlayingSlot(int trackId) { ThrowIfDisposed(); return NativeMethods.SessionPlayingSlot(_handle, trackId); }
    public double SessionSlotPosition(int trackId) { ThrowIfDisposed(); return NativeMethods.SessionSlotPosition(_handle, trackId); }
    public double LaunchQuant { get { ThrowIfDisposed(); return NativeMethods.SessionGetLaunchQuant(_handle); } }

    public bool SessionFollow
    {
        get { ThrowIfDisposed(); return NativeMethods.SessionGetFollow(_handle) != 0; }
        set { ThrowIfDisposed(); NativeMethods.SessionSetFollow(_handle, value ? 1 : 0); }
    }

    public double SessionRecordLength
    {
        get { ThrowIfDisposed(); return NativeMethods.SessionGetRecordLength(_handle); }
        set { ThrowIfDisposed(); NativeMethods.SessionSetRecordLength(_handle, value); }
    }

    public int RecordSessionScene(int scene) { ThrowIfDisposed(); return NativeMethods.SessionRecordScene(_handle, scene); }

    public bool TryGetSessionRecordTarget(out int trackId, out int scene, out double elapsedBeats)
    { ThrowIfDisposed(); return NativeMethods.SessionRecordTarget(_handle, out trackId, out scene, out elapsedBeats) != 0; }

    public int GetSessionSlotPeaks(int trackId, int scene, float[] outMinMax, int maxPoints)
    { ThrowIfDisposed(); return NativeMethods.SessionSlotPeaks(_handle, trackId, scene, outMinMax, Math.Min(maxPoints, outMinMax.Length / 2)); }

    public bool MoveScene(int from, int to)
    { ThrowIfDisposed(); return NativeMethods.SessionMoveScene(_handle, from, to) == NativeMethods.NotaResult.Ok; }

    public bool TakeSceneTempoChange(out double bpm, out int num, out int den)
    { ThrowIfDisposed(); return NativeMethods.SessionTakeSceneTempo(_handle, out bpm, out num, out den) != 0; }
}
