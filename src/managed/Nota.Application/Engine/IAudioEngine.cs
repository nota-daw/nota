// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The audio-engine port: the instance-level surface of the real-time engine that
// the Application and Presentation layers program against. Infrastructure's
// NotaEngine implements it over the native handle. Handle-free utilities (version,
// self-tests, plugin catalog, device enumeration) are not part of this port — they
// live on separate ports / static helpers.

namespace Nota.Application;

/// <summary>Command-and-query surface of the real-time audio engine. All calls are
/// UI-thread safe: mutations enqueue commands, reads are lock-free snapshots.</summary>
public interface IAudioEngine : IDisposable
{
    // --- Device / diagnostics ---------------------------------------------
    double SampleRate { get; }
    void Start();
    void Stop();
    void SetTestTone(bool enabled);
    void SetFrequency(float hz);

    // --- Transport ---------------------------------------------------------
    void Play();
    void StopTransport();
    void SetBpm(double bpm);
    double Bpm { get; }
    void SetTimeSignature(int num, int denom);
    void SetLoop(bool enabled, double startBeat, double endBeat);
    void SetMetronome(bool enabled);
    void Seek(double beat);
    double PositionBeats { get; }
    bool IsPlaying { get; }
    /// <summary>Loop region mirror (for UI display + toggling).</summary>
    bool LoopEnabled { get; }
    double LoopStart { get; }
    double LoopEnd { get; }

    // --- Mixer -------------------------------------------------------------
    void SetMasterVolume(float volume);

    // --- Tracks & clips ----------------------------------------------------
    int AddAudioTrack();
    int AddAudioClip(int trackId, string path, double startBeat);
    void SetTrackVolume(int trackId, float volume);
    void SetTrackPan(int trackId, float pan);
    void SetTrackMute(int trackId, bool mute);
    void SetTrackSolo(int trackId, bool solo);
    int TrackCount { get; }
    int DuplicateTrack(int trackId);   // deep copy after the source; new id or -1
    bool RemoveTrack(int trackId);
    void MoveTrack(int trackId, int toIndex);   // reorder a non-return track within the regular list
    int AddGroupTrack();                         // empty top-level group (submix) track
    int CreateGroup(int[] trackIds);             // group tracks under a new group; returns group id or -1
    void Ungroup(int groupId);                   // dissolve; children reparent up one level
    void SetTrackGroup(int trackId, int groupId); // move a track into groupId (-1 = top-level)

    // --- Send / return buses (M6-1) ----------------------------------------
    int AddReturnTrack();
    void SetTrackSend(int trackId, int bus, float level);
    float GetTrackSend(int trackId, int bus);
    int ReturnTrackCount { get; }
    int TrackReturnIndex(int trackId);

    // --- Session view (M5) -------------------------------------------------
    int SceneCount { get; }
    void AddSessionMidiClip(int trackId, int scene, double lengthBeats = 4.0);
    /// <summary>Empties a session slot (stops it if playing). False if already empty.</summary>
    bool ClearSessionSlot(int trackId, int scene);
    /// <summary>Appends a scene row; returns its index.</summary>
    int AddScene();
    /// <summary>Removes a scene row (always keeps at least one). False if it couldn't.</summary>
    bool RemoveScene(int scene);
    int SessionSlotState(int trackId, int scene);
    double SessionSlotLength(int trackId, int scene);
    void SetSessionSlotLength(int trackId, int scene, double lengthBeats);
    /// <summary>Audio-slot playback gain (linear; 1.0 when not an audio take).</summary>
    float SessionSlotGain(int trackId, int scene);
    void SetSessionSlotGain(int trackId, int scene, float gain);
    void SetLaunchQuant(double beats);
    void LaunchSlot(int trackId, int scene);
    void StopSlot(int trackId);
    void LaunchScene(int scene);
    void StopScene(int scene);
    void StopAllSession();
    /// <summary>Stop every session clip and hand all tracks back to the Arrangement timeline.</summary>
    void BackToArrangement();
    /// <summary>True when the Arrangement is playing; false in a session-only jam.</summary>
    bool ArrangementActive { get; }
    void RecordSessionSlot(int trackId, int scene);
    void StopSessionRecord();
    int SessionSlotToArrangement(int trackId, int scene, double startBeat);
    void ArrangementClipToSession(int trackId, int clipIndex, int scene);
    /// <summary>Copies an arrangement audio clip into a session slot (loops the whole sample).</summary>
    bool ArrangementAudioClipToSession(int trackId, int clipIndex, int scene);
    bool AudioRecordSelfTest();
    bool SessionAudioRecordSelfTest();
    void SetSessionNotes(int trackId, int scene, NotaNote[] notes);
    NotaNote[] GetSessionNotes(int trackId, int scene);

    // --- Arrangement geometry (M4) -----------------------------------------
    bool TryGetTrackInfo(int index, out NotaTrackInfo info);
    bool TryGetClipInfo(int trackId, int clipIndex, out NotaClipInfo info);

