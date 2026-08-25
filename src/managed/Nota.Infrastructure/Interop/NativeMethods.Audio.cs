// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Audio (M7-1) and MIDI (M7-2) device settings.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_audio_record_selftest")]
    internal static partial int AudioRecordSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_refresh_devices")]
    internal static partial void AudioRefreshDevices();

    [LibraryImport(Lib, EntryPoint = "nota_audio_output_device_count")]
    internal static partial int AudioOutputDeviceCount();

    [LibraryImport(Lib, EntryPoint = "nota_audio_input_device_count")]
    internal static partial int AudioInputDeviceCount();

    [LibraryImport(Lib, EntryPoint = "nota_audio_output_device_uid")]
    internal static partial IntPtr AudioOutputDeviceUid(int index);

    [LibraryImport(Lib, EntryPoint = "nota_audio_output_device_name")]
    internal static partial IntPtr AudioOutputDeviceName(int index);

    [LibraryImport(Lib, EntryPoint = "nota_audio_input_device_uid")]
    internal static partial IntPtr AudioInputDeviceUid(int index);

    [LibraryImport(Lib, EntryPoint = "nota_audio_input_device_name")]
    internal static partial IntPtr AudioInputDeviceName(int index);

    [LibraryImport(Lib, EntryPoint = "nota_audio_set_output_device", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult AudioSetOutputDevice(IntPtr engine, string uid);

    [LibraryImport(Lib, EntryPoint = "nota_audio_set_input_device", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult AudioSetInputDevice(IntPtr engine, string uid);

    [LibraryImport(Lib, EntryPoint = "nota_audio_set_sample_rate")]
    internal static partial NotaResult AudioSetSampleRate(IntPtr engine, double sampleRate);

    [LibraryImport(Lib, EntryPoint = "nota_audio_set_buffer_frames")]
    internal static partial NotaResult AudioSetBufferFrames(IntPtr engine, int frames);

    [LibraryImport(Lib, EntryPoint = "nota_audio_set_wasapi_exclusive")]
    internal static partial NotaResult AudioSetWasapiExclusive(IntPtr engine, int enabled);

    [LibraryImport(Lib, EntryPoint = "nota_audio_wasapi_exclusive")]
    internal static partial int AudioWasapiExclusive(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_output_device")]
    internal static partial IntPtr AudioOutputDevice(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_input_device")]
    internal static partial IntPtr AudioInputDevice(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_sample_rate")]
    internal static partial double AudioSampleRate(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_buffer_frames")]
    internal static partial int AudioBufferFrames(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_apply")]
    internal static partial NotaResult AudioApply(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_negotiated_sample_rate")]
    internal static partial double AudioNegotiatedSampleRate(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_negotiated_buffer_frames")]
    internal static partial int AudioNegotiatedBufferFrames(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_audio_exclusive_fallback")]
    internal static partial int AudioExclusiveFallback(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_midi_refresh_devices")]
    internal static partial void MidiRefreshDevices();

    [LibraryImport(Lib, EntryPoint = "nota_midi_input_device_count")]
    internal static partial int MidiInputDeviceCount();

    [LibraryImport(Lib, EntryPoint = "nota_midi_input_device_uid")]
    internal static partial IntPtr MidiInputDeviceUid(int index);

    [LibraryImport(Lib, EntryPoint = "nota_midi_input_device_name")]
    internal static partial IntPtr MidiInputDeviceName(int index);

    [LibraryImport(Lib, EntryPoint = "nota_midi_set_input_enabled", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult MidiSetInputEnabled(IntPtr engine, string uid, int enabled);

    [LibraryImport(Lib, EntryPoint = "nota_midi_input_enabled", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int MidiInputEnabled(IntPtr engine, string uid);

    [LibraryImport(Lib, EntryPoint = "nota_midi_apply")]
    internal static partial NotaResult MidiApply(IntPtr engine);

    // Fills `buf` with up to `max` events of 4 int32 each {kind, channel, number, value};
    // returns the event count. Used by MIDI-learn to drain incoming CC/note-on.
    [LibraryImport(Lib, EntryPoint = "nota_midi_poll_control_events")]
    internal static partial int MidiPollControlEvents(IntPtr engine, [Out] int[] buf, int max);
}
