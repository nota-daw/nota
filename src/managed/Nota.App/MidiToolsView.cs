// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The clip-tools panel on the right of the piano roll (nota-design "Nota Clip Editor" 1a):
// Generate / Transform segments, the tool list (the chosen one on Brass Wash with a 2px
// brass bar), a parameter list built from the tool's own descriptor (no per-tool panel),
// the seed, the Replace / Add output, and at the foot the result line, Apply — solid brass
// only while something is pending — and Revert / Defaults. Every knob move re-runs the
// tool and previews the result in the roll (brass outlines over the notes) — audible,
// undo-free — so Apply is a decision the user has already heard.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Midi;
using static Nota.App.ClipEditorKit;

namespace Nota.App;

public sealed class MidiToolsView : UserControl
{
    public const double RailWidth = ToolsW;

    private readonly PianoRollView _roll;
    private readonly Dictionary<string, MidiToolSettings> _settings = new(StringComparer.Ordinal);

    private readonly StackPanel _toolList = new();
    private readonly StackPanel _paramList = new() { Spacing = 9 };
    private readonly TextBlock _paramTitle = new();
    private readonly TextBlock _scopeText = Meta("");
    private readonly TextBlock _seedText = Mono("1");
    private readonly TextBlock _resultText = Mono("", 10, NotaPalette.TextMuted);
    private readonly Button _applyButton;
    private Action<int> _setKind = _ => { };

    private MidiToolKind _kind = MidiToolKind.Generate;
    private MidiTool? _tool;
    private MidiToolBlend _blend = MidiToolBlend.Replace;
    private int _seed = 1;

    // The clip as it stood before the preview, plus the selection the tool is scoped to.
    private List<NotaNote> _source = new();
    private HashSet<int> _scope = new();
    private bool _suppressResync;

    public MidiToolsView(PianoRollView roll)
    {
        _roll = roll;
        Width = RailWidth;
        Background = NotaPalette.Panel;
        BorderBrush = NotaPalette.BorderDefault;
        BorderThickness = new Thickness(1, 0, 0, 0);

        var kinds = Segments(new[] { "Generate", "Transform" }, 0, i =>
        {
            var kind = i == 0 ? MidiToolKind.Generate : MidiToolKind.Transform;
            if (_kind == kind) return;
            _kind = kind;
            SelectTool(null);
        }, out _setKind, fill: true);

        var title = SectionTitle("");
        _paramTitle = (TextBlock)title.Children[0];
        var seedBox = Field(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { CapsLabel("SEED"), _seedText },
        });
        var reroll = ClipEditorKit.Button("Reroll", () =>
        {
            _seed = Random.Shared.Next(1, 9999);
            _seedText.Text = _seed.ToString(NotaNum.Culture);
            Preview();
        });
        reroll.HorizontalAlignment = HorizontalAlignment.Right;
        var seedRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        seedRow.Children.Add(seedBox);
        Grid.SetColumn(reroll, 1); seedRow.Children.Add(reroll);
        var parameters = Section(title, 9, _paramList, seedRow);

        var output = Section(SectionTitle("OUTPUT", _scopeText), 8,
            Segments(new[] { "Replace", "Add" }, 0, i =>
            {
                _blend = i == 0 ? MidiToolBlend.Replace : MidiToolBlend.Add;
                Preview();
            }, out _, fill: true));

