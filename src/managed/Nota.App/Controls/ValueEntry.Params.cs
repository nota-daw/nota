// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Fields over engine parameters for the graph controls of device cards (specs/035): the
// owner knows the parameter and its read-out, the graph knows where its handles are.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Nota.Application;

namespace Nota.App;

internal static partial class ValueEntry
{
    /// <summary>A device (audio / MIDI effect) parameter: the same setter and automation gesture a drag uses.</summary>
    public static ValueField DeviceParam(IAudioEngine e, int track, int di, int p, string name, Func<double, string> text, Action? after = null)
        => new(name, () => e.DeviceGetParam(track, di, p), text, v => e.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1)),
            () => e.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
            () => e.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
            e.DeviceParamDefault(track, di, p)) { After = after };

    /// <summary>A device parameter held in its own units over <paramref name="lo"/>…<paramref name="hi"/>
    /// (EQ gains in dB, …): the field's position runs linearly over that range.</summary>
    public static ValueField DeviceRaw(IAudioEngine e, int track, int di, int p, string name, double lo, double hi, Func<double, string> text, Action? after = null)
        => new(name, () => (e.DeviceGetParam(track, di, p) - lo) / (hi - lo), n => text(lo + Math.Clamp(n, 0, 1) * (hi - lo)),
            n => e.DeviceSetParam(track, di, p, (float)(lo + Math.Clamp(n, 0, 1) * (hi - lo))),
            () => e.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
            () => e.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
            (e.DeviceParamDefault(track, di, p) - lo) / (hi - lo)) { After = after };

    /// <summary>An instrument parameter (index <paramref name="pi"/>, id <paramref name="id"/>).</summary>
    public static ValueField InstrumentParam(IAudioEngine e, int track, int pi, string id, string name, Func<double, string> text, Action? after = null)
        => new(name, () => e.PluginParamGet(track, -1, pi), text, v => e.PluginParamSet(track, -1, pi, (float)Math.Clamp(v, 0, 1)),
            () => e.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id),
            () => e.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id),
            e.PluginParamDefault(track, -1, pi)) { After = after };

    /// <summary>A graph that is one handle all over (a drag anywhere moves it): ⌥-click anywhere
    /// types <paramref name="fields"/>, in order; the box sits at the top middle.</summary>
    public static void AttachAll(Control view, params ValueField[] fields)
        => AttachHost(view, new Whole(view, fields));

    private sealed class Whole(Control view, IReadOnlyList<ValueField> fields) : IValueEntryHost
    {
        public IReadOnlyList<ValueField>? FieldsAt(Point p) => fields;
        public Rect AnchorOf(ValueField field) => Near(new Point(view.Bounds.Width / 2, 0), view.Bounds.Size);
        public IReadOnlyList<ValueField> AllFields => fields;
    }
}
