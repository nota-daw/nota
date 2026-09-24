// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- Tracks & clips ----------------------------------------------------

    /// <summary>Adds an audio track. Returns its id (&gt; 0).</summary>
    public int AddAudioTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddAudioTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add track.");
        return id;
    }

    /// <summary>Loads a WAV/FLAC/MP3 and places it at startBeat. Returns the clip index, or -1 on failure.</summary>
    public int AddAudioClip(int trackId, string path, double startBeat)
    {
        ThrowIfDisposed();
        return NativeMethods.AddAudioClip(_handle, trackId, path, startBeat);
    }

    public void SetTrackVolume(int trackId, float volume) { ThrowIfDisposed(); Check(NativeMethods.SetTrackVolume(_handle, trackId, volume)); }
    public void SetTrackPan(int trackId, float pan) { ThrowIfDisposed(); Check(NativeMethods.SetTrackPan(_handle, trackId, pan)); }
    public void SetTrackMute(int trackId, bool mute) { ThrowIfDisposed(); Check(NativeMethods.SetTrackMute(_handle, trackId, mute ? 1 : 0)); }
    public void SetTrackSolo(int trackId, bool solo) { ThrowIfDisposed(); Check(NativeMethods.SetTrackSolo(_handle, trackId, solo ? 1 : 0)); }
    public int TrackCount { get { ThrowIfDisposed(); return NativeMethods.TrackCount(_handle); } }

    /// <summary>Duplicate a track (deep copy) after the source; returns the new id or -1.</summary>
    public int DuplicateTrack(int trackId) { ThrowIfDisposed(); return NativeMethods.DuplicateTrack(_handle, trackId); }
    /// <summary>Remove a track. False if the id is unknown.</summary>
    public bool RemoveTrack(int trackId) { ThrowIfDisposed(); return NativeMethods.RemoveTrack(_handle, trackId) == NativeMethods.NotaResult.Ok; }
    /// <summary>Reorder a non-return track to toIndex within the regular track list.</summary>
    public void MoveTrack(int trackId, int toIndex) { ThrowIfDisposed(); NativeMethods.MoveTrack(_handle, trackId, toIndex); }
    /// <summary>Add an empty top-level group (submix) track. Returns its id.</summary>
    public int AddGroupTrack() { ThrowIfDisposed(); return NativeMethods.AddGroupTrack(_handle); }
    /// <summary>Group the given tracks under a new group track; returns the group id (or -1).</summary>
    public int CreateGroup(int[] trackIds) { ThrowIfDisposed(); return trackIds is { Length: > 0 } ? NativeMethods.CreateGroup(_handle, trackIds, trackIds.Length) : -1; }
    /// <summary>Dissolve a group; its children reparent up one level.</summary>
    public void Ungroup(int groupId) { ThrowIfDisposed(); NativeMethods.Ungroup(_handle, groupId); }
    /// <summary>Move a track into groupId (-1 = top-level). Rejects cycles/returns.</summary>
    public void SetTrackGroup(int trackId, int groupId) { ThrowIfDisposed(); NativeMethods.SetTrackGroup(_handle, trackId, groupId); }

    // --- Send / return buses (M6-1) ----------------------------------------

    /// <summary>Adds a return (aux) effect bus. Returns the track id (>0), or 0 if all return slots are used.</summary>
    public int AddReturnTrack() { ThrowIfDisposed(); return NativeMethods.AddReturnTrack(_handle); }
    /// <summary>Post-fader send level (0 = off) from a track to return bus `bus`.</summary>
    public void SetTrackSend(int trackId, int bus, float level) { ThrowIfDisposed(); Check(NativeMethods.SetTrackSend(_handle, trackId, bus, level)); }
    public float GetTrackSend(int trackId, int bus) { ThrowIfDisposed(); return NativeMethods.GetTrackSend(_handle, trackId, bus); }
    /// <summary>Number of return buses currently in the graph.</summary>
    public int ReturnTrackCount { get { ThrowIfDisposed(); return NativeMethods.ReturnTrackCount(_handle); } }
    /// <summary>Bus slot for a return track (>=0), or -1 if the track is not a return.</summary>
    public int TrackReturnIndex(int trackId) { ThrowIfDisposed(); return NativeMethods.TrackReturnIndex(_handle, trackId); }

    // --- Arrangement geometry (M4) -----------------------------------------

    /// <summary>Track summary at <paramref name="index"/> (0..TrackCount-1).</summary>
    public bool TryGetTrackInfo(int index, out NotaTrackInfo info)
    {
        ThrowIfDisposed();
        return NativeMethods.GetTrackInfo(_handle, index, out info) != 0;
    }

    /// <summary>Clip geometry for clip <paramref name="clipIndex"/> on a track.</summary>
    public bool TryGetClipInfo(int trackId, int clipIndex, out NotaClipInfo info)
    {
        ThrowIfDisposed();
        return NativeMethods.GetClipInfo(_handle, trackId, clipIndex, out info) != 0;
    }

    // --- Clip editing (M4-2) -----------------------------------------------

    /// <summary>Moves a clip to a new (snapped) start beat.</summary>
    public void MoveClip(int trackId, int clipIndex, double newStartBeat)
    { ThrowIfDisposed(); Check(NativeMethods.ClipMove(_handle, trackId, clipIndex, newStartBeat)); }

    /// <summary>Moves a clip to another same-type track (instrument→instrument or audio→audio, atomic). Same-track == MoveClip.</summary>
    public void MoveClipToTrack(int srcTrackId, int clipIndex, int destTrackId, double newStartBeat)
    { ThrowIfDisposed(); Check(NativeMethods.ClipMoveToTrack(_handle, srcTrackId, clipIndex, destTrackId, newStartBeat)); }

    /// <summary>Trims/resizes a clip to a new start + length in beats.</summary>
    public void TrimClip(int trackId, int clipIndex, double newStartBeat, double newLengthBeats)
    { ThrowIfDisposed(); Check(NativeMethods.ClipTrim(_handle, trackId, clipIndex, newStartBeat, newLengthBeats)); }

    public void ResizeAudioClip(int trackId, int clipIndex, double newStartBeat, double newLengthBeats)
    { ThrowIfDisposed(); Check(NativeMethods.ClipResizeAudio(_handle, trackId, clipIndex, newStartBeat, newLengthBeats)); }

    /// <summary>Sets an unwarped audio clip's source region (clip Start/End); no-op on warped clips.</summary>
    public void SetClipSourceRegion(int trackId, int clipIndex, double offsetFrames, long lengthFrames)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetSourceRegion(_handle, trackId, clipIndex, offsetFrames, lengthFrames)); }

    /// <summary>Trims a warped clip's played window (beats over the warp); no-op on unwarped clips.</summary>
    public void SetClipWarpTrim(int trackId, int clipIndex, double playStart, double playEnd)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetWarpTrim(_handle, trackId, clipIndex, playStart, playEnd)); }

    /// <summary>Sets an audio clip's linear playback gain (runtime).</summary>
    public void SetClipGain(int trackId, int clipIndex, float gain)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetGain(_handle, trackId, clipIndex, gain)); }

    /// <summary>Clip deactivate (key 0): an inactive clip stays on the timeline but plays
    /// nothing (audio or MIDI). Works on audio and instrument tracks.</summary>
    public void SetClipActive(int trackId, int clipIndex, bool active)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetActive(_handle, trackId, clipIndex, active ? 1 : 0)); }

    /// <summary>Sets an audio clip's varispeed transpose in semitones (runtime).</summary>
    public void SetClipPitch(int trackId, int clipIndex, float semitones)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetPitch(_handle, trackId, clipIndex, semitones)); }

    /// <summary>Reverses an audio clip: the played region is read back-to-front. Nothing is
    /// re-rendered (the sample and any warp cache stay in file order), so this is as cheap
    /// as a gain change.</summary>
    public void SetClipReverse(int trackId, int clipIndex, bool reversed)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetReverse(_handle, trackId, clipIndex, reversed ? 1 : 0)); }

    /// <summary>Enables/disables warp and picks the mode (rebuilds the stretch cache).</summary>
    public void SetClipWarp(int trackId, int clipIndex, bool enabled, int mode)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetWarp(_handle, trackId, clipIndex, enabled ? 1 : 0, mode)); }

    /// <summary>Sets a warped clip's target musical length in beats.</summary>
    public void SetClipWarpLength(int trackId, int clipIndex, double beats)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetWarpLength(_handle, trackId, clipIndex, beats)); }

    /// <summary>Detects the clip's source tempo, enables warp, and snaps its length
    /// to the beat grid so it conforms to the project BPM. Returns the detected BPM
    /// (0 when detection fails and the clip is left unchanged).</summary>
    public double AutoWarpClip(int trackId, int clipIndex)
    { ThrowIfDisposed(); return NativeMethods.ClipAutoWarp(_handle, trackId, clipIndex); }

    /// <summary>Beats-mode warp: detects transients and pins a grid-snapped marker at
    /// each hit so percussion locks tightly to the grid. Returns detected BPM (0 = failed).</summary>
    public double BeatWarpClip(int trackId, int clipIndex)
    { ThrowIfDisposed(); return NativeMethods.ClipBeatWarp(_handle, trackId, clipIndex); }

    /// <summary>Project load: when set, warp edits skip the (heavy) stretch so applying a
    /// project stays fast; the caches are then filled by <see cref="WarpBuildStep"/>.</summary>
    public void SetDeferWarpBuild(bool defer)
    { ThrowIfDisposed(); NativeMethods.SetDeferWarpBuild(_handle, defer ? 1 : 0); }

    /// <summary>Builds ~<paramref name="maxFrames"/> device frames of warped audio into the
    /// deferred caches; returns the frames of warped audio still needing a cache (0 = done).</summary>
    public long WarpBuildStep(int maxFrames)
    { ThrowIfDisposed(); return NativeMethods.WarpBuildStep(_handle, maxFrames); }

    /// <summary>Frames of warped audio still needing a cache (progress denominator).</summary>
    public long WarpBuildRemaining
    { get { ThrowIfDisposed(); return NativeMethods.WarpBuildRemaining(_handle); } }

    /// <summary>Per-clip volume envelope (0..1, clip-local beats).</summary>
    public AutomationPoint[] GetClipVolumeEnvelope(int trackId, int clipIndex)
    {
        ThrowIfDisposed();
        int count = NativeMethods.ClipVolumeEnvGet(_handle, trackId, clipIndex, null, 0);
        if (count == 0) return System.Array.Empty<AutomationPoint>();
        var buf = new AutomationPoint[count];
        int written = NativeMethods.ClipVolumeEnvGet(_handle, trackId, clipIndex, buf, count);
        return written == count ? buf : buf[..written];
    }

    public void SetClipVolumeEnvelope(int trackId, int clipIndex, AutomationPoint[] points)
    { ThrowIfDisposed(); Check(NativeMethods.ClipVolumeEnvSet(_handle, trackId, clipIndex, points, points.Length)); }

    /// <summary>Per-clip pan envelope (-1..1, clip-local beats).</summary>
    public AutomationPoint[] GetClipPanEnvelope(int trackId, int clipIndex)
    {
        ThrowIfDisposed();
        int count = NativeMethods.ClipPanEnvGet(_handle, trackId, clipIndex, null, 0);
        if (count == 0) return System.Array.Empty<AutomationPoint>();
        var buf = new AutomationPoint[count];
        int written = NativeMethods.ClipPanEnvGet(_handle, trackId, clipIndex, buf, count);
        return written == count ? buf : buf[..written];
    }

    public void SetClipPanEnvelope(int trackId, int clipIndex, AutomationPoint[] points)
    { ThrowIfDisposed(); Check(NativeMethods.ClipPanEnvSet(_handle, trackId, clipIndex, points, points.Length)); }

    /// <summary>MIDI clip envelope (velocity or volume; 0..1, clip-local beats).</summary>
    public AutomationPoint[] GetMidiClipEnvelope(int trackId, int clipIndex, MidiClipEnvelope kind)
    {
        ThrowIfDisposed();
        int count = NativeMethods.MidiClipEnvGet(_handle, trackId, clipIndex, (int)kind, null, 0);
        if (count == 0) return System.Array.Empty<AutomationPoint>();
        var buf = new AutomationPoint[count];
        int written = NativeMethods.MidiClipEnvGet(_handle, trackId, clipIndex, (int)kind, buf, count);
        return written == count ? buf : buf[..written];
    }

    public void SetMidiClipEnvelope(int trackId, int clipIndex, MidiClipEnvelope kind, AutomationPoint[] points)
    { ThrowIfDisposed(); Check(NativeMethods.MidiClipEnvSet(_handle, trackId, clipIndex, (int)kind, points, points.Length)); }

    /// <summary>Replaces a warped clip's source↔beat markers (≥2; sorted internally).</summary>
    public void SetClipWarpMarkers(int trackId, int clipIndex, double[] srcFrames, double[] beats)
    { ThrowIfDisposed(); Check(NativeMethods.ClipSetWarpMarkers(_handle, trackId, clipIndex, srcFrames, beats, srcFrames.Length)); }

    /// <summary>Reads a warped clip's markers into the buffers; returns the total count.</summary>
    public int GetClipWarpMarkers(int trackId, int clipIndex, double[] outSrc, double[] outBeat)
    { ThrowIfDisposed(); return NativeMethods.ClipGetWarpMarkers(_handle, trackId, clipIndex, outSrc, outBeat, outSrc.Length); }

    /// <summary>Splits a clip at an absolute beat. Returns the new clip index, or -1.</summary>
    public int SplitClip(int trackId, int clipIndex, double atBeat)
    { ThrowIfDisposed(); return NativeMethods.ClipSplit(_handle, trackId, clipIndex, atBeat); }

    /// <summary>Duplicates a clip immediately after itself. Returns the new clip index, or -1.</summary>
    public int DuplicateClip(int trackId, int clipIndex)
    { ThrowIfDisposed(); return NativeMethods.ClipDuplicate(_handle, trackId, clipIndex); }

    /// <summary>Deletes a clip from its track.</summary>
    public void DeleteClip(int trackId, int clipIndex)
    { ThrowIfDisposed(); Check(NativeMethods.ClipDelete(_handle, trackId, clipIndex)); }

    /// <summary>Deletes a clip, returning false when the engine rejects the index (stale selection)
    /// instead of throwing.</summary>
    public bool TryDeleteClip(int trackId, int clipIndex)
    { ThrowIfDisposed(); return NativeMethods.ClipDelete(_handle, trackId, clipIndex) == NativeMethods.NotaResult.Ok; }

    /// <summary>Deletes clip content in [start,end) across the given tracks, splitting at the
    /// range edges. One undo step, no ripple. False when nothing was affected.</summary>
    public bool DeleteClipsInRange(int[] trackIds, double start, double end)
    { ThrowIfDisposed(); return NativeMethods.ClipsDeleteRange(_handle, trackIds, trackIds.Length, start, end) == NativeMethods.NotaResult.Ok; }

    /// <summary>Duplicates the [start,end) slice of the given tracks to [end, end+len). Returns
    /// the range length (&gt;0) so the caller can move the selection onto the copy, or 0 on no-op.</summary>
    public double DuplicateRange(int[] trackIds, double start, double end)
    { ThrowIfDisposed(); double r = NativeMethods.ClipsDuplicateRange(_handle, trackIds, trackIds.Length, start, end); return r > 0 ? r : 0; }

    /// <summary>Splits the given tracks' clips at both [start] and [end], keeping all content, so
    /// the covered slice becomes its own clip(s). One undo step, no ripple. False on no-op.</summary>
    public bool SplitClipsInRange(int[] trackIds, double start, double end)
    { ThrowIfDisposed(); return NativeMethods.ClipsSplitRange(_handle, trackIds, trackIds.Length, start, end) == NativeMethods.NotaResult.Ok; }

    /// <summary>Consolidate: replaces each given track's content in [start,end) with one clip
    /// spanning the range (MIDI merged, audio rendered to a new sample). One undo step; the new
    /// clips are reported by <see cref="LastPlacedClips"/>. False when no track had content.</summary>
    public bool ConsolidateRange(int[] trackIds, double start, double end)
    { ThrowIfDisposed(); return NativeMethods.ClipsConsolidateRange(_handle, trackIds, trackIds.Length, start, end) == NativeMethods.NotaResult.Ok; }

    /// <summary>Freezes track envelopes so clip moves no longer carry automation (req 8.3.3).</summary>
    public void SetAutomationLock(bool locked)
    { ThrowIfDisposed(); NativeMethods.SetAutomationLock(_handle, locked ? 1 : 0); }

    public bool AutomationLock
    { get { ThrowIfDisposed(); return NativeMethods.AutomationLock(_handle) != 0; } }

    /// <summary>True when the last cross-track move left device/plugin automation on the source.</summary>
    public bool LastMoveKeptDeviceAutomation()
    { ThrowIfDisposed(); return NativeMethods.LastMoveKeptDeviceAutomation(_handle) != 0; }

    /// <summary>Copies a clip to the clipboard. Returns false if the clip doesn't exist.</summary>
    public bool CopyClip(int trackId, int clipIndex)
    { ThrowIfDisposed(); return NativeMethods.ClipCopy(_handle, trackId, clipIndex) == NativeMethods.NotaResult.Ok; }

    /// <summary>Cuts a clip (copies it + its automation to the clipboard, then removes both the
    /// clip and the track automation in its span). Returns false if the clip doesn't exist.</summary>
    public bool CutClip(int trackId, int clipIndex)
    { ThrowIfDisposed(); return NativeMethods.ClipCut(_handle, trackId, clipIndex) == NativeMethods.NotaResult.Ok; }

    /// <summary>Pastes the clipboard clip onto a type-matching track at/after the beat
    /// (shifted right to avoid overlap). Returns the new clip index, or -1.</summary>
    public int PasteClip(int destTrackId, double atBeat)
    { ThrowIfDisposed(); return NativeMethods.ClipPaste(_handle, destTrackId, atBeat); }

    /// <summary>Clipboard clip kind: -1 empty, 0 audio, 1 midi.</summary>
    public int ClipboardClipKind()
    { ThrowIfDisposed(); return NativeMethods.ClipClipboardKind(_handle); }

    private static (int[] tracks, int[] clips) SplitSel((int trackId, int clipIndex)[] sel)
    {
        var t = new int[sel.Length]; var c = new int[sel.Length];
        for (int i = 0; i < sel.Length; i++) { t[i] = sel[i].trackId; c[i] = sel[i].clipIndex; }
        return (t, c);
    }

    /// <summary>Copies a set of clips to the block clipboard (with their automation), rebased
    /// to the block start. Returns false when nothing valid was captured.</summary>
    public bool CopyClipBlock((int trackId, int clipIndex)[] sel)
    { ThrowIfDisposed(); var (t, c) = SplitSel(sel); return NativeMethods.ClipsBlockCopy(_handle, t, c, sel.Length) == NativeMethods.NotaResult.Ok; }

    /// <summary>Cuts a set of clips: copies the block to the clipboard, then removes the clips
    /// and their track automation. Returns false when nothing valid was captured.</summary>
    public bool CutClipBlock((int trackId, int clipIndex)[] sel)
    { ThrowIfDisposed(); var (t, c) = SplitSel(sel); return NativeMethods.ClipsBlockCut(_handle, t, c, sel.Length) == NativeMethods.NotaResult.Ok; }

    /// <summary>Pastes the block clipboard at <paramref name="atBeat"/> (preserving relative
    /// geometry, one shared non-overlap shift). destTrackId -1 pastes onto the source tracks;
    /// otherwise the block is remapped by the track-index delta to that track. Returns clips pasted.</summary>
    public int PasteClipBlock(double atBeat, int destTrackId = -1)
    { ThrowIfDisposed(); return NativeMethods.ClipsBlockPaste(_handle, atBeat, destTrackId); }

    /// <summary>Duplicates a set of clips as one block placed right after itself. Returns the
    /// block length (>0), or -1 when nothing valid was captured.</summary>
    public double DuplicateClipBlock((int trackId, int clipIndex)[] sel)
    { ThrowIfDisposed(); var (t, c) = SplitSel(sel); return NativeMethods.ClipsBlockDuplicate(_handle, t, c, sel.Length); }

    /// <summary>Number of clips in the block clipboard (0 = empty).</summary>
    public int ClipboardBlockCount()
    { ThrowIfDisposed(); return NativeMethods.ClipsBlockCount(_handle); }

    /// <summary>The (trackId, clipIndex) of every clip the last block paste/duplicate produced.</summary>
    public (int trackId, int clipIndex)[] LastPlacedClips()
    {
        ThrowIfDisposed();
        int n = NativeMethods.ClipsLastPlaced(_handle, null, null, 0);
        if (n <= 0) return Array.Empty<(int, int)>();
        var t = new int[n]; var c = new int[n];
        int written = NativeMethods.ClipsLastPlaced(_handle, t, c, n);
        var outp = new (int, int)[written];
        for (int i = 0; i < written; i++) outp[i] = (t[i], c[i]);
        return outp;
    }

    /// <summary>Sets a clip's user-facing name.</summary>
    public void SetClipName(int trackId, int clipIndex, string name)
    { ThrowIfDisposed(); NativeMethods.ClipSetName(_handle, trackId, clipIndex, name ?? ""); }

    /// <summary>Gets a clip's user-facing name (empty when unset).</summary>
    public string GetClipName(int trackId, int clipIndex)
    { ThrowIfDisposed(); return ReadNativeString((b, c) => NativeMethods.ClipGetName(_handle, trackId, clipIndex, b, c)); }

    /// <summary>Sets a track's user-facing name.</summary>
    public void SetTrackName(int trackId, string name)
    { ThrowIfDisposed(); NativeMethods.TrackSetName(_handle, trackId, name ?? ""); }

    /// <summary>Gets a track's user-facing name (empty when unset).</summary>
    public string GetTrackName(int trackId)
    { ThrowIfDisposed(); return ReadNativeString((b, c) => NativeMethods.TrackGetName(_handle, trackId, b, c)); }

    /// <summary>Sets a track's palette colour slot (-1 = auto).</summary>
    public void SetTrackColorIndex(int trackId, int colorIndex)
    { ThrowIfDisposed(); NativeMethods.TrackSetColor(_handle, trackId, colorIndex); }

    /// <summary>Gets a track's palette colour slot (-1 = auto).</summary>
    public int GetTrackColorIndex(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TrackGetColor(_handle, trackId); }

    /// <summary>Copies a whole track (incl. devices/clips/automation) to the clipboard.</summary>
    public bool CopyTrack(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TrackCopy(_handle, trackId) == NativeMethods.NotaResult.Ok; }

    /// <summary>Pastes the clipboard track as a new track. Returns the new track id, or -1.</summary>
    public int PasteTrack()
    { ThrowIfDisposed(); return NativeMethods.TrackPaste(_handle); }

    /// <summary>True when a track has been copied to the clipboard.</summary>
    public bool HasTrackClipboard()
    { ThrowIfDisposed(); return NativeMethods.TrackHasClipboard(_handle) != 0; }

    /// <summary>Sets an audio track's record input source: 0 hardware, -1 master, &gt;0 source track id.</summary>
    public void SetTrackRecordInput(int trackId, int source)
    { ThrowIfDisposed(); NativeMethods.TrackSetRecordInput(_handle, trackId, source); }

    /// <summary>Gets an audio track's record input source (0 hardware, -1 master, &gt;0 track id).</summary>
    public int GetTrackRecordInput(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TrackGetRecordInput(_handle, trackId); }

    /// <summary>Sets an instrument track's MIDI input source (receive that track's MIDI; -1 = off).</summary>
    public void SetTrackMidiSource(int trackId, int sourceTrackId)
    { ThrowIfDisposed(); NativeMethods.TrackSetMidiSource(_handle, trackId, sourceTrackId); }

    /// <summary>Gets an instrument track's MIDI input source track id (-1 = off).</summary>
    public int GetTrackMidiSource(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TrackGetMidiSource(_handle, trackId); }

    // --- freeze (M7) -------------------------------------------------------

    /// <summary>Arms an offline freeze capture of a track. Call with the backend stopped;
    /// returns the number of frames to render (0 = failed/unsupported). Seek to 0 and pump
    /// <see cref="RenderOffline(float[],int)"/> for that many frames, then <see cref="EndFreeze"/>.</summary>
    public long BeginFreeze(int trackId, double lengthBeats)
    { ThrowIfDisposed(); return NativeMethods.TrackFreezeBegin(_handle, trackId, lengthBeats); }

    /// <summary>Publishes the captured buffer and marks the track frozen.</summary>
    public void EndFreeze(int trackId) { ThrowIfDisposed(); NativeMethods.TrackFreezeEnd(_handle, trackId); }

    /// <summary>Aborts an armed freeze capture without freezing anything.</summary>
    public void CancelFreeze() { ThrowIfDisposed(); NativeMethods.TrackFreezeCancel(_handle); }

    /// <summary>Drops a track's freeze buffer and returns it to live processing.</summary>
    public void UnfreezeTrack(int trackId) { ThrowIfDisposed(); NativeMethods.TrackUnfreeze(_handle, trackId); }

    /// <summary>True when a track is currently frozen (playing a captured buffer).</summary>
    public bool IsTrackFrozen(int trackId) { ThrowIfDisposed(); return NativeMethods.TrackIsFrozen(_handle, trackId) != 0; }

    /// <summary>Reads a frozen track's opaque audio blob for persistence (empty = not frozen).</summary>
    public byte[] GetFreezeState(int trackId)
    {
        ThrowIfDisposed();
        int n = NativeMethods.TrackFreezeGetState(_handle, trackId, null, 0);
        if (n <= 0) return System.Array.Empty<byte>();
        var buf = new byte[n];
        NativeMethods.TrackFreezeGetState(_handle, trackId, buf, n);
        return buf;
    }

    /// <summary>Restores a frozen track's audio blob (from persistence) and marks it frozen.</summary>
    public void SetFreezeState(int trackId, byte[] data)
    { ThrowIfDisposed(); if (data is { Length: > 0 }) NativeMethods.TrackFreezeSetState(_handle, trackId, data, data.Length); }

    // Fill a growing buffer from a length-returning native getter, then decode UTF-8.
    private static string ReadNativeString(System.Func<byte[], int, int> fill)
    {
        var buf = new byte[128];
        int len = fill(buf, buf.Length);
        if (len >= buf.Length) { buf = new byte[len + 1]; len = fill(buf, buf.Length); }
        return len <= 0 ? "" : System.Text.Encoding.UTF8.GetString(buf, 0, System.Math.Min(len, buf.Length));
    }

    /// <summary>Fetches waveform peaks (min,max pairs) for a clip. Returns the number of buckets written.</summary>
    public int GetClipPeaks(int trackId, int clipIndex, float[] outMinMax, int maxPoints)
    {
        ThrowIfDisposed();
        return NativeMethods.ClipGetPeaks(_handle, trackId, clipIndex, outMinMax, maxPoints);
    }

    /// <summary>Fetches peaks over the whole sample (ignores the clip's trimmed region) for the clip editor.</summary>
    public int GetClipSourcePeaks(int trackId, int clipIndex, float[] outMinMax, int maxPoints)
    {
        ThrowIfDisposed();
        return NativeMethods.ClipGetSourcePeaks(_handle, trackId, clipIndex, outMinMax, maxPoints);
    }

    /// <summary>Fetches peaks over the full warped material (ignores the trim window) for the clip editor.</summary>
    public int GetClipWarpFullPeaks(int trackId, int clipIndex, float[] outMinMax, int maxPoints)
    {
        ThrowIfDisposed();
        return NativeMethods.ClipGetWarpFullPeaks(_handle, trackId, clipIndex, outMinMax, maxPoints);
    }

    // --- Instrument tracks, MIDI clips & notes -----------------------------

    /// <summary>Adds an instrument track with the built-in Nota Synth. Returns its id.</summary>
    public int AddInstrumentTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddInstrumentTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add instrument track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Physical synth. Returns its id.</summary>
    public int AddPhysicalSynthTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddPhysicalSynthTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add physical synth track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Aurora wavetable synth. Returns its id.</summary>
    public int AddWavetableSynthTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddWavetableSynthTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add wavetable synth track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Volt virtual-analog synth. Returns its id.</summary>
    public int AddVoltSynthTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddVoltSynthTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Volt synth track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Bass synth. Returns its id.</summary>
    public int AddBassSynthTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddBassSynthTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Bass synth track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Pendulum synth. Returns its id.</summary>
    public int AddPendulumSynthTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddPendulumSynthTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Pendulum synth track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Operator FM synth. Returns its id.</summary>
    public int AddOperatorSynthTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddOperatorSynthTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Operator synth track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in sampler. Returns id, or throws on decode failure.</summary>
    public int AddSamplerTrack(string path, int rootNote = 60, bool loop = false)
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddSamplerTrack(_handle, path, rootNote, loop ? 1 : 0);
        if (id <= 0) throw new NotaEngineException($"Failed to load sampler from {path}.");
        if (TryGetSamplerInfo(id, out var si)) RememberSampleName(si.SampleId, path);
        return id;
    }

    /// <summary>Adds an empty Sampler instrument track (a sample is loaded later). Returns its id.</summary>
    public int AddSamplerInstrumentTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddSamplerInstrumentTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add sampler track.");
        return id;
    }

    /// <summary>Loads a sample file into an existing Sampler track, keeping its params. Returns success.</summary>
    public bool SetTrackSamplerSample(int trackId, string path, int rootNote = 60)
    {
        ThrowIfDisposed();
        bool ok = NativeMethods.SetTrackSamplerSample(_handle, trackId, path, rootNote) != 0;
        if (ok && TryGetSamplerInfo(trackId, out var si)) RememberSampleName(si.SampleId, path);
        return ok;
    }

    // Source names of loaded samples (the native buffer keeps only the audio).
    private readonly Dictionary<long, string> _sampleNames = new();

    private void RememberSampleName(long sampleId, string path)
    {
        if (sampleId == 0) return;
        lock (_sampleNames) _sampleNames[sampleId] = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    public string SampleName(long sampleId)
    { lock (_sampleNames) return _sampleNames.TryGetValue(sampleId, out var n) ? n : ""; }

    public void SetSampleName(long sampleId, string name)
    { if (sampleId != 0) lock (_sampleNames) _sampleNames[sampleId] = name ?? ""; }

    public bool SetTrackSamplerRoot(int trackId, int rootNote)
    { ThrowIfDisposed(); return NativeMethods.SetTrackSamplerRoot(_handle, trackId, rootNote) != 0; }

    public float SamplerPlayPosition(int trackId)
    { ThrowIfDisposed(); return NativeMethods.SamplerPlayPosition(_handle, trackId); }

    /// <summary>Adds an empty MIDI clip to an instrument track. Returns clip index, or -1.</summary>
    public int AddMidiClip(int trackId, double startBeat, double lengthBeats)
    {
        ThrowIfDisposed();
        return NativeMethods.AddMidiClip(_handle, trackId, startBeat, lengthBeats);
    }

    /// <summary>Replaces all notes of a MIDI clip (Piano Roll pushes the whole set).</summary>
    public void SetClipNotes(int trackId, int clipIndex, NotaNote[] notes)
    {
        ThrowIfDisposed();
        Check(NativeMethods.ClipSetNotes(_handle, trackId, clipIndex, notes, notes.Length));
    }

    /// <summary>Live note update with no undo checkpoint — a piano-roll drag pushes this every
    /// frame so playback follows instantly; the UI seeds one undo entry at the gesture start.</summary>
    public void SetClipNotesLive(int trackId, int clipIndex, NotaNote[] notes)
    {
        ThrowIfDisposed();
        Check(NativeMethods.ClipSetNotesLive(_handle, trackId, clipIndex, notes, notes.Length));
    }

    /// <summary>Currently-pressed live-input pitches (computer keyboard + MIDI), independent of
    /// track routing. Returns the count written into <paramref name="outNotes"/>.</summary>
    public int LiveHeldNotes(int[] outNotes)
    { ThrowIfDisposed(); return NativeMethods.LiveHeldNotes(_handle, outNotes, outNotes.Length); }

    /// <summary>Reads all notes of a MIDI clip.</summary>
    public NotaNote[] GetClipNotes(int trackId, int clipIndex)
    {
        ThrowIfDisposed();
        int count = NativeMethods.ClipNoteCount(_handle, trackId, clipIndex);
        if (count == 0) return Array.Empty<NotaNote>();
        var buf = new NotaNote[count];
        int written = NativeMethods.ClipGetNotes(_handle, trackId, clipIndex, buf, count);
        return written == count ? buf : buf[..written];
    }

    // --- Audio persistence (M7-6b) -----------------------------------------

    /// <summary>Full geometry of an audio clip. False if the clip at that index isn't audio.</summary>
    public bool TryGetAudioClipInfo(int trackId, int clipIndex, out NotaAudioClipInfo info)
    { ThrowIfDisposed(); return NativeMethods.GetAudioClipInfo(_handle, trackId, clipIndex, out info) != 0; }

    /// <summary>Adds an audio clip restoring its source offset/length/gain. Returns clip index or -1.</summary>
    public int AddAudioClipEx(int trackId, string path, double startBeat, double sourceOffsetFrames, long lengthFrames, float gain)
    { ThrowIfDisposed(); return NativeMethods.AddAudioClipEx(_handle, trackId, path, startBeat, sourceOffsetFrames, lengthFrames, gain); }

    /// <summary>Decoded-sample metadata by id. False if the id isn't found in the live graph.</summary>
    public bool TryGetSampleInfo(long sampleId, out NotaSampleInfo info)
    { ThrowIfDisposed(); return NativeMethods.SampleGetInfo(_handle, sampleId, out info) != 0; }

    /// <summary>Reads a sample's full interleaved float data (empty if the id isn't found).</summary>
    public float[] ReadSample(long sampleId)
    {
        ThrowIfDisposed();
        long total = NativeMethods.SampleRead(_handle, sampleId, null, 0);
        if (total <= 0) return Array.Empty<float>();
        var buf = new float[total];
        NativeMethods.SampleRead(_handle, sampleId, buf, total);
        return buf;
    }

    /// <summary>Built-in Sampler settings. False if the track's instrument isn't a Sampler.</summary>
    public bool TryGetSamplerInfo(int trackId, out NotaSamplerInfo info)
    { ThrowIfDisposed(); return NativeMethods.TrackSamplerInfo(_handle, trackId, out info) != 0; }

    /// <summary>Adds an instrument track with the built-in Nota Grain synth. Returns its id.</summary>
    public int AddGrainSynthTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddGrainSynthTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Grain synth track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Flux vector-morph synth. Returns its id.</summary>
    public int AddFluxSynthTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddFluxSynthTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Flux synth track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Rhythm drum machine. Returns its id.</summary>
    public int AddRhythmTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddRhythmTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Rhythm track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Monolith mono synth. Returns its id.</summary>
    public int AddMonolithTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddMonolithTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Monolith track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Pentad poly synth. Returns its id.</summary>
    public int AddPentadTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddPentadTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Pentad track.");
        return id;
    }

    /// <summary>Adds an instrument track with the built-in Nota Consort paraphonic synth. Returns its id.</summary>
    public int AddConsortTrack()
    {
        ThrowIfDisposed();
        var id = NativeMethods.AddConsortTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to add Consort track.");
        return id;
    }

    /// <summary>UI editing channel for the track's instrument (e.g. Nota Rhythm step patterns).</summary>
    public void InstrumentAction(int trackId, int id, int iarg, float farg)
    { ThrowIfDisposed(); NativeMethods.InstrumentAction(_handle, trackId, id, iarg, farg); }

    public bool SetTrackGrainSample(int trackId, string path, int rootNote = 60)
    { ThrowIfDisposed(); return NativeMethods.SetTrackGrainSample(_handle, trackId, path, rootNote) != 0; }

    public bool TryGetGrainInfo(int trackId, out NotaSamplerInfo info)
    { ThrowIfDisposed(); return NativeMethods.TrackGrainInfo(_handle, trackId, out info) != 0; }

    public bool SetRhythmVoiceSample(int trackId, int voice, string path)
    {
        ThrowIfDisposed();
        bool ok = NativeMethods.RhythmSetVoiceSample(_handle, trackId, voice, path) != 0;
        if (ok && TryGetRhythmVoiceInfo(trackId, voice, out var vi)) RememberSampleName(vi.SampleId, path);
        return ok;
    }
    public bool TryGetRhythmVoiceInfo(int trackId, int voice, out NotaSamplerInfo info)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceInfo(_handle, trackId, voice, out info) != 0; }
    public int RhythmVoiceSource(int trackId, int voice)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceSource(_handle, trackId, voice); }

    public int RhythmVoiceDeviceCount(int trackId, int voice)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceCount(_handle, trackId, voice); }
    public int RhythmAddVoiceDevice(int trackId, int voice, int kind)
    { ThrowIfDisposed(); return NativeMethods.RhythmAddVoiceDevice(_handle, trackId, voice, kind); }
    public bool RhythmRemoveVoiceDevice(int trackId, int voice, int dev)
    { ThrowIfDisposed(); return NativeMethods.RhythmRemoveVoiceDevice(_handle, trackId, voice, dev) != 0; }
    public void RhythmMoveVoiceDevice(int trackId, int voice, int from, int to)
    { ThrowIfDisposed(); NativeMethods.RhythmMoveVoiceDevice(_handle, trackId, voice, from, to); }
    public string RhythmVoiceDeviceName(int trackId, int voice, int dev)
    { ThrowIfDisposed(); return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(NativeMethods.RhythmVoiceDeviceName(_handle, trackId, voice, dev)) ?? ""; }
    public int RhythmVoiceDeviceBuiltinKind(int trackId, int voice, int dev)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceBuiltinKind(_handle, trackId, voice, dev); }
    public int RhythmVoiceDeviceParamCount(int trackId, int voice, int dev)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceParamCount(_handle, trackId, voice, dev); }
    public string RhythmVoiceDeviceParamName(int trackId, int voice, int dev, int param)
    { ThrowIfDisposed(); return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(NativeMethods.RhythmVoiceDeviceParamName(_handle, trackId, voice, dev, param)) ?? ""; }
    public float RhythmVoiceDeviceParamMin(int trackId, int voice, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceParamMin(_handle, trackId, voice, dev, param); }
    public float RhythmVoiceDeviceParamMax(int trackId, int voice, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceParamMax(_handle, trackId, voice, dev, param); }
    public float RhythmVoiceDeviceParamGet(int trackId, int voice, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceParamGet(_handle, trackId, voice, dev, param); }
    public void RhythmVoiceDeviceParamSet(int trackId, int voice, int dev, int param, float value)
    { ThrowIfDisposed(); NativeMethods.RhythmVoiceDeviceParamSet(_handle, trackId, voice, dev, param, value); }
    public bool RhythmVoiceDeviceBypassed(int trackId, int voice, int dev)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceBypassed(_handle, trackId, voice, dev) != 0; }
    public void RhythmSetVoiceDeviceBypassed(int trackId, int voice, int dev, bool bypassed)
    { ThrowIfDisposed(); NativeMethods.RhythmSetVoiceDeviceBypassed(_handle, trackId, voice, dev, bypassed ? 1 : 0); }
    public float RhythmVoiceDeviceGainReduction(int trackId, int voice, int dev)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceGainReduction(_handle, trackId, voice, dev); }
    public int RhythmVoiceDeviceScope(int trackId, int voice, int dev, float[] outSamples, int maxSamples)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceScope(_handle, trackId, voice, dev, outSamples, maxSamples); }
    public int RhythmVoiceDeviceLayerWave(int trackId, int voice, int dev, int layer, float[] outSamples, int maxSamples)
    { ThrowIfDisposed(); return NativeMethods.RhythmVoiceDeviceLayerWave(_handle, trackId, voice, dev, layer, outSamples, maxSamples); }
    public void RhythmVoiceDeviceAction(int trackId, int voice, int dev, int id, int iarg, float farg)
    { ThrowIfDisposed(); NativeMethods.RhythmVoiceDeviceAction(_handle, trackId, voice, dev, id, iarg, farg); }
    public string RhythmVoiceDeviceText(int trackId, int voice, int dev, int id)
    {
        ThrowIfDisposed();
        int n = NativeMethods.RhythmVoiceDeviceText(_handle, trackId, voice, dev, id, null, 0);
        if (n <= 0) return "";
        var buf = new byte[n + 1];
        NativeMethods.RhythmVoiceDeviceText(_handle, trackId, voice, dev, id, buf, buf.Length);
        return System.Text.Encoding.UTF8.GetString(buf, 0, n);
    }
    public string RhythmKitName(int trackId)
    { ThrowIfDisposed(); return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(NativeMethods.RhythmKitName(_handle, trackId)) ?? ""; }
    public void RhythmSetKitName(int trackId, string name)
    { ThrowIfDisposed(); NativeMethods.RhythmSetKitName(_handle, trackId, name ?? ""); }
    public string RhythmMacroName(int trackId, int macro)
    { ThrowIfDisposed(); return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(NativeMethods.RhythmMacroName(_handle, trackId, macro)) ?? ""; }
    public void RhythmSetMacroName(int trackId, int macro, string name)
    { ThrowIfDisposed(); NativeMethods.RhythmSetMacroName(_handle, trackId, macro, name ?? ""); }
    public int RhythmAddMacroMapping(int trackId, int macro, int voice, int device, int param, float lo, float hi)
    { ThrowIfDisposed(); return NativeMethods.RhythmAddMacroMapping(_handle, trackId, macro, voice, device, param, lo, hi); }
    public int RhythmMacroMappingCount(int trackId)
    { ThrowIfDisposed(); return NativeMethods.RhythmMacroMappingCount(_handle, trackId); }
    public bool RhythmTryGetMacroMapping(int trackId, int index, out RackMacroMapping mapping, out int curve)
    {
        ThrowIfDisposed();
        bool ok = NativeMethods.RhythmMacroMappingInfo(_handle, trackId, index, out int m, out int v, out int d, out int p, out float lo, out float hi, out curve) != 0;
        mapping = ok ? new RackMacroMapping(m, v, d, p, lo, hi) : default;
        return ok;
    }
    public bool RhythmRemoveMacroMapping(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.RhythmRemoveMacroMapping(_handle, trackId, index) != 0; }
    public bool RhythmSetMacroMappingRange(int trackId, int index, float lo, float hi)
    { ThrowIfDisposed(); return NativeMethods.RhythmSetMacroMappingRange(_handle, trackId, index, lo, hi) != 0; }
    public bool RhythmSetMacroMappingCurve(int trackId, int index, int curve)
    { ThrowIfDisposed(); return NativeMethods.RhythmSetMacroMappingCurve(_handle, trackId, index, curve) != 0; }
    public void RhythmClearMacros(int trackId)
    { ThrowIfDisposed(); NativeMethods.RhythmClearMacros(_handle, trackId); }

    public int GrainPlayPositions(int trackId, float[] outPos)
    { ThrowIfDisposed(); return NativeMethods.TrackGrainPlayPositions(_handle, trackId, outPos, outPos.Length); }

    public int InstrumentVoiceCount(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TrackActiveVoices(_handle, trackId); }

    public int InstrumentHeldNotes(int trackId, int[] outNotes)
    { ThrowIfDisposed(); return NativeMethods.TrackHeldNotes(_handle, trackId, outNotes, outNotes.Length); }

    public int InstrumentScope(int trackId, float[] outv)
    { ThrowIfDisposed(); return NativeMethods.TrackInstrumentScope(_handle, trackId, outv, outv.Length); }
}
