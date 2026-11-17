// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Choosing what a CV cable drives (specs/101, #656). A cable dropped on a parameter control —
// a knob on a node's card or a row of an XS node — links that parameter straight away; dropped
// anywhere else on a node, or on another track's header, it opens the searchable picker
// (ParamPickerPanel) in place of the menus that grew past the screen on big plug-ins. The two
// "Modulate from…" items of a parameter's right-click menu open the same picker for sources.
// A control's parameter comes from the MIDI-learn binding every card already sets (#605).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ModularView
{
    // What a picked target entry links: CvLinkAddToTarget's (kind, device, param).
    private readonly record struct TargetRef(int Kind, int Device, int Param);

    private string ParamName(int track, int kind, int device, int param)
        => kind == 2 ? _engine.MidiEffectParamName(track, device, param)
         : kind == 1 ? _engine.PluginParamName(track, -1, param)
                     : _engine.DeviceParamName(track, device, param);

    private int ParamCount(int track, int kind, int device)
        => kind == 2 ? _engine.MidiEffectParamCount(track, device)
         : kind == 1 ? _engine.PluginParamCount(track, -1)
                     : _engine.DeviceParamCount(track, device);

    // The card-size param is editor state, not sound: never offered as a CV target.
    private bool IsSizeParam(int track, int kind, int device, int param)
        => kind == 1 ? InstrumentView.IsViewParam(_engine.PluginParamId(track, -1, param))
         : kind == 2 ? _engine.MidiEffectParamName(track, device, param) == DeviceView.ParamName
                     : _engine.DeviceParamName(track, device, param) == DeviceView.ParamName;

    // One device's parameters as picker entries (optionally under a heading named after it).
    private void AddDeviceEntries(List<ParamPickerEntry> into, int track, int kind, int device, string? group)
    {
        int pc = ParamCount(track, kind, device);
        var seen = ParamPickerModel.NewSeen();
        bool headed = false;
        for (int p = 0; p < pc; p++)
        {
            if (IsSizeParam(track, kind, device, p)) continue;
            if (group is not null && !headed) { into.Add(ParamPickerEntry.Heading(group)); headed = true; }
            string label = ParamPickerModel.Label(ParamName(track, kind, device, p), p, seen, n => Loc.F("modular.picker_param_fallback", n));
            into.Add(ParamPickerEntry.Item(label, new TargetRef(kind, device, p), group));
        }
    }

    private static (int kind, int device) TargetOf(GNode node)
        => node.Slot == Slot.MidiFx ? (2, node.SlotIndex) : node.Slot == Slot.Instrument ? (1, -1) : (0, node.SlotIndex);

    // True when modulator `modId` on this track already drives (kind, device, param) here.
    private bool AlreadyLinked(int modId, int kind, int device, int param)
    {
        int lc = _engine.CvLinkCount(_trackId);
        for (int i = 0; i < lc; i++)
            if (_engine.CvLinkSourceKind(_trackId, i) != 1 && _engine.CvLinkSource(_trackId, i) == modId
                && _engine.CvLinkTargetTrack(_trackId, i) < 0 && _engine.CvLinkTargetKind(_trackId, i) == kind
                && _engine.CvLinkDevice(_trackId, i) == device && _engine.CvLinkParam(_trackId, i) == param)
                return true;
        return false;
    }

    // Links modulator `modId` to a parameter of this track (no duplicate of an existing link).
    private void LinkHere(int modId, int kind, int device, int param)
    {
        if (!AlreadyLinked(modId, kind, device, param)) _engine.CvLinkAddToTarget(_trackId, modId, kind, -1, device, param);
        Rebuild(); Changed?.Invoke();
    }

    // A device that left the chain while the picker was open (or a stale index) links nothing.
    private bool DeviceStillThere(int track, int kind, int device, int param)
        => kind == 1 ? param < _engine.PluginParamCount(track, -1)
         : kind == 2 ? device < _engine.TrackMidiEffectCount(track) && param < _engine.MidiEffectParamCount(track, device)
                     : device < _engine.TrackDeviceCount(track) && param < _engine.DeviceParamCount(track, device);

    // ---- cable dropped on a node (FR-001) ---------------------------------------

    /// <summary>The picker for a cable dropped on a device node: that device's parameters.</summary>
    private void ShowTargetPicker(GNode node, int modId)
    {
        int t = _trackId;
        var (kind, di) = TargetOf(node);
        var entries = new List<ParamPickerEntry>();
        AddDeviceEntries(entries, t, kind, di, null);
        var model = new ParamPickerModel(entries);
        ParamPickerPanel.Show(node.View, node.Title, model, payload =>
        {
            if (payload is not TargetRef r || _trackId != t || !DeviceStillThere(t, r.Kind, r.Device, r.Param)) return;
            LinkHere(modId, r.Kind, r.Device, r.Param);
        });
    }

    // ---- cable dropped on another track's header (FR-002) ------------------------

    private void ShowCrossTrackPicker(Control anchor, int targetTrackId, int modId)
    {
        var e = _engine;
        var entries = new List<ParamPickerEntry>();
        bool inst = e.TryGetTrackInfo(TrackIndex(targetTrackId), out var ti) && ti.IsInstrument;
        if (inst)
        {
            int mc = e.TrackMidiEffectCount(targetTrackId);
            for (int m = 0; m < mc; m++) AddDeviceEntries(entries, targetTrackId, 2, m, e.MidiEffectName(targetTrackId, m));
            AddDeviceEntries(entries, targetTrackId, 1, -1, e.DeviceName(targetTrackId, -1));
        }
        int dc = e.TrackDeviceCount(targetTrackId);
        for (int d = 0; d < dc; d++) AddDeviceEntries(entries, targetTrackId, 0, d, e.DeviceName(targetTrackId, d));

        var model = new ParamPickerModel(entries, grouped: true) { NoParamsText = Loc.T("modular.no_modulatable_parameters_on_that") };
        int owner = _trackId;
        ParamPickerPanel.Show(anchor, TrackName(targetTrackId), model, payload =>
        {
            if (payload is not TargetRef r || !DeviceStillThere(targetTrackId, r.Kind, r.Device, r.Param)) return;
            e.CvLinkAddToTarget(owner, modId, r.Kind, targetTrackId, r.Device, r.Param);
            Rebuild(); Changed?.Invoke();
        });
    }

    // ---- right-click "Modulate from…" (FR-002a) ----------------------------------

    /// <summary>Sources for "Modulate from a parameter": the parameters exposed as CV outputs on
    /// this track, grouped by device. Empty when nothing is exposed.</summary>
    private List<ParamPickerEntry> ParamSourceEntries(int targetKind, int device, int param)
    {
        int t = _trackId;
        var entries = new List<ParamPickerEntry>();
        foreach (var grp in _exposedParams
                     .Where(x => x.track == t && !(targetKind == 0 && x.dev == device && x.param == param))
                     .GroupBy(x => x.dev).OrderBy(g => g.Key))
        {
            string dn = _engine.DeviceName(t, grp.Key);
            entries.Add(ParamPickerEntry.Heading(dn));
            var seen = ParamPickerModel.NewSeen();
            foreach (var (_, sd, sp) in grp.OrderBy(x => x.param))
                entries.Add(ParamPickerEntry.Item(
                    ParamPickerModel.Label(_engine.DeviceParamName(t, sd, sp), sp, seen, n => Loc.F("modular.picker_param_fallback", n)),
                    (sd, sp), dn));
        }
        return entries;
    }

    /// <summary>Sources for "Modulate from another track": every other track's modulators, grouped by track.</summary>
    private List<ParamPickerEntry> CrossSourceEntries()
    {
        int t = _trackId; var e = _engine;
        var entries = new List<ParamPickerEntry>();
        int tn = e.TrackCount;
        for (int i = 0; i < tn; i++)
        {
            if (!e.TryGetTrackInfo(i, out var ti) || ti.Id == t) continue;
            int omc = e.ModulatorCount(ti.Id);
            if (omc <= 0) continue;
            string tname = TrackName(ti.Id);
            entries.Add(ParamPickerEntry.Heading(tname));
            for (int m = 0; m < omc; m++)
            {
                int modId = e.ModulatorIdAt(ti.Id, m);
                entries.Add(ParamPickerEntry.Item($"{(e.ModulatorKind(ti.Id, modId) == 1 ? "Env" : "LFO")} {modId}", (ti.Id, modId), tname));
            }
        }
        return entries;
    }

    private void ShowParamSourcePicker(Control anchor, int targetKind, int device, int param, string paramName)
    {
        int t = _trackId;
        var model = new ParamPickerModel(ParamSourceEntries(targetKind, device, param), grouped: true);
        ParamPickerPanel.Show(anchor, Loc.F("modular.picker_title_modulate", paramName), model, payload =>
        {
            if (payload is not ValueTuple<int, int> src || _trackId != t) return;
            _engine.CvLinkAddFromParamToTarget(t, src.Item1, src.Item2, targetKind, -1, device, param);
            Rebuild(); Changed?.Invoke();
        });
    }

    private void ShowCrossSourcePicker(Control anchor, int targetKind, int device, int param, string paramName)
    {
        int t = _trackId;
        var model = new ParamPickerModel(CrossSourceEntries(), grouped: true);
        ParamPickerPanel.Show(anchor, Loc.F("modular.picker_title_modulate", paramName), model, payload =>
        {
            if (payload is not ValueTuple<int, int> src || _trackId != t) return;
            _engine.CvLinkAddToTarget(src.Item1, src.Item2, targetKind, t, device, param);
            Rebuild(); Changed?.Invoke();
        });
    }

    // ---- cable dropped right on a parameter control (FR-001a) --------------------

    // The control under the cable that would be linked directly, its node and what it drives.
    private (Control control, GNode node, TargetRef target)? _hoverParam;
    private readonly Rectangle _paramRing = new()
    {
        Stroke = NotaPalette.AccentBright, StrokeThickness = 1.5, RadiusX = 3, RadiusY = 3,
        IsHitTestVisible = false, IsVisible = false,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
    };

    /// <summary>What a parameter control on a node drives, from its MIDI-learn binding: an
    /// effect param (kind 0), the instrument's (kind 1) or a MIDI effect's (kind 2) on this track.
    /// Null for anything else (rack-inner params, buttons, tabs, graphs).</summary>
    private TargetRef? TargetOfControl(Control c, GNode node)
    {
        if (MidiLearn.GetBinding(c) is not { } b) return null;
        var tg = b.Target;
        if (tg.TrackId != _trackId) return null;
        var (kind, di) = TargetOf(node);
        TargetRef? r = tg.Kind switch
        {
            MidiTargetKind.DeviceParam when kind == 0 && tg.DeviceIndex == di && tg.DeviceIndex >= 0 => new TargetRef(0, di, tg.ParamIndex),
            MidiTargetKind.PluginParam when kind == 1 && tg.DeviceIndex == -1 => new TargetRef(1, -1, tg.ParamIndex),
            // A hosted plug-in effect's card binds its knobs as plug-in params of device di —
            // the same parameters the engine exposes as that device's params.
            MidiTargetKind.PluginParam when kind == 0 && tg.DeviceIndex == di && di >= 0 => new TargetRef(0, di, tg.ParamIndex),
            MidiTargetKind.MidiDeviceParam when kind == 2 && tg.DeviceIndex == di => new TargetRef(2, di, tg.ParamIndex),
            _ => null,
        };
        if (r is { } rr && (rr.Param < 0 || IsSizeParam(_trackId, rr.Kind, rr.Device, rr.Param))) return null;
        return r;
    }

    /// <summary>What a control drives, or — for a part of an XS parameter row (its name or value,
    /// siblings of the row's slider) — what the row's slider drives.</summary>
    private (Control control, TargetRef target)? BoundTarget(Control c, GNode node)
    {
        if (TargetOfControl(c, node) is { } r) return (c, r);
        if (c is Grid { Tag: SliderTrack slider } && TargetOfControl(slider, node) is { } rr) return (slider, rr);
        return null;
    }

    /// <summary>The bound parameter control under a viewport point, walking up from the hit
    /// element to its node.</summary>
    private (Control control, GNode node, TargetRef target)? ParamControlAt(Point viewportPoint)
    {
        if (_viewport.InputHitTest(viewportPoint) is not Visual hit) return null;
        GNode? node = null;
        foreach (var anc in hit.GetSelfAndVisualAncestors())
        {
            if (anc == _viewport) break;
            node ??= _nodes.FirstOrDefault(n => n.View == anc);
        }
        if (node is null || !IsConnectTarget(node) || node.IsMathNode || node.IsScopeNode) return null;
        foreach (var anc in hit.GetSelfAndVisualAncestors())
        {
            if (anc == node.View) break;
            if (anc is Control c && BoundTarget(c, node) is { } b) return (b.control, node, b.target);
        }
        return null;
    }

    /// <summary>While a cable is dragged: outline the parameter control it would link.</summary>
    private void TrackParamUnderCable(Point viewportPoint)
    {
        var found = ParamControlAt(viewportPoint);
        _hoverParam = found;
        if (found is not { } f || f.control.TranslatePoint(new Point(0, 0), _viewport) is not { } tl)
        {
            _paramRing.IsVisible = false;
            return;
        }
        if (!_viewport.Children.Contains(_paramRing)) _viewport.Children.Add(_paramRing);
        double pad = 2;
        _paramRing.Width = f.control.Bounds.Width * _scale + pad * 2;
        _paramRing.Height = f.control.Bounds.Height * _scale + pad * 2;
        _paramRing.Margin = new Thickness(tl.X - pad, tl.Y - pad, 0, 0);   // the viewport is a plain Panel
        _paramRing.IsVisible = true;
    }

    private void ClearParamUnderCable() { _hoverParam = null; _paramRing.IsVisible = false; }

    /// <summary>Links the dragged modulator to a bound control directly. Tests call it with the
    /// control a drop would hit; false when the control drives nothing on its node.</summary>
    internal bool TryLinkAtControl(Control control, int modId)
    {
        var node = _nodes.FirstOrDefault(n => control.GetSelfAndVisualAncestors().Contains(n.View));
        if (node is null) return false;
        foreach (var anc in control.GetSelfAndVisualAncestors())
        {
            if (anc == node.View) break;
            if (anc is Control c && BoundTarget(c, node) is { } b) { LinkHere(modId, b.target.Kind, b.target.Device, b.target.Param); return true; }
        }
        return false;
    }
}
