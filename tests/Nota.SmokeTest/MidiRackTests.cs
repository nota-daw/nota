// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI Effect Rack (specs/040-racks-redesign, US4): notes routed by key, velocity and select
// zones through chains of MIDI effects, merged into the instrument — routing, no stuck notes
// under edits, and persistence (blob, project, undo, presets, copy).
// Runs with the other rack sections: `dotnet run --project tests/Nota.SmokeTest -- --racks`.

namespace Nota.SmokeTest;

internal static class MidiRackTests
{
    /// <summary>The smoke-test sections, in run order (filled in by the US4 tasks).</summary>
    public static IEnumerable<(string title, Func<IEnumerable<(bool ok, string label)>> run)> Sections()
    {
        yield break;
    }
}
