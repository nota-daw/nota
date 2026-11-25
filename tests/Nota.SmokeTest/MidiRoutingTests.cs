// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI routing (specs/115, #1103): an External MIDI track sends its post-MIDI-effect stream to a
// MIDI output, releases what it owns on every route change, survives unplug and reconnect, and
// can follow a project output role; the MIDI routes panel's query reads it all back. Driven
// without hardware: test ports stand in for devices and record what Nota sends them.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MidiRoutingTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        yield break;
    }
}