    // --- Clip editing (M4-2) -----------------------------------------------
    void MoveClip(int trackId, int clipIndex, double newStartBeat);
    /// <summary>Moves a clip to another same-type track — instrument→instrument or
    /// audio→audio (atomic). Same-track == MoveClip.</summary>
    void MoveClipToTrack(int srcTrackId, int clipIndex, int destTrackId, double newStartBeat);
    void TrimClip(int trackId, int clipIndex, double newStartBeat, double newLengthBeats);
    /// <summary>Grid resize for an audio clip (grid-relative): warped clips (or unwarped
    /// clips dragged past their source length) stretch; unwarped clips within source
    /// bounds trim. Auto-enables warp when a stretch is needed.</summary>
    void ResizeAudioClip(int trackId, int clipIndex, double newStartBeat, double newLengthBeats);
    /// <summary>Sets an unwarped audio clip's source region directly (clip Start/End):
    /// which part of the sample plays. Timeline position is unchanged. No-op on warped clips.</summary>
    void SetClipSourceRegion(int trackId, int clipIndex, double offsetFrames, long lengthFrames);
    /// <summary>Trims a warped clip's played window (beats over the warp material). No-op on unwarped clips.</summary>
    void SetClipWarpTrim(int trackId, int clipIndex, double playStart, double playEnd);
    /// <summary>Sets an audio clip's linear playback gain (runtime).</summary>
    void SetClipGain(int trackId, int clipIndex, float gain);
    /// <summary>Clip deactivate (key 0): an inactive clip stays on the timeline but plays
    /// nothing (audio or MIDI). Works on audio and instrument tracks.</summary>
    void SetClipActive(int trackId, int clipIndex, bool active);
    /// <summary>Sets an audio clip's varispeed transpose in semitones (runtime).</summary>
    void SetClipPitch(int trackId, int clipIndex, float semitones);
    /// <summary>Reverses an audio clip (non-destructive): the played region is read
    /// back-to-front. Composes with gain/pitch/warp; trims and splits mirror.</summary>
    void SetClipReverse(int trackId, int clipIndex, bool reversed);
    /// <summary>Enables/disables warp + mode for an audio clip (rebuilds the stretch cache).</summary>
    void SetClipWarp(int trackId, int clipIndex, bool enabled, int mode);
    /// <summary>Sets a warped clip's target musical length in beats.</summary>
    void SetClipWarpLength(int trackId, int clipIndex, double beats);
    /// <summary>Detects the clip's source tempo, enables warp, and snaps its length to
    /// the beat grid so it conforms to the project BPM. Returns the detected BPM (0 = failed).</summary>
    double AutoWarpClip(int trackId, int clipIndex);
    /// <summary>Beats-mode warp: detects transients and pins a grid-snapped marker at each
    /// hit so percussion locks tightly to the grid. Returns the detected BPM (0 = failed).</summary>
    double BeatWarpClip(int trackId, int clipIndex);
    /// <summary>Project load: when set, warp edits skip the heavy stretch so applying a
    /// project stays fast; caches are then filled incrementally via <see cref="WarpBuildStep"/>.</summary>
    void SetDeferWarpBuild(bool defer);
    /// <summary>Builds ~<paramref name="maxFrames"/> device frames of warped audio into the
    /// deferred caches; returns frames of warped audio still needing a cache (0 = done).</summary>
    long WarpBuildStep(int maxFrames);
    /// <summary>Frames of warped audio still needing a cache (progress denominator).</summary>
    long WarpBuildRemaining { get; }
    /// <summary>Replaces a warped clip's source↔beat markers (≥2).</summary>
    void SetClipWarpMarkers(int trackId, int clipIndex, double[] srcFrames, double[] beats);
    /// <summary>Reads a warped clip's markers into the buffers; returns the total count.</summary>
    int GetClipWarpMarkers(int trackId, int clipIndex, double[] outSrc, double[] outBeat);
    int SplitClip(int trackId, int clipIndex, double atBeat);
    int DuplicateClip(int trackId, int clipIndex);
    void DeleteClip(int trackId, int clipIndex);
    /// <summary>Deletes a clip, returning false (instead of throwing) when the engine rejects
    /// the track/clip index — e.g. a stale multi-selection entry. Use for bulk deletes where a
    /// single invalid index must not abort the whole operation.</summary>
    bool TryDeleteClip(int trackId, int clipIndex);
    /// <summary>Deletes clip content in the time range [start,end) across the given tracks,
    /// splitting clips at the range edges (one undo step, no ripple). False = nothing changed.</summary>
    bool DeleteClipsInRange(int[] trackIds, double start, double end);
    /// <summary>Duplicates the [start,end) slice of the given tracks to [end, end+len), splitting
    /// at the edges. Returns the range length (&gt;0), or 0 when nothing was duplicated.</summary>
    double DuplicateRange(int[] trackIds, double start, double end);
    /// <summary>Splits the given tracks' clips at both [start] and [end] (keeping all content) so
    /// the covered slice becomes its own clip(s). One undo step, no ripple. False = nothing changed.</summary>
    bool SplitClipsInRange(int[] trackIds, double start, double end);
    /// <summary>Consolidate: replaces each given track's content in [start,end) with ONE clip
    /// spanning the range — MIDI notes merged, audio clips rendered (gain/pitch/warp/fades/clip
    /// envelopes baked) into a new sample. Parts of clips outside the range survive. One undo
    /// step; <see cref="LastPlacedClips"/> then reports the new clips. False = nothing changed.</summary>
    bool ConsolidateRange(int[] trackIds, double start, double end);
    /// <summary>Locks track envelopes so clip moves stop carrying automation (req 8.3.3).</summary>
    void SetAutomationLock(bool locked);
    bool AutomationLock { get; }
    /// <summary>True when the last cross-track move left device/plugin automation on the source
    /// track (only Volume/Pan follow across tracks) — for an explicit UX hint (req 8.3.4).</summary>
    bool LastMoveKeptDeviceAutomation();
    /// <summary>Copies a clip to the engine clip clipboard.</summary>
    bool CopyClip(int trackId, int clipIndex);
    /// <summary>Cuts a clip: copies it (with its automation) to the clipboard, then removes
    /// the clip and the track automation in its span. Delete, by contrast, leaves automation.</summary>
    bool CutClip(int trackId, int clipIndex);
    /// <summary>Pastes the clipboard clip onto a type-matching track at/after the beat (no overlap). Returns new index or -1.</summary>
    int PasteClip(int destTrackId, double atBeat);
    /// <summary>Clipboard clip kind: -1 empty, 0 audio, 1 midi.</summary>
    int ClipboardClipKind();
    /// <summary>Copies a set of clips to the block clipboard (with their automation). False if none valid.</summary>
    bool CopyClipBlock((int trackId, int clipIndex)[] sel);
    /// <summary>Cuts a set of clips: copies the block to the clipboard, then removes the clips + their automation.</summary>
    bool CutClipBlock((int trackId, int clipIndex)[] sel);
    /// <summary>Pastes the block clipboard at the beat (preserving geometry). destTrackId -1 = source
    /// tracks; otherwise the block is remapped by the track-index delta to that track. Returns clips pasted.</summary>
    int PasteClipBlock(double atBeat, int destTrackId = -1);
    /// <summary>Duplicates a set of clips as one block placed right after itself. Returns the block length (>0), or -1.</summary>
    double DuplicateClipBlock((int trackId, int clipIndex)[] sel);
    /// <summary>Number of clips in the block clipboard (0 = empty).</summary>
    int ClipboardBlockCount();
    /// <summary>The (trackId, clipIndex) of every clip the last block paste/duplicate produced.</summary>
    (int trackId, int clipIndex)[] LastPlacedClips();
    /// <summary>Sets/gets a clip's user-facing name (empty = default).</summary>
    void SetClipName(int trackId, int clipIndex, string name);
    string GetClipName(int trackId, int clipIndex);
    /// <summary>Sets/gets a track's user-facing name (empty = default).</summary>
    void SetTrackName(int trackId, string name);
    string GetTrackName(int trackId);
    /// <summary>Sets/gets a track's palette colour slot (-1 = auto by position).</summary>
    void SetTrackColorIndex(int trackId, int colorIndex);
    int GetTrackColorIndex(int trackId);
    /// <summary>Copies a whole track to the clipboard; pastes it as a new track (-1 on fail).</summary>
    bool CopyTrack(int trackId);
    int PasteTrack();
    bool HasTrackClipboard();
    /// <summary>Audio-track record input source: 0 hardware, -1 master, &gt;0 source track id.</summary>
    void SetTrackRecordInput(int trackId, int source);
    int GetTrackRecordInput(int trackId);
    /// <summary>Set an instrument track's MIDI input source — receive another instrument track's
    /// MIDI output ("MIDI In"). sourceTrackId = -1 turns it off.</summary>
    void SetTrackMidiSource(int trackId, int sourceTrackId);
    int GetTrackMidiSource(int trackId);

