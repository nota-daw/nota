// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The transport bar follows a tempo set outside the UI (MCP set_tempo, .specify/bugs/
// mcp-tempo-ui-sync), so the BPM field shows it and saving keeps it. SetBpm only queues the
// change for the audio thread; here RenderOffline stands in for that thread, so these pin
// both directions: an outside tempo reaches the view-model, and the field's own change is
// never snapped back by a Tick that still reads the old tempo.

using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class TempoFollowTests
{
    private const int Chunk = 256;
    private const double Sr = 48000.0;

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        var vm = new TransportViewModel(e) { Bpm = 120 };
        Apply(e); vm.Tick();

        // An outside tempo (what MCP set_tempo does) reaches the view-model on the next tick.
        e.SetBpm(90); Apply(e); vm.Tick();
        yield return (vm.Bpm == 90m, "an outside tempo change reaches the BPM field");

        // The field's resolution is hundredths, so an outside 110.333 shows as 110.33.
        e.SetBpm(110.333); Apply(e); vm.Tick();
        yield return (vm.Bpm == 110.33m, "…rounded to the field's hundredths");
        Apply(e); vm.Tick();   // following never writes back: the engine keeps its exact tempo
        yield return (Math.Abs(e.Bpm - 110.333) < 1e-9 && vm.Bpm == 110.33m, "…without echoing the rounded value back to the engine");

        // Two outside changes in a row (a controller knob turning): both are followed.
        e.SetBpm(95); Apply(e); vm.Tick();
        e.SetBpm(96); Apply(e); vm.Tick();
        yield return (vm.Bpm == 96m, "consecutive outside changes are each followed");

        // The user's own change is in flight until the engine applies it: a tick that still
        // reads the old tempo must not snap the field back.
        vm.Bpm = 121; vm.Tick();
        yield return (vm.Bpm == 121m, "a tick before the engine applies the field's tempo keeps it");
        Apply(e); vm.Tick();
        yield return (vm.Bpm == 121m && Math.Abs(e.Bpm - 121) < 1e-9, "…and once applied, field and engine agree");

        // A fast drag: several sends queue up before the engine catches up; ticks in between
        // must keep the latest value, not the tempo the engine still reports.
        vm.Bpm = 122; vm.Bpm = 123; vm.Bpm = 124; vm.Tick();
        yield return (vm.Bpm == 124m, "a drag ahead of the engine keeps the latest value");
        Apply(e); vm.Tick();
        yield return (vm.Bpm == 124m && Math.Abs(e.Bpm - 124) < 1e-9, "…and lands on it");

        // After the field's change has landed, an outside change is followed again.
        e.SetBpm(100); Apply(e); vm.Tick();
        yield return (vm.Bpm == 100m, "outside changes are followed again after the field's own lands");
    }

    // Stand-in for the audio thread: one render drains the queued commands.
    private static void Apply(NotaEngine e) => e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
}
