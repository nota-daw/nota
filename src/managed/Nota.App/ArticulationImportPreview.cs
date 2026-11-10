// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What an articulation map file holds, before it lands on a track (specs/085, US1): every
// articulation with its colour and MIDI, and everything the import couldn't carry over, grouped
// by how much it matters. Accept returns true; the caller assigns and stores the map. A file that
// couldn't be read shows only its errors, with Close.

using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Articulations;
using Nota.Application.Localization;

namespace Nota.App;

public sealed class ArticulationImportPreview : NotaWindow
{
    /// <param name="acceptLabel">The accept button's text; null = "Assign to track" (the inspector's import).</param>
    public ArticulationImportPreview(string fileName, ArticulationImportResult result, string? acceptLabel = null)
    {
        Title = Loc.T("articulations.preview.title");
        Width = 560;
        Height = 560;
        CanResize = true;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.Panel;

        var body = new StackPanel { Spacing = 14 };
        var map = result.Map;
        body.Children.Add(new TextBlock
        {
            Text = map?.Name ?? fileName, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        body.Children.Add(new TextBlock
        {
            Text = map is null ? fileName : Loc.F("articulations.preview.subtitle", ArticulationText.Count(map), fileName),
            FontSize = 11, Foreground = NotaPalette.TextTertiary, TextTrimming = TextTrimming.CharacterEllipsis,
        });

        foreach (var (severity, ink) in new[] { (ImportSeverity.Error, NotaPalette.Danger), (ImportSeverity.Warning, NotaPalette.Warning), (ImportSeverity.Info, NotaPalette.TextSecondary) })
        {
            var msgs = result.Messages.Where(m => m.Severity == severity).ToList();
            if (msgs.Count == 0) continue;
            var list = new StackPanel { Spacing = 4 };
            foreach (var m in msgs)
                list.Children.Add(new TextBlock { Text = m.Text, FontSize = 11, LineHeight = 16, Foreground = ink, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(Section(severity switch
            {
                ImportSeverity.Error => Loc.T("articulations.preview.errors"),
                ImportSeverity.Warning => Loc.T("articulations.preview.warnings"),
                _ => Loc.T("articulations.preview.notes"),
            }, list));
        }

        if (map is not null && map.Articulations.Count > 0)
        {
            var rows = new StackPanel { Spacing = 2 };
            foreach (var a in map.Articulations) rows.Children.Add(Row(a));
            body.Children.Add(Section(Loc.T("articulations.preview.articulations"), rows));
        }

        var accept = new Button { Content = acceptLabel ?? Loc.T("articulations.preview.assign"), Classes = { "primary" }, IsVisible = map is not null };
        accept.Click += (_, _) => Close(true);
        var cancel = new Button { Content = map is null ? Loc.T("common.close") : Loc.T("common.cancel"), Classes = { "ghost" } };
        cancel.Click += (_, _) => Close(false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20, 12, 20, 20), Children = { cancel, accept },
        };

        var scroll = new ScrollViewer
        {
            Content = body, Padding = new Thickness(20, 20, 20, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(scroll);
        SetBody(root);
    }

    private static Control Section(string head, Control body)
        => new StackPanel { Spacing = 8, Children = { SessionView.SectionHead(head), body } };

    private static TextBlock Wrapped(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; return t; }

    // One articulation: swatch, name (short name), and the MIDI it sends.
    private static Control Row(Articulation a)
    {
        var swatch = new Border
        {
            Width = 10, Height = 10, CornerRadius = NotaRadius.Badge, Background = Swatch(a),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 8, 0),
        };
        string head = a.ShortName.Length > 0 && a.ShortName != a.Name ? $"{a.Name}  ({a.ShortName})" : a.Name;
        var text = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = head, FontSize = 11, Foreground = a.Enabled ? NotaPalette.TextPrimary : NotaPalette.TextDisabled, TextTrimming = TextTrimming.CharacterEllipsis },
                Wrapped(ClipEditorKit.Mono(ArticulationText.Actions(a), 10, NotaPalette.TextTertiary)),
            },
        };
        var row = new DockPanel { Margin = new Thickness(0, 2) };
        DockPanel.SetDock(swatch, Dock.Left);
        row.Children.Add(swatch);
        row.Children.Add(text);
        return row;
    }

    // The articulation's own colour, else the track palette colour for its slot
    // (ArticulationColours.PaletteSlot) — the same colour its notes take in the piano roll.
    internal static IBrush Swatch(Articulation a)
    {
        if (a.ColorArgb is uint argb) return new SolidColorBrush(Color.FromUInt32(argb));
        return ArrangementView.TrackBrush(ArticulationColours.PaletteSlot(a, ArrangementView.PaletteBases) * ArrangementView.PaletteShades);
    }

    /// <summary>The same colour as <see cref="Swatch"/>, as a value for drawing: read at render time,
    /// so a palette slot follows the theme.</summary>
    internal static Color ColourOf(Articulation a)
        => a.ColorArgb is uint argb ? Color.FromUInt32(argb)
            : ArrangementView.TrackColorForIndex(ArticulationColours.PaletteSlot(a, ArrangementView.PaletteBases) * ArrangementView.PaletteShades);
}