    // --- freeze (M7): bounce a track's pre-fader audio to a buffer and play it back
    //     in place of the live instrument+device chain. Drive offline with the backend
    //     stopped (BeginFreeze → seek 0 → pump RenderOffline → EndFreeze). ---
    /// <summary>Arms an offline freeze capture; returns frames to render (0 = unsupported).</summary>
    long BeginFreeze(int trackId, double lengthBeats);
    /// <summary>Publishes the captured buffer and marks the track frozen.</summary>
    void EndFreeze(int trackId);
    /// <summary>Aborts an armed freeze capture.</summary>
    void CancelFreeze();
    /// <summary>Drops a track's freeze buffer (back to live processing).</summary>
    void UnfreezeTrack(int trackId);
    /// <summary>True when the track is frozen.</summary>
    bool IsTrackFrozen(int trackId);
    /// <summary>Reads a frozen track's audio blob for persistence (empty = not frozen).</summary>
    byte[] GetFreezeState(int trackId);
    /// <summary>Restores a frozen track's audio blob and marks it frozen.</summary>
    void SetFreezeState(int trackId, byte[] data);

    int GetClipPeaks(int trackId, int clipIndex, float[] outMinMax, int maxPoints);
    /// <summary>Peaks over the whole sample (ignores the trimmed region) for the clip editor's Start/End brackets.</summary>
    int GetClipSourcePeaks(int trackId, int clipIndex, float[] outMinMax, int maxPoints);
    /// <summary>Peaks over the full warped material (ignores the trim window) for the warped-clip Start/End brackets.</summary>
    int GetClipWarpFullPeaks(int trackId, int clipIndex, float[] outMinMax, int maxPoints);

    // --- Instrument tracks, MIDI clips & notes -----------------------------
    int AddInstrumentTrack();
    /// <summary>Adds an instrument track with the built-in Nota Physical (physical modeling) synth.</summary>
    int AddPhysicalSynthTrack();
    /// <summary>Adds an instrument track with the built-in Nota Aurora (wavetable) synth.</summary>
    int AddWavetableSynthTrack();
    /// <summary>Adds an instrument track with the built-in Nota Volt (virtual-analog) synth.</summary>
    int AddVoltSynthTrack();
    /// <summary>Adds an instrument track with the built-in Nota Bass synth.</summary>
    int AddBassSynthTrack();
    /// <summary>Adds an instrument track with the built-in Nota Pendulum synth.</summary>
    int AddPendulumSynthTrack();
    /// <summary>Adds an instrument track with the built-in Nota Operator FM synth.</summary>
    int AddOperatorSynthTrack();
    /// <summary>Adds an instrument track with the built-in Nota Grain granular synth.</summary>
    int AddGrainSynthTrack();
    /// <summary>Adds an instrument track with the built-in Nota Flux vector-morph synth.</summary>
    int AddFluxSynthTrack();
    /// <summary>Adds an instrument track with the built-in Nota Rhythm drum machine (kind 12).</summary>
    int AddRhythmTrack();
    /// <summary>Adds an instrument track with the built-in Nota Monolith mono synth (kind 13).</summary>
    int AddMonolithTrack();
    /// <summary>Adds an instrument track with the built-in Nota Pentad 5-voice poly synth (kind 14).</summary>
    int AddPentadTrack();
    /// <summary>Adds an instrument track with the built-in Nota Consort paraphonic semi-modular synth (kind 15).</summary>
    int AddConsortTrack();
    /// <summary>UI editing channel for the track's instrument (e.g. Rhythm step patterns).</summary>
    void InstrumentAction(int trackId, int id, int iarg, float farg);
    /// <summary>Loads a sample file into a Nota Grain track (kind 10). True on success.</summary>
    bool SetTrackGrainSample(int trackId, string path, int rootNote = 60);
    /// <summary>Grain sample info (sample id / root). True if the track is a Nota Grain.</summary>
    bool TryGetGrainInfo(int trackId, out NotaSamplerInfo info);
    /// <summary>Nota Rhythm Phase 2 — load a one-shot into a drum voice (switches it to Sample).</summary>
    bool SetRhythmVoiceSample(int trackId, int voice, string path);
    /// <summary>Per-voice sample id (0 = none) for Nota Rhythm.</summary>
    bool TryGetRhythmVoiceInfo(int trackId, int voice, out NotaSamplerInfo info);
    /// <summary>Per-voice source: 0 = Synth, 1 = Sample.</summary>
    int RhythmVoiceSource(int trackId, int voice);
    /// <summary>Live Nota Grain read positions (0..1) of active voices into outPos; returns the count.</summary>
    int GrainPlayPositions(int trackId, float[] outPos);
    /// <summary>Active synth-voice count for a built-in instrument, or -1 if unsupported.</summary>
    int InstrumentVoiceCount(int trackId);
    /// <summary>Held chord pitches for a generative instrument (Nota Pendulum) into outNotes; returns the count.</summary>
    int InstrumentHeldNotes(int trackId, int[] outNotes);
    /// <summary>Currently-pressed live-input pitches (computer keyboard + MIDI), independent of track routing.</summary>
    int LiveHeldNotes(int[] outNotes);
    /// <summary>Live viz telemetry (Nota Operator harmonic spectrum) into outv; returns the count written.</summary>
    int InstrumentScope(int trackId, float[] outv);
    int AddSamplerTrack(string path, int rootNote = 60, bool loop = false);
    /// <summary>Adds an empty Sampler instrument track (a sample is loaded later).</summary>
    int AddSamplerInstrumentTrack();
    /// <summary>Loads a sample into an existing Sampler track, keeping its params.</summary>
    bool SetTrackSamplerSample(int trackId, string path, int rootNote = 60);
    /// <summary>Sets the Sampler track's root note (lock-free).</summary>
    bool SetTrackSamplerRoot(int trackId, int rootNote);
    /// <summary>Live Sampler playback position (0..1 of the sample, -1 = silent) for the UI cursor.</summary>
    float SamplerPlayPosition(int trackId);
    int AddMidiClip(int trackId, double startBeat, double lengthBeats);

    // --- Hosted plugins (M3-3) ---------------------------------------------
    int AddPluginInstrumentTrack(int catalogIndex);
    void SetTrackInstrumentPlugin(int trackId, int catalogIndex);
    /// <summary>Replaces an instrument track's instrument with a fresh built-in synth of the
    /// given kind. Returns false for Racks/unknown kinds or non-instrument tracks.</summary>
    bool SetTrackBuiltinInstrument(int trackId, int kind);
    int AddTrackEffectPlugin(int trackId, int catalogIndex);
    int TrackDeviceCount(int trackId);
    /// <summary>Copies an audio clip's played region down-mixed to mono for offline audio→MIDI
    /// analysis. Pass null to query the frame count. Returns the frame count; sr = source rate.</summary>
    int GetClipAudioMono(int trackId, int clipIndex, float[]? outOrNull, out double sr);

