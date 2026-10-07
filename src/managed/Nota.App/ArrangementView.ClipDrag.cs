// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// An audio clip dragged to an instrument. Moving a clip onto the row of a track whose
// instrument plays samples (Sampler, Grain, Drum Rack, Instrument Rack) loads the clip's audio
// there instead of moving it; pulling it out of the arrangement turns the move into a sample
// drag that every sample drop target takes (a Drum Rack pad, a Grain chamber…). Resting over
// such a row opens its devices, so a specific pad is a drag away. The clip itself stays put.

using System;
using Avalonia.Input;
using Avalonia.Threading;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    /// <summary>A clip's audio (as a sample item) dropped on an instrument track: item, track id.</summary>
    public event Action<BrowserItem, int>? SampleToInstrument;

    /// <summary>A clip drag rested over an instrument track: show its devices (track id).</summary>
    public event Action<int>? DevicesPeekRequested;

    // Instruments that take a dropped sample: Sampler, Instrument Rack, Drum Rack, Grain.
    internal bool TakesSample(int row)
    {
        if (_engine is null || row < 0 || row >= _tracks.Count) return false;
        var t = _tracks[row];
        if (!t.IsInstrument || t.IsGroup || t.IsReturn) return false;
        return _engine.TrackInstrumentKind(t.Id) is 1 or 3 or 4 or 10;
    }

    // Spring-loaded devices: the row a clip drag rests over opens in the Devices panel.
    private DispatcherTimer? _peekTimer;
    private int _peekTrackId = -1;

    internal void DwellOverTrack(int trackId)
    {
        if (trackId == _peekTrackId) return;
        _peekTrackId = trackId;
        _peekTimer?.Stop();
        if (trackId <= 0) return;
        _peekTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _peekTimer.Tick -= OnPeekTick;
        _peekTimer.Tick += OnPeekTick;
        _peekTimer.Start();
    }

    private void OnPeekTick(object? sender, EventArgs e)
    {
        _peekTimer?.Stop();
        if (_peekTrackId > 0) DevicesPeekRequested?.Invoke(_peekTrackId);
    }

    internal void LoadClipIntoTrack(BrowserItem item, int trackId) => SampleToInstrument?.Invoke(item, trackId);

    // A clip pulled out and dragged back over the lanes: only sample-taking instrument rows
    // accept it (the clip already lives here, a drop elsewhere would duplicate it).
    private void OnClipDragOverLanes(DragEventArgs e)
    {
        int row = RowAtY(e.GetPosition(_lanes).Y);
        bool ok = TakesSample(row);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropTrack(ok ? row : -1);
        DwellOverTrack(ok ? _tracks[row].Id : -1);
    }

    private void OnClipDropOnLanes(DragEventArgs e)
    {
        SetDropTrack(-1);
        DwellOverTrack(-1);
        int row = RowAtY(e.GetPosition(_lanes).Y);
        if (!TakesSample(row) || BrowserView.CurrentDrag is not { } item) return;
        LoadClipIntoTrack(item, _tracks[row].Id);
        e.Handled = true;
    }
}
