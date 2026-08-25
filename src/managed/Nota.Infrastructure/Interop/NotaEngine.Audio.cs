// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- audio device settings (M7-1) ---

    /// <summary>Re-scans connected CoreAudio devices and returns the playback-capable ones.</summary>
    public static IReadOnlyList<AudioDevice> AudioOutputDevices()
    {
        NativeMethods.AudioRefreshDevices();
        int n = NativeMethods.AudioOutputDeviceCount();
        var list = new List<AudioDevice>(n);
        for (int i = 0; i < n; i++)
            list.Add(new AudioDevice(
                Marshal.PtrToStringUTF8(NativeMethods.AudioOutputDeviceUid(i)) ?? "",
                Marshal.PtrToStringUTF8(NativeMethods.AudioOutputDeviceName(i)) ?? ""));
        return list;
    }

    /// <summary>Re-scans connected CoreAudio devices and returns the capture-capable ones.</summary>
    public static IReadOnlyList<AudioDevice> AudioInputDevices()
    {
        NativeMethods.AudioRefreshDevices();
        int n = NativeMethods.AudioInputDeviceCount();
        var list = new List<AudioDevice>(n);
        for (int i = 0; i < n; i++)
            list.Add(new AudioDevice(
                Marshal.PtrToStringUTF8(NativeMethods.AudioInputDeviceUid(i)) ?? "",
                Marshal.PtrToStringUTF8(NativeMethods.AudioInputDeviceName(i)) ?? ""));
        return list;
    }

    /// <summary>Stages the output device (UID; "" = system default). Call <see cref="ApplyAudio"/> to apply.</summary>
    public void SetAudioOutputDevice(string uid)
    { ThrowIfDisposed(); Check(NativeMethods.AudioSetOutputDevice(_handle, uid ?? "")); }

    /// <summary>Stages the input device (UID; "" = system default). Call <see cref="ApplyAudio"/> to apply.</summary>
    public void SetAudioInputDevice(string uid)
    { ThrowIfDisposed(); Check(NativeMethods.AudioSetInputDevice(_handle, uid ?? "")); }

    /// <summary>Stages the sample rate (0 = device default). Call <see cref="ApplyAudio"/> to apply.</summary>
    public void SetAudioSampleRate(double sampleRate)
    { ThrowIfDisposed(); Check(NativeMethods.AudioSetSampleRate(_handle, sampleRate)); }

    /// <summary>Stages the buffer size in frames (0 = device default). Call <see cref="ApplyAudio"/> to apply.</summary>
    public void SetAudioBufferFrames(int frames)
    { ThrowIfDisposed(); Check(NativeMethods.AudioSetBufferFrames(_handle, frames)); }

    /// <summary>Stages WASAPI exclusive mode (Windows only; ignored on other platforms).
    /// Call <see cref="ApplyAudio"/> to apply. When enabled, Nota opens the output
    /// device exclusively — other apps can't play through it, and the device's
    /// native sample rate / buffer size override any requested values.</summary>
    public void SetAudioWasapiExclusive(bool on)
    { ThrowIfDisposed(); Check(NativeMethods.AudioSetWasapiExclusive(_handle, on ? 1 : 0)); }

    /// <summary>Currently staged audio config (output UID, input UID, sample rate, buffer frames,
    /// WASAPI exclusive flag).</summary>
    public (string OutputUid, string InputUid, double SampleRate, int BufferFrames, bool WasapiExclusive) GetAudioConfig()
    {
        ThrowIfDisposed();
        return (
            Marshal.PtrToStringUTF8(NativeMethods.AudioOutputDevice(_handle)) ?? "",
            Marshal.PtrToStringUTF8(NativeMethods.AudioInputDevice(_handle)) ?? "",
            NativeMethods.AudioSampleRate(_handle),
            NativeMethods.AudioBufferFrames(_handle),
            NativeMethods.AudioWasapiExclusive(_handle) != 0);
    }

    /// <summary>Persists the staged config and restarts the backend. Throws on device failure.</summary>
    public void ApplyAudio()
    { ThrowIfDisposed(); Check(NativeMethods.AudioApply(_handle)); }

    /// <summary>Negotiated sample rate after the backend started (0 if not running).</summary>
    public double NegotiatedSampleRate
    { get { ThrowIfDisposed(); return NativeMethods.AudioNegotiatedSampleRate(_handle); } }

    /// <summary>Negotiated buffer size in frames after the backend started (0 if unknown).</summary>
    public int NegotiatedBufferFrames
    { get { ThrowIfDisposed(); return NativeMethods.AudioNegotiatedBufferFrames(_handle); } }

    /// <summary>True if the last <see cref="ApplyAudio"/> requested WASAPI exclusive mode
    /// but the device refused it and the backend fell back to shared mode. Always false
    /// on platforms without exclusive mode. Read after ApplyAudio() to warn the user.</summary>
    public bool AudioExclusiveFallback
    { get { ThrowIfDisposed(); return NativeMethods.AudioExclusiveFallback(_handle) != 0; } }

    // --- MIDI device settings (M7-2) ---

    /// <summary>Re-scans connected MIDI input sources.</summary>
    public static IReadOnlyList<MidiDevice> MidiInputDevices()
    {
        NativeMethods.MidiRefreshDevices();
        int n = NativeMethods.MidiInputDeviceCount();
        var list = new List<MidiDevice>(n);
        for (int i = 0; i < n; i++)
            list.Add(new MidiDevice(
                Marshal.PtrToStringUTF8(NativeMethods.MidiInputDeviceUid(i)) ?? "",
                Marshal.PtrToStringUTF8(NativeMethods.MidiInputDeviceName(i)) ?? ""));
        return list;
    }

    /// <summary>Whether the MIDI input with <paramref name="uid"/> is active in the staged config.</summary>
    public bool IsMidiInputEnabled(string uid)
    { ThrowIfDisposed(); return NativeMethods.MidiInputEnabled(_handle, uid ?? "") != 0; }

    /// <summary>Stages whether the MIDI input is active. Call <see cref="ApplyMidi"/> to apply.</summary>
    public void SetMidiInputEnabled(string uid, bool enabled)
    { ThrowIfDisposed(); Check(NativeMethods.MidiSetInputEnabled(_handle, uid ?? "", enabled ? 1 : 0)); }

    /// <summary>Persists the staged MIDI selection and reconnects the MIDI port.</summary>
    public void ApplyMidi()
    { ThrowIfDisposed(); Check(NativeMethods.MidiApply(_handle)); }

    /// <summary>MIDI-learn: drains incoming CC/note-on events into <paramref name="quadBuffer"/>
    /// (4 int32 per event: kind, channel, number, value) and returns the event count.
    /// The buffer is caller-owned and reused each tick to avoid per-frame allocation.</summary>
    public int PollMidiControlEvents(int[] quadBuffer)
    {
        ThrowIfDisposed();
        if (quadBuffer is null || quadBuffer.Length < 4) return 0;
        return NativeMethods.MidiPollControlEvents(_handle, quadBuffer, quadBuffer.Length / 4);
    }
}