    // --- Built-in devices + params (M4-4/5) --------------------------------
    int AddBuiltinDevice(int trackId, int kind);
    void MoveDevice(int trackId, int fromIndex, int toIndex);
    void RemoveDevice(int trackId, int deviceIndex);
    string DeviceName(int trackId, int deviceIndex);
    int DeviceParamCount(int trackId, int deviceIndex);
    string DeviceParamName(int trackId, int deviceIndex, int paramIndex);
    float DeviceParamMin(int trackId, int deviceIndex, int paramIndex);
    float DeviceParamMax(int trackId, int deviceIndex, int paramIndex);
    float DeviceGetParam(int trackId, int deviceIndex, int paramIndex);
    void DeviceSetParam(int trackId, int deviceIndex, int paramIndex, float value);
    /// <summary>Live gain reduction (dB, ≥0) of a dynamics device (built-in Compressor); 0 otherwise.</summary>
    float DeviceGainReduction(int trackId, int deviceIndex);
    /// <summary>Factory-default value of a parameter, for double-click reset.</summary>
    float DeviceParamDefault(int trackId, int deviceIndex, int paramIndex);
    float InstrumentParamDefault(int trackId, int paramIndex);
    float MidiEffectParamDefault(int trackId, int index, int paramIndex);

    // --- MIDI effects (before the instrument) ---
    int AddMidiEffect(int trackId, int kind);           // 0 = Arpeggiator; returns index or -1
    int TrackMidiEffectCount(int trackId);
    void MoveMidiEffect(int trackId, int fromIndex, int toIndex);
    void RemoveMidiEffect(int trackId, int index);
    int MidiEffectKind(int trackId, int index);
    /// <summary>Last note-on IN/OUT pitch a MIDI effect remapped (Nota Scale readout); -1 = none.</summary>
    int MidiEffectLastIn(int trackId, int index);
    int MidiEffectLastOut(int trackId, int index);
    /// <summary>Float scope buffer for a MIDI effect editor (Nota Velocity in/out pairs); returns count written.</summary>
    int MidiEffectScope(int trackId, int index, float[] outv);
    string MidiEffectName(int trackId, int index);
    int MidiEffectParamCount(int trackId, int index);
    string MidiEffectParamName(int trackId, int index, int paramIndex);
    float MidiEffectParamMin(int trackId, int index, int paramIndex);
    float MidiEffectParamMax(int trackId, int index, int paramIndex);
    float MidiEffectGetParam(int trackId, int index, int paramIndex);
    void MidiEffectSetParam(int trackId, int index, int paramIndex, float value);
    void SetMidiEffectBypassed(int trackId, int index, bool bypassed);
    bool MidiEffectBypassed(int trackId, int index);
    void SetMidiEffectCcDest(int trackId, int index, int destDevice, int destParam);
    void SetMidiEffectCcDepth(int trackId, int index, float depth);
    int MidiEffectCcDestDevice(int trackId, int index);
    int MidiEffectCcDestParam(int trackId, int index);
    float MidiEffectCcDepth(int trackId, int index);
    /// <summary>Copies up to maxSamples of the device's live readings into outSamples; returns
    /// the count written. Each device defines its own layout (the analysers: a telemetry block,
    /// then spectra — e.g. Nota EQ-8's in / out peaks, auto gain, then its pre and post
    /// spectra); zero for devices without one. Lock-free.</summary>
    int DeviceScope(int trackId, int deviceIndex, float[] outSamples, int maxSamples);
    /// <summary>Interactive-device command channel (the looper's transport + per-layer mute/gain).
    /// Applied at the next quantize boundary by the engine.</summary>
    void DeviceAction(int trackId, int deviceIndex, int id, int iarg, float farg);
    /// <summary>Per-layer waveform envelope for multi-layer devices (the looper): peak bins into
    /// outSamples (oldest→newest); returns the count.</summary>
    int DeviceLayerWave(int trackId, int deviceIndex, int layer, float[] outSamples, int maxSamples);
    /// <summary>Opaque device-state blob (the looper's recorded PCM) for project save/restore;
    /// empty for param-only devices.</summary>
    byte[] DeviceGetState(int trackId, int deviceIndex);
    void DeviceSetState(int trackId, int deviceIndex, byte[] data);
    /// <summary>Load an auxiliary audio file into a device (Nota Chamber: a user impulse
    /// response — WAV / FLAC / MP3, mono, stereo or 4-channel true stereo). False if unsupported.</summary>
    bool DeviceLoadFile(int trackId, int deviceIndex, string path);
    /// <summary>A device's resource text (Nota Chamber: 0 IR name, 1 IR category, 2 user IR
    /// name, 10 the built-in IR list as "name\tcategory\tseconds" lines); "" when none.</summary>
    string DeviceText(int trackId, int deviceIndex, int id);
    /// <summary>Sidechain routing: point a device's detector at another track's signal (-1 clears).</summary>
    void SetDeviceSidechainSource(int trackId, int deviceIndex, int sourceTrackId);
    int DeviceSidechainSource(int trackId, int deviceIndex);
    /// <summary>Whether a device can key off a sidechain source: the Compressor, or a plugin
    /// exposing a sidechain input bus (Phase C). Drives whether the UI offers a source picker.</summary>
    bool DeviceAcceptsSidechain(int trackId, int deviceIndex);
    /// <summary>Instrument sidechain ("React", Nota Flux): route a listening instrument to a
    /// source track (-1 clears). Source is the previous-block, post-fader signal.</summary>
    void SetInstrumentSidechainSource(int trackId, int sourceTrackId);
    int InstrumentSidechainSource(int trackId);
    /// <summary>Whether a track's instrument listens to a sidechain (Nota Flux).</summary>
    bool InstrumentAcceptsSidechain(int trackId);
    /// <summary>Sidechain detector gain in dB (Phase D): how hard the source drives the effect.</summary>
    void SetDeviceSidechainGain(int trackId, int deviceIndex, float gainDb);
    float DeviceSidechainGain(int trackId, int deviceIndex);
    /// <summary>Dry/wet of the device's effect, 0..1 (Phase D).</summary>
    void SetDeviceSidechainMix(int trackId, int deviceIndex, float mix);
    float DeviceSidechainMix(int trackId, int deviceIndex);
    /// <summary>Source tap point (Phase D): true = pre-FX pre-fader, false = post-FX post-fader.</summary>
    void SetDeviceSidechainTapPre(int trackId, int deviceIndex, bool pre);
    bool DeviceSidechainTapPre(int trackId, int deviceIndex);
    void OpenPluginEditor(int trackId, int deviceIndex);
    void ClosePluginEditor(int trackId, int deviceIndex);
    byte[] GetPluginState(int trackId, int deviceIndex);
    void SetPluginState(int trackId, int deviceIndex, byte[] data);
    void SetDeviceBypassed(int trackId, int deviceIndex, bool bypassed);
    bool DeviceBypassed(int trackId, int deviceIndex);
    int TrackLatencySamples(int trackId);

