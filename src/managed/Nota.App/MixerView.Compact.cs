// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Compact Mixer strips (specs/070, #386): the same strip at 72 px so far more tracks fit on a
// screen. Name; I/O on one line; A/B sends as thin full-width bars; the arrangement's PanBar;
// a wide meter that is also the fader (MeterFader); the dB number; M, S and Arm full width.
// Every control rides the same engine calls, undo gestures, automation writes, MIDI-learn
// targets and StripSync as the regular strip, so the two layouts behave the same. Returns and
// the master keep empty send / pan slots so every meter starts at the same height.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class MixerView
{
    private const double CompactInset = 4;

    private Border CompactStrip(NotaTrackInfo ti, IBrush color, int returns, StripEntry entry)
    {
        int id = ti.Id;
        bool isReturn = ti.IsReturn;
        var col = new StackPanel();
        col.Children.Add(Header(TrackNames.Of(_engine, ti), color, Card, ti.IsGroup ? entry : null));

        Control io;
        if (ti.IsInstrument) io = MidiIoRow(id, compact: true);
        else
        {
            string from = isReturn ? Loc.T("Return in") : ti.IsGroup ? Loc.T("Group in") : Loc.T("In 1");
            string to = isReturn ? Loc.T("→ Master")
                      : ti.IsGroup ? "→ " + (ti.GroupId >= 0 ? TrackNames.Of(_engine, ti.GroupId) : Loc.T("Master"))
                      : Loc.T("Monitor in");
            io = CompactIoRow(from + " " + to);
        }
        io.IsVisible = IoShown;
        _ioSections.Add(io);
        col.Children.Add(io);

        var sends = isReturn ? EmptySlot(SendsH, top: false) : CompactSendsRow(id, returns);
        sends.IsVisible = SendsShown;
        _sendSections.Add(sends);
        col.Children.Add(sends);

        col.Children.Add(CompactPanRow(id, ti.Pan, ti.IsGroup));
        col.Children.Add(CompactLevel(id, ti.Volume, out var db));
        col.Children.Add(db);
        col.Children.Add(CompactButtons(id, ti, isReturn));

        return new Border
        {
            Width = CompactMetrics.Width, Background = Card, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Panel, ClipToBounds = true, Child = col,
        };
    }

    private Border CompactMasterStrip()
    {
        var col = new StackPanel();
        col.Children.Add(Header(Loc.T("Master"), NotaPalette.Master, MasterBg));
        var io = CompactIoRow(Loc.T("Sum bus") + " " + Loc.T("→ Output"));
        io.IsVisible = IoShown;
        _ioSections.Add(io);
        col.Children.Add(io);
        var sends = EmptySlot(SendsH, top: false);
        sends.IsVisible = SendsShown;
        _sendSections.Add(sends);
        col.Children.Add(sends);
        col.Children.Add(EmptySlot(PanH, top: false));
        col.Children.Add(CompactLevel(-1, GetMasterVolume?.Invoke() ?? 1f, out var db));
        col.Children.Add(db);
        col.Children.Add(EmptySlot(ButtonsH, top: true));   // the master has no M / S / Arm
        return new Border
        {
            Width = CompactMetrics.Width, Background = MasterBg, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Panel, ClipToBounds = true, Child = col,
        };
    }

    private Border EmptySlot(double height, bool top) => new()
    {
        Height = height, BorderBrush = BorderInner, BorderThickness = top ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1),
    };

    // I/O on one line; the full routing in the tooltip.
    private Border CompactIoRow(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 8, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var b = new Border { Height = IoH, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(CompactInset, 0), Child = t };
        ToolTip.SetTip(b, text);
        return b;
    }

    // A and B as thin full-width bars with their letter — the regular strip's MiniFaders.
    private Border CompactSendsRow(int id, int returns)
    {
        var body = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        for (int bus = 0; bus < 2; bus++)
        {
            char letter = (char)('A' + bus);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(new TextBlock { Text = letter.ToString(), FontSize = 7, Foreground = TextTertiary, Width = 8, VerticalAlignment = VerticalAlignment.Center });
            Control bar;
            if (bus < returns)
            {
                var f = new MiniFader(_engine.GetTrackSend(id, bus), 1.0) { VerticalAlignment = VerticalAlignment.Center };
                int b = bus;
                f.ValueChanged += v => _engine.SetTrackSend(id, b, (float)v);
                ValueEntry.Attach(f, f.EntryField(Loc.F("Send {0}", (char)('A' + b)), NotaNum.Pct));
                _sendFaders.Add((id, b, f));
                bar = f;
            }
            else bar = new Border { Height = 3, Background = NotaPalette.TrackOff, CornerRadius = NotaRadius.Clip, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1);
            grid.Children.Add(bar);
            body.Children.Add(grid);
        }
        return new Border { Height = SendsH, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(CompactInset, 3), Child = body };
    }

    // The arrangement's PanBar, full width; a group keeps its BAL tag beside it (specs/006).
    private Border CompactPanRow(int id, float pan, bool isGroup)
    {
        var bar = new PanBar(pan) { VerticalAlignment = VerticalAlignment.Center };
        if (id > 0) SyncOf(id).PanBar = bar;
        var undo = new MixGesture(_engine, MixTarget.Pan(id));
        bar.GestureBegin += undo.Begin;
        Action<double> setPan = p => _engine.SetTrackPan(id, (float)p);
        bar.PanChanged += p => undo.Change(p, setPan);
        if (id > 0)
        {
            bar.GestureBegin += () => _engine.BeginAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
            bar.GestureEnd   += () => _engine.EndAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
            MidiLearn.Bind(bar, MidiTarget.TrackPan(id), "Track Pan");
        }
        bar.GestureEnd += undo.End;
        ValueEntry.Attach(bar, bar.EntryField(Loc.T("Pan")));

        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
        if (isGroup)
        {
            var bal = Tag("BAL", 7, 0.84, 11, Loc.T("Groups keep a unity-centre balance so grouping stays level-transparent"));
            bal.IsVisible = _engine.PanLaw == PanLaw.Stereo;
            bal.Margin = new Thickness(3, 0, 0, 0);
            _balBadges.Add(bal);
            DockPanel.SetDock(bal, Dock.Right);
            row.Children.Add(bal);
        }
        row.Children.Add(bar);
        return new Border { Height = PanH, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(CompactInset, 0), Child = row };
    }

    // The wide meter that is the fader, then the dB number — wired like FaderRow.
    private Control CompactLevel(int id, float vol, out Control db)
    {
        var fader = new MeterFader(vol) { HorizontalAlignment = HorizontalAlignment.Center };
        var dbNum = new DragNumber(LinToDb(vol), MinDb, MaxDb, 0.3, "+0.0;−0.0;0.0", 8) { Width = 52, HorizontalAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(dbNum, Loc.T("Set the gain — drag, or double-click to type"));
        void SetVolume(double v) { if (id < 0) SetMasterVolume?.Invoke((float)v); else _engine.SetTrackVolume(id, (float)v); }
        if (id < 0) { _masterMeterFader = fader; _masterDb = dbNum; }
        else { var sync = SyncOf(id); sync.MeterFader = fader; sync.Db = dbNum; }
        var undo = new MixGesture(_engine, id < 0 ? MixTarget.Master : MixTarget.Volume(id));
        fader.GestureBegin += undo.Begin;
        dbNum.GestureBegin += undo.Begin;
        Action<double> setVol = SetVolume;
        fader.ValueChanged += v => { undo.Change(v, setVol); dbNum.Value = LinToDb(v); };
        dbNum.ValueChanged += d => { double v = DbToLin(d); undo.Change(v, setVol); fader.SetValueExternal(v); };
        if (id > 0)
        {
            fader.GestureBegin += () => _engine.BeginAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
            fader.GestureEnd   += () => _engine.EndAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
        }
        fader.GestureEnd += undo.End;
        dbNum.GestureEnd += undo.End;
        MidiLearn.Bind(fader, id > 0 ? MidiTarget.TrackVolume(id) : MidiTarget.MasterVolume, id > 0 ? "Track Volume" : "Master Volume");
        ValueEntry.Attach(fader, fader.EntryField(Loc.T("Volume"), v => NotaNum.GainDb(v, signed: true), 1.0));
        _meters.Add((id, fader, fader.Push));
        db = dbNum;

        var host = new Border { Height = _faderH, Margin = new Thickness(CompactInset, 4), Child = fader };
        _faderGrids.Add(host);
        return host;
    }

    // M, S and Arm stacked at full width.
    private Border CompactButtons(int id, NotaTrackInfo ti, bool isReturn)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(CompactInset) };
        var sync = SyncOf(id);
        var mute = Toggle("M", false, ti.Muted != 0, v => _engine.SetTrackMute(id, v), out sync.Mute);
        var solo = Toggle("S", false, ti.Soloed != 0, v => _engine.SetTrackSolo(id, v), out sync.Solo);
        MidiLearn.Bind(mute, MidiTarget.TrackMute(id), "Mute");
        MidiLearn.Bind(solo, MidiTarget.TrackSolo(id), "Solo");
        stack.Children.Add(Wide(mute));
        stack.Children.Add(Wide(solo));
        if (!isReturn) stack.Children.Add(Wide(ArmToggle(ti.Armed != 0, (v, additive) => TrackArming.Set(_engine, id, v, additive), out sync.Arm)));
        return new Border { Height = ButtonsH, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0), Child = stack };
    }

    private static Border Wide(Border key)
    {
        key.Width = double.NaN;
        key.Height = 18;
        key.HorizontalAlignment = HorizontalAlignment.Stretch;
        return key;
    }
}
