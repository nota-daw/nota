// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>
/// Idiomatic C# wrapper over the native nota.engine handle. This is the only
/// type the app layer should use — it owns the native handle lifetime and
/// translates result codes into exceptions at the boundary. The wrapper surface
/// is split by domain across the NotaEngine.*.cs partials; this file holds the
/// handle lifetime, engine-global controls, and the shared guards.
/// </summary>
public sealed partial class NotaEngine : IAudioEngine
{
    private IntPtr _handle;

    public NotaEngine()
    {
        _handle = NativeMethods.Create();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create native engine.");
    }

    /// <summary>Engine library version, e.g. "0.2.5" (src/native/nota.engine/VERSION).</summary>
    public static string Version => Marshal.PtrToStringUTF8(NativeMethods.Version()) ?? "?";

    /// <summary>Negotiated output sample rate, or 0 if not started.</summary>
    public double SampleRate
    {
        get
        {
            ThrowIfDisposed();
            return NativeMethods.SampleRate(_handle);
        }
    }

    /// <summary>Open the default output device and start the audio thread.</summary>
    public void Start()
    {
        ThrowIfDisposed();
        Check(NativeMethods.Start(_handle));
    }

    /// <summary>Stop the audio thread and close the device.</summary>
    public void Stop()
    {
        ThrowIfDisposed();
        Check(NativeMethods.Stop(_handle));
    }

    /// <summary>Enable/disable the diagnostic test tone (M0).</summary>
    public void SetTestTone(bool enabled)
    {
        ThrowIfDisposed();
        Check(NativeMethods.SetTestTone(_handle, enabled ? 1 : 0));
    }

    /// <summary>Set the test tone frequency in Hz (0 &lt; hz ≤ 20000).</summary>
    public void SetFrequency(float hz)
    {
        ThrowIfDisposed();
        Check(NativeMethods.SetFrequency(_handle, hz));
    }

    // --- Transport ---------------------------------------------------------

    public void Play()  { ThrowIfDisposed(); Check(NativeMethods.TransportPlay(_handle)); }
    public void StopTransport() { ThrowIfDisposed(); Check(NativeMethods.TransportStop(_handle)); }
    public void SetBpm(double bpm) { ThrowIfDisposed(); Check(NativeMethods.SetBpm(_handle, bpm)); }
    public double Bpm { get { ThrowIfDisposed(); return NativeMethods.TransportBpm(_handle); } }
    public void SetTimeSignature(int num, int denom) { ThrowIfDisposed(); Check(NativeMethods.SetTimeSignature(_handle, num, denom)); }
    public void SetLoop(bool enabled, double startBeat, double endBeat) { ThrowIfDisposed(); Check(NativeMethods.SetLoop(_handle, enabled ? 1 : 0, startBeat, endBeat)); }
    public void SetMetronome(bool enabled) { ThrowIfDisposed(); Check(NativeMethods.SetMetronome(_handle, enabled ? 1 : 0)); }
    public void Seek(double beat) { ThrowIfDisposed(); Check(NativeMethods.Seek(_handle, beat)); }

    /// <summary>Current playhead position in beats (lock-free UI read).</summary>
    public double PositionBeats { get { ThrowIfDisposed(); return NativeMethods.PositionBeats(_handle); } }
    public bool IsPlaying { get { ThrowIfDisposed(); return NativeMethods.IsPlaying(_handle) != 0; } }

    /// <summary>Loop region mirror (for UI display + toggling).</summary>
    public bool LoopEnabled { get { ThrowIfDisposed(); return NativeMethods.LoopEnabled(_handle) != 0; } }
    public double LoopStart { get { ThrowIfDisposed(); return NativeMethods.LoopStart(_handle); } }
    public double LoopEnd { get { ThrowIfDisposed(); return NativeMethods.LoopEnd(_handle); } }

    // --- Mixer -------------------------------------------------------------

    public void SetMasterVolume(float volume) { ThrowIfDisposed(); Check(NativeMethods.SetMasterVolume(_handle, volume)); }

    // --- Level meters (M6-2) -----------------------------------------------

    /// <summary>Post-fader peak/RMS for a track over the last block. False if unknown id.</summary>
    public bool TryGetTrackMeter(int trackId, out NotaMeter meter)
    {
        ThrowIfDisposed();
        return NativeMethods.TrackMeter(_handle, trackId, out meter) != 0;
    }

    /// <summary>Post-fader peak/RMS of the master bus over the last block.</summary>
    public NotaMeter MasterMeter()
    {
        ThrowIfDisposed();
        Check(NativeMethods.MasterMeter(_handle, out var m));
        return m;
    }

    // --- Live MIDI, arming & recording -------------------------------------