    // --- Instrument Rack (parallel chains + 8 macros) ----------------------
    /// <summary>Adds an instrument track hosting an Instrument Rack with one default Synth chain.</summary>
    int AddInstrumentRackTrack();
    /// <summary>Adds an instrument track hosting an (empty) Drum Rack; pads are added via chains + trigger notes.</summary>
    int AddDrumRackTrack();
    /// <summary>Adds a Sampler chain (drum pad / sampler chain) loaded from a file to the track's rack. Returns the chain index or -1.</summary>
    int RackAddSamplerChain(int trackId, string path, int rootNote, bool loop);
    /// <summary>Load a sample file into a rack chain's Sampler (keeps its params + devices).</summary>
    bool RackSetChainSamplerSample(int trackId, int chain, string path, int rootNote);
    /// <summary>Adds a hosted-plugin (VST3/AU) instrument as a new chain to the track's rack. Returns the chain index or -1.</summary>
    int RackAddPluginInstrumentChain(int trackId, int catalogIndex);
    /// <summary>Adds a hosted-plugin effect into a chain of the track's instrument rack. Returns the device index or -1.</summary>
    int RackAddPluginChainDevice(int trackId, int chain, int catalogIndex);
    int RackChainTriggerNote(int trackId, int chain);
    void RackSetChainTriggerNote(int trackId, int chain, int note);
    /// <summary>The chain's display name — a Drum Rack pad label. Empty means "use the
    /// chain instrument's own name" (every Sampler pad would otherwise read the same).</summary>
    string RackChainName(int trackId, int chain);
    void RackSetChainName(int trackId, int chain, string name);
    int RackChainCount(int trackId);
    int RackAddChain(int trackId, int instKind);                 // 0=Synth, 2=Physical
    bool RackRemoveChain(int trackId, int chain);
    bool RackSetChainInstrument(int trackId, int chain, int instKind);
    int RackChainInstrumentKind(int trackId, int chain);
    string RackChainInstrumentName(int trackId, int chain);
    int RackChainInstrumentParamCount(int trackId, int chain);
    string RackChainInstrumentParamName(int trackId, int chain, int param);
    /// <summary>Stable id ("volume", "start", …) of a chain instrument param.</summary>
    string RackChainInstrumentParamId(int trackId, int chain, int param);
    float RackChainInstrumentParamGet(int trackId, int chain, int param);
    void RackChainInstrumentParamSet(int trackId, int chain, int param, float normalized);
    /// <summary>Factory-default value (0..1) of a chain instrument param, for double-click reset.</summary>
    float RackChainInstrumentParamDefault(int trackId, int chain, int param);
    /// <summary>Built-in Sampler settings for a chain instrument. False if it isn't a Sampler.</summary>
    bool RackChainSamplerInfo(int trackId, int chain, out NotaSamplerInfo info);
    /// <summary>Sets a chain Sampler's root note (lock-free). False if the chain isn't a Sampler.</summary>
    bool RackSetChainSamplerRoot(int trackId, int chain, int rootNote);
    /// <summary>A chain Sampler's live playback position (0..1), -1 when silent.</summary>
    float RackChainSamplerPlayPosition(int trackId, int chain);
    /// <summary>Opens the chain instrument's native editor (hosted plugins).</summary>
    void RackOpenChainInstrumentEditor(int trackId, int chain);
    /// <summary>Open a rack chain's hosted-plugin effect native editor window.</summary>
    void RackOpenChainDeviceEditor(int trackId, int chain, int dev);
    /// <summary>Stable plugin identifier of the chain instrument ("" for built-ins).</summary>
    string RackChainInstrumentPluginId(int trackId, int chain);
    /// <summary>Opaque state of the chain instrument (for preset capture).</summary>
    byte[] RackGetChainInstrumentState(int trackId, int chain);
    int RackChainDeviceCount(int trackId, int chain);
    int RackAddChainDevice(int trackId, int chain, int deviceKind); // 0=EQ..4=Utility
    bool RackRemoveChainDevice(int trackId, int chain, int dev);
    bool RackMoveChainDevice(int trackId, int chain, int fromIndex, int toIndex);
    string RackChainDeviceName(int trackId, int chain, int dev);
    int RackChainDeviceBuiltinKind(int trackId, int chain, int dev);
    int RackChainDeviceParamCount(int trackId, int chain, int dev);
    string RackChainDeviceParamName(int trackId, int chain, int dev, int param);
    float RackChainDeviceParamMin(int trackId, int chain, int dev, int param);
    float RackChainDeviceParamMax(int trackId, int chain, int dev, int param);
    float RackChainDeviceParamGet(int trackId, int chain, int dev, int param);
    void RackChainDeviceParamSet(int trackId, int chain, int dev, int param, float value);
    void RackSetChainDeviceBypassed(int trackId, int chain, int dev, bool bypassed);
    bool RackChainDeviceBypassed(int trackId, int chain, int dev);
    void RackSetChainGain(int trackId, int chain, float v);
    void RackSetChainPan(int trackId, int chain, float v);
    void RackSetChainMute(int trackId, int chain, bool mute);
    void RackSetChainSolo(int trackId, int chain, bool solo);
    float RackChainGain(int trackId, int chain);
    float RackChainPan(int trackId, int chain);
    bool RackChainMute(int trackId, int chain);
    bool RackChainSolo(int trackId, int chain);
    float RackMacroGet(int trackId, int macro);
    void RackMacroSet(int trackId, int macro, float v);
    int RackAddMacroMapping(int trackId, int macro, int chain, int deviceIndex, int paramIndex, float rangeMin, float rangeMax);
    int RackMappingCount(int trackId);
    bool RackTryGetMapping(int trackId, int index, out RackMacroMapping mapping);
    bool RackRemoveMapping(int trackId, int index);
    // Instrument-Rack extras (mockup 2p): key/velocity zones, per-chain meter, named
    // macros, rack output (volume/glide), macro-map range/curve editing.
    void RackChainZone(int trackId, int chain, out int keyLo, out int keyHi, out int velLo, out int velHi);
    void RackSetChainZone(int trackId, int chain, int keyLo, int keyHi, int velLo, int velHi);
    float RackChainMeter(int trackId, int chain);
    string RackMacroName(int trackId, int macro);
    void RackSetMacroName(int trackId, int macro, string name);
    float RackVolume(int trackId);
    void RackSetVolume(int trackId, float v);
    float RackGlide(int trackId);
    void RackSetGlide(int trackId, float v);
    bool RackSetMappingRange(int trackId, int index, float rangeMin, float rangeMax);
    int RackMappingCurve(int trackId, int index);
    bool RackSetMappingCurve(int trackId, int index, int curve);
    // Drum Rack per-pad shaping (choke group / tune semitones / decay 0..1) + kit swing/humanize.
    void RackSetChainChoke(int trackId, int chain, int group);
    int RackChainChoke(int trackId, int chain);
    void RackSetChainTune(int trackId, int chain, int semitones);
    int RackChainTune(int trackId, int chain);
    void RackSetChainDecay(int trackId, int chain, float v);
    float RackChainDecay(int trackId, int chain);
    void RackSetSwing(int trackId, float v);
    float RackSwing(int trackId);
    void RackSetHumanize(int trackId, float v);
    float RackHumanize(int trackId);

