// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Preset audition (a standalone chain rendered offline, played on the preview voice).</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NotaAuditionNote
    {
        public double StartBeat;
        public double LengthBeats;
        public int Pitch;
        public float Velocity;
    }

    [LibraryImport(Lib, EntryPoint = "nota_audition_create")]
    internal static partial IntPtr AuditionCreate(double sampleRate);

    [LibraryImport(Lib, EntryPoint = "nota_audition_destroy")]
    internal static partial void AuditionDestroy(IntPtr rig);

    [LibraryImport(Lib, EntryPoint = "nota_audition_set_instrument")]
    internal static partial int AuditionSetInstrument(IntPtr rig, int kind);

    [LibraryImport(Lib, EntryPoint = "nota_audition_instrument_param", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionInstrumentParam(IntPtr rig, string id, float value);

    [LibraryImport(Lib, EntryPoint = "nota_audition_add_device")]
    internal static partial int AuditionAddDevice(IntPtr rig, int kind);

    [LibraryImport(Lib, EntryPoint = "nota_audition_device_param", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionDeviceParam(IntPtr rig, int index, string name, float value);

    [LibraryImport(Lib, EntryPoint = "nota_audition_add_midi_effect")]
    internal static partial int AuditionAddMidiEffect(IntPtr rig, int kind);

    [LibraryImport(Lib, EntryPoint = "nota_audition_midi_param", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionMidiParam(IntPtr rig, int index, string name, float value);

    [LibraryImport(Lib, EntryPoint = "nota_audition_set_sampler_sample", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionSetSamplerSample(IntPtr rig, string? path, int rootNote);

    [LibraryImport(Lib, EntryPoint = "nota_audition_add_source_from")]
    internal static partial int AuditionAddSourceFrom(IntPtr rig, IntPtr part, float gain);

    [LibraryImport(Lib, EntryPoint = "nota_audition_use_cached_source", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionUseCachedSource(IntPtr rig, string key);

    [LibraryImport(Lib, EntryPoint = "nota_audition_cache_source", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionCacheSource(IntPtr rig, string key, float targetPeak);

    [LibraryImport(Lib, EntryPoint = "nota_audition_use_kit")]
    internal static partial int AuditionUseKit(IntPtr rig);

    [LibraryImport(Lib, EntryPoint = "nota_audition_kit_add_pad", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionKitAddPad(IntPtr rig, int note, string path, float gain, float pan, int choke);

    [LibraryImport(Lib, EntryPoint = "nota_audition_kit_pad_add_device")]
    internal static partial int AuditionKitPadAddDevice(IntPtr rig, int pad, int kind);

    [LibraryImport(Lib, EntryPoint = "nota_audition_kit_pad_device_param", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionKitPadDeviceParam(IntPtr rig, int pad, int device, string name, float value);

    [LibraryImport(Lib, EntryPoint = "nota_audition_set_source_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionSetSourceFile(IntPtr rig, string path, double maxSeconds);

    [LibraryImport(Lib, EntryPoint = "nota_audition_render")]
    internal static partial long AuditionRender(IntPtr rig, [In] NotaAuditionNote[] notes, int count,
                                                double bpm, double phraseBeats, double maxTailSeconds, int flags);

    [LibraryImport(Lib, EntryPoint = "nota_audition_cancel")]
    internal static partial void AuditionCancel(IntPtr rig);

    [LibraryImport(Lib, EntryPoint = "nota_audition_peaks")]
    internal static partial int AuditionPeaks(IntPtr rig, [Out] float[] outMinMax, int maxPoints);

    [LibraryImport(Lib, EntryPoint = "nota_audition_seconds")]
    internal static partial double AuditionSeconds(IntPtr rig);

    [LibraryImport(Lib, EntryPoint = "nota_engine_audition_store", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult AuditionStore(IntPtr engine, IntPtr rig, string key);

    [LibraryImport(Lib, EntryPoint = "nota_engine_audition_cached", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AuditionCached(IntPtr engine, string key);

    [LibraryImport(Lib, EntryPoint = "nota_engine_preview_cached_at", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int PreviewCachedAt(IntPtr engine, string key, double startSeconds);

    [LibraryImport(Lib, EntryPoint = "nota_engine_preview_scope")]
    internal static partial int PreviewScope(IntPtr engine, [Out] float[] output, int n);
}
