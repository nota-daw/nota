// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The instrument as a device in the chain (specs/043): Replace… and Delete on the instrument
// card, and moving a device to another track (menu or drag onto a track header). An instrument
// track never stands without its instrument, so deleting it — or moving it away — takes the track.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nota.Application;
using Nota.Presentation;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private void HookDeviceMoves()
    {
        if (_deviceChain is null) return;
        _deviceChain.InstrumentReplaceRequested += OnInstrumentReplaceRequested;
        _deviceChain.TrackDeleteRequested += (t, cut, n) => _ = OnTrackDeleteRequestedAsync(t, cut, n);
        _deviceChain.MoveRequested += r => _ = OnDeviceMoveRequestedAsync(r);
        _deviceChain.MovesRequested += m => _ = OnDevicesMoveRequestedAsync(m);
        Timeline.DeviceDropped += r => _ = OnDeviceMoveRequestedAsync(r);   // a card dragged onto a track row
    }

    // Session view is built later than the device chain; it hooks its own drops here.
    private void HookSessionDeviceMoves(SessionView session)
        => session.DeviceDropped += r => _ = OnDeviceMoveRequestedAsync(r);

    private void OnInstrumentReplaceRequested(BrowserItem item)
    {
        int t = _deviceChain?.TrackId ?? -1;
        if (t <= 0) return;
        ApplyInsertResult(Insertion.Insert(item, new InsertTarget(t, InsertPlacement.ReplaceInstrument)));
    }

    // Any clip on the track, in the arrangement or a Session slot.
    private bool TrackHasClips(int trackId)
    {
        for (int i = 0; i < Engine.TrackCount; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId && ti.ClipCount > 0) return true;
        for (int s = 0; s < Engine.SceneCount; s++)
            if (Engine.SessionSlotState(trackId, s) != 0) return true;
        return false;
    }

    // The track's name as the Arrangement shows it (an unnamed instrument track reads as its instrument).
    private string TrackLabel(int trackId)
        => trackId == Engine.MasterTrackId ? Loc.T("common.master") : TrackNames.Of(Engine, trackId);

    // Asks before a device-panel action takes a track that has clips; a track without clips goes
    // without asking (one undo brings it back either way).
    private async Task<bool> ConfirmTrackGoesAsync(int trackId, string message, string affirmative)
    {
        if (!TrackHasClips(trackId)) return true;
        return await new ConfirmWindow(Loc.T("main.device_moves.delete_track"), message, affirmative, Loc.T("common.cancel")).ShowDialog<bool>(this);
    }

    // selected: how many devices the panel had selected. With the instrument and more (specs/044)
    // the host asks every time: a Shift-click range that swept over the instrument must not
    // take the track silently.
    private async Task OnTrackDeleteRequestedAsync(int trackId, bool cut, int selected = 1)
    {
        if (_vm is null || trackId <= 0) return;
        string name = TrackLabel(trackId), inst = Engine.DeviceName(trackId, -1);
        if (selected > 1)
        {
            if (!await new ConfirmWindow(Loc.T("main.device_moves.delete_track"), Loc.F("main.device_moves.delete_track_the_track_and", name, selected),
                    Loc.T("common.delete"), Loc.T("common.cancel")).ShowDialog<bool>(this)) return;
        }
        else if (!await ConfirmTrackGoesAsync(trackId, Loc.F("main.device_moves.has_clips_delete_the_track", name), Loc.T("common.delete"))) return;
        if (!Timeline.DeleteTrack(trackId)) return;
        if (_deviceChain?.TrackId == trackId) ShowDevices(-1, takeOverPanel: false);   // its cards would edit a removed track
        _session?.Refresh();
        _vm.StatusText = cut ? Loc.F("main.device_moves.cut_deleted", inst, name) : Loc.F("main.device_moves.deleted_n", name);
    }

    // Move to track ▸ for several selected devices (specs/044): one undo step, in chain order so
    // they land in order at the end of the target's sections; the instrument goes last (it takes
    // the source track) and, with others, always asks first.
    private async Task OnDevicesMoveRequestedAsync(IReadOnlyList<DeviceMoveRequest> moves)
    {
        if (_vm is null || moves.Count == 0) return;
        if (moves.Count == 1) { await OnDeviceMoveRequestedAsync(moves[0]); return; }
        int srcId = moves[0].SrcTrackId, dstId = moves[0].DstTrackId;
        string src = TrackLabel(srcId), dst = TrackLabel(dstId);
        bool withInstrument = moves.Any(m => m.Family == DeviceFamily.Instrument);
        if (withInstrument && !await new ConfirmWindow(Loc.T("main.device_moves.delete_track"),
                Loc.F("main.device_moves.move_devices_to_and_delete", moves.Count, dst, src), Loc.T("main.device_moves.move"), Loc.T("common.cancel")).ShowDialog<bool>(this))
            return;
        int moved = 0, failed = 0;
        var shift = new Dictionary<DeviceFamily, int>();   // earlier moves of a section close its gaps
        Engine.BeginUndoGroup();
        try
        {
            foreach (var r in moves.Where(m => m.Family != DeviceFamily.Instrument).Concat(moves.Where(m => m.Family == DeviceFamily.Instrument)))
            {
                int gone = shift.TryGetValue(r.Family, out var g) ? g : 0;
                var req = r with { Index = r.Family == DeviceFamily.Instrument ? r.Index : r.Index - gone };
                int at = Engine.TransferDevice(req.SrcTrackId, req.Family, req.Index, req.DstTrackId, -1, false, out _);
                if (at < 0) { failed++; continue; }
                _deviceChain?.DeviceTransferred(req, at);
                shift[r.Family] = gone + 1;
                moved++;
            }
        }
        finally { Engine.EndUndoGroup(); }
        Timeline.Refresh();
        _session?.Refresh();
        if (dstId != Engine.MasterTrackId) Timeline.Select(dstId, -1);
        ShowDevices(dstId);
        _vm.StatusText = failed == 0 ? Loc.F("main.device_moves.moved_devices_to", moved, dst) : Loc.F("main.device_moves.moved_devices_to_couldnt_move", moved, dst, failed);
    }

    // Move to track ▸, or a device card dropped on a track header (⌥ = copy). One undo step; an
    // instrument's move takes its track away, so a track with clips asks first.
    private async Task OnDeviceMoveRequestedAsync(DeviceMoveRequest r)
    {
        if (_vm is null) return;
        string src = TrackLabel(r.SrcTrackId), dst = TrackLabel(r.DstTrackId);
        string device = r.Family switch
        {
            DeviceFamily.Instrument => Engine.DeviceName(r.SrcTrackId, -1),
            DeviceFamily.Midi => Engine.MidiEffectName(r.SrcTrackId, r.Index),
            _ => Engine.DeviceName(r.SrcTrackId, r.Index),
        };
        if (r.Family == DeviceFamily.Instrument && !r.Copy
            && !await ConfirmTrackGoesAsync(r.SrcTrackId, Loc.F("main.device_moves.move_to_and_delete_with", device, dst, src), Loc.T("main.device_moves.move")))
            return;
        int at = Engine.TransferDevice(r.SrcTrackId, r.Family, r.Index, r.DstTrackId, -1, r.Copy, out var flags);
        if (at < 0)
        {
            _vm.StatusText = at == -2 ? Loc.F("main.device_moves.cant_take", dst, device) : at == -3 ? Loc.F("main.device_moves.couldnt_copy", device) : Loc.F("main.device_moves.couldnt_move", device);
            return;
        }
        _deviceChain?.DeviceTransferred(r, at);   // card state (preset label, A/B) follows the device
        Timeline.Refresh();   // a moved instrument took its source track with it
        _session?.Refresh();
        // The device panel follows the device to its new track.
        if (r.DstTrackId != Engine.MasterTrackId) Timeline.Select(r.DstTrackId, -1);
        ShowDevices(r.DstTrackId);
        string msg = r.Copy ? Loc.F("main.device_moves.copied_to", device, dst) : Loc.F("main.device_moves.moved_to", device, dst);
        if (flags.HasFlag(TransferFlags.SidechainCleared)) msg += Loc.T("main.device_moves.sidechain_cleared");
        if (flags.HasFlag(TransferFlags.LinksDropped)) msg += Loc.F("main.device_moves.modulation_links_left_on", src);
        if (flags.HasFlag(TransferFlags.SourceTrackRemoved)) msg += Loc.F("main.device_moves.deleted", src);
        _vm.StatusText = msg;
    }
}
