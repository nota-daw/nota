// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session view — the toolbar: Session Rec with a fixed record length, the launch quantum,
// the global follow-action switch, Capture Scene; on the right the I/O · Sends · Mixer
// section toggles, the position readout, Back to Arrangement and Stop All.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed partial class SessionView
{
    // Launch quantum steps (beats, label). 0 = launch at once.
    internal static readonly (double Beats, string Label)[] QuantSteps =
    {
        (0.0, "None"), (0.25, "1/16"), (0.5, "1/8"), (1.0, "1/4"), (2.0, "1/2"),
        (4.0, "1 Bar"), (8.0, "2 Bars"), (16.0, "4 Bars"), (32.0, "8 Bars"),
    };
    private static readonly int[] FixedBars = { 0, 1, 2, 4, 8 };

    private Button? _recBtn;
    private Glyph? _recDot;
    private TextBlock? _recText;
    private TextBlock? _quantLabel;
    private SwitchTrack? _followSwitch;
    private TextBlock? _followText;
    private Border? _fixedSeg;
    private TextBlock? _posText, _tempoText;
    private Button? _backBtn;
    private readonly List<ToggleButton> _sectionToggles = new();

    /// <summary>Tempo and time signature for the toolbar readout.</summary>
    public Func<(double Bpm, int Num, int Den)>? GetTempo { get; set; }

    private Control BuildToolbar()
    {
        _recDot = new Glyph(GlyphKind.Record, 8);
        _recText = new TextBlock { Text = "Session Rec", FontSize = 11, FontWeight = FontWeight.SemiBold };
        _recBtn = new Button
        {
            Height = NotaSize.Shell, Padding = new Thickness(10, 0),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _recDot, _recText } },
        };
        ToolTip.SetTip(_recBtn, "Record into the armed track at the selected scene · click again to keep the take");
        _recBtn.Click += (_, _) => SessionRec();

        var fixedLabel = Caps("FIXED");
        _fixedSeg = new Border();
        var fixedRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { fixedLabel, _fixedSeg } };
        ToolTip.SetTip(fixedRow, "Fixed record length in bars — the take stops by itself");

        _quantLabel = new TextBlock { FontSize = 10, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        _quantLabel.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var quant = new Button
        {
            Height = NotaSize.Shell, Padding = new Thickness(9, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { Caps("QUANT"), _quantLabel, new Glyph(GlyphKind.ChevronDown, 7) { Foreground = NotaPalette.TextDisabled } },
            },
        };
        ToolTip.SetTip(quant, "Launch quantize: when clips and scenes start");
        quant.Click += (_, _) =>
        {
            var f = new MenuFlyout();
            double cur = _engine.LaunchQuant;
            foreach (var (beats, label) in QuantSteps)
            {
                var mi = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = Math.Abs(beats - cur) < 1e-6 };
                mi.Click += (_, _) => { _engine.SetLaunchQuant(beats); SyncToolbar(); };
                f.Items.Add(mi);
            }
            f.ShowAt(quant);
        };

        _followSwitch = new SwitchTrack { VerticalAlignment = VerticalAlignment.Center };
        _followText = new TextBlock { Text = "Follow", FontSize = 11, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        var follow = new Button
        {
            Height = NotaSize.Shell, Padding = new Thickness(9, 0),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _followSwitch, _followText } },
        };
        ToolTip.SetTip(follow, "Follow actions on or off for every clip and scene");
        follow.Click += (_, _) => { _engine.SessionFollow = !_engine.SessionFollow; SyncToolbar(); RebuildInspector(); };

        var capture = new Button { Content = "Capture Scene", Height = NotaSize.Shell, Padding = new Thickness(10, 0), FontSize = 11 };
        ToolTip.SetTip(capture, "Copy the clips playing now into a new scene");
        capture.Click += (_, _) => CaptureScene();

        // Section toggles: each shows or hides one mixer section under the grid.
        var sections = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        void Section(string label, Func<bool> get, Action<bool> set)
        {
            var tb = new ToggleButton { Content = label, Classes = { "seg" }, IsChecked = get(), Height = 20, FontSize = 10, Padding = new Thickness(9, 0) };
            tb.Click += (_, _) => { set(tb.IsChecked == true); Refresh(); };
            _sectionToggles.Add(tb);
            sections.Children.Add(tb);
        }
        Section("I/O", () => _showIO, v => _showIO = v);
        Section("Sends", () => _showSends, v => _showSends = v);
        Section("Mixer", () => _showMixer, v => _showMixer = v);
        var sectionsWell = new Border { Classes = { "segmented" }, Padding = new Thickness(2), Child = sections };

        _posText = new TextBlock { FontSize = 12, Foreground = NotaPalette.TextPrimary, MinWidth = 56, VerticalAlignment = VerticalAlignment.Center };
        _posText.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        _tempoText = new TextBlock { FontSize = 10, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        _tempoText.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var readout = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0), Children = { _posText, _tempoText } };

        _backBtn = new Button { Content = "Back to Arrangement", Height = NotaSize.Shell, Padding = new Thickness(10, 0), FontSize = 11 };
        ToolTip.SetTip(_backBtn, "Stop session clips and hand every track back to the Arrangement");
        _backBtn.Click += (_, _) => { _engine.BackToArrangement(); UpdateStates(); };

        var stopAll = new Button
        {
            Height = NotaSize.Shell, Padding = new Thickness(10, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 7,
                Children = { new Glyph(GlyphKind.Stop, 7), new TextBlock { Text = "Stop All", FontSize = 11, FontWeight = FontWeight.SemiBold } },
            },
        };
        ToolTip.SetTip(stopAll, "Stop every session clip");
        stopAll.Click += (_, _) => { _engine.StopAllSession(); UpdateStates(); };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { _recBtn, fixedRow, quant, follow, capture } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { sectionsWell, readout, _backBtn, stopAll } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(10, 0) };
        grid.Children.Add(left);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        return new Border
        {
            Height = 44, Background = NotaPalette.BgSunken, BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
        };
    }

    private static TextBlock Caps(string text) => new()
    {
        Text = text, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };

    private void SessionRec()
    {
        // A take in progress: keep it (it loops back), whatever the button's scene.
        if (_engine.TryGetSessionRecordTarget(out _, out _, out _)) { _engine.StopSessionRecord(); Refresh(); return; }
        int scene = _sel.Scene;
        int id = _engine.RecordSessionScene(scene);
        if (id == 0) { Say("Arm a track with an empty slot in this scene to record."); return; }
        Refresh();
    }

    /// <summary>Re-reads the engine's session settings into the toolbar (after a load).</summary>
    public void SyncToolbar()
    {
        double q = _engine.LaunchQuant;
        string ql = q > 0 ? NotaNum.Str(q, "0.##") + " beats" : "None";
        foreach (var (beats, label) in QuantSteps) if (Math.Abs(beats - q) < 1e-6) ql = label;
        if (_quantLabel is not null) _quantLabel.Text = ql;

        bool follow = _engine.SessionFollow;
        if (_followSwitch is not null) _followSwitch.IsOn = follow;
        if (_followText is not null) _followText.Foreground = follow ? NotaPalette.TextPrimary : NotaPalette.TextMuted;

        if (_fixedSeg is not null)
        {
            double len = _engine.SessionRecordLength;
            int sel = Array.FindIndex(FixedBars, b => Math.Abs(b * 4 - len) < 1e-6);
            var seg = Segmented(Array.ConvertAll(FixedBars, b => b == 0 ? "Off" : b.ToString(NotaNum.Culture)), sel < 0 ? 0 : sel,
                i => _engine.SessionRecordLength = FixedBars[i] * 4.0, mono: true);
            _fixedSeg.Child = seg;
        }
        for (int i = 0; i < _sectionToggles.Count; i++)
            _sectionToggles[i].IsChecked = i switch { 0 => _showIO, 1 => _showSends, _ => _showMixer };
    }

    private void UpdateToolbar()
    {
        bool rec = _recScene >= 0;
        if (_recBtn is not null)
        {
            if (rec) { _recBtn.Background = NotaPalette.Record; _recBtn.BorderBrush = NotaPalette.Record; }
            else { _recBtn.ClearValue(BackgroundProperty); _recBtn.ClearValue(BorderBrushProperty); }
            _recDot!.Foreground = rec ? NotaPalette.RecordInk : NotaPalette.Record;
            _recText!.Foreground = rec ? NotaPalette.RecordInk : NotaPalette.TextSecondary;
        }
        if (_posText is not null) _posText.Text = FormatPosition(_engine.PositionBeats);
        if (_tempoText is not null && GetTempo?.Invoke() is { } t) _tempoText.Text = $"{NotaNum.Bpm(t.Bpm)} · {t.Num}/{t.Den}";
        if (_backBtn is not null)
        {
            _backBtn.Classes.Set("primary", _sessionActive);
            _backBtn.IsEnabled = _sessionActive;
        }
    }
}
