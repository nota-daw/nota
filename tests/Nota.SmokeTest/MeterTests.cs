// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Bars follow the time signature (#405): the transport's bar.beat readout counts a bar as the
// numerator in beats — what the metronome clicks and the arrangement grid now draws — instead
// of a fixed four.

using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class MeterTests
{
    private const int Chunk = 256;
    private const double Sr = 48000.0;

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        var vm = new TransportViewModel(e);
        e.Seek(3.0); Apply(e); vm.Tick();
        yield return (vm.PositionText == "1.4.00", "meter: beat 3 reads 1.4 in 4/4");

        vm.TimeSigNumerator = 3;
        yield return (vm.PositionText == "2.1.00", "meter: changing to 3/4 re-counts the readout at once (2.1)");
        e.Seek(7.5); Apply(e); vm.Tick();
        yield return (vm.PositionText == "3.2.50", "meter: beat 7.5 reads 3.2.50 in 3/4");

        vm.TimeSigNumerator = 5;
        yield return (vm.PositionText == "2.3.50", "meter: …and 2.3.50 in 5/4");
    }

    // Stand-in for the audio thread: one render drains the queued commands.
    private static void Apply(NotaEngine e) => e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
}
