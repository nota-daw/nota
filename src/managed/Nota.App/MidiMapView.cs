// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The "MIDI Map" browser tab: a live list of learned mappings. Each row shows the
// mapped control, its MIDI source, and the editable output range + invert, with a
// delete. Rebuilds whenever the service's table changes.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class MidiMapView : UserControl
{
    private readonly MidiLearnService _learn;
    private readonly StackPanel _rows = new() { Spacing = 6 };

    public MidiMapView(MidiLearnService learn)
    {
        _learn = learn;
        _learn.MappingsChanged += Rebuild;

        var header = new TextBlock
        {
            Text = Loc.T("MIDI mappings"), FontSize = 10, Margin = new Avalonia.Thickness(12, 12, 12, 2),
            Foreground = NotaPalette.TextTertiary,
        };
        var body = new StackPanel { Margin = new Avalonia.Thickness(8, 4), Children = { _rows } };
        Content = new ScrollViewer { Content = new StackPanel { Children = { header, body } } };
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        if (_learn.Mappings.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = Loc.T("No mappings yet.\n\nClick MIDI (top-right), then click any highlighted\ncontrol and move a knob or fader on your controller —\nor press a button or move a stick on a gamepad."),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 16,
                Margin = new Avalonia.Thickness(6, 10), Foreground = NotaPalette.TextTertiary,
            });
            return;
        }
        foreach (var m in _learn.Mappings) _rows.Children.Add(Row(m));
    }

    // The name a mapping was learned under is saved in the project in English; it is translated
    // here, when the row is drawn, so a project reads in whichever language is running. Only the
    // names Nota itself gives (by target kind): a plug-in or device parameter keeps the name it has.
    private static string Shown(MidiMapping m)
    {
        var name = m.DisplayName;
        switch (m.Target.Kind)
        {
            case MidiTargetKind.TransportPlay: return name == "Play" ? Loc.T("Play") : name;
            case MidiTargetKind.TransportStop: return name == "Stop" ? Loc.T("Stop") : name;
            case MidiTargetKind.TransportRecord: return name == "Record" ? Loc.T("Record") : name;
            case MidiTargetKind.MasterVolume: return name == "Master Volume" ? Loc.T("Master Volume") : name;
            case MidiTargetKind.TrackVolume: return name == "Track Volume" ? Loc.T("Track Volume") : name;
            case MidiTargetKind.TrackPan: return name == "Track Pan" ? Loc.T("Track Pan") : name;
            case MidiTargetKind.TrackMute: return name == "Mute" ? Loc.T("Mute") : name;
            case MidiTargetKind.TrackSolo: return name == "Solo" ? Loc.T("Solo") : name;
            case MidiTargetKind.PluginParam when MacroLabel().Match(name) is { Success: true } g:
                return Loc.F("Macro {0} · {1}", g.Groups[1].Value, g.Groups[2].Value);   // a rack macro
            default: return name;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^Macro (\d+) · (.*)$")]
    private static partial System.Text.RegularExpressions.Regex MacroLabel();

    private Control Row(MidiMapping m)
    {
        var name = new TextBlock
        {
            Text = string.IsNullOrEmpty(m.DisplayName) ? m.Target.Kind.ToString() : Shown(m),
            FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var del = new Button
        {
            Content = new Glyph(GlyphKind.Close, 10), Padding = new Avalonia.Thickness(6, 1),
            Background = Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0),
            Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        del.Click += (_, _) => _learn.RemoveMapping(m);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(name);
        Grid.SetColumn(del, 1);
        top.Children.Add(del);

        var src = new TextBlock { Text = _learn.SourceLabel(m), FontSize = 10, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        src.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        // Source on the left; the invert toggle rides the right of the same line so the
        // range row below has room for just MIN / MAX.
        var srcRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        if (m.SourceKind == MidiSourceKind.Phone)
        {
            // Nota Remote: a drawn phone outline marks the source, as the gamepad has its word.
            var phone = new Border
            {
                Width = 7, Height = 11, CornerRadius = NotaRadius.Bar, BorderThickness = new Avalonia.Thickness(1.3),
                BorderBrush = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center,
            };
            srcRow.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { phone, src } });
        }
        else srcRow.Children.Add(src);

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(top);
        body.Children.Add(srcRow);

        // Range + invert only make sense for continuous (non-button) targets.
        if (!m.Target.IsButton)
        {
            var inv = new CheckBox { Content = Loc.T("Invert"), FontSize = 10, IsChecked = m.Invert, VerticalAlignment = VerticalAlignment.Center, Foreground = NotaPalette.TextSecondary };
            inv.IsCheckedChanged += (_, _) => m.Invert = inv.IsChecked == true;
            Grid.SetColumn(inv, 1);
            srcRow.Children.Add(inv);

            var range = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 16,
                VerticalAlignment = VerticalAlignment.Center,
            };
            range.Children.Add(RangeField(Loc.T("Min"), m.RangeMin, v => m.RangeMin = v));
            range.Children.Add(RangeField(Loc.T("Max"), m.RangeMax, v => m.RangeMax = v));
            body.Children.Add(range);
        }

        return new Border
        {
            Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.BorderStrong, BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6), Padding = new Avalonia.Thickness(10, 8),
            Child = body,
        };
    }

    private static Control RangeField(string label, double value, System.Action<double> set)
    {
        var l = new TextBlock { Text = label, FontSize = 9, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var n = new DragNumber(value, 0, 1, 0.01, "0.00", 10) { Width = 32, VerticalAlignment = VerticalAlignment.Center };
        n.ValueChanged += v => set(v);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { l, n } };
    }
}
