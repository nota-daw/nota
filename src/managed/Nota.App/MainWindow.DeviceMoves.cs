// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The instrument as a device in the chain (specs/043): Replace… and Delete on the instrument
// card, and moving a device to another track (menu or drag onto a track header). An instrument
// track never stands without its instrument, so deleting it — or moving it away — takes the track.

using System.Threading.Tasks;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private void HookDeviceMoves()
    {
        if (_deviceChain is null) return;
        _deviceChain.InstrumentReplaceRequested += OnInstrumentReplaceRequested;
        _deviceChain.TrackDeleteRequested += (t, cut) => _ = OnTrackDeleteRequestedAsync(t, cut);
        _deviceChain.MoveRequested += r => _ = OnDeviceMoveRequestedAsync(r);
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
        => trackId == Engine.MasterTrackId ? "Master" : TrackNames.Of(Engine, trackId);

    // Asks before a device-panel action takes a track that has clips; a track without clips goes
    // without asking (one undo brings it back either way).
    private async Task<bool> ConfirmTrackGoesAsync(int trackId, string message, string affirmative)
    {
        if (!TrackHasClips(trackId)) return true;
        return await new ConfirmWindow("Delete track", message, affirmative, "Cancel").ShowDialog<bool>(this);
    }

    private async Task OnTrackDeleteRequestedAsync(int trackId, bool cut)
    {
        if (_vm is null || trackId <= 0) return;
        string name = TrackLabel(trackId), inst = Engine.DeviceName(trackId, -1);
        if (!await ConfirmTrackGoesAsync(trackId, $"“{name}” has clips. Delete the track with its clips?", "Delete")) return;
        if (!Timeline.DeleteTrack(trackId)) return;
        if (_deviceChain?.TrackId == trackId) ShowDevices(-1, takeOverPanel: false);   // its cards would edit a removed track
        _session?.Refresh();
        _vm.StatusText = cut ? $"Cut {inst} — deleted {name}" : $"Deleted {name}";
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
            && !await ConfirmTrackGoesAsync(r.SrcTrackId, $"Move {device} to “{dst}” and delete “{src}” with its clips?", "Move"))
            return;
        int at = Engine.TransferDevice(r.SrcTrackId, r.Family, r.Index, r.DstTrackId, -1, r.Copy, out var flags);
        if (at < 0)
        {
            _vm.StatusText = at == -2 ? $"“{dst}” can't take {device}." : at == -3 ? $"Couldn't copy {device}." : $"Couldn't move {device}.";
            return;
        }
        _deviceChain?.DeviceTransferred(r, at);   // card state (preset label, A/B) follows the device
        Timeline.Refresh();   // a moved instrument took its source track with it
        _session?.Refresh();
        // The device panel follows the device to its new track.
        if (r.DstTrackId != Engine.MasterTrackId) Timeline.Select(r.DstTrackId, -1);
        ShowDevices(r.DstTrackId);
        string msg = $"{(r.Copy ? "Copied" : "Moved")} {device} to {dst}";
        if (flags.HasFlag(TransferFlags.SidechainCleared)) msg += " · sidechain cleared";
        if (flags.HasFlag(TransferFlags.LinksDropped)) msg += $" · modulation links left on {src}";
        if (flags.HasFlag(TransferFlags.SourceTrackRemoved)) msg += $" · deleted {src}";
        _vm.StatusText = msg;
    }
}
