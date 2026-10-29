// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — selection + clipboard + drag-reorder for the device chain.
// A device card can be selected (accent border), copied/cut/pasted (Ctrl+C/X/V or the
// header context menu) and deleted (Delete). The clipboard survives track switches so a
// device can be pasted onto another track. Reorder is also possible by dragging a card's
// ⠿ handle; an accent bar marks the drop gap within the same domain (MIDI FX / audio FX).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    // ---- selection --------------------------------------------------------
    // Several devices across the MIDI effects, the instrument and the audio effects (specs/044):
    // click selects one, Shift-click a range in chain order, ⌘/Ctrl-click toggles one. The rules
    // (ranges, what ⌘G can make of it) live in DeviceSelection.
    private readonly DeviceSelection _sel = new();

    private static DeviceSection SectionOf(ChainKind k)
        => k switch { ChainKind.Midi => DeviceSection.Midi, ChainKind.Instrument => DeviceSection.Instrument, _ => DeviceSection.Audio };
    private static ChainKind KindOf(DeviceSection s)
        => s switch { DeviceSection.Midi => ChainKind.Midi, DeviceSection.Instrument => ChainKind.Instrument, _ => ChainKind.Effect };
    private static ChainSlot SlotOf(ChainKind k, int di) => new(SectionOf(k), k == ChainKind.Instrument ? -1 : di);

    // Audio effects, MIDI effects and the instrument take copy/cut/paste/delete (sends/add
    // aren't devices). The instrument's Delete and Cut take its track (specs/043).
    private static bool SelectableKind(ChainKind k) => k is ChainKind.Effect or ChainKind.Midi or ChainKind.Instrument;
    private bool HasDeviceSelection => !_sel.IsEmpty;
    // Any effect (MIDI or audio) selected: what ⌘D needs.
    private bool HasEffectSelection => _sel.Members.Any(m => m.Section != DeviceSection.Instrument);
    // Exactly one device: the single-device commands (Save preset, the instrument's own menu).
    private ChainSlot? SingleSelection => _sel.Count == 1 ? _sel.Members[0] : null;

    private int MidiCount => _engine.TrackMidiEffectCount(_trackId);
    private int FxCount => _engine.TrackDeviceCount(_trackId);

    private void SelectDevice(ChainKind kind, int index, KeyModifiers mods = KeyModifiers.None)
    {
        if (!SelectableKind(kind)) return;
        Focus();   // route Ctrl+C/X/V + Delete here
        bool hadRack = _rackSel != null;
        _rackSel = null; _rackCmds = null;   // one selection on the panel (specs/042)
        var slot = SlotOf(kind, index);
        if ((mods & KeyModifiers.Shift) != 0) _sel.ExtendTo(slot, MidiCount, IsInstrumentTrack(_trackId), FxCount);
        else if ((mods & (KeyModifiers.Meta | KeyModifiers.Control)) != 0) _sel.Toggle(slot);
        else if (!hadRack && _sel.Count == 1 && _sel.Contains(slot)) return;
        else if (!hadRack && _sel.Count > 1 && _sel.Contains(slot)) { _pendingClick = slot; return; }   // may be a drag
        else _sel.Click(slot);
        Rebuild();
    }

    // A plain press on a device inside a multi-selection: the press may start a drag of the whole
    // selection, so it narrows to this device only when the button comes up without a drag.
    private ChainSlot? _pendingClick;
    private void ApplyPendingClick()
    {
        if (_pendingClick is not { } s) return;
        _pendingClick = null;
        _sel.Click(s);
        Rebuild();
    }

    // Select exactly these devices (after a paste, a group, an ungroup).
    private void SelectDevices(IEnumerable<(ChainKind Kind, int Index)> devices)
        => _sel.Set(devices.Select(d => SlotOf(d.Kind, d.Index)));

    private void ClearDeviceSelection() => _sel.Clear();

    // A device in a rack chain (specs/042): the rack card selects it and hands over what the
    // keys do to it; the track chain's selection clears.
    private RackDeviceSel? _rackSel;
    private RackDeviceCommands? _rackCmds;

    private void SelectRackDevice(RackDeviceSel? sel, RackDeviceCommands? cmds)
    {
        if (sel != null) Focus();
        _rackSel = sel;
        _rackCmds = sel != null ? cmds : null;
        if (sel != null && HasDeviceSelection)
        {
            ClearDeviceSelection();
            foreach (var r in _selRings) r.IsVisible = false; // no rebuild: the rack card may be mid-drag
        }
    }

    // The track cards' selection rings, refilled on every rebuild.
    private readonly System.Collections.Generic.List<Border> _selRings = new();

    private bool IsSelected(ChainKind kind, int index) => SelectableKind(kind) && _sel.Contains(SlotOf(kind, index));

    // ---- clipboard --------------------------------------------------------
    // The device itself is copied by the engine (#238): a full clone with every parameter,
    // its state blob, bypass and sidechain key (a rack or plug-in copies itself), so a paste
    // has the source's settings even after the source was cut or changed. Only what the UI
    // owns rides here: the domain and the card's preset label.
    private sealed class DeviceClip
    {
        public ChainKind Kind;
        public string Preset = "", PresetId = "";   // the source card's preset label follows the paste
    }
    private DeviceClip? _clip;
    // The engine's clipboard is shared with the rack chains (specs/042): a rack copy replaces it,
    // so the preset label in _clip rides along only while the clipboard still holds the track's copy.
    private bool _clipFromTrack;
    internal void RackCopied() => _clipFromTrack = false;
    private ChainKind ClipKind => _engine.DeviceClipboardKind() switch { 1 => ChainKind.Effect, 2 => ChainKind.Midi, _ => ChainKind.Instrument };
    private bool CanPaste => _trackId > 0 && _engine.DeviceClipboardKind() != 0;

    private void CopySelectedDevice() => TryCopySelectedDevice();

    // False when nothing was copied (a plug-in that won't open a second instance): Cut stops then.
    // One device keeps its card's preset label on the clipboard; several go as the engine's run.
    private bool TryCopySelectedDevice()
    {
        if (!HasDeviceSelection) return false;
        if (SingleSelection is { } one)
        {
            var k = KindOf(one.Section); int di = one.Index;
            bool ok = k switch
            {
                ChainKind.Instrument => _engine.CopyInstrument(_trackId),
                ChainKind.Effect => _engine.CopyDevice(_trackId, di),
                _ => _engine.CopyMidiEffect(_trackId, di),
            };
            if (!ok) return false;
            _clipFromTrack = true;
            var ex = Extra(k, di);
            _clip = new DeviceClip { Kind = k, Preset = ex.Preset, PresetId = ex.PresetId };
            return true;
        }
        if (_engine.CopyDevices(_trackId, _sel.Indices(DeviceSection.Midi).ToArray(), _sel.HasInstrument,
                _sel.Indices(DeviceSection.Audio).ToArray()) < 0)
            return false;
        _clipFromTrack = false; _clip = null;
        return true;
    }

    private void DeleteSelectedDevice()
    {
        if (!HasDeviceSelection) return;
        // The instrument takes its track (specs/043); with more selected the host always asks.
        if (_sel.HasInstrument) { TrackDeleteRequested?.Invoke(_trackId, false, _sel.Count); return; }
        if (SingleSelection is { } one) { RemoveDeviceAt(KindOf(one.Section), one.Index); return; }
        int n = _sel.Count;
        _engine.BeginUndoGroup();
        try
        {
            // Highest index first, so the ones still to go keep theirs.
            foreach (int i in _sel.Indices(DeviceSection.Audio).OrderByDescending(i => i))
            { _engine.RemoveDevice(_trackId, i); ExtrasRemoved(ChainKind.Effect, i); }
            foreach (int i in _sel.Indices(DeviceSection.Midi).OrderByDescending(i => i))
            { _engine.RemoveMidiEffect(_trackId, i); ExtrasRemoved(ChainKind.Midi, i); }
        }
        finally { _engine.EndUndoGroup(); }
        ClearDeviceSelection();
        StatusMessage?.Invoke($"Deleted {n} devices");
        Rebuild(); Changed?.Invoke();
    }

    // Delete, the menu's Delete and the header's Remove control (#110). An instrument track never
    // stands without its instrument: removing it takes the track (specs/043), which the host asks about.
    private void RemoveDeviceAt(ChainKind k, int di)
    {
        if (k == ChainKind.Instrument) { TrackDeleteRequested?.Invoke(_trackId, false, 1); return; }
        if (k == ChainKind.Effect) _engine.RemoveDevice(_trackId, di);
        else if (k == ChainKind.Midi) _engine.RemoveMidiEffect(_trackId, di);
        else return;
        ExtrasRemoved(k, di);
        RemapSelection(k, i => i == di ? -1 : i > di ? i - 1 : i);   // the selection stays on its own devices
        Rebuild(); Changed?.Invoke();
    }

    // Keep the selection on its devices when indices in one section move.
    private void RemapSelection(ChainKind k, Func<int, int> map)
    {
        var sec = SectionOf(k);
        _sel.Set(_sel.Members.Select(m => m.Section == sec ? new ChainSlot(sec, map(m.Index)) : m)
                             .Where(m => m.Section == DeviceSection.Instrument || m.Index >= 0).ToList());
    }

    private void CutSelectedDevice()
    {
        // Nothing removed unless the copy landed: a cut device must stay pasteable.
        if (!TryCopySelectedDevice())
        {
            if (HasDeviceSelection) StatusMessage?.Invoke(_sel.Count > 1 ? "These devices can't be copied, so they weren't cut." : "This device can't be copied, so it wasn't cut.");
            return;
        }
        if (_sel.HasInstrument) TrackDeleteRequested?.Invoke(_trackId, true, _sel.Count);
        else DeleteSelectedDevice();
    }

    // ⌘D: each run of selected effects copied right after itself, the clipboard untouched
    // (specs/042, specs/044); the copies become the selection. One undo step.
    private void DuplicateSelectedDevice()
    {
        if (!HasEffectSelection) return;
        var copies = new List<(ChainKind Kind, int Index)>();
        bool failed = false;
        _engine.BeginUndoGroup();
        try
        {
            foreach (var k in new[] { ChainKind.Effect, ChainKind.Midi })
            {
                var runs = Runs(_sel.Indices(SectionOf(k)).OrderBy(i => i).ToList());
                for (int r = runs.Count - 1; r >= 0; r--)   // the last run first: earlier indices hold
                {
                    var (a, b) = runs[r];
                    for (int i = 0; i <= b - a; i++)
                    {
                        int end = k == ChainKind.Midi ? MidiCount : FxCount;
                        int src = a + i;
                        int newIdx = k == ChainKind.Midi ? _engine.DuplicateMidiEffect(_trackId, src) : _engine.DuplicateDevice(_trackId, src);
                        if (newIdx < 0) { failed = true; continue; }
                        if (newIdx < end) ExtrasMoved(k, end, newIdx);
                        TrackExtras().Remove(ExtraKey(k, newIdx));
                        var from = Extra(k, src);
                        int to = b + 1 + i;   // after the run and the copies before it
                        if (newIdx != to)
                        {
                            if (k == ChainKind.Midi) _engine.MoveMidiEffect(_trackId, newIdx, to); else _engine.MoveDevice(_trackId, newIdx, to);
                            ExtrasMoved(k, newIdx, to);
                        }
                        var copy = Extra(k, to); copy.Preset = from.Preset; copy.PresetId = from.PresetId;
                        copies.Add((k, to));
                    }
                    // The copies of later runs moved up by this run's length.
                    int len = b - a + 1;
                    for (int c = 0; c < copies.Count - (b - a + 1); c++)
                        if (copies[c].Kind == k && copies[c].Index > b) copies[c] = (k, copies[c].Index + len);
                }
            }
        }
        finally { _engine.EndUndoGroup(); }
        if (failed) StatusMessage?.Invoke("Some devices can't be duplicated");
        if (copies.Count > 0) SelectDevices(copies);
        Rebuild(); Changed?.Invoke();
    }

    // Consecutive runs (first, last) of sorted indices.
    private static List<(int First, int Last)> Runs(List<int> sorted)
    {
        var runs = new List<(int, int)>();
        for (int i = 0; i < sorted.Count; i++)
        {
            int a = sorted[i];
            while (i + 1 < sorted.Count && sorted[i + 1] == sorted[i] + 1) i++;
            runs.Add((a, sorted[i]));
        }
        return runs;
    }

    // Where a paste of `section` goes: after the last selected device there, else the end (-1).
    private int PasteAt(DeviceSection section)
    {
        var idx = _sel.Indices(section).ToList();
        return idx.Count > 0 ? idx.Max() + 1 : -1;
    }

    private void PasteDevice()
    {
        if (!CanPaste) return;
        if (_engine.DeviceClipboardKind() == 4) { PasteDevices(); return; }
        var c = _clipFromTrack && _clip is { } own && own.Kind == ClipKind ? own : new DeviceClip { Kind = ClipKind };
        if (c.Kind == ChainKind.Instrument) { PasteInstrument(c); return; }
        if (c.Kind != ChainKind.Effect && c.Kind != ChainKind.Midi) return;
        // Drop it right after the last selected device of the same domain, else at the end.
        int at = PasteAt(SectionOf(c.Kind));
        int end = c.Kind == ChainKind.Midi ? MidiCount : FxCount;
        if (at > end) at = -1;
        int newIdx = c.Kind == ChainKind.Midi ? _engine.PasteMidiEffect(_trackId, at) : _engine.PasteDevice(_trackId, at);
        if (newIdx < 0) return;
        // A fresh slot: drop any stale entry, and let the extras after it shift up with their devices.
        if (newIdx < end) ExtrasMoved(c.Kind, end, newIdx);
        TrackExtras().Remove(ExtraKey(c.Kind, newIdx));
        var pasted = Extra(c.Kind, newIdx); pasted.Preset = c.Preset; pasted.PresetId = c.PresetId;
        SelectDevices(new[] { (c.Kind, newIdx) });
        Rebuild(); Changed?.Invoke();
    }

    // Several devices on the clipboard (specs/044): each lands in its section after the last
    // selected device there (else at the end), an instrument replaces this track's; one undo step.
    private void PasteDevices()
    {
        int mEnd = MidiCount, fEnd = FxCount;
        int midiAt = PasteAt(DeviceSection.Midi), fxAt = PasteAt(DeviceSection.Audio);
        int n = _engine.PasteDevices(_trackId, midiAt, fxAt, out int skipped);
        if (n < 0) { StatusMessage?.Invoke(n == -3 ? "Couldn't paste: a device failed to load." : "Nothing to paste."); return; }
        if (n == 0) { StatusMessage?.Invoke("This track can't take the copied devices."); return; }
        int km = MidiCount - mEnd, kf = FxCount - fEnd;
        int ma = midiAt < 0 ? mEnd : midiAt, fa = fxAt < 0 ? fEnd : fxAt;
        if (km > 0) RemapExtras(ChainKind.Midi, i => i >= ma ? i + km : i);
        if (kf > 0) RemapExtras(ChainKind.Effect, i => i >= fa ? i + kf : i);
        bool instrument = n - km - kf > 0;
        if (instrument) ForgetInstrumentState(_trackId);
        var pasted = new List<(ChainKind, int)>();
        for (int i = 0; i < km; i++) pasted.Add((ChainKind.Midi, ma + i));
        if (instrument) pasted.Add((ChainKind.Instrument, -1));
        for (int i = 0; i < kf; i++) pasted.Add((ChainKind.Effect, fa + i));
        SelectDevices(pasted);
        StatusMessage?.Invoke(skipped > 0 ? $"Pasted {n} devices, skipped {skipped} this track can't take" : $"Pasted {n} devices");
        Rebuild(); Changed?.Invoke();
    }

    // An instrument on the clipboard replaces this track's instrument (specs/043): with the
    // instrument card or nothing selected; with only effects selected it would read as "paste here".
    private void PasteInstrument(DeviceClip c)
    {
        if (HasDeviceSelection && !_sel.HasInstrument) { StatusMessage?.Invoke("Paste an instrument on an instrument card."); return; }
        int r = _engine.PasteInstrument(_trackId);
        if (r == -2) { StatusMessage?.Invoke("Only an instrument track takes an instrument."); return; }
        if (r < 0) { StatusMessage?.Invoke("Couldn't paste the instrument."); return; }
        ForgetInstrumentState(_trackId);
        var ex = Extra(ChainKind.Instrument, -1); ex.Preset = c.Preset; ex.PresetId = c.PresetId;
        SelectDevices(new[] { (ChainKind.Instrument, -1) });
        StatusMessage?.Invoke($"Pasted {_engine.DeviceName(_trackId, -1)}");
        Rebuild(); Changed?.Invoke();
    }

    // ---- ⌘G / ⌘⇧G: group the selection into a rack / ungroup a rack (#257, #328) ----
    // With the Devices panel focused these act on devices, never on the tracks: grouping
    // tracks stays ⌘G in the arrangement and the track headers. The rack type follows from
    // what is selected (DeviceSelection.Shape); links on the devices follow them (specs/044).

    // A one-chain rack card the selection can ungroup: its chain count (0 when not a rack).
    private int RackChains(ChainSlot s) => s.Section switch
    {
        DeviceSection.Audio => _engine.TrackDeviceBuiltinKind(_trackId, s.Index) == 5 ? _engine.RackDevChainCount(_trackId, s.Index) : 0,
        DeviceSection.Midi => _engine.MidiEffectKind(_trackId, s.Index) == 6 ? _engine.MidiRackChainCount(_trackId, s.Index) : 0,
        _ => _engine.TrackInstrumentKind(_trackId) == 3 ? _engine.RackChainCount(_trackId) : 0,
    };
    private bool SelectedIsRack => SingleSelection is { } s && RackChains(s) > 0;
    private GroupShape SelectionShape => _sel.Shape(MidiCount, FxCount);

    private static string GroupRefusalText(int r) => (GroupRefusal)r switch
    {
        GroupRefusal.NotContiguous => "Select devices next to each other to group them",
        GroupRefusal.RackInRun => "A rack can't go inside another rack",
        GroupRefusal.CantHost => "A plug-in can't go in a MIDI Effect Rack",
        GroupRefusal.InstrumentCantNest => "An Instrument Rack can't go inside another Instrument Rack",
        GroupRefusal.NotInstrumentTrack => "Only an instrument track takes MIDI effects and an instrument",
        _ => "These devices can't be grouped",
    };

    private void GroupSelectedDevice()
    {
        if (!HasDeviceSelection) { StatusMessage?.Invoke("Select devices to put them in a rack"); return; }
        var shape = SelectionShape;
        if (!shape.CanGroup) { StatusMessage?.Invoke(shape.Reason ?? "These devices can't be grouped"); return; }
        // The grouped devices' card state (A/B, preset label) rides on the rack's card until
        // ungroup brings it back.
        var wrapped = new List<CardExtra>();
        foreach (var m in _sel.Members)
            if (TrackExtras().TryGetValue(ExtraKey(KindOf(m.Section), m.Index), out var ex)) wrapped.Add(ex);
        int n = _sel.Count;
        string first = n == 1 && SingleSelection is { } one ? CardName(KindOf(one.Section), one.Index) : "";
        int r = _engine.GroupDevices(_trackId, shape.MidiFirst, shape.MidiCount, shape.WithInstrument, shape.FxFirst, shape.FxCount);
        bool ok = shape.WithInstrument ? r == -1 : r >= 0 || r <= -1000;
        if (!ok) { StatusMessage?.Invoke(GroupRefusalText(r)); return; }
        ChainKind rackKind; int rackIdx; string rackName;
        switch (shape.Kind)
        {
            case GroupShapeKind.AudioRack:
            {
                int a = shape.FxFirst, c = shape.FxCount;
                RemapExtras(ChainKind.Effect, i => i < a ? i : i < a + c ? -1 : i - c + 1);
                rackKind = ChainKind.Effect; rackIdx = a; rackName = "an Audio Effect Rack";
                break;
            }
            case GroupShapeKind.MidiRack:
            {
                int a = shape.MidiFirst, c = shape.MidiCount;
                RemapExtras(ChainKind.Midi, i => i < a ? i : i < a + c ? -1 : i - c + 1);
                rackKind = ChainKind.Midi; rackIdx = a; rackName = "a MIDI Effect Rack";
                break;
            }
            default:
            {
                int mf = shape.MidiFirst, fc = shape.FxCount;
                if (shape.MidiCount > 0) RemapExtras(ChainKind.Midi, i => i >= mf ? -1 : i);
                if (fc > 0) RemapExtras(ChainKind.Effect, i => i < fc ? -1 : i - fc);
                ForgetInstrumentState(_trackId);
                rackKind = ChainKind.Instrument; rackIdx = -1; rackName = "an Instrument Rack";
                break;
            }
        }
        var rex = Extra(rackKind, rackIdx);
        if (wrapped.Count == 1) rex.Wrapped = wrapped[0];   // ⌘Z / ⌘⇧Z swap the two (ExtraFor)
        else if (wrapped.Count > 1) rex.WrappedMany = wrapped;
        SelectDevices(new[] { (rackKind, rackIdx) });
        StatusMessage?.Invoke(n == 1 && first.Length > 0 ? $"Put {first} in {rackName}" : $"Put {n} devices in {rackName}");
        Rebuild(); Changed?.Invoke();
    }

    private string CardName(ChainKind k, int di)
        => k == ChainKind.Midi ? _engine.MidiEffectName(_trackId, di) : _engine.DeviceName(_trackId, k == ChainKind.Instrument ? -1 : di);

    private void UngroupSelectedDevice()
    {
        if (SingleSelection is not { } s || RackChains(s) == 0) { StatusMessage?.Invoke("Select a rack to take its devices out"); return; }
        if (RackChains(s) != 1) { StatusMessage?.Invoke("Only a rack with one chain can be ungrouped"); return; }
        var k = KindOf(s.Section);
        TrackExtras().TryGetValue(ExtraKey(k, s.Index), out var rex);
        int mBefore = MidiCount, fBefore = FxCount;
        int n = _engine.UngroupRack(_trackId, FamilyOf(k), k == ChainKind.Instrument ? 0 : s.Index, out var dropped);
        if (n < 0)
        {
            StatusMessage?.Invoke((UngroupRefusal)n switch
            {
                UngroupRefusal.SeveralChains => "Only a rack with one chain can be ungrouped",
                UngroupRefusal.DrumRack => "A Drum Rack can't be ungrouped",
                UngroupRefusal.NoInstrument => "The rack's chain has no instrument to put back",
                _ => "This rack can't be ungrouped",
            });
            return;
        }
        // The cards after the rack shift; the restored devices are selected and get their own
        // card state back where it matches (the same kind and name).
        var restored = new List<(ChainKind Kind, int Index)>();
        if (k == ChainKind.Instrument)
        {
            int km = MidiCount - mBefore, kf = FxCount - fBefore;
            ForgetInstrumentState(_trackId);
            if (kf > 0) RemapExtras(ChainKind.Effect, i => i + kf);
            for (int i = 0; i < km; i++) restored.Add((ChainKind.Midi, mBefore + i));
            restored.Add((ChainKind.Instrument, -1));
            for (int i = 0; i < kf; i++) restored.Add((ChainKind.Effect, i));
        }
        else
        {
            int a = s.Index;
            TrackExtras().Remove(ExtraKey(k, a));
            RemapExtras(k, i => i > a ? i + n - 1 : i);
            for (int i = 0; i < n; i++) restored.Add((k, a + i));
        }
        var saved = rex?.WrappedMany ?? (rex?.Wrapped is { } w ? new List<CardExtra> { w } : new List<CardExtra>());
        foreach (var (rk, ri) in restored)
        {
            string sig = CardSig(_trackId, rk, ri);
            var hit = saved.FirstOrDefault(x => x.Sig == sig);
            if (hit is null) continue;
            saved.Remove(hit);
            TrackExtras()[ExtraKey(rk, ri)] = hit;
        }
        SelectDevices(restored);
        string msg = n == 1 ? "Took the device out of the rack" : $"Took {n} devices out of the rack";
        if (dropped != UngroupDropped.None) msg += " — its macros and chain settings were dropped";
        StatusMessage?.Invoke(msg);
        Rebuild(); Changed?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool mod = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0;
        if (mod && e.Key == Key.G && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0) UngroupSelectedDevice(); else GroupSelectedDevice();
            e.Handled = true; return;
        }
        // A selected rack chain device takes the clipboard keys (specs/042).
        if (_rackSel != null && _rackCmds is { } rc)
        {
            Action? run = mod ? e.Key switch { Key.C => rc.Copy, Key.X => rc.Cut, Key.V => rc.Paste, Key.D => rc.Duplicate, _ => null }
                : e.Key is Key.Delete or Key.Back ? rc.Delete : null;
            if (run != null) { run(); e.Handled = true; return; }
        }
        if (mod && e.Key == Key.C && HasDeviceSelection) { CopySelectedDevice(); e.Handled = true; return; }
        if (mod && e.Key == Key.X && HasDeviceSelection) { CutSelectedDevice(); e.Handled = true; return; }
        if (mod && e.Key == Key.V && CanPaste) { PasteDevice(); e.Handled = true; return; }
        if (mod && e.Key == Key.D && HasEffectSelection) { DuplicateSelectedDevice(); e.Handled = true; return; }
        if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && HasDeviceSelection) { DeleteSelectedDevice(); e.Handled = true; return; }
        if (!mod && e.Key == Key.Escape && HasDeviceSelection) { ClearDeviceSelection(); Rebuild(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    // ---- context menu (header right-click) --------------------------------
    // The device named at the top (spec 034), then what the card's header hands in (preset,
    // compare, move, docs, fold), Save preset, the clipboard group, and Delete last.
    private void ShowDeviceContextMenu(Control anchor, ChainKind kind, int di, Action<MenuFlyout>? header = null)
    {
        // A right-click inside the selection acts on all of it; outside, it selects that card alone.
        if (!_sel.Contains(SlotOf(kind, di))) SelectDevice(kind, di);
        if (_sel.Count > 1) { ShowSelectionMenu(anchor); return; }
        var flyout = new MenuFlyout();
        string name = kind switch
        {
            ChainKind.Instrument => _engine.DeviceName(_trackId, -1),
            ChainKind.Midi => _engine.MidiEffectName(_trackId, di),
            _ => _engine.DeviceName(_trackId, di),
        };
        string badge = kind switch
        {
            ChainKind.Instrument => "Instrument",
            ChainKind.Midi => $"MIDI effect · {di + 1}",
            _ => $"Audio effect · {di + 1}",
        };
        flyout.Items.Add(MenuKit.Header(name.Length > 0 ? name : "Device", null, badge));
        flyout.Items.Add(new Separator());
        header?.Invoke(flyout);   // ends with its own separator
        if (kind == ChainKind.Instrument)
        {
            // The instrument (specs/043): swap it, copy it to another instrument track, or take
            // its track away — an instrument track never stands without one.
            MenuKit.AddGroups(flyout.Items,
                new Control[] { ReplaceInstrumentMenu() },
                new Control[] { MenuKit.Item("Save preset", GlyphKind.Save, () => PresetSaveRequested?.Invoke(-1)) },
                new Control[]
                {
                    MenuKit.Item("Copy", GlyphKind.Copy, CopySelectedDevice, MenuKit.CopyKey),
                    MenuKit.Item("Cut", GlyphKind.Cut, CutSelectedDevice, MenuKit.CutKey),
                    MenuKit.Item("Paste", GlyphKind.Paste, PasteDevice, MenuKit.PasteKey, _engine.DeviceClipboardKind() == 3),
                },
                MoveToTrackGroup(kind, di),
                new Control[] { GroupItem() },
                new Control[] { MenuKit.Danger("Delete track", GlyphKind.Trash, DeleteSelectedDevice, MenuKit.DeleteKey) });
            flyout.ShowAt(anchor, showAtPointer: true);
            return;
        }
        bool selectable = SelectableKind(kind);
        MenuKit.AddGroups(flyout.Items,
            new Control[] { MenuKit.Item("Save preset", GlyphKind.Save, () => PresetSaveRequested?.Invoke(kind == ChainKind.Midi ? PresetService.MidiEffectBase - di : di)) },
            new Control[]
            {
                MenuKit.Item("Copy", GlyphKind.Copy, CopySelectedDevice, MenuKit.CopyKey, selectable),
                MenuKit.Item("Cut", GlyphKind.Cut, CutSelectedDevice, MenuKit.CutKey, selectable),
                MenuKit.Item("Paste", GlyphKind.Paste, PasteDevice, MenuKit.PasteKey, CanPaste),
                MenuKit.Item("Duplicate", GlyphKind.Duplicate, DuplicateSelectedDevice, MenuKit.DuplicateKey, selectable),
            },
            MoveToTrackGroup(kind, di),
            new Control[] { GroupItem() },
            new Control[] { MenuKit.Danger("Delete", GlyphKind.Trash, DeleteSelectedDevice, MenuKit.DeleteKey, selectable) });
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // Group into rack, or Ungroup rack on a rack card (any of the three; one chain to be enabled).
    private Control GroupItem()
    {
        if (SingleSelection is { } s && RackChains(s) > 0)
            return MenuKit.Item("Ungroup rack", GlyphKind.Ungroup, UngroupSelectedDevice, MenuKit.UngroupKey, RackChains(s) == 1);
        return MenuKit.Item("Group into rack", GlyphKind.Folder, GroupSelectedDevice, MenuKit.GroupKey, SelectionShape.CanGroup);
    }

    // The menu of several selected devices (specs/044): what it does, it does to all of them.
    private void ShowSelectionMenu(Control anchor)
    {
        var flyout = new MenuFlyout();
        int n = _sel.Count;
        flyout.Items.Add(MenuKit.Header($"{n} devices", null, _sel.HasInstrument ? "With the instrument" : "Selected"));
        flyout.Items.Add(new Separator());
        MenuKit.AddGroups(flyout.Items,
            new Control[]
            {
                MenuKit.Item("Copy", GlyphKind.Copy, CopySelectedDevice, MenuKit.CopyKey),
                MenuKit.Item("Cut", GlyphKind.Cut, CutSelectedDevice, MenuKit.CutKey),
                MenuKit.Item("Paste", GlyphKind.Paste, PasteDevice, MenuKit.PasteKey, CanPaste),
                MenuKit.Item("Duplicate", GlyphKind.Duplicate, DuplicateSelectedDevice, MenuKit.DuplicateKey, HasEffectSelection),
            },
            MoveSelectionToTrackGroup(),
            new Control[] { GroupItem() },
            new Control[]
            {
                _sel.HasInstrument
                    ? MenuKit.Danger("Delete track", GlyphKind.Trash, DeleteSelectedDevice, MenuKit.DeleteKey)
                    : MenuKit.Danger("Delete", GlyphKind.Trash, DeleteSelectedDevice, MenuKit.DeleteKey),
            });
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // ---- drag-reorder (grab a card by its header) ---------------------------
    // Tag stamped on each device card so a drag can find its same-domain siblings in _row.
    private sealed record DevTag(ChainKind Kind, int Index);

    private ChainKind _devDragKind;
    private int _devDragFrom = -1;
    private bool _devDragging;
    private Point _devDragStart;
    private Control? _devDragCard;
    private readonly Border _devDropBar = new()
    {
        Width = 3, Background = AccentBright, CornerRadius = NotaRadius.Clip,
        Margin = new Thickness(-4, 6, 1, 6), IsHitTestVisible = false,
    };

    // The device cards in _row that belong to the given domain, in visual order.
    private System.Collections.Generic.List<Control> DomainCards(ChainKind kind)
        => _row.Children.OfType<Control>().Where(c => c.Tag is DevTag t && t.Kind == kind).ToList();

    // The instrument card drags too, but only out of the panel (onto a track header): it has no
    // slot to reorder into. Its index is -1 while dragging (_devDragFrom holds -2 for "the instrument").
    private void HookDeviceDrag(Control handle, Control card, ChainKind kind, int di)
    {
        bool inst = kind == ChainKind.Instrument;
        if (!inst) card.Tag = new DevTag(kind, di);
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
            _devDragKind = kind; _devDragFrom = inst ? -2 : di; _devDragging = false; _devDragCard = card;
            _devDragStart = e.GetPosition(_row);
            _devDragPress = e;   // a drag out of the panel needs the press (DoDragDropAsync)
            e.Pointer.Capture(handle);
            if (!inst) e.Handled = true;   // the instrument's header still selects on this press
        };
        handle.PointerMoved += (_, e) =>
        {
            if (_devDragFrom == -1) return;
            // A release that never arrived (the card was rebuilt under the click) leaves no drag.
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) { EndDeviceDrag(e.Pointer); return; }
            var p = e.GetPosition(_row);
            if (!_devDragging && Math.Abs(p.X - _devDragStart.X) < 5 && Math.Abs(p.Y - _devDragStart.Y) < 5) return;
            // Out of the panel: hand over to a system drag the track headers take (specs/043).
            var q = e.GetPosition(this);
            if ((q.X < 0 || q.Y < 0 || q.X > Bounds.Width || q.Y > Bounds.Height) && _devDragPress is { } press)
            {
                var k = _devDragKind; int from = _devDragFrom == -2 ? -1 : _devDragFrom;
                EndDeviceDrag(e.Pointer);
                _ = DeviceDrag.DragAsync(press, _trackId, k, from);
                return;
            }
            if (inst) return;
            _devDragging = true;
            if (_devDragCard != null) _devDragCard.Opacity = 0.55;
            PositionDropBar(GapAt(p.X));
        };
        handle.PointerReleased += (_, e) =>
        {
            if (inst) { bool moved = _devDragging; EndDeviceDrag(e.Pointer); if (!moved) ApplyPendingClick(); else _pendingClick = null; return; }
            if (_devDragFrom < 0) { ApplyPendingClick(); return; }
            var kindL = _devDragKind; int from = _devDragFrom; bool dragged = _devDragging;
            if (dragged) _pendingClick = null; else ApplyPendingClick();
            int gap = _devDragging ? GapAt(e.GetPosition(_row).X) : -1;
            if (_devDragCard != null) _devDragCard.Opacity = 1.0;
            _devDragFrom = -1; _devDragging = false; _devDragCard = null;
            _row.Children.Remove(_devDropBar);
            e.Pointer.Capture(null);
            if (!dragged || gap < 0) return;
            int m = DomainCards(kindL).Count;
            // A selected card drags its whole section of the selection along (specs/044).
            if (_sel.Contains(SlotOf(kindL, from)))
            {
                var group = _sel.Indices(SectionOf(kindL)).OrderBy(i => i).ToList();
                if (group.Count > 1) { MoveBlock(kindL, group, gap, m); return; }
            }
            int to = gap > from ? gap - 1 : gap;
            to = Math.Clamp(to, 0, m - 1);
            if (to == from) return;
            if (kindL == ChainKind.Midi) _engine.MoveMidiEffect(_trackId, from, to);
            else _engine.MoveDevice(_trackId, from, to);
            ExtrasMoved(kindL, from, to);
            SelectDevices(new[] { (kindL, to) });
            Rebuild(); Changed?.Invoke();
        };
    }

    private PointerPressedEventArgs? _devDragPress;

    // Several selected devices of one section dropped on a gap (0..m): they land there together,
    // in order, one undo step; they stay selected.
    private void MoveBlock(ChainKind k, List<int> group, int gap, int m)
    {
        var others = Enumerable.Range(0, m).Where(i => !group.Contains(i)).ToList();
        int at = gap - group.Count(i => i < gap);
        var final = others.Take(at).Concat(group).Concat(others.Skip(at)).ToList();
        var cur = Enumerable.Range(0, m).ToList();
        if (cur.SequenceEqual(final)) return;
        _engine.BeginUndoGroup();
        try
        {
            for (int p = 0; p < m; p++)
            {
                if (cur[p] == final[p]) continue;
                int from = cur.IndexOf(final[p]);
                if (k == ChainKind.Midi) _engine.MoveMidiEffect(_trackId, from, p); else _engine.MoveDevice(_trackId, from, p);
                ExtrasMoved(k, from, p);
                int id = cur[from]; cur.RemoveAt(from); cur.Insert(p, id);
            }
        }
        finally { _engine.EndUndoGroup(); }
        SelectDevices(Enumerable.Range(at, group.Count).Select(i => (k, i)));
        Rebuild(); Changed?.Invoke();
    }

    // Ends an in-panel header drag without dropping: the card, the drop bar and the capture go back.
    private void EndDeviceDrag(IPointer pointer)
    {
        if (_devDragCard != null) _devDragCard.Opacity = 1.0;
        _devDragFrom = -1; _devDragging = false; _devDragCard = null; _devDragPress = null;
        _row.Children.Remove(_devDropBar);
        pointer.Capture(null);
    }

    // Insertion gap (0..m) among the dragged domain's cards for a pointer X in _row space.
    private int GapAt(double x)
    {
        var cards = DomainCards(_devDragKind);
        int gap = 0;
        foreach (var c in cards) { if (x > c.Bounds.X + c.Bounds.Width * 0.5) gap++; else break; }
        return Math.Clamp(gap, 0, cards.Count);
    }

    private void PositionDropBar(int gap)
    {
        _row.Children.Remove(_devDropBar);
        var cards = DomainCards(_devDragKind);
        if (cards.Count == 0) return;
        int childIndex = gap < cards.Count
            ? _row.Children.IndexOf(cards[gap])
            : _row.Children.IndexOf(cards[cards.Count - 1]) + 1;
        childIndex = Math.Clamp(childIndex, 0, _row.Children.Count);
        _row.Children.Insert(childIndex, _devDropBar);
    }
}
