// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One wiring for every send control (specs/069, #143): the arrangement header's knobs, the
// Mixer window's and Session mixer's faders and the Devices panel's Sends card. A send is a
// live level only — not an undo step and not an automation target — so this writes
// SetTrackSend and nothing else, whichever surface moves it.

using System;
using Nota.Application;

namespace Nota.App;

internal static class SendControl
{
    /// <summary>Wire a knob: drag, reset and typed values set the send to <paramref name="bus"/>;
    /// <paramref name="changed"/> runs after each value (e.g. to update a read-out).</summary>
    public static void Attach(Knob knob, IAudioEngine engine, int trackId, int bus, Action<double>? changed = null)
        => knob.ValueChanged += v => { engine.SetTrackSend(trackId, bus, (float)v); changed?.Invoke(v); };

    /// <summary>Wire a mini fader the same way.</summary>
    public static void Attach(MiniFader fader, IAudioEngine engine, int trackId, int bus, Action<double>? changed = null)
        => fader.ValueChanged += v => { engine.SetTrackSend(trackId, bus, (float)v); changed?.Invoke(v); };
}