    // --- Audio Effect Rack (a RackDevice in a track's device chain) --------
    // Same surface, addressed by (trackId, deviceIndex). Add one via AddBuiltinDevice(trackId, 5).
    int RackDevAddPluginChainDevice(int trackId, int deviceIndex, int chain, int catalogIndex);
    int RackDevChainTriggerNote(int trackId, int deviceIndex, int chain);
    void RackDevSetChainTriggerNote(int trackId, int deviceIndex, int chain, int note);
    int RackDevChainCount(int trackId, int deviceIndex);
    int RackDevAddChain(int trackId, int deviceIndex, int instKind);
    bool RackDevRemoveChain(int trackId, int deviceIndex, int chain);
    bool RackDevSetChainInstrument(int trackId, int deviceIndex, int chain, int instKind);
    int RackDevChainInstrumentKind(int trackId, int deviceIndex, int chain);
    string RackDevChainInstrumentName(int trackId, int deviceIndex, int chain);
    int RackDevChainInstrumentParamCount(int trackId, int deviceIndex, int chain);
    string RackDevChainInstrumentParamName(int trackId, int deviceIndex, int chain, int param);
    float RackDevChainInstrumentParamGet(int trackId, int deviceIndex, int chain, int param);
    void RackDevChainInstrumentParamSet(int trackId, int deviceIndex, int chain, int param, float normalized);
    int RackDevChainDeviceCount(int trackId, int deviceIndex, int chain);
    int RackDevAddChainDevice(int trackId, int deviceIndex, int chain, int deviceKind);
    bool RackDevRemoveChainDevice(int trackId, int deviceIndex, int chain, int dev);
    bool RackDevMoveChainDevice(int trackId, int deviceIndex, int chain, int fromIndex, int toIndex);
    string RackDevChainDeviceName(int trackId, int deviceIndex, int chain, int dev);
    int RackDevChainDeviceBuiltinKind(int trackId, int deviceIndex, int chain, int dev);
    void RackDevOpenChainDeviceEditor(int trackId, int deviceIndex, int chain, int dev);
    int RackDevChainDeviceParamCount(int trackId, int deviceIndex, int chain, int dev);
    string RackDevChainDeviceParamName(int trackId, int deviceIndex, int chain, int dev, int param);
    float RackDevChainDeviceParamMin(int trackId, int deviceIndex, int chain, int dev, int param);
    float RackDevChainDeviceParamMax(int trackId, int deviceIndex, int chain, int dev, int param);
    float RackDevChainDeviceParamGet(int trackId, int deviceIndex, int chain, int dev, int param);
    void RackDevChainDeviceParamSet(int trackId, int deviceIndex, int chain, int dev, int param, float value);
    void RackDevSetChainDeviceBypassed(int trackId, int deviceIndex, int chain, int dev, bool bypassed);
    bool RackDevChainDeviceBypassed(int trackId, int deviceIndex, int chain, int dev);
    void RackDevSetChainGain(int trackId, int deviceIndex, int chain, float v);
    void RackDevSetChainPan(int trackId, int deviceIndex, int chain, float v);
    void RackDevSetChainMute(int trackId, int deviceIndex, int chain, bool mute);
    void RackDevSetChainSolo(int trackId, int deviceIndex, int chain, bool solo);
    float RackDevChainGain(int trackId, int deviceIndex, int chain);
    float RackDevChainPan(int trackId, int deviceIndex, int chain);
    bool RackDevChainMute(int trackId, int deviceIndex, int chain);
    bool RackDevChainSolo(int trackId, int deviceIndex, int chain);
    float RackDevMacroGet(int trackId, int deviceIndex, int macro);
    void RackDevMacroSet(int trackId, int deviceIndex, int macro, float v);
    int RackDevAddMacroMapping(int trackId, int deviceIndex, int macro, int chain, int targetDevice, int paramIndex, float rangeMin, float rangeMax);
    int RackDevMappingCount(int trackId, int deviceIndex);
    bool RackDevTryGetMapping(int trackId, int deviceIndex, int index, out RackMacroMapping mapping);
    bool RackDevRemoveMapping(int trackId, int deviceIndex, int index);
    // Audio Effect Rack: rack-out (gain/dry-wet), routing (mode/PDC/chain selector), meter, select zone, macro names + mapping edit.
    float RackDevChainMeter(int trackId, int deviceIndex, int chain);
    void RackDevChainZone(int trackId, int deviceIndex, int chain, out int velLo, out int velHi);
    void RackDevSetChainZone(int trackId, int deviceIndex, int chain, int velLo, int velHi);
    string RackDevMacroName(int trackId, int deviceIndex, int macro);
    void RackDevSetMacroName(int trackId, int deviceIndex, int macro, string name);
    bool RackDevSetMappingRange(int trackId, int deviceIndex, int index, float rangeMin, float rangeMax);
    int RackDevMappingCurve(int trackId, int deviceIndex, int index);
    bool RackDevSetMappingCurve(int trackId, int deviceIndex, int index, int curve);
    float RackDevVolume(int trackId, int deviceIndex);
    void RackDevSetVolume(int trackId, int deviceIndex, float v);
    int RackDevMode(int trackId, int deviceIndex);
    void RackDevSetMode(int trackId, int deviceIndex, int mode);
    float RackDevDryWet(int trackId, int deviceIndex);
    void RackDevSetDryWet(int trackId, int deviceIndex, float v);
    bool RackDevPdc(int trackId, int deviceIndex);
    void RackDevSetPdc(int trackId, int deviceIndex, bool on);
    float RackDevChainSelect(int trackId, int deviceIndex);
    void RackDevSetChainSelect(int trackId, int deviceIndex, float v);
    bool RackDevSelFollow(int trackId, int deviceIndex);
    void RackDevSetSelFollow(int trackId, int deviceIndex, bool on);
    float RackDevLiveSelector(int trackId, int deviceIndex);

