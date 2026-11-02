// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using Avalonia.Controls;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private Window? _playheadFrameOwner;
    private Action<TimeSpan>? _playheadFrameCallback;
    private bool _playheadFramePending;
    private bool _playheadFramesClosed;
    private int _playheadFrameGeneration;

    private Window? VisiblePlayheadWindow()
        => IsVisible && WindowState != WindowState.Minimized ? this
         : _detailWindow is { IsVisible: true, WindowState: not WindowState.Minimized } detail ? detail : null;

    // The 30 Hz service tick wakes this loop. Only the cursor runs on display frames;
    // recording materialization, meters and device polling retain their own cadence.
    private void EnsurePlayheadFrames()
    {
        var vm = _vm;
        Window? owner = !_playheadFramesClosed && vm is { SuspendEnginePolling: false }
            && vm.Engine.IsRunning && vm.Engine.IsPlaying ? VisiblePlayheadWindow() : null;
        if (!ReferenceEquals(owner, _playheadFrameOwner))
        {
            _playheadFrameOwner = owner;
            _playheadFramePending = false;
            int generation = ++_playheadFrameGeneration;
            _playheadFrameCallback = owner is null ? null : _ => OnPlayheadFrame(generation);
        }
        if (owner is null || _playheadFramePending) return;
        _playheadFramePending = true;
        owner.RequestAnimationFrame(_playheadFrameCallback!);
    }

    private void OnPlayheadFrame(int generation)
    {
        if (_playheadFramesClosed || generation != _playheadFrameGeneration) return;
        _playheadFramePending = false;
        var vm = _vm;
        if (vm is null || vm.SuspendEnginePolling) { StopPlayheadFrames(); return; }
        // A minimize, docking or export may have happened since this frame was queued.
        if (!ReferenceEquals(VisiblePlayheadWindow(), _playheadFrameOwner) || !vm.Engine.IsRunning)
        {
            EnsurePlayheadFrames();
            return;
        }
        if (vm.Engine.TryGetTransportSnapshot(out var snapshot))
        {
            double displayBeats = TransportDisplayPosition.At(snapshot);
            if (IsVisible && WindowState != WindowState.Minimized)
                Timeline.SetPlayheadFrame(snapshot.PositionBeats, displayBeats, snapshot.IsPlaying);
            // Editor cursors use cheap overlays too, including in the floating detail window.
            if (_audioEditor is { IsEffectivelyVisible: true })
                _audioEditor.OnPlayhead(displayBeats, snapshot.IsPlaying);
            if (_clipEditor is { IsEffectivelyVisible: true } && _editorRoll is not null && _editorTrackId > 0)
                _editorRoll.SetPlayhead(displayBeats - _editorRoll.Origin, true);
        }
        EnsurePlayheadFrames(); // stops once the engine reports stop; no idle animation loop
    }

    private void StopPlayheadFrames()
    {
        ++_playheadFrameGeneration; // invalidate queued callbacks before disposing the engine
        _playheadFrameOwner = null;
        _playheadFrameCallback = null;
        _playheadFramePending = false;
    }
}
