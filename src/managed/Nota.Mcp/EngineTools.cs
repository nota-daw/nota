// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Base for every MCP tool group: holds the shared engine + dispatch + refresh, and gives two
// helpers so a tool body stays one line — Read (query on the UI thread) and Mutate (edit on the
// UI thread, then refresh the UI). Exceptions are turned into a readable string rather than
// thrown across the transport.

using Nota.Application;

namespace Nota.Mcp;

public abstract class EngineTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
{
    protected readonly IAudioEngine E = engine;
    private readonly IEngineDispatch _d = dispatch;
    private readonly IArrangementRefresh _r = refresh;

    /// <summary>Run a read on the engine's UI thread and return the value.</summary>
    protected Task<T> Read<T>(Func<T> func) => _d.InvokeAsync(func);

    /// <summary>Run an edit on the UI thread, then refresh the arrangement/device panel.</summary>
    protected Task Mutate(Action action) => _d.InvokeAsync(() => { action(); _r.Refresh(); });

    /// <summary>Run an edit that returns a value on the UI thread, then refresh.</summary>
    protected Task<T> Mutate<T>(Func<T> func) => _d.InvokeAsync(() => { var v = func(); _r.Refresh(); return v; });

    /// <summary>Resolve a track id to its enumeration index (0..TrackCount-1), or -1. UI thread.</summary>
    protected int IndexOfTrack(int trackId)
    {
        int n = E.TrackCount;
        for (int i = 0; i < n; i++) if (E.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return i;
        return -1;
    }

    protected static string TypeName(int type) => type switch { 1 => "instrument", 2 => "return", 3 => "group", _ => "audio" };
}
