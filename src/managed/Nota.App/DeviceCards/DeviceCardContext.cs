// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the seam a device-card builder is handed instead of reaching
// into DeviceChainView's private state. It carries the engine + current track and
// exposes the live-refresh registry and orchestration callbacks as methods, so the
// underlying List<Action>/event wiring (and its 60 Hz tick contract) stays owned by
// the view. Card strategies stay stateless; all per-rebuild state lives here.

using System;
using Avalonia.Controls;
using Nota.Application;

namespace Nota.App;

internal sealed class DeviceCardContext(
    IAudioEngine engine,
    int trackId,
    Action<Action> addDeviceRefresher,
    Action notifyChanged,
    Action<int, Knob, TextBlock, Func<float, string>?> addInstFader,
    Action<Action?> setInstLiveViz,
    Action<int> requestPresetSave,
    Func<int> getSelectedChain,
    Action<int> setSelectedChain,
    Action<Action> addRackParamRefresher,
    Action invokeRackParamRefreshers,
    Action requestRebuild,
    Action<int> requestRackPresetSave,
    Action hideDropGlow,
    IFactoryPresets? factory = null)
{
    public IAudioEngine Engine => engine;
    public int TrackId => trackId;

    /// <summary>The shipped preset library — rack chain full-UI popups list their kind's presets from it.</summary>
    public IFactoryPresets? Factory => factory;

    /// <summary>Register a closure invoked on every UI tick (compressor GR meter, param
    /// faders following automation, …). Kept as a plain list append — no per-frame cost.</summary>
    public void AddDeviceRefresher(Action refresher) => addDeviceRefresher(refresher);

    /// <summary>Signal a change that affects track headers / timeline (e.g. sidechain source).</summary>
    public void NotifyChanged() => notifyChanged();

    /// <summary>Register a built-in-instrument knob for automation live-follow: the tick
    /// re-reads plugin-param <paramref name="paramIndex"/> and moves the knob (unless dragged).</summary>
    public void AddInstFader(int paramIndex, Knob knob, TextBlock value, Func<float, string>? fmt = null) => addInstFader(paramIndex, knob, value, fmt);

    /// <summary>Set the single closure that redraws the active instrument's live graphs
    /// (synth ADSR/filter, physical glyph, aurora morph, volt). Null for device-only cards.</summary>
    public void SetInstLiveViz(Action? viz) => setInstLiveViz(viz);

    /// <summary>Request saving this device/instrument as a preset (index -1 = the instrument).</summary>
    public void RequestPresetSave(int index) => requestPresetSave(index);

    // ---- rack / drum-rack seam --------------------------------------------

    /// <summary>The rack chain whose device area is shown; persists across rebuilds.</summary>
    public int SelectedChain { get => getSelectedChain(); set => setSelectedChain(value); }

    /// <summary>Register a chain param fader so it follows its mapped macro (and automation)
    /// on the UI tick; also invoked immediately when a macro is turned.</summary>
    public void AddRackParamRefresher(Action refresher) => addRackParamRefresher(refresher);

    /// <summary>Run all rack param refreshers now (a macro moved → mapped faders follow live).</summary>
    public void InvokeRackParamRefreshers() => invokeRackParamRefreshers();

    /// <summary>Rebuild the whole card row (after a structural change: add/remove/reorder/select).</summary>
    public void RequestRebuild() => requestRebuild();

    /// <summary>Request saving a rack chain's instrument as a preset (arg is the chain index).</summary>
    public void RequestRackPresetSave(int chain) => requestRackPresetSave(chain);

    /// <summary>Hide the panel-wide drag glow (a per-chain / per-pad target owns the highlight).</summary>
    public void HideDropGlow() => hideDropGlow();
}
