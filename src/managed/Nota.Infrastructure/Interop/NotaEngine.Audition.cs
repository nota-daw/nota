// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- Preset audition -----------------------------------------------------

    /// <summary>A standalone offline chain at the engine's rate (any thread; no engine state).</summary>
    public IAuditionRig CreateAuditionRig()
    {
        ThrowIfDisposed();
        double sr = NativeMethods.SampleRate(_handle);
        sr = sr > 0 ? sr : 48000;
        return new AuditionRig(NativeMethods.AuditionCreate(sr), sr);
    }

    public void StoreAudition(IAuditionRig rig, string key)
    {
        ThrowIfDisposed();
        if (rig is not AuditionRig r) throw new ArgumentException("Not a NotaEngine audition.", nameof(rig));
        NativeMethods.AuditionStore(_handle, r.Handle, key);
    }

    public bool IsAuditionCached(string key)
    { ThrowIfDisposed(); return NativeMethods.AuditionCached(_handle, key) != 0; }

    public bool PreviewAuditionAt(string key, double startSeconds)
    { ThrowIfDisposed(); return NativeMethods.PreviewCachedAt(_handle, key, startSeconds) != 0; }

    private sealed class AuditionRig : IAuditionRig
    {
        private IntPtr _h;
        private readonly double _sr;
        public AuditionRig(IntPtr h, double sr) { _h = h; _sr = sr; }

        public IAuditionRig CreatePart() => new AuditionRig(NativeMethods.AuditionCreate(_sr), _sr);

        internal IntPtr Handle => _h != IntPtr.Zero ? _h : throw new ObjectDisposedException(nameof(AuditionRig));

        public bool SetInstrument(int kind) => NativeMethods.AuditionSetInstrument(Handle, kind) != 0;
        public bool InstrumentParam(string id, float value) => NativeMethods.AuditionInstrumentParam(Handle, id, value) != 0;
        public int AddDevice(int kind) => NativeMethods.AuditionAddDevice(Handle, kind);
        public bool DeviceParam(int index, string name, float value) => NativeMethods.AuditionDeviceParam(Handle, index, name, value) != 0;
        public int AddMidiEffect(int kind) => NativeMethods.AuditionAddMidiEffect(Handle, kind);
        public bool MidiParam(int index, string name, float value) => NativeMethods.AuditionMidiParam(Handle, index, name, value) != 0;
        public bool SetSamplerSample(string? path) => NativeMethods.AuditionSetSamplerSample(Handle, path) != 0;
        public bool AddSourceFrom(IAuditionRig part, float gain)
            => part is AuditionRig p && NativeMethods.AuditionAddSourceFrom(Handle, p.Handle, gain) != 0;
        public bool UseCachedSource(string key) => NativeMethods.AuditionUseCachedSource(Handle, key) != 0;
        public bool CacheSource(string key, float targetPeak) => NativeMethods.AuditionCacheSource(Handle, key, targetPeak) != 0;
        public bool UseKit() => NativeMethods.AuditionUseKit(Handle) != 0;
        public int KitAddPad(int note, string path, float gain, float pan, int choke)
            => NativeMethods.AuditionKitAddPad(Handle, note, path, gain, pan, choke);
        public int KitPadAddDevice(int pad, int kind) => NativeMethods.AuditionKitPadAddDevice(Handle, pad, kind);
        public bool KitPadDeviceParam(int pad, int device, string name, float value)
            => NativeMethods.AuditionKitPadDeviceParam(Handle, pad, device, name, value) != 0;
        public bool SetSourceFile(string path, double maxSeconds) => NativeMethods.AuditionSetSourceFile(Handle, path, maxSeconds) != 0;

        public long Render(IReadOnlyList<AuditionNote> notes, double bpm, double phraseBeats, double maxTailSeconds, bool rolling)
        {
            var arr = new NativeMethods.NotaAuditionNote[notes.Count];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = new NativeMethods.NotaAuditionNote
                {
                    StartBeat = notes[i].StartBeat, LengthBeats = notes[i].LengthBeats,
                    Pitch = notes[i].Pitch, Velocity = notes[i].Velocity,
                };
            return NativeMethods.AuditionRender(Handle, arr, arr.Length, bpm, phraseBeats, maxTailSeconds, rolling ? 1 : 0);
        }

        // Cancel comes from another thread while Render runs; the lock keeps it off a rig
        // that Dispose is freeing at the same moment.
        private readonly object _gate = new();

        public void Cancel()
        {
            lock (_gate) if (_h != IntPtr.Zero) NativeMethods.AuditionCancel(_h);
        }

        public int ReadPeaks(float[] outMinMax, int maxPoints)
            => NativeMethods.AuditionPeaks(Handle, outMinMax, Math.Min(maxPoints, outMinMax.Length / 2));

        public double Seconds => NativeMethods.AuditionSeconds(Handle);

        public void Dispose()
        {
            lock (_gate)
            {
                if (_h == IntPtr.Zero) return;
                NativeMethods.AuditionDestroy(_h);
                _h = IntPtr.Zero;
            }
        }
    }
}
