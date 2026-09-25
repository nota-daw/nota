// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Paste Bounced Audio (⌘⇧V / Ctrl+Shift+V), as in Ableton Live. The most recent time
// selection (Shift+drag) on an audio or instrument track is rendered through that track's
// whole chain — instrument, MIDI FX, devices; pre-fader, like Freeze — and pasted as a new
// audio clip onto the focused audio track at the playhead, overwriting what it covers.
// The render takes the engine offline (like Freeze / Export), so it runs off the UI thread
// behind the modal progress dialog.

using System;
using System.Linq;
using System.Threading.Tasks;
using Nota.Application;

namespace Nota.App;

public partial class MainWindow
{
    private bool _bounceBusy;   // the menu accelerator and the key handler can both fire

    private void OnMenuPasteBounced(object? sender, EventArgs e) => _ = PasteBouncedAsync();

    // Keyboard / Edit menu: onto the focused track at the playhead. The lane context menu passes
    // the right-clicked track and beat instead.
    private async Task PasteBouncedAsync(int targetId = -1, double? atBeat = null)
    {
        if (_vm is null || _bounceBusy) return;
        if (Timeline.BounceSource is not { } src)
        { _vm.StatusText = "Select a time range first (Shift+drag), then paste it bounced onto an audio track."; return; }

        // Only clip tracks render content (groups/returns have no chain of their own to bounce).
        var sources = src.TrackIds.Where(id => TrackTypeOf(id) is 0 or 1).ToArray();
        if (sources.Length == 0) { _vm.StatusText = "The selected range has no audio or instrument track to bounce."; return; }
        if (sources.Length > 1) { _vm.StatusText = "Paste Bounced Audio takes a range on a single track."; return; }
        int sourceId = sources[0];

        if (targetId <= 0) targetId = Timeline.FocusTrackId;
        if (TrackTypeOf(targetId) != 0) { _vm.StatusText = "Click an audio track to paste the bounced audio onto."; return; }

        double at = Math.Max(0.0, atBeat ?? Engine.PositionBeats);   // read now: the render moves the playhead
        string name = Engine.GetTrackName(sourceId);

        _bounceBusy = true;
        _vm.SuspendEnginePolling = true;
        float[]? pcm = null;
        try
        {
            await RunBlockingAsync("Paste Bounced Audio", "Rendering…", async prog =>
            {
                var frac = new Progress<double>(f => prog.Report(ProgressReport.At(f, $"Rendering… {f * 100:0} %")));
                pcm = await Task.Run(() => _freezer.BounceRange(Engine, sourceId, src.Start, src.End,
                    _vm.Transport.LoopOn, _vm.Transport.MetronomeOn, frac));
            });
        }
        catch (Exception ex) { _vm.StatusText = $"Paste Bounced Audio failed: {ex.Message}"; return; }
        finally { _vm.SuspendEnginePolling = false; _bounceBusy = false; }

        if (pcm is null) { _vm.StatusText = "Couldn't render this track."; return; }
        if (Engine.PasteAudioFrames(targetId, pcm, pcm.Length / 2, at, name) < 0)
        { _vm.StatusText = "Couldn't paste the bounced audio."; return; }

        Timeline.RefreshAndSelectPlaced();
        _session?.Refresh();
        _vm.StatusText = "Pasted bounced audio";
    }

    // Engine track type (0 audio, 1 instrument, 2 return, 3 group), or -1 when the id is gone.
    private int TrackTypeOf(int trackId)
        => trackId > 0 && Engine.TryGetTrackInfo(TrackIndexOf(trackId), out var ti) ? ti.Type : -1;
}
