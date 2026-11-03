// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One wiring for every send control (specs/072): the arrangement header's knobs, the Mixer
// window's and Session mixer's faders and the Devices panel's Sends card. A drag is one undo
// step (a value gesture, specs/009) and writes the send's automation lane while recording —
// or overrides it while not — exactly as the volume and pan controls do, so a send behaves
// the same whichever surface moves it.

using System;
using Nota.Application;
using Nota.Application.Editing;

namespace Nota.App;

internal static class SendControl
{
    /// <summary>Wire a knob: drag, reset and typed values set the send to <paramref name="bus"/>;
    /// <paramref name="changed"/> runs after each value (e.g. to update a read-out).</summary>
    public static void Attach(Knob knob, IAudioEngine engine, int trackId, int bus, Action<double>? changed = null)
    {
        var g = new Gesture(engine, trackId, bus);
        knob.GestureBegin += g.Begin;
        knob.ValueChanged += v => { g.Change(v); changed?.Invoke(v); };
        knob.GestureEnd += g.End;
    }

    /// <summary>Wire a mini fader the same way.</summary>
    public static void Attach(MiniFader fader, IAudioEngine engine, int trackId, int bus, Action<double>? changed = null)
    {
        var g = new Gesture(engine, trackId, bus);
        fader.GestureBegin += g.Begin;
        fader.ValueChanged += v => { g.Change(v); changed?.Invoke(v); };
        fader.GestureEnd += g.End;
    }

    // The undo gesture opens before the automation write, so a lane the write creates while
    // recording and the value undo as one step; it closes after it.
    private sealed class Gesture(IAudioEngine engine, int trackId, int bus)
    {
        private readonly MixGesture _undo = new(engine, MixTarget.Send(trackId, bus));
        private readonly Action<double> _set = v => engine.SetTrackSend(trackId, bus, (float)v);

        public void Begin()
        {
            _undo.Begin();
            engine.BeginAutomationWrite(trackId, AutomationTarget.Send, bus, -1, "");
        }

        public void Change(double v) => _undo.Change(v, _set);

        public void End()
        {
            engine.EndAutomationWrite(trackId, AutomationTarget.Send, bus, -1, "");
            _undo.End();
        }
    }
}
