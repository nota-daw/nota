// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Cue strip and the tracks' Cue keys (specs/089, #483). Cue is the performer's private
// mix on its own output pair: tracks put in Cue are heard there before their faders, mute
// and solo, at the Cue level, together with the click when it is sent there. The strip sits
// next to Master and says what feeds it — the output pair, the cued tracks, the click — and
// when its pair isn't on this device. It shows only while a Cue output is set (or a track is
// in Cue), so a stereo laptop's mixer looks as it always did.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class MixerView
{
    private const int CueMeterId = int.MinValue;   // _meters key of the Cue meter

    private bool _builtCueShown;                    // the strips carry Cue keys and the Cue strip
    private TextBlock? _cueSources, _cueOutput;
    private VFader? _cueFader;
    private MeterFader? _cueMeterFader;
    private DragNumber? _cueDb;
    private string _cueSourcesShown = "", _cueOutputShown = "";

    private bool CueShown() => CueKey.Shown(_engine);

    private string _mainOutputShown = "";

    // Master's output line: "Output" on 1/2 as always; the pair when Main moved, and a
    // fallback note when Main's pair isn't on this device (FR-015).
    private string MainOutputText()
    {
        if (_engine.GetOutputRoutingState().HasFlag(OutputRoutingState.MainFellBack))
            return Loc.T("mixer.main_fallback");
        var s = OutputSetupAccess.Current(_engine);
        return s.MainFirst == 1 && s.MainWidth == 2 ? Loc.T("mixer.output")
            : "→ " + PreferencesWindow.OutputPickLabel(_engine, s.MainFirst, s.MainWidth);
    }

    private Border CueToggle(int id, out Action<bool> sync)
        => CueKey.Create(_engine, id, 20, 17, NotaRadius.Field, out sync);

    // "Out 3/4 · Phones", or why it doesn't play.
    private string CueOutputText()
    {
        var s = OutputSetupAccess.Current(_engine);
        if (s.CueFirst is not int first) return Loc.T("mixer.cue_no_output");
        string pair = PreferencesWindow.OutputPickLabel(_engine, first, s.CueWidth);
        return _engine.GetOutputRoutingState().HasFlag(OutputRoutingState.CueUnavailable)
            ? Loc.F("mixer.cue_not_on_device", pair) : pair;
    }

    // The cued tracks by name, "3 tracks" past two, and the click when it goes here.
    private string CueSourcesText()
    {
        var names = new List<string>();
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && _engine.GetTrackCue(ti.Id)) names.Add(TrackNames.Of(_engine, ti));
        string tracks = names.Count switch
        {
            0 => Loc.T("mixer.cue_no_tracks"),
            <= 2 => string.Join(", ", names),
            _ => Loc.F("mixer.cue_tracks_count", names.Count),
        };
        var click = OutputSetupAccess.Current(_engine).Click;
        return click == ClickDestination.Main ? tracks : Loc.F("mixer.cue_with_click", tracks);
    }

    private Border CueStrip()
    {
        var col = new StackPanel();
        col.Children.Add(Header(Loc.T("mixer.cue"), NotaPalette.Accent, MasterBg));
        // Where it goes and what feeds it, where the strips show their I/O.
        _cueOutputShown = CueOutputText();
        _cueSourcesShown = CueSourcesText();
        _cueOutput = new TextBlock { Text = _cueOutputShown, FontSize = 8, Foreground = TextTertiary, TextTrimming = TextTrimming.CharacterEllipsis };
        _cueSources = new TextBlock { Text = _cueSourcesShown, FontSize = 8, Foreground = TextTertiary, TextTrimming = TextTrimming.CharacterEllipsis };
        PaintCueOutput();
        var io = new Border
        {
            Height = 34, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 4),
            Child = new StackPanel { Spacing = 2, Children = { _cueOutput, _cueSources } },
        };
        io.IsVisible = IoShown;
        _ioSections.Add(io);
        col.Children.Add(io);
        var sends = Blank(SendsH);
        sends.IsVisible = SendsShown;
        _sendSections.Add(sends);
        col.Children.Add(sends);
        col.Children.Add(Blank(38));

        // The Cue level: a listening level, so no undo step and no automation (specs/089).
        float vol = _engine.CueVolume;
        var fader = new VFader(vol, NotaPalette.Accent);
        var dbNum = DbField(vol);
        ToolTip.SetTip(dbNum, Loc.T("mixer.cue_level_tip"));
        fader.ValueChanged += v => { _engine.CueVolume = (float)v; dbNum.Value = LinToDb(v); };
        dbNum.ValueChanged += d => { double v = DbToLin(d); _engine.CueVolume = (float)v; fader.SetValueExternal(v); };
        MidiLearn.Bind(fader, MidiTarget.CueVolume, Loc.T("mixer.cue_level"));
        ValueEntry.Attach(fader, fader.EntryField(Loc.T("mixer.cue_level"), v => NotaNum.GainDb(v, signed: true), 1.0));
        _cueFader = fader;
        _cueDb = dbNum;
        var meter = new MeterBar();
        _meters.Add((CueMeterId, meter, meter.Push));
        var grid = new Grid { Height = _faderH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(7, 8) };
        grid.Children.Add(fader);
        Grid.SetColumn(meter, 1);
        meter.Margin = new Thickness(4, 0, 0, 0);
        grid.Children.Add(meter);
        _faderGrids.Add(grid);
        col.Children.Add(grid);
        col.Children.Add(dbNum);
        col.Children.Add(new Border { Height = 28, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0) });

        return new Border
        {
            Width = 120, Background = MasterBg, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Group, ClipToBounds = true, Child = col,
        };
    }

    private Border CompactCueStrip()
    {
        var col = new StackPanel();
        col.Children.Add(Header(Loc.T("mixer.cue"), NotaPalette.Accent, MasterBg));
        _cueOutputShown = CueOutputText();
        _cueSourcesShown = CueSourcesText();
        _cueOutput = new TextBlock { Text = _cueOutputShown, FontSize = 8, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _cueSources = null;
        PaintCueOutput();
        var io = new Border { Height = IoH, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(CompactInset, 0), Child = _cueOutput };
        ToolTip.SetTip(io, _cueSourcesShown);
        io.IsVisible = IoShown;
        _ioSections.Add(io);
        col.Children.Add(io);
        var sends = EmptySlot(SendsH, top: false);
        sends.IsVisible = SendsShown;
        _sendSections.Add(sends);
        col.Children.Add(sends);
        col.Children.Add(EmptySlot(PanH, top: false));

        float vol = _engine.CueVolume;
        var fader = new MeterFader(vol) { HorizontalAlignment = HorizontalAlignment.Center };
        var dbNum = DbField(vol);
        fader.ValueChanged += v => { _engine.CueVolume = (float)v; dbNum.Value = LinToDb(v); };
        dbNum.ValueChanged += d => { double v = DbToLin(d); _engine.CueVolume = (float)v; fader.SetValueExternal(v); };
        MidiLearn.Bind(fader, MidiTarget.CueVolume, Loc.T("mixer.cue_level"));
        ValueEntry.Attach(fader, fader.EntryField(Loc.T("mixer.cue_level"), v => NotaNum.GainDb(v, signed: true), 1.0));
        _meters.Add((CueMeterId, fader, fader.Push));
        _cueMeterFader = fader;
        _cueDb = dbNum;
        var host = new Border { Height = _faderH, Margin = new Thickness(CompactInset, 4), Child = fader };
        _faderGrids.Add(host);
        col.Children.Add(host);
        col.Children.Add(dbNum);
        col.Children.Add(EmptySlot(ButtonsH, top: true));
        return new Border
        {
            Width = CompactMetrics.Width, Background = MasterBg, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Group, ClipToBounds = true, Child = col,
        };
    }

    // The output line: the pair, or a warning when it isn't on this device (FR-014: Cue then
    // stays silent rather than moving to Main). The full text is the tooltip too.
    private void PaintCueOutput()
    {
        if (_cueOutput is not { } o) return;
        o.Text = _cueOutputShown;
        bool missing = _engine.GetOutputRoutingState().HasFlag(OutputRoutingState.CueUnavailable);
        o.Foreground = missing ? NotaPalette.Warning : TextTertiary;
        ToolTip.SetTip(o, missing ? Loc.T("mixer.cue_unavailable_tip") : Loc.T("mixer.cue_output_tip"));
    }

    // UI tick: the strip follows the Cue level, the cued tracks and the device as they change
    // elsewhere (a MIDI controller, a track header, a device unplugged).
    private void UpdateCue()
    {
        if (CueShown() != _builtCueShown || MainOutputText() != _mainOutputShown) { Refresh(); return; }
        if (!_builtCueShown) return;
        string output = CueOutputText(), sources = CueSourcesText();
        if (output != _cueOutputShown) { _cueOutputShown = output; PaintCueOutput(); }
        if (sources != _cueSourcesShown)
        {
            _cueSourcesShown = sources;
            if (_cueSources is { } s) s.Text = sources;
            else if (_cueOutput?.Parent is Control io) ToolTip.SetTip(io, sources);
        }
        double v = _engine.CueVolume;
        if (_cueFader is { Dragging: false } f && Math.Abs(f.Value - v) > 1e-3) { f.SetValueExternal(v); if (_cueDb is { } db) db.Value = LinToDb(v); }
        if (_cueMeterFader is { Dragging: false } mf && Math.Abs(mf.Value - v) > 1e-3) { mf.SetValueExternal(v); if (_cueDb is { } db) db.Value = LinToDb(v); }
    }
}