    public void SetTrackArmed(int trackId, bool armed) { ThrowIfDisposed(); Check(NativeMethods.SetTrackArmed(_handle, trackId, armed ? 1 : 0)); }
    public void NoteOn(int pitch, float velocity) { ThrowIfDisposed(); Check(NativeMethods.NoteOn(_handle, pitch, velocity)); }
    public void NoteOff(int pitch) { ThrowIfDisposed(); Check(NativeMethods.NoteOff(_handle, pitch)); }
    public void SetAuditionTrack(int trackId) { ThrowIfDisposed(); NativeMethods.SetAuditionTrack(_handle, trackId); }
    public void SetRecording(bool enabled) { ThrowIfDisposed(); Check(NativeMethods.SetRecording(_handle, enabled ? 1 : 0)); }
    public bool IsRecording { get { ThrowIfDisposed(); return NativeMethods.IsRecording(_handle) != 0; } }
    /// <summary>Why the last SetRecording(true) did (not) start: 0 ok, 1 no armed track, 2 audio input failed.</summary>
    public int RecordStartStatus { get { ThrowIfDisposed(); return NativeMethods.RecordStartStatus(_handle); } }
    /// <summary>Track id of the in-progress audio take (0 when not capturing), for drawing a growing clip.</summary>
    public int AudioRecordTrackId { get { ThrowIfDisposed(); return NativeMethods.AudioRecordTrack(_handle); } }
    /// <summary>Beat where the in-progress audio take began.</summary>
    public double AudioRecordStartBeat { get { ThrowIfDisposed(); return NativeMethods.AudioRecordStartBeat(_handle); } }
    /// <summary>Length (beats) captured so far in the in-progress take.</summary>
    public double AudioRecordLengthBeats { get { ThrowIfDisposed(); return NativeMethods.AudioRecordLengthBeats(_handle); } }
    /// <summary>Reserved track id for the master effect chain.</summary>
    public int MasterTrackId { get { ThrowIfDisposed(); return NativeMethods.MasterTrackId(_handle); } }
    /// <summary>Live min/max peaks over the in-progress capture buffer (growing-take waveform). Returns buckets written.</summary>
    public int AudioRecordPeaks(float[] outMinMax, int maxPoints) { ThrowIfDisposed(); return NativeMethods.AudioRecordPeaks(_handle, outMinMax, maxPoints); }

    /// <summary>Message-thread pump: materialises recorded notes into clips. Call from the UI timer.</summary>
    public void Poll() { ThrowIfDisposed(); NativeMethods.Poll(_handle); }

    // --- Undo / redo (M6-6) ------------------------------------------------

    /// <summary>Undo the last structural edit. Returns false if there was nothing to undo.</summary>
    public bool Undo() { ThrowIfDisposed(); return NativeMethods.Undo(_handle) == NativeMethods.NotaResult.Ok; }
    /// <summary>Redo the last undone edit. Returns false if there was nothing to redo.</summary>
    public bool Redo() { ThrowIfDisposed(); return NativeMethods.Redo(_handle) == NativeMethods.NotaResult.Ok; }
    public bool CanUndo { get { ThrowIfDisposed(); return NativeMethods.CanUndo(_handle) != 0; } }
    public bool CanRedo { get { ThrowIfDisposed(); return NativeMethods.CanRedo(_handle) != 0; } }

    // --- Project load (M7-6) -----------------------------------------------

    /// <summary>Clears the session to an empty project (used by File → New / Open).</summary>
    public void Reset() { ThrowIfDisposed(); NativeMethods.Reset(_handle); }

    // --- Offline render (tests / export) -----------------------------------

    /// <summary>Renders `frames` of interleaved stereo into `outBuffer` (length &gt;= 2*frames).</summary>
    public void RenderOffline(float[] outBuffer, int frames)
    {
        ThrowIfDisposed();
        Check(NativeMethods.RenderOffline(_handle, outBuffer, frames));
    }

    /// <summary>
    /// Renders `frames` of interleaved stereo at <paramref name="sampleRate"/> (WAV export, M6-4).
    /// Stop the audio backend first — this mutates transport state the audio thread reads.
    /// </summary>
    public void RenderOffline(float[] outBuffer, int frames, double sampleRate)
    {
        ThrowIfDisposed();
        Check(NativeMethods.RenderOfflineAt(_handle, outBuffer, frames, sampleRate));
    }

    // --- audio preview / audition (M7-4a) ---

    /// <summary>Decodes and auditions <paramref name="path"/> through the live output (one at a time).</summary>
    public void PreviewFile(string path)
    { ThrowIfDisposed(); Check(NativeMethods.PreviewFile(_handle, path)); }

    /// <summary>Stops the current audition.</summary>
    public void StopPreview()
    { ThrowIfDisposed(); Check(NativeMethods.StopPreview(_handle)); }

    /// <summary>Whether an audition is currently playing.</summary>
    public bool IsPreviewActive
    { get { ThrowIfDisposed(); return NativeMethods.PreviewActive(_handle) != 0; } }

    /// <summary>Device-free self-test: auditions a synthetic buffer and asserts audible output.</summary>
    public bool PreviewSelfTest()
    { ThrowIfDisposed(); return NativeMethods.PreviewSelfTest(_handle) != 0; }

    // --- xrun / dropout telemetry (M7-8) ---

    /// <summary>Running count of audio-device dropouts since launch.</summary>
    public int XrunCount
    { get { ThrowIfDisposed(); return NativeMethods.XrunCount(_handle); } }

    /// <summary>Device-free self-test: notifying a dropout bumps the counter by one.</summary>
    public bool XrunSelfTest()
    { ThrowIfDisposed(); return NativeMethods.XrunSelfTest(_handle) != 0; }

    /// <summary>Smoothed real-time DSP load, 0..1 (render time / block budget).</summary>
    public float CpuLoad
    { get { ThrowIfDisposed(); return NativeMethods.CpuLoad(_handle); } }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.Destroy(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_handle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(NotaEngine));
    }

    private static void Check(NativeMethods.NotaResult result)
    {
        if (result != NativeMethods.NotaResult.Ok)
            throw new NotaEngineException(result.ToString());
    }
}
