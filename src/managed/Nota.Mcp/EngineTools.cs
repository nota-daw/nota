// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Base for every MCP tool group: holds the shared engine + dispatch + refresh, and gives two
// helpers so a tool body stays one line — Read (query on the UI thread) and Mutate (edit on the
// UI thread, then refresh the UI). Exceptions are turned into a readable string rather than
// thrown across the transport. A Mutate is one undo group, so a tool call undoes as one step
// whatever it changed (specs/016); parameter writes go through DevParam / MidiParam /
// PlugParam so a plain value change lands in that step too.

using Nota.Application;
using Nota.Application.Editing;

namespace Nota.Mcp;

public abstract class EngineTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
{
    protected readonly IAudioEngine E = engine;
    private readonly IEngineDispatch _d = dispatch;
    private readonly IArrangementRefresh _r = refresh;

    /// <summary>Run a read on the engine's UI thread and return the value.</summary>
    protected Task<T> Read<T>(Func<T> func) => _d.InvokeAsync(func);

    /// <summary>Run an edit on the UI thread, then refresh the arrangement/device panel.</summary>
    protected Task Mutate(Action action) => _d.InvokeAsync(() =>
    {
        E.BeginUndoGroup();
        try { action(); }
        finally { E.EndUndoGroup(); }
        _r.Refresh();
    });

    /// <summary>Run an edit that returns a value on the UI thread, then refresh.</summary>
    protected Task<T> Mutate<T>(Func<T> func) => _d.InvokeAsync(() =>
    {
        T v;
        E.BeginUndoGroup();
        try { v = func(); }
        finally { E.EndUndoGroup(); }
        _r.Refresh();
        return v;
    });

    /// <summary>Set a built-in audio effect's parameter as an undoable value change.</summary>
    protected void DevParam(int trackId, int deviceIndex, int paramIndex, float value)
        => MixEdit.Step(E, () => E.DeviceSetParam(trackId, deviceIndex, paramIndex, value), MixTarget.DeviceParam(trackId, deviceIndex, paramIndex));

    /// <summary>Set a MIDI effect's parameter as an undoable value change.</summary>
    protected void MidiParam(int trackId, int index, int paramIndex, float value)
        => MixEdit.Step(E, () => E.MidiEffectSetParam(trackId, index, paramIndex, value), MixTarget.MidiDeviceParam(trackId, index, paramIndex));

    /// <summary>Set a normalized plug-in / instrument parameter (deviceIndex -1 = the instrument)
    /// as an undoable value change. A hosted plug-in that hasn't loaded has no parameters yet and
    /// would drop the write (specs/113), so that is an error, not a silent success; tools that
    /// write a hosted plug-in's params call <see cref="SettlePluginLoadsAsync"/> first.</summary>
    protected void PlugParam(int trackId, int deviceIndex, int paramIndex, float value)
    {
        RequireLoaded(trackId, deviceIndex);
        MixEdit.Step(E, () => E.PluginParamSet(trackId, deviceIndex, paramIndex, value), MixTarget.PluginParam(trackId, deviceIndex, paramIndex));
    }

    /// <summary>Throws a readable error when a hosted plug-in (deviceIndex -1 = the instrument) is
    /// still loading or couldn't load: it has no parameters yet. Built-ins always pass. UI thread.</summary>
    protected void RequireLoaded(int trackId, int deviceIndex)
    {
        var state = E.DevicePluginLoadState(trackId, deviceIndex);
        if (state == PluginLoadState.Loaded) return;
        throw new InvalidOperationException(state == PluginLoadState.Failed
            ? "the plug-in couldn't load, so it has no parameters to set (Try again on its card)"
            : "the plug-in is still loading; set its parameters once it has loaded");
    }

    /// <summary>
    /// Hosted plug-ins load after they are inserted (specs/113). Gives the ones still loading up to
    /// 10 s to land, one service pass per UI tick with this (MCP) thread waiting in between, so the
    /// window stays responsive. Returns how many still load. Call it before an export (the render
    /// holds the UI thread, so nothing attaches during it) and before writing a hosted plug-in's
    /// parameters.
    /// </summary>
    protected async Task<int> SettlePluginLoadsAsync()
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        int left = await Read(() => E.WaitPluginLoads(0));
        while (left > 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(33);
            left = await Read(() => E.WaitPluginLoads(0));
        }
        return left;
    }

    /// <summary>Resolve a track id to its enumeration index (0..TrackCount-1), or -1. UI thread.</summary>
    protected int IndexOfTrack(int trackId)
    {
        int n = E.TrackCount;
        for (int i = 0; i < n; i++) if (E.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return i;
        return -1;
    }

    protected static string TypeName(int type) => type switch { 1 => "instrument", 2 => "return", 3 => "group", _ => "audio" };
}