    // --- Level meters (M6-2) -----------------------------------------------
    bool TryGetTrackMeter(int trackId, out NotaMeter meter);
    NotaMeter MasterMeter();

    // --- MIDI clip notes ---------------------------------------------------
    void SetClipNotes(int trackId, int clipIndex, NotaNote[] notes);
    /// <summary>Live note update with no undo checkpoint (piano-roll drag pushing every frame).</summary>
    void SetClipNotesLive(int trackId, int clipIndex, NotaNote[] notes);
    NotaNote[] GetClipNotes(int trackId, int clipIndex);

    // --- Live MIDI, arming & recording -------------------------------------
    void SetTrackArmed(int trackId, bool armed);
    void NoteOn(int pitch, float velocity);
    void NoteOff(int pitch);
    /// <summary>Live notes also reach this track when unarmed (rack/drum pad audition). -1 = none.</summary>
    void SetAuditionTrack(int trackId);
    void SetRecording(bool enabled);
    bool IsRecording { get; }
    int RecordStartStatus { get; }
    /// <summary>Track id of the in-progress audio take (0 when not capturing).</summary>
    int AudioRecordTrackId { get; }
    /// <summary>Beat where the in-progress audio take began.</summary>
    double AudioRecordStartBeat { get; }
    /// <summary>Length (beats) captured so far in the in-progress take (grows monotonically).</summary>
    double AudioRecordLengthBeats { get; }
    /// <summary>Reserved track id for the master effect chain (drive it via the track device ops).</summary>
    int MasterTrackId { get; }
    /// <summary>Live min/max peaks over the in-progress capture buffer (growing-take waveform). Returns buckets written.</summary>
    int AudioRecordPeaks(float[] outMinMax, int maxPoints);
    void Poll();

    // --- Undo / redo (M6-6) ------------------------------------------------
    bool Undo();
    bool Redo();
    bool CanUndo { get; }
    bool CanRedo { get; }

    // --- Project load (M7-6) -----------------------------------------------
    void Reset();
    int TrackInstrumentKind(int trackId);
    int TrackDeviceBuiltinKind(int trackId, int deviceIndex);
    string TrackInstrumentPluginId(int trackId);
    string TrackDevicePluginId(int trackId, int deviceIndex);

    // --- Audio persistence (M7-6b) -----------------------------------------
    bool TryGetAudioClipInfo(int trackId, int clipIndex, out NotaAudioClipInfo info);
    int AddAudioClipEx(int trackId, string path, double startBeat, double sourceOffsetFrames, long lengthFrames, float gain);
    bool TryGetSampleInfo(long sampleId, out NotaSampleInfo info);
    float[] ReadSample(long sampleId);
    bool TryGetSamplerInfo(int trackId, out NotaSamplerInfo info);
    bool TryGetSessionAudioSlot(int trackId, int scene, out NotaSessionAudioSlot info);
    bool AddSessionAudioClip(int trackId, int scene, string path, double lengthBeats, double sourceOffsetFrames, long lengthFrames, float gain);
    bool AddSessionAudioFile(int trackId, int scene, string path);

    // --- Offline render (tests / export) -----------------------------------
    void RenderOffline(float[] outBuffer, int frames);
    void RenderOffline(float[] outBuffer, int frames, double sampleRate);

    // --- Audio device settings (M7-1) --------------------------------------
    void SetAudioOutputDevice(string uid);
    void SetAudioInputDevice(string uid);
    void SetAudioSampleRate(double sampleRate);
    void SetAudioBufferFrames(int frames);
    /// <summary>Stage WASAPI exclusive mode (Windows only; ignored elsewhere). Apply with <see cref="ApplyAudio"/>.</summary>
    void SetAudioWasapiExclusive(bool on);
    (string OutputUid, string InputUid, double SampleRate, int BufferFrames, bool WasapiExclusive) GetAudioConfig();
    void ApplyAudio();
    double NegotiatedSampleRate { get; }
    int NegotiatedBufferFrames { get; }
    /// <summary>True if the last <see cref="ApplyAudio"/> requested WASAPI exclusive but the device refused
    /// it and the backend fell back to shared. Always false on platforms without exclusive mode.</summary>
    bool AudioExclusiveFallback { get; }

    // --- MIDI device settings (M7-2) ---------------------------------------
    bool IsMidiInputEnabled(string uid);
    void SetMidiInputEnabled(string uid, bool enabled);
    void ApplyMidi();

    // --- MIDI learn: drain incoming CC/note-on events (4 int32 per event) ---
    int PollMidiControlEvents(int[] quadBuffer);

    // --- gamepad input (live note source) ----------------------------------
    // Poll-driven: the UI ticks PollGamepadEvents and turns each edge into
    // NoteOn/NoteOff — the same entry points the computer keyboard uses, so
    // armed-track recording and the piano-roll highlight apply unchanged.
    void GamepadStart();
    void GamepadStop();
    int GamepadCount { get; }
    IReadOnlyList<GamepadDevice> Gamepads();
    int PollGamepadEvents(GamepadButtonEvent[] buffer);
    /// <summary>Latest position of every axis on <paramref name="pad"/>, written into
    /// <paramref name="buffer"/> in <see cref="GamepadAxis"/> order as 0..127. Returns the
    /// count written (0 for an out-of-range pad). Sampled rather than queued: a stick
    /// would flood an event ring and only its latest position matters.</summary>
    int GamepadAxisValues(int pad, int[] buffer);

    // --- Audio preview / audition (M7-4a) ----------------------------------
    void PreviewFile(string path);
    void StopPreview();
    bool IsPreviewActive { get; }
    bool PreviewSelfTest();

    // --- xrun / dropout telemetry (M7-8) -----------------------------------
    int XrunCount { get; }
    bool XrunSelfTest();

