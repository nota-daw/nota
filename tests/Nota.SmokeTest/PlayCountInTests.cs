// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// #988: the count-in belongs to Record only. The transport bar's Play, Play / Stop (Space) and
// Play / Pause (⇧Space) roll at once even with a count-in set; Record from stop still counts in
// (the engine side is pinned in Program.cs "count-in" and LiveRecordTests).

using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class PlayCountInTests
{
    private const int Chunk = 256;
    private const double Sr = 48000.0;

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        var vm = new TransportViewModel(e) { Bpm = 120 };
        e.SetCountIn(2);
        Apply(e); vm.Tick();

        vm.PlayCommand.Execute(null); Apply(e);
        yield return (e.IsPlaying && e.CountInBeats == 0, "#988: Play rolls at once with a count-in set");
        vm.StopCommand.Execute(null); Apply(e);

        vm.PlayStopCommand.Execute(null); Apply(e);
        yield return (e.IsPlaying && e.CountInBeats == 0, "#988: Play / Stop rolls at once with a count-in set");
        vm.PlayStopCommand.Execute(null); Apply(e);
        yield return (!e.IsPlaying, "#988: …and the second press stops");

        vm.PlayPauseCommand.Execute(null); Apply(e);
        yield return (e.IsPlaying && e.CountInBeats == 0, "#988: Play / Pause rolls at once with a count-in set");
        vm.StopCommand.Execute(null); Apply(e);
    }

    private static void Apply(NotaEngine e) => e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
}
