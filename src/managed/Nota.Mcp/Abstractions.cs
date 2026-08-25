// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Mcp;

/// <summary>Marshals engine access onto the UI/message thread that owns <c>IAudioEngine</c>.
/// MCP tool handlers run on Kestrel request threads; every engine call must go through this.
/// Also serializes concurrent MCP calls (single UI thread) — no engine re-entrancy.
/// Implemented by Nota.App via Avalonia's Dispatcher; the tests use a synchronous stub.</summary>
public interface IEngineDispatch
{
    Task<T> InvokeAsync<T>(Func<T> func);
    Task InvokeAsync(Action action);
}

/// <summary>Raised after a mutating tool so the arrangement + device panel redraw and the user
/// sees AI edits live. Nota.App wires this to Timeline/DeviceChain refresh; no-op in tests.</summary>
public interface IArrangementRefresh
{
    void Refresh();
}

/// <summary>One learned MIDI mapping, flattened for the MCP tools.</summary>
public readonly record struct MidiMappingInfo(
    int Index, string Control, string SourceKind, int Channel, int Number,
    double RangeMin, double RangeMax, bool Invert, string Source);

/// <summary>A distinct controller source seen recently (for discovery), value 0..127.</summary>
public readonly record struct MidiControlSeen(string Kind, int Channel, int Number, int LastValue, int Count);

/// <summary>Facade over the app's MIDI-learn service so the MCP tools can inspect connected
/// controllers, view/manage learned mappings, and arm learn — without referencing Nota.App.
/// Implemented in Nota.App over MidiLearnService; every call runs on the engine's UI thread.</summary>
public interface IMidiLearnAccess
{
    /// <summary>Learn mode: mappable controls highlight and a click selects a target to bind.</summary>
    bool Armed { get; set; }
    IReadOnlyList<MidiMappingInfo> Mappings();
    bool RemoveMapping(int index);
    void ClearMappings();
    bool SetMappingRange(int index, double rangeMin, double rangeMax, bool invert);
    IReadOnlyList<MidiControlSeen> RecentControls();
    void ClearRecentControls();
}