    // --- Parameter automation (M9) -----------------------------------------
    // Lanes are structural (snapshot + undo). AddAutomationLane returns the lane
    // index, reusing an existing lane on the same target.
    bool AutomationSelfTest();
    bool AutomationCurveSelfTest();    // M9-D: device-free curvature check
    bool PluginAutomationSelfTest();   // M9-B: device-free structural check
    // Hosted-plugin params (M9-B): deviceIndex < 0 = the instrument. Values 0..1,
    // identified by a stable string paramID.
    int PluginParamCount(int trackId, int deviceIndex);
    string PluginParamId(int trackId, int deviceIndex, int paramIndex);
    string PluginParamName(int trackId, int deviceIndex, int paramIndex);
    float PluginParamGet(int trackId, int deviceIndex, int paramIndex);
    void PluginParamSet(int trackId, int deviceIndex, int paramIndex, float normalized);
    int AddPluginAutomationLane(int trackId, int deviceIndex, string paramId);
    string AutomationLaneParamId(int trackId, int laneIndex);
    int PluginLastTouchedParam(int trackId, int deviceIndex);   // "Learn" (M9-B3)
    bool AutomationWriteSelfTest();   // M9-C: device-free write-path check
    // Automation record (M9-C). There are no record modes: lanes always play back, and
    // a control gesture records while automation record is on (the transport record
    // button drives it). deviceIndex < 0 = instrument; paramId for PluginParam.
    void SetAutomationRecord(bool on);
    bool AutomationRecording { get; }
    /// <summary><paramref name="latch"/> marks a hardware/MIDI control: it keeps writing past
    /// the release until the transport stops. Mouse gestures pass false.</summary>
    void BeginAutomationWrite(int trackId, AutomationTarget target, int deviceIndex, int paramIndex, string paramId, bool latch = false);
    /// <summary>Raised (UI thread) when a control begins an automation-write gesture, so the
    /// arrangement can follow the touched param in automation mode. Args: trackId, target,
    /// deviceIndex, paramIndex, paramId.</summary>
    event System.Action<int, AutomationTarget, int, int, string>? AutomationTouched;
    void EndAutomationWrite(int trackId, AutomationTarget target, int deviceIndex, int paramIndex, string paramId);
    /// <summary>Hand every lane a hand-moved control took over back to playback.</summary>
    void ReenableAutomation();
    bool AutomationOverridden { get; }
    int AddAutomationLane(int trackId, AutomationTarget target, int deviceIndex, int paramIndex);
    int AutomationLaneCount(int trackId);
    AutomationLaneInfo AutomationLaneInfo(int trackId, int laneIndex);
    AutomationPoint[] GetAutomationPoints(int trackId, int laneIndex);
    void SetAutomationPoints(int trackId, int laneIndex, AutomationPoint[] points);
    /// <summary>Set a lane's points without an undo checkpoint — for real-time updates while
    /// dragging a point (the caller checkpoints once at drag start, commits via the undo path on release).</summary>
    void SetAutomationPointsLive(int trackId, int laneIndex, AutomationPoint[] points);
    void RemoveAutomationLane(int trackId, int laneIndex);
    /// <summary>Master-volume automation (graph-level, not tied to a track).</summary>
    AutomationPoint[] GetMasterVolumeAutomation();
    void SetMasterVolumeAutomation(AutomationPoint[] points);
    /// <summary>Per-clip volume envelope for an audio clip (0..1, clip-local beats).</summary>
    AutomationPoint[] GetClipVolumeEnvelope(int trackId, int clipIndex);
    void SetClipVolumeEnvelope(int trackId, int clipIndex, AutomationPoint[] points);
    /// <summary>Per-clip pan envelope for an audio clip (-1..1, clip-local beats).</summary>
    AutomationPoint[] GetClipPanEnvelope(int trackId, int clipIndex);
    void SetClipPanEnvelope(int trackId, int clipIndex, AutomationPoint[] points);
    /// <summary>MIDI clip envelope (velocity or volume; 0..1, clip-local beats).</summary>
    AutomationPoint[] GetMidiClipEnvelope(int trackId, int clipIndex, MidiClipEnvelope kind);
    void SetMidiClipEnvelope(int trackId, int clipIndex, MidiClipEnvelope kind, AutomationPoint[] points);

    /// <summary>Smoothed real-time DSP load, 0..1 (0 when no live device runs).</summary>
    float CpuLoad { get; }

    // --- CV modulation (Phase 3, Modular editor) ---------------------------
    // Modulator (LFO) sources + CV links to built-in device params on the same track.
    // Modulator field selector: 0 waveform, 1 tempoSync, 2 rateHz, 3 rateSyncBeats,
    // 4 depth, 5 phase. CV modes: 0 Add, 1 Multiply, 2 Override.
    int ModulatorAdd(int trackId, int kind);
    void ModulatorRemove(int trackId, int modId);
    int ModulatorCount(int trackId);
    int ModulatorIdAt(int trackId, int index);
    int ModulatorKind(int trackId, int modId);
    float ModulatorGet(int trackId, int modId, int field);
    void ModulatorSet(int trackId, int modId, int field, float value);
    /// <summary>Live modulator output at the current transport position (for the node viz), -1..1×depth.</summary>
    float ModulatorValue(int trackId, int modId);
    /// <summary>Rolling CV history for a Scope modulator; fills <paramref name="outv"/> oldest→newest, returns count.</summary>
    int ModulatorScope(int trackId, int modId, float[] outv);
    int CvLinkAdd(int trackId, int modId, int deviceIndex, int paramIndex);
    /// <summary>Cross-track link: modulator on trackId modulates a param on targetTrack (-1 = self).</summary>
    int CvLinkAddTo(int trackId, int modId, int targetTrack, int deviceIndex, int paramIndex);
    /// <summary>Param → param link: a source param (srcDevice/srcParam on trackId) drives a target param.</summary>
    int CvLinkAddFromParam(int trackId, int srcDevice, int srcParam, int targetTrack, int targetDevice, int targetParam);
    /// <summary>Target-kind variant (0 device, 1 instrument [dev=-1], 2 MIDI-FX) for a modulator source.</summary>
    int CvLinkAddToTarget(int trackId, int modId, int targetKind, int targetTrack, int targetDevice, int targetParam);
    int CvLinkAddFromParamToTarget(int trackId, int srcDevice, int srcParam, int targetKind, int targetTrack, int targetDevice, int targetParam);
    int CvLinkTargetKind(int trackId, int index);
    /// <summary>True if (targetKind, track, device, param) is a modulation target.</summary>
    bool ParamModulated(int targetKind, int trackId, int deviceIndex, int paramIndex);
    void CvLinkRemove(int trackId, int index);
    int CvLinkCount(int trackId);
    int CvLinkSource(int trackId, int index);
    /// <summary>CV source kind: 0 = modulator, 1 = parameter.</summary>
    int CvLinkSourceKind(int trackId, int index);
    int CvLinkSourceDevice(int trackId, int index);
    int CvLinkSourceParam(int trackId, int index);
    /// <summary>Target track id of a link (-1 = the modulator's own track).</summary>
    int CvLinkTargetTrack(int trackId, int index);
    int CvLinkDevice(int trackId, int index);
    int CvLinkParam(int trackId, int index);
    float CvLinkDepth(int trackId, int index);
    int CvLinkMode(int trackId, int index);
    void SetCvLinkDepth(int trackId, int index, float depth);
    void SetCvLinkMode(int trackId, int index, int mode);
    /// <summary>Modulation centre (base) value the link swings around; edited by the UI.</summary>
    float CvLinkBase(int trackId, int index);
    void SetCvLinkBase(int trackId, int index, float baseValue);
    bool DeviceParamModulated(int trackId, int deviceIndex, int paramIndex);
}
