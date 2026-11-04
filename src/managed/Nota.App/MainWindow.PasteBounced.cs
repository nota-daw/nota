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
using Nota.Application.Localization;

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
        { _vm.StatusText = Loc.T("main.paste_bounced.select_a_time_range_first"); return; }

        // Only clip tracks render content (groups/returns have no chain of their own to bounce).
        var sources = src.TrackIds.Where(id => TrackTypeOf(id) is 0 or 1).ToArray();
        if (sources.Length == 0) { _vm.StatusText = Loc.T("main.paste_bounced.the_selected_range_has_no"); return; }
        if (sources.Length > 1) { _vm.StatusText = Loc.T("main.paste_bounced.paste_bounced_audio_takes_a"); return; }
        int sourceId = sources[0];

        if (targetId <= 0) targetId = Timeline.FocusTrackId;
        if (TrackTypeOf(targetId) != 0) { _vm.StatusText = Loc.T("main.paste_bounced.click_an_audio_track_to"); return; }

        double at = Math.Max(0.0, atBeat ?? Engine.PositionBeats);   // read now: the render moves the playhead
        string name = Engine.GetTrackName(sourceId);

        _bounceBusy = true;
        _vm.SuspendEnginePolling = true;
        float[]? pcm = null;
        try
        {
            await RunBlockingAsync(Loc.T("common.paste_bounced_audio"), Loc.T("main.rendering"), async prog =>
            {
                var frac = new Progress<double>(f => prog.Report(ProgressReport.At(f, Loc.F("main.rendering_n", f * 100))));
                pcm = await Task.Run(() => _freezer.BounceRange(Engine, sourceId, src.Start, src.End,
                    _vm.Transport.LoopOn, _vm.Transport.MetronomeOn, frac));
            });
        }
        catch (Exception ex) { _vm.StatusText = Loc.F("main.paste_bounced.paste_bounced_audio_failed", ex.Message); return; }
        finally { _vm.SuspendEnginePolling = false; _bounceBusy = false; }

        if (pcm is null) { _vm.StatusText = Loc.T("main.paste_bounced.couldnt_render_this_track"); return; }
        if (Engine.PasteAudioFrames(targetId, pcm, pcm.Length / 2, at, name) < 0)
        { _vm.StatusText = Loc.T("main.paste_bounced.couldnt_paste_the_bounced_audio"); return; }

        Timeline.RefreshAndSelectPlaced();
        _session?.Refresh();
        _vm.StatusText = Loc.T("main.paste_bounced.pasted_bounced_audio");
    }

    // Engine track type (0 audio, 1 instrument, 2 return, 3 group), or -1 when the id is gone.
    private int TrackTypeOf(int trackId)
        => trackId > 0 && Engine.TryGetTrackInfo(TrackIndexOf(trackId), out var ti) ? ti.Type : -1;
}
