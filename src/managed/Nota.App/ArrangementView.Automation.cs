// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // --- parameter automation (M9-A3) --------------------------------------
    internal bool AutomationModeActive => _automationMode;

    private string AutoLabelFor(TrackVM t) => t.AutoTarget switch
    {
        AutomationTarget.Volume => "Vol",
        AutomationTarget.Pan => "Pan",
        AutomationTarget.PluginParam => _engine is { } e
            ? Short(e.PluginParamName(t.Id, t.AutoDeviceIndex, ResolvePluginParamIndex(t))) : "Param",
        AutomationTarget.MidiDeviceParam => _engine is { } e
            ? Short(e.MidiEffectParamName(t.Id, t.AutoDeviceIndex, t.AutoParamIndex)) : "Param",
        _ => _engine is { } e ? Short(e.DeviceParamName(t.Id, t.AutoDeviceIndex, t.AutoParamIndex)) : "Param",
    };
    private static string Short(string s) => s.Length <= 10 ? s : s.Substring(0, 10);

    /// <summary>Value range of a track's current automation target (plugin params are 0..1).</summary>
    internal (float min, float max) AutoRange(TrackVM t) => t.AutoTarget switch
    {
        AutomationTarget.Volume => (0f, 2f),
        AutomationTarget.Pan => (-1f, 1f),
        AutomationTarget.PluginParam => (0f, 1f),   // normalized
        AutomationTarget.MidiDeviceParam => _engine is { } e
            ? (e.MidiEffectParamMin(t.Id, t.AutoDeviceIndex, t.AutoParamIndex),
               e.MidiEffectParamMax(t.Id, t.AutoDeviceIndex, t.AutoParamIndex))
            : (0f, 1f),
        _ => _engine is { } e
            ? (e.DeviceParamMin(t.Id, t.AutoDeviceIndex, t.AutoParamIndex),
               e.DeviceParamMax(t.Id, t.AutoDeviceIndex, t.AutoParamIndex))
            : (0f, 1f),
    };

    /// <summary>Current live value of the target (baseline when the lane has no points).</summary>
    internal float AutoCurrent(TrackVM t) => t.AutoTarget switch
    {
        AutomationTarget.Volume => t.Volume,
        AutomationTarget.Pan => t.Pan,
        AutomationTarget.PluginParam => _engine is { } e ? e.PluginParamGet(t.Id, t.AutoDeviceIndex, ResolvePluginParamIndex(t)) : 0f,
        AutomationTarget.MidiDeviceParam => _engine is { } e ? e.MidiEffectGetParam(t.Id, t.AutoDeviceIndex, t.AutoParamIndex) : 0f,
        _ => _engine is { } e ? e.DeviceGetParam(t.Id, t.AutoDeviceIndex, t.AutoParamIndex) : 0f,
    };

    // Plugin params are addressed by stable id; resolve the current index on demand.
    private int ResolvePluginParamIndex(TrackVM t)
    {
        if (_engine is not { } e || t.AutoParamId.Length == 0) return -1;
        int n = e.PluginParamCount(t.Id, t.AutoDeviceIndex);
        for (int i = 0; i < n; i++)
            if (e.PluginParamId(t.Id, t.AutoDeviceIndex, i) == t.AutoParamId) return i;
        return -1;
    }

    private int FindAutoLane(TrackVM t)
    {
        if (_engine is not { } e) return -1;
        int n = e.AutomationLaneCount(t.Id);
        for (int i = 0; i < n; i++)
        {
            var info = e.AutomationLaneInfo(t.Id, i);
            if (info.Target != t.AutoTarget) continue;
            bool match = t.AutoTarget switch
            {
                AutomationTarget.DeviceParam or AutomationTarget.MidiDeviceParam =>
                    info.DeviceIndex == t.AutoDeviceIndex && info.ParamIndex == t.AutoParamIndex,
                AutomationTarget.PluginParam => info.DeviceIndex == t.AutoDeviceIndex
                                                && e.AutomationLaneParamId(t.Id, i) == t.AutoParamId,
                _ => true,
            };
            if (match) return i;
        }
        return -1;
    }

    private void LoadAutoPoints(TrackVM t)
    {
        t.AutoPoints.Clear();
        if (_engine is not { } e) return;
        int lane = FindAutoLane(t);
        if (lane < 0) return;
        foreach (var p in e.GetAutomationPoints(t.Id, lane))
            t.AutoPoints.Add(new AutoPt { Beat = p.Beat, Value = p.Value, Curve = p.Curve });
    }

    // Any control's gesture-begin (knob/fader/plugin control) — while in automation mode,
    // follow the touched param so its envelope is what the track's lane shows.
    private void OnAutomationTouched(int trackId, AutomationTarget target, int dev, int param, string paramId)
    {
        if (!_automationMode) return;
        var t = _tracks.FirstOrDefault(x => x.Id == trackId) ?? _returns.FirstOrDefault(x => x.Id == trackId);
        if (t is null) return;
        // Already showing this exact target? leave it be (don't reload/redraw mid-gesture).
        bool same = t.AutoTarget == target && target switch
        {
            AutomationTarget.DeviceParam or AutomationTarget.MidiDeviceParam => t.AutoDeviceIndex == dev && t.AutoParamIndex == param,
            AutomationTarget.PluginParam => t.AutoDeviceIndex == dev && t.AutoParamId == (paramId ?? ""),
            _ => true,
        };
        if (same) return;
        if (target == AutomationTarget.PluginParam) SetAutoPluginTarget(t, dev, paramId ?? "");
        else SetAutoTarget(t, target, dev, param);
    }

    internal void SetAutoTarget(TrackVM t, AutomationTarget target, int dev, int param)
    {
        t.AutoTarget = target;
        t.AutoDeviceIndex = target is AutomationTarget.DeviceParam or AutomationTarget.PluginParam or AutomationTarget.MidiDeviceParam ? dev : -1;
        t.AutoParamIndex = target is AutomationTarget.DeviceParam or AutomationTarget.MidiDeviceParam ? param : -1;
        t.AutoParamId = "";
        _autoTargets[t.Id] = (t.AutoTarget, t.AutoDeviceIndex, t.AutoParamIndex, t.AutoParamId);
        t.AutoLabel = AutoLabelFor(t);
        if (ReferenceEquals(_autoSelTrack, t)) _autoSelTrack = null;   // selection was for the old lane
        LoadAutoPoints(t);
        Redraw();
    }

    /// <summary>Select a hosted-plugin parameter (by stable id) as the lane target (M9-B3).</summary>
    internal void SetAutoPluginTarget(TrackVM t, int deviceIndex, string paramId)
    {
        t.AutoTarget = AutomationTarget.PluginParam;
        t.AutoDeviceIndex = deviceIndex;
        t.AutoParamIndex = -1;
        t.AutoParamId = paramId;
        _autoTargets[t.Id] = (t.AutoTarget, t.AutoDeviceIndex, t.AutoParamIndex, t.AutoParamId);
        t.AutoLabel = AutoLabelFor(t);
        if (ReferenceEquals(_autoSelTrack, t)) _autoSelTrack = null;   // selection was for the old lane
        LoadAutoPoints(t);
        Redraw();
    }

    // --- automation range selection + clipboard (Phase 2) ------------------
    // A selection is one track's current lane between two beats; the range clipboard
    // holds that lane's points made relative to the range start, plus its length. All
    // point-level (no engine round-trip): edits go through the live AutoPoints + CommitAuto.
    internal TrackVM? _autoSelTrack;
    internal double _autoSelStart, _autoSelEnd;   // beats, kept normalized (start <= end)
    private readonly List<AutoPt> _autoClip = new();
    private double _autoClipLen;
    internal bool HasAutoSelection => _autoSelTrack is not null && _autoSelEnd - _autoSelStart > 1e-6;
    internal bool HasAutoClip => _autoClip.Count > 0 && _autoClipLen > 1e-6;

    /// <summary>Marks (or extends) the time-range selection on a track's lane.</summary>
    internal void SetAutoSelection(TrackVM t, double a, double b)
    {
        _autoSelTrack = t;
        _autoSelStart = Math.Max(0, Math.Min(a, b));
        _autoSelEnd = Math.Max(a, b);
        Redraw();
    }

    internal void ClearAutoSelection()
    {
        if (_autoSelTrack is null) return;
        _autoSelTrack = null;
        Redraw();
    }

    // Drop every live point of a track's lane in [s,e] (Cut/Delete, and the pre-clear
    // before a paste re-lands its points). Caller commits.
    private static void ClearAutoRange(TrackVM t, double s, double e)
        => t.AutoPoints.RemoveAll(p => p.Beat >= s - 1e-6 && p.Beat <= e + 1e-6);

    /// <summary>Copies the selected lane's points in range into the range clipboard
    /// (beats relative to the range start). False when there is no range selected.</summary>
    public bool CopyAutoSelection()
    {
        if (!HasAutoSelection) return false;
        var t = _autoSelTrack!;
        double s = _autoSelStart, e = _autoSelEnd;
        _autoClip.Clear();
        foreach (var p in t.AutoPoints.Where(p => p.Beat >= s - 1e-6 && p.Beat <= e + 1e-6).OrderBy(p => p.Beat))
            _autoClip.Add(new AutoPt { Beat = p.Beat - s, Value = p.Value, Curve = p.Curve });
        _autoClipLen = e - s;
        return true;
    }

    /// <summary>Copies the selection, then clears it from the lane.</summary>
    public bool CutAutoSelection()
    {
        if (!CopyAutoSelection()) return false;
        ClearAutoRange(_autoSelTrack!, _autoSelStart, _autoSelEnd);
        CommitAuto(_autoSelTrack!);
        Redraw();
        return true;
    }

    /// <summary>Clears the selected range from the lane (no clipboard change).</summary>
    public bool DeleteAutoSelection()
    {
        if (!HasAutoSelection) return false;
        ClearAutoRange(_autoSelTrack!, _autoSelStart, _autoSelEnd);
        CommitAuto(_autoSelTrack!);
        Redraw();
        return true;
    }

    /// <summary>Pastes the range clipboard onto a track's current lane at <paramref name="atBeat"/>
    /// (clearing that span first), then selects what was pasted. False if the clipboard is empty.</summary>
    internal bool PasteAutoAt(TrackVM t, double atBeat)
    {
        if (!HasAutoClip) return false;
        double s = Math.Max(0, atBeat), e = s + _autoClipLen;
        PasteRange(t, s, _autoClipLen, _autoClip);
        CommitAuto(t);
        SetAutoSelection(t, s, e);   // land the selection on the paste so it's visible/re-pasteable
        return true;
    }

    // Lays a set of relative points (beats from 0) onto the lane starting at `at`, spanning
    // `len`. Clears that span but keeps a point sitting exactly at `at` — the boundary the
    // incoming automation continues from — and skips a source point at relative 0 so it never
    // overwrites that boundary. That avoids the redundant flat point you'd otherwise get when
    // pasting/duplicating right after an existing point.
    private static void PasteRange(TrackVM t, double at, double len, IReadOnlyList<AutoPt> rel)
    {
        t.AutoPoints.RemoveAll(p => p.Beat > at + 1e-6 && p.Beat <= at + len + 1e-6);
        bool boundary = t.AutoPoints.Any(p => Math.Abs(p.Beat - at) <= 1e-6);
        foreach (var p in rel)
        {
            if (boundary && p.Beat <= 1e-6) continue;   // don't clobber the existing boundary point
            t.AutoPoints.Add(new AutoPt { Beat = at + p.Beat, Value = p.Value, Curve = p.Curve });
        }
    }

    /// <summary>Keyboard paste: onto the selected track's lane (fallback: first track) at the playhead.</summary>
    public bool PasteAuto()
    {
        var t = _autoSelTrack ?? (_tracks.Count > 0 ? _tracks[0] : null);
        return t is not null && PasteAutoAt(t, Snap(Math.Max(0.0, _playheadBeats)));
    }

    /// <summary>Cmd/Ctrl+D: duplicate the selected automation range immediately after itself
    /// (independent of the clipboard), then move the selection onto the copy so repeats chain.
    /// False when there's no range selected. </summary>
    public bool DuplicateAutoSelection()
    {
        if (!HasAutoSelection) return false;
        var t = _autoSelTrack!;
        double s = _autoSelStart, e = _autoSelEnd, len = e - s;
        var src = t.AutoPoints
            .Where(p => p.Beat >= s - 1e-6 && p.Beat <= e + 1e-6)
            .OrderBy(p => p.Beat)
            .Select(p => new AutoPt { Beat = p.Beat - s, Value = p.Value, Curve = p.Curve })
            .ToList();
        if (src.Count == 0) return false;
        PasteRange(t, e, len, src);
        CommitAuto(t);
        SetAutoSelection(t, e, e + len);
        Redraw();
        return true;
    }

    // --- master-volume automation (graph-level, M9 follow-up) --------------
    // Edited in the pinned footer master row. Value axis 0..2 (like track volume).
    internal const float MasterAutoMin = 0f, MasterAutoMax = 2f;
    internal readonly List<AutoPt> _masterAuto = new();

    internal void LoadMasterAuto()
    {
        _masterAuto.Clear();
        if (_engine is not { } e) return;
        foreach (var p in e.GetMasterVolumeAutomation())
            _masterAuto.Add(new AutoPt { Beat = p.Beat, Value = p.Value, Curve = p.Curve });
    }

    internal void CommitMasterAuto()
    {
        if (_engine is not { } e) return;
        e.SetMasterVolumeAutomation(_masterAuto
            .OrderBy(p => p.Beat)
            .Select(p => new AutomationPoint(p.Beat, p.Value, p.Curve))
            .ToArray());
    }

    /// <summary>Push a track's edited envelope back to the engine (creates/reuses the lane).</summary>
    internal void CommitAuto(TrackVM t) => CommitAutoImpl(t, live: false);

    /// <summary>Live commit while dragging a point (no undo checkpoint): the engine lane — and
    /// thus the device, via read-mode automation at the playhead — follows in real time. The
    /// drag checkpoints once at its start via CommitAuto; the release does a final live commit.</summary>
    internal void CommitAutoLive(TrackVM t) => CommitAutoImpl(t, live: true);

    private void CommitAutoImpl(TrackVM t, bool live)
    {
        if (_engine is not { } e) return;
        int lane = t.AutoTarget == AutomationTarget.PluginParam
            ? e.AddPluginAutomationLane(t.Id, t.AutoDeviceIndex, t.AutoParamId)
            : e.AddAutomationLane(t.Id, t.AutoTarget, t.AutoDeviceIndex, t.AutoParamIndex);
        if (lane < 0) return;
        var pts = t.AutoPoints
            .OrderBy(p => p.Beat)
            .Select(p => new AutomationPoint(p.Beat, p.Value, p.Curve))
            .ToArray();
        if (live) e.SetAutomationPointsLive(t.Id, lane, pts);
        else      e.SetAutomationPoints(t.Id, lane, pts);
    }

    /// <summary>Target picker: Volume / Pan / built-in device params, plus a
    /// filterable param picker + "Learn" for each hosted plugin (M9-B3).</summary>
    internal void ShowAutoTargetMenu(Control anchor, TrackVM t)
    {
        var flyout = new MenuFlyout();

        // Params that already carry automation (lane with points): pinned as a brass-dotted
        // quick-access section at the top, and their keys flag the same params in the tree.
        var automated = new HashSet<string>();
        if (_engine is { } eA)
        {
            var top = new List<(string label, Action apply)>();
            int laneN = eA.AutomationLaneCount(t.Id);
            for (int i = 0; i < laneN; i++)
            {
                var info = eA.AutomationLaneInfo(t.Id, i);
                if (info.PointCount <= 0) continue;
                string key; Action apply;
                switch (info.Target)
                {
                    case AutomationTarget.Volume:
                        key = "V"; apply = () => SetAutoTarget(t, AutomationTarget.Volume, -1, -1); break;
                    case AutomationTarget.Pan:
                        key = "PAN"; apply = () => SetAutoTarget(t, AutomationTarget.Pan, -1, -1); break;
                    case AutomationTarget.PluginParam:
                    { string pid = eA.AutomationLaneParamId(t.Id, i); int dv = info.DeviceIndex;
                      key = $"P:{dv}:{pid}"; apply = () => SetAutoPluginTarget(t, dv, pid); break; }
                    case AutomationTarget.MidiDeviceParam:
                    { int dv = info.DeviceIndex, pr = info.ParamIndex;
                      key = $"M:{dv}:{pr}"; apply = () => SetAutoTarget(t, AutomationTarget.MidiDeviceParam, dv, pr); break; }
                    default:   // DeviceParam
                    { int dv = info.DeviceIndex, pr = info.ParamIndex;
                      key = $"D:{dv}:{pr}"; apply = () => SetAutoTarget(t, AutomationTarget.DeviceParam, dv, pr); break; }
                }
                if (!automated.Add(key)) continue;   // one quick-access entry per param
                top.Add((AutoLaneLabel(eA, t, info, i), apply));
            }
            foreach (var (label, apply) in top)
            {
                var it = new MenuItem { Header = label, Icon = AutoDot(), Foreground = DeviceCardKit.AccentBright };
                it.Click += (_, _) => apply();
                flyout.Items.Add(it);
            }
            if (top.Count > 0) flyout.Items.Add(new Separator());
        }

        flyout.Items.Add(Leaf("Volume", "V", () => SetAutoTarget(t, AutomationTarget.Volume, -1, -1), automated));
        flyout.Items.Add(Leaf("Pan", "PAN", () => SetAutoTarget(t, AutomationTarget.Pan, -1, -1), automated));
        if (_engine is { } e)
        {
            // An instrument (deviceIndex -1) exposing params — the built-in Nota Synth
            // or a hosted plugin.
            if (t.IsInstrument && e.PluginParamCount(t.Id, -1) > 0)
            {
                flyout.Items.Add(new Separator());
                // Built-in instrument (kind >= 0): list its params directly, grouped by
                // section — one click sets a valid target. Hosted plugins keep the
                // filterable picker + Learn.
                flyout.Items.Add(e.TrackInstrumentKind(t.Id) >= 0
                    ? BuildBuiltinInstrumentAutoMenu(e, t, automated)
                    : BuildPluginTargetMenu(e, t, -1, "Instrument"));
            }
            int devCount = e.TrackDeviceCount(t.Id);
            bool sepAdded = false;
            for (int d = 0; d < devCount; d++)
            {
                string dn = e.DeviceName(t.Id, d);
                int builtinPc = e.DeviceParamCount(t.Id, d);
                if (builtinPc > 0)   // built-in device: plain submenu
                {
                    if (!sepAdded) { flyout.Items.Add(new Separator()); sepAdded = true; }
                    var devMenu = new MenuItem { Header = dn };
                    // Nota Chamber (45 params) groups by its section prefix ("IR", "Algo", "EQ" …),
                    // Nota Prism (52) by band ("Low", "Mid", "High", "Crossover") and Nota Lens (37) by
                    // view ("Spectrum", "Scope", "Waterfall", "Cursor"), the Compressor its key
                    // ("SC HP", "SC Gain" …), the Auto Filter its sources ("Env", "LFO", "Mod"), Nota
                    // Vintage its tone and wow ("Tone Low", "Wow Rate" …), Nota Valve its mic ("Mic Distance" …),
                    // Nota Utility its mono, phase and true-peak params ("Mono Freq", "Invert L", "TP Ceiling" …),
                    // Nota Shutter its detector ("Det HP", "Det LP", "Det Filter"), Nota Auto Shift its
                    // scale notes, detector and MIDI target ("Note C#", "Det Low", "MIDI Glide" …) and Nota
                    // Beat Repeat its repeat filter ("Filter Freq", "Filter Type", "Filter Narrow" …):
                    // a word shared by two or more params becomes a submenu.
                    bool grouped = e.TrackDeviceBuiltinKind(t.Id, d) is 1 or 4 or 6 or 7 or 8 or 10 or 11 or 19 or 20 or 21 or 22;
                    var names = new string[builtinPc];
                    for (int p = 0; p < builtinPc; p++) names[p] = e.DeviceParamName(t.Id, d, p);
                    static string Head(string n) { int sp = n.IndexOf(' '); return sp > 0 ? n[..sp] : ""; }
                    var groups = new System.Collections.Generic.Dictionary<string, MenuItem>();
                    for (int p = 0; p < builtinPc; p++)
                    {
                        int dd = d, pp = p;
                        string head = grouped ? Head(names[p]) : "";
                        bool sub = head.Length > 0 && names.Count(n => Head(n) == head) >= 2;
                        var leaf = Leaf(sub ? names[p][(head.Length + 1)..] : names[p], $"D:{dd}:{pp}",
                            () => SetAutoTarget(t, AutomationTarget.DeviceParam, dd, pp), automated);
                        if (!sub) { devMenu.Items.Add(leaf); continue; }
                        if (!groups.TryGetValue(head, out var g)) { g = new MenuItem { Header = head }; groups[head] = g; devMenu.Items.Add(g); }
                        g.Items.Add(leaf);
                    }
                    flyout.Items.Add(devMenu);
                }
                else if (e.PluginParamCount(t.Id, d) > 0)   // hosted plugin: picker + Learn
                {
                    if (!sepAdded) { flyout.Items.Add(new Separator()); sepAdded = true; }
                    flyout.Items.Add(BuildPluginTargetMenu(e, t, d, dn));
                }
            }

            // MIDI effects (before the instrument): a submenu of params per effect.
            int midiCount = e.TrackMidiEffectCount(t.Id);
            if (midiCount > 0) flyout.Items.Add(new Separator());
            for (int m = 0; m < midiCount; m++)
            {
                int mpc = e.MidiEffectParamCount(t.Id, m);
                if (mpc <= 0) continue;
                var mMenu = new MenuItem { Header = e.MidiEffectName(t.Id, m) };
                int shown = Math.Min(mpc, 16);   // globals (+ a few lanes); per-step lanes are edited in the grid
                for (int p = 0; p < shown; p++)
                {
                    int mm = m, pp = p;
                    mMenu.Items.Add(Leaf(e.MidiEffectParamName(t.Id, m, p), $"M:{mm}:{pp}",
                        () => SetAutoTarget(t, AutomationTarget.MidiDeviceParam, mm, pp), automated));
                }
                flyout.Items.Add(mMenu);
            }
        }
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // A small brass dot dropped into a MenuItem.Icon slot to flag an already-automated param.
    private static Control AutoDot() => new Avalonia.Controls.Shapes.Ellipse
    {
        Width = 7, Height = 7, Fill = DeviceCardKit.Brass,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
    };

    // A leaf target item, dot-flagged + accent-tinted when its param already carries automation.
    private MenuItem Leaf(string header, string key, Action apply, HashSet<string> automated)
    {
        var it = new MenuItem { Header = header };
        if (automated.Contains(key)) { it.Icon = AutoDot(); it.Foreground = DeviceCardKit.AccentBright; }
        it.Click += (_, _) => apply();
        return it;
    }

    // Human-readable "Device · Param" label for an existing lane (used in the quick-access section).
    private string AutoLaneLabel(IAudioEngine e, TrackVM t, AutomationLaneInfo info, int laneIndex) => info.Target switch
    {
        AutomationTarget.Volume => "Volume",
        AutomationTarget.Pan => "Pan",
        AutomationTarget.MidiDeviceParam => $"{e.MidiEffectName(t.Id, info.DeviceIndex)} · {e.MidiEffectParamName(t.Id, info.DeviceIndex, info.ParamIndex)}",
        AutomationTarget.PluginParam => $"{e.DeviceName(t.Id, info.DeviceIndex)} · {PluginParamNameById(e, t, info.DeviceIndex, e.AutomationLaneParamId(t.Id, laneIndex))}",
        _ => $"{e.DeviceName(t.Id, info.DeviceIndex)} · {e.DeviceParamName(t.Id, info.DeviceIndex, info.ParamIndex)}",
    };

    private static string PluginParamNameById(IAudioEngine e, TrackVM t, int dev, string paramId)
    {
        int n = e.PluginParamCount(t.Id, dev);
        for (int i = 0; i < n; i++)
            if (e.PluginParamId(t.Id, dev, i) == paramId) return e.PluginParamName(t.Id, dev, i);
        return paramId;
    }

    // A built-in instrument's params listed directly, grouped by name section (the part
    // before the last space, e.g. "Filter 1"); single-word params (Attack, Volume…) sit
    // at the top level. Picking one sets a valid PluginParam target (paramId).
    private MenuItem BuildBuiltinInstrumentAutoMenu(IAudioEngine e, TrackVM t, HashSet<string> automated)
    {
        var root = new MenuItem { Header = e.DeviceName(t.Id, -1) };
        var groups = new System.Collections.Generic.Dictionary<string, MenuItem>();
        int n = e.PluginParamCount(t.Id, -1);
        // Group by the name's head ("Res1 Decay" → Res1 › Decay); a head only one param has
        // stays a plain entry under its full name ("Note Off", not Note › Off).
        static string Head(string nm) { int sp = nm.LastIndexOf(' '); return sp >= 0 ? nm[..sp] : ""; }
        var sizes = new System.Collections.Generic.Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            string h = Head(e.PluginParamName(t.Id, -1, i));
            sizes[h] = sizes.TryGetValue(h, out var c) ? c + 1 : 1;
        }
        for (int i = 0; i < n; i++)
        {
            string id = e.PluginParamId(t.Id, -1, i);
            string nm = e.PluginParamName(t.Id, -1, i);
            if (id.Length == 0) continue;
            string grp = Head(nm);
            if (grp.Length > 0 && sizes[grp] < 2) grp = "";
            string leaf = grp.Length > 0 ? nm[(grp.Length + 1)..] : nm;
            string pid = id;
            var item = Leaf(leaf, $"P:-1:{pid}", () => SetAutoPluginTarget(t, -1, pid), automated);
            if (grp.Length == 0) root.Items.Add(item);
            else
            {
                if (!groups.TryGetValue(grp, out var g)) { g = new MenuItem { Header = grp }; groups[grp] = g; root.Items.Add(g); }
                g.Items.Add(item);
            }
        }
        return root;
    }

    // A hosted plugin's menu entry: "Choose parameter…" (filterable picker) + "Learn".
    private MenuItem BuildPluginTargetMenu(IAudioEngine e, TrackVM t, int deviceIndex, string name)
    {
        var menu = new MenuItem { Header = name };
        var choose = new MenuItem { Header = "Choose parameter…" };
        choose.Click += (_, _) => ShowPluginParamPicker(t, deviceIndex, name);
        var learn = new MenuItem { Header = "Learn (touch a control in the plugin)" };
        learn.Click += (_, _) => StartLearn(t, deviceIndex);
        menu.Items.Add(choose);
        menu.Items.Add(learn);
        return menu;
    }

    // Filterable popup listing every plugin parameter; picking one sets the target.
    private void ShowPluginParamPicker(TrackVM t, int deviceIndex, string name)
    {
        if (_engine is not { } e) return;
        int n = e.PluginParamCount(t.Id, deviceIndex);
        var all = new List<(string id, string label)>(n);
        for (int i = 0; i < n; i++)
            all.Add((e.PluginParamId(t.Id, deviceIndex, i), e.PluginParamName(t.Id, deviceIndex, i)));

        var filter = new TextBox { PlaceholderText = "Filter parameters…", Margin = new Thickness(0, 0, 0, 6) };
        var list = new ListBox { MaxHeight = 260, Width = 260 };
        void Rebuild(string q)
        {
            list.ItemsSource = all
                .Where(p => q.Length == 0 || p.label.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.label).ToList();
        }
        Rebuild("");
        filter.TextChanged += (_, _) => Rebuild(filter.Text ?? "");

        var panel = new StackPanel { Margin = new Thickness(8), MinWidth = 260 };
        panel.Children.Add(new TextBlock { Text = name, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(filter);
        panel.Children.Add(list);
        var popup = new Flyout { Content = panel };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not string lbl) return;
            var hit = all.FirstOrDefault(p => p.label == lbl);
            if (hit.id is not null) SetAutoPluginTarget(t, deviceIndex, hit.id);
            popup.Hide();
        };
        popup.ShowAt(_lanes);
    }

    // Learn: poll the plugin for the parameter the user next moves in its GUI.
    private void StartLearn(TrackVM t, int deviceIndex)
    {
        if (_engine is not { } e) return;
        e.OpenPluginEditor(t.Id, deviceIndex);           // so there's a control to touch
        e.PluginLastTouchedParam(t.Id, deviceIndex);     // clear any stale gesture
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            int idx = e.PluginLastTouchedParam(t.Id, deviceIndex);
            if (idx >= 0)
            {
                timer.Stop();
                SetAutoPluginTarget(t, deviceIndex, e.PluginParamId(t.Id, deviceIndex, idx));
            }
            else if (++ticks > 250) timer.Stop();        // give up after ~30s
        };
        timer.Start();
    }
}