        _applyButton = new Button { Content = "Apply", Height = 30, FontSize = NotaType.Name, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "primary" } };
        _applyButton.Click += (_, _) => Apply();
        _resultText.HorizontalAlignment = HorizontalAlignment.Center;
        var foot = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                _resultText, _applyButton,
                Pair(ClipEditorKit.Button("Revert", Revert), ClipEditorKit.Button("Defaults", ResetTool)),
            },
        };
        DockPanel.SetDock(foot, Dock.Bottom);
        foot.Margin = new Thickness(0, 10, 8, 0);

        // The scroll content keeps 8px clear on the right for the overlay scrollbar.
        var top = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 8, 0), Children = { kinds, _toolList, Rule(), parameters, Rule(), output } };
        Content = new DockPanel
        {
            Margin = new Thickness(14, 12, 6, 12),
            Children =
            {
                foot,
                new ScrollViewer
                {
                    Content = top,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            },
        };

        RebuildToolList();
        RebuildParams();
        _roll.Changed += OnRollChanged;
        // A preview must not outlive the rail: switching clips replaces the whole editor,
        // and the engine is still holding the previewed notes with nothing in undo to get
        // back from them.
        DetachedFromVisualTree += (_, _) => _roll.CancelPreview();
        Resync();
        PaintResult(null);
    }

    /// <summary>Called by the clip editor when the rail is shown or hidden. Hiding drops any
    /// preview, so a tool the user walked away from never silently sticks.</summary>
    public void SetActive(bool active)
    {
        if (active) { Resync(); Preview(); }
        else { _roll.CancelPreview(); PaintResult(null); }
    }

    // --- state ------------------------------------------------------------

    private void OnRollChanged()
    {
        // ShowPreview raises Changed too; re-reading then would capture the preview as the
        // source and the tool would compound on its own output.
        if (_suppressResync || _roll.Previewing) return;
        Resync();
        PaintApply();
    }

    /// <summary>Re-reads the clip and the selection as the working set for the next run.</summary>
    private void Resync()
    {
        _source = new List<NotaNote>(_roll.SourceNotes);
        _scope = new HashSet<int>(_roll.SelectionIndices.Where(i => i >= 0 && i < _source.Count));
        _scopeText.Text = _scope.Count > 0
            ? $"{_scope.Count} selected"
            : $"clip · {_source.Count}";
    }

    private MidiToolContext Context() => new()
    {
        LengthBeats = _roll.LengthBeats,
        Grid = _roll.Grid,
        ScaleOn = _roll.ScaleOn,
        ScaleRoot = _roll.ScaleRoot,
        ScaleMask = MidiScales.MaskAt(_roll.ScaleIndex),
        Seed = _seed,
    };

    private MidiToolSettings SettingsFor(MidiTool tool)
    {
        if (!_settings.TryGetValue(tool.Id, out var s)) _settings[tool.Id] = s = tool.NewSettings();
        return s;
    }

    private void Preview()
    {
        if (_tool is null) { _roll.CancelPreview(); PaintResult(null); return; }
        var result = MidiToolRunner.Apply(_tool, _source, _scope, SettingsFor(_tool), Context(), _blend);
        _suppressResync = true;
        _roll.ShowPreview(result);
        _suppressResync = false;
        PaintResult(result.Count);
    }

    private void Apply()
    {
        if (_tool is null || !_roll.Previewing) return;
        _suppressResync = true;
        _roll.ApplyPreview();
        _suppressResync = false;
        Resync();
        // Deliberately no re-preview: the applied result is the new source, so running the
        // tool again here would show it applied twice before the user asked for anything.
        // The next knob move (or Reroll) arms a fresh preview from the new source.
        _resultText.Text = $"{_source.Count} notes · applied";
        _resultText.Foreground = NotaPalette.TextMuted;
        PaintApply();
    }

    private void Revert()
    {
        _roll.CancelPreview();
        Resync();
        PaintResult(null);
    }

    private void ResetTool()
    {
        _seed = 1;
        _seedText.Text = "1";
        if (_tool is null) return;
        SettingsFor(_tool).Reset();
        RebuildParams();
        Preview();
    }

    /// <summary>The line over Apply: what the pending result does, or what to do first.</summary>
    private void PaintResult(int? resultCount)
    {
        if (resultCount is not int count)
            _resultText.Text = _tool is null ? "Pick a tool" : "—";
        else
        {
            int delta = count - _source.Count;
            _resultText.Text = _blend == MidiToolBlend.Add
                ? $"{delta:+0;−0;\u00b10} notes"
                : $"{count} notes, was {_source.Count}";
        }
        _resultText.Foreground = NotaPalette.TextMuted;
        PaintApply();
    }

    /// <summary>Apply is solid brass only while something is actually pending.</summary>
    private void PaintApply() => _applyButton.IsEnabled = _roll.Previewing && _tool is not null;

    private void SelectTool(MidiTool? tool)
    {
        _roll.CancelPreview();
        Resync();
        _tool = tool;
        RebuildToolList();
        RebuildParams();
        Preview();
    }

    // --- tool list --------------------------------------------------------

    private void RebuildToolList()
    {
        _setKind(_kind == MidiToolKind.Generate ? 0 : 1);
        _toolList.Children.Clear();
        foreach (var tool in MidiToolCatalog.All.Where(t => t.Kind == _kind))
        {
            bool active = _tool?.Id == tool.Id;
            var name = new TextBlock
            {
                Text = tool.Name, FontSize = NotaType.Name, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center,
                Foreground = active ? NotaPalette.TextPrimary : NotaPalette.TextSecondary,
            };
            var row = new Border
            {
                Height = 26, CornerRadius = NotaRadius.Badge, Padding = new Thickness(10, 0),
                Background = active ? NotaPalette.AccentSubtle : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid
                {
                    Children =
                    {
                        new Border
                        {
                            Width = 2, Margin = new Thickness(-10, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left,
                            Background = active ? NotaPalette.Accent : Brushes.Transparent,
                        },
                        name,
                    },
                },
            };
            if (!string.IsNullOrEmpty(tool.Blurb)) ToolTip.SetTip(row, tool.Blurb);
            if (!active)
            {
                row.PointerEntered += (_, _) => row.Background = NotaPalette.SurfaceRaised;
                row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            }
            var captured = tool;
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                SelectTool(captured);
            };
            _toolList.Children.Add(row);
        }
    }

    // --- parameter list ---------------------------------------------------

    private void RebuildParams()
    {
        _paramList.Children.Clear();
        _paramTitle.Text = _tool is null ? "PARAMETERS" : _tool.Name.ToUpperInvariant();
        if (_tool is null)
        {
            _paramList.Children.Add(new TextBlock { Text = "Pick a tool to see its parameters", FontSize = NotaType.Body, Foreground = NotaPalette.TextTertiary });
            return;
        }
        var settings = SettingsFor(_tool);
        foreach (var p in _tool.Params) _paramList.Children.Add(ParamRow(p, settings));
    }

    private Control ParamRow(MidiToolParam p, MidiToolSettings settings)
    {
        switch (p.Kind)
        {
            case MidiParamKind.Choice:
            {
                var value = Mono(p.Display(settings[p.Id]));
                var drop = Dropdown(value, a => ShowMenu(a, p.Choices ?? Array.Empty<string>(), (int)Math.Round(settings[p.Id]), i =>
                {
                    settings.Set(p.Id, i);
                    value.Text = p.Display(settings[p.Id]);
                    Preview();
                }));
                return Labelled(p.Name, drop);
            }

            case MidiParamKind.Toggle:
            {
                return SwitchRow(p.Name, () => settings.Flag(p.Id), () =>
                {
                    settings.Set(p.Id, settings.Flag(p.Id) ? 0 : 1);
                    Preview();
                }, out _, height: 20);
            }

            default:
            {
                double span = Math.Max(1e-9, p.Max - p.Min);
                return SliderRow(p.Name.ToUpperInvariant(), 62,
                    () => (settings[p.Id] - p.Min) / span,
                    v =>
                    {
                        double raw = p.Min + v * span;
                        if (p.Kind is MidiParamKind.Int or MidiParamKind.Note) raw = Math.Round(raw);
                        if (Math.Abs(raw - settings[p.Id]) < 1e-9) return;
                        settings.Set(p.Id, raw);
                        Preview();
                    },
                    () => p.Display(settings[p.Id]), out _,
                    reset: () => { settings.Set(p.Id, p.Default); Preview(); });
            }
        }
    }

    // A caps label in the slider-label column, then a control filling the rest.
    private static Control Labelled(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("62,*"), ColumnSpacing = 10 };
        grid.Children.Add(CapsLabel(label.ToUpperInvariant()));
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        return grid;
    }

    private static TextBlock CapsLabel(string text) => new()
    {
        Text = text, FontSize = 9, FontWeight = FontWeight.Bold, LetterSpacing = 0.9, Foreground = NotaPalette.TextTertiary,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };
}
