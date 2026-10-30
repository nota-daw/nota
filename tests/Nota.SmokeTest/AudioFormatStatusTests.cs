// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Status bar audio format (#373, specs/055): the "48 kHz · 256" readout, the block latency
// each buffer menu item shows, and who owns the buffer (Nota, the ASIO driver, or the device
// in WASAPI exclusive mode).

using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class AudioFormatStatusTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (AudioFormatStatus.Text(48000, 256) == "48\u2009kHz · 256", "48 kHz at 256 frames reads \"48 kHz · 256\"");
        yield return (AudioFormatStatus.Text(44100, 512) == "44.1\u2009kHz · 512", "44.1 kHz keeps its decimal");
        yield return (AudioFormatStatus.Text(96000, 0) == "96\u2009kHz", "no running block shows the rate alone");

        yield return (AudioFormatStatus.Latency(48000, 256) == "5.3\u2009ms", "256 frames at 48 kHz is 5.3 ms");
        yield return (AudioFormatStatus.Latency(44100, 0) == "" && AudioFormatStatus.Latency(0, 256) == "",
            "device default or an unknown rate shows no latency");

        var sizes = AudioFormatStatus.BufferSizes;
        yield return (sizes[0] == 0 && sizes.Skip(1).SequenceEqual(new[] { 64, 128, 256, 512, 1024, 2048 }),
            "the menu offers device default, then 64 to 2048 frames");

        yield return (AudioFormatStatus.Owner(false, false) == BufferOwner.Nota, "shared mode: Nota sets the buffer");
        yield return (AudioFormatStatus.Owner(true, false) == BufferOwner.Driver, "ASIO: the driver sets the buffer");
        yield return (AudioFormatStatus.Owner(false, true) == BufferOwner.Device, "WASAPI exclusive: the device sets the buffer");
    }
}
