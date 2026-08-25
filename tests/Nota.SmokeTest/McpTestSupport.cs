// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Test doubles for exercising the MCP tool classes over a real NotaEngine without Kestrel or a
// UI thread: dispatch runs synchronously, refresh is a no-op.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nota.SmokeTest;

internal sealed class SyncDispatch : Nota.Mcp.IEngineDispatch
{
    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
}

internal sealed class NoRefresh : Nota.Mcp.IArrangementRefresh
{
    public void Refresh() { }
}

/// <summary>In-memory MIDI-learn facade so the MCP MIDI tools can be exercised without the app.</summary>
internal sealed class FakeMidiLearn : Nota.Mcp.IMidiLearnAccess
{
    private readonly List<Nota.Mcp.MidiMappingInfo> _m = new();
    private readonly List<Nota.Mcp.MidiControlSeen> _seen = new();
    public bool Armed { get; set; }
    public void Seed(Nota.Mcp.MidiMappingInfo m) => _m.Add(m);
    public void SeedSeen(Nota.Mcp.MidiControlSeen s) => _seen.Add(s);
    public IReadOnlyList<Nota.Mcp.MidiMappingInfo> Mappings() => _m;
    public bool RemoveMapping(int i) { if (i < 0 || i >= _m.Count) return false; _m.RemoveAt(i); return true; }
    public void ClearMappings() => _m.Clear();
    public bool SetMappingRange(int i, double min, double max, bool inv)
    { if (i < 0 || i >= _m.Count) return false; _m[i] = _m[i] with { RangeMin = min, RangeMax = max, Invert = inv }; return true; }
    public IReadOnlyList<Nota.Mcp.MidiControlSeen> RecentControls() => _seen;
    public void ClearRecentControls() => _seen.Clear();
}
