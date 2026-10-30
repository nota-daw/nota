// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The status bar's audio format readout and buffer menu (#373, specs/061): "48 kHz · 256",
// the buffer sizes Nota offers (shared with Settings → Audio), and who decides the buffer.

using System.Globalization;

namespace Nota.Presentation;

/// <summary>Who sets the buffer size of the running device.</summary>
public enum BufferOwner
{
    /// <summary>Nota asks for it (shared WASAPI, Core Audio, PulseAudio / ALSA).</summary>
    Nota,
    /// <summary>The ASIO driver, in its own settings panel.</summary>
    Driver,
    /// <summary>The device's native period (WASAPI exclusive mode).</summary>
    Device,
}

public static class AudioFormatStatus
{
    /// <summary>Buffer sizes in frames; 0 is "device default".</summary>
    public static readonly int[] BufferSizes = { 0, 64, 128, 256, 512, 1024, 2048 };

    private const string Thin = "\u2009";

    /// <summary>"48 kHz · 256" — the negotiated rate and block; just the rate while no
    /// block is known (no device running).</summary>
    public static string Text(double sampleRate, int bufferFrames)
    {
        string rate = (sampleRate / 1000).ToString("0.#", CultureInfo.InvariantCulture) + Thin + "kHz";
        return bufferFrames > 0 ? $"{rate} · {bufferFrames}" : rate;
    }

    /// <summary>One block's latency, "5.3 ms"; empty when the rate is unknown.</summary>
    public static string Latency(double sampleRate, int frames)
        => sampleRate > 0 && frames > 0
            ? (frames / sampleRate * 1000).ToString("0.0", CultureInfo.InvariantCulture) + Thin + "ms"
            : "";

    public static BufferOwner Owner(bool asioRunning, bool exclusiveRunning)
        => asioRunning ? BufferOwner.Driver : exclusiveRunning ? BufferOwner.Device : BufferOwner.Nota;
}
