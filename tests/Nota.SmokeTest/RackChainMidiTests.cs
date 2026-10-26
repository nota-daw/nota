// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI effects inside Instrument Rack chains (specs/040-racks-redesign, US6, issue #329):
// a chain runs MIDI effects → instrument → audio effects, sample-accurate and on the same
// clock as a track's MIDI effects, and keeps them through save, presets, copy and undo.
// Runs with the other rack sections: `dotnet run --project tests/Nota.SmokeTest -- --racks`.

namespace Nota.SmokeTest;

internal static class RackChainMidiTests
{
    /// <summary>The smoke-test sections, in run order (filled in by the US6 tasks).</summary>
    public static IEnumerable<(string title, Func<IEnumerable<(bool ok, string label)>> run)> Sections()
    {
        yield break;
    }
}
