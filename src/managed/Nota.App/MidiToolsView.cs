// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The clip-tools rail on the right of the piano roll: a Generate / Transform
// picker, a parameter list built from the tool's own descriptor (no per-tool
// panel), and the seed / blend / apply row. Every knob move re-runs the tool and
// previews the result in the roll — audible, undo-free — so Apply is a decision
// the user has already heard.

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

namespace Nota.App;

public sealed class MidiToolsView : UserControl
{
    private static IBrush Panel => NotaPalette.SurfaceCard;
    private static IBrush Sunken => NotaPalette.BgSunken;
    private static IBrush Raised => NotaPalette.SurfaceRaised;
    private static IBrush BorderDef => NotaPalette.BorderDefault;
    private static IBrush BorderStrong => NotaPalette.BorderStrong;
    private static IBrush Brass => NotaPalette.Accent;
    private static IBrush AccentBright => NotaPalette.AccentBright;
    private static IBrush AccentSubtle => NotaPalette.AccentSubtle;
    private static IBrush TextPrimary => NotaPalette.TextPrimary;
    private static IBrush TextSecondary => NotaPalette.TextSecondary;
    private static IBrush TextTertiary => NotaPalette.TextTertiary;

    public const double RailWidth = 236;

    private readonly PianoRollView _roll;
    private readonly Dictionary<string, MidiToolSettings> _settings = new(StringComparer.Ordinal);

    private readonly StackPanel _toolList = new() { Spacing = 2 };
    private readonly StackPanel _paramList = new() { Spacing = 5 };
    private readonly TextBlock _blurb;
    private readonly TextBlock _scopeText;
    private readonly TextBlock _seedText;
    private readonly TextBlock _resultText;
    private Border _generateTab = null!, _transformTab = null!;
    private Border _replaceChip = null!, _addChip = null!;
    private Border? _applyButton;

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
        Background = Panel;
        BorderBrush = BorderDef;
        BorderThickness = new Thickness(1, 0, 0, 0);

        _blurb = new TextBlock { FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, MinHeight = 24 };
        _scopeText = new TextBlock { FontSize = 9, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap };
        _resultText = Mono("—");
        _resultText.Foreground = TextTertiary;
        _seedText = Mono("1");

        var body = new StackPanel { Spacing = 9, Margin = new Thickness(10) };
        body.Children.Add(KindTabs());
        body.Children.Add(new Border
        {
            Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(3), MaxHeight = 236,
            Child = new ScrollViewer { Content = _toolList, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        });
        body.Children.Add(_blurb);
        body.Children.Add(Rule());
        body.Children.Add(Section("PARAMETERS", _paramList));
        body.Children.Add(Rule());
        body.Children.Add(SeedRow());
        body.Children.Add(Section("OUTPUT", new StackPanel { Spacing = 5, Children = { BlendRow(), _scopeText, ResultRow() } }));
        body.Children.Add(ActionRow());

        Content = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        RebuildToolList();
        RebuildParams();
        _roll.Changed += OnRollChanged;
        // A preview must not outlive the rail: switching clips replaces the whole editor,
        // and the engine is still holding the previewed notes with nothing in undo to get
        // back from them.
        DetachedFromVisualTree += (_, _) => _roll.CancelPreview();
        Resync();
    }

    /// <summary>Called by the clip editor when the rail is shown or hidden. Hiding drops any
    /// preview, so a tool the user walked away from never silently sticks.</summary>
    public void SetActive(bool active)
    {
        if (active) { Resync(); Preview(); }
        else _roll.CancelPreview();
    }

    // --- state ------------------------------------------------------------

    private void OnRollChanged()
    {
        // ShowPreview raises Changed too; re-reading then would capture the preview as the
        // source and the tool would compound on its own output.
        if (_suppressResync || _roll.Previewing) return;
        Resync();
    }

    /// <summary>Re-reads the clip and the selection as the working set for the next run.</summary>
    private void Resync()
    {
        _source = new List<NotaNote>(_roll.SourceNotes);
        _scope = new HashSet<int>(_roll.SelectionIndices.Where(i => i >= 0 && i < _source.Count));
        _scopeText.Text = _scope.Count > 0
            ? $"Scope · {_scope.Count} selected note{(_scope.Count == 1 ? "" : "s")}"
            : $"Scope · whole clip ({_source.Count} note{(_source.Count == 1 ? "" : "s")})";
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
        if (_tool is null) { _roll.CancelPreview(); return; }
        var result = MidiToolRunner.Apply(_tool, _source, _scope, SettingsFor(_tool), Context(), _blend);
        _suppressResync = true;
        _roll.ShowPreview(result);
        _suppressResync = false;
        int delta = result.Count - _source.Count;
        _resultText.Text = $"{result.Count} notes ({delta:+0;-0;\u00b10})";
        _resultText.Foreground = delta == 0 ? TextTertiary : AccentBright;
        PaintApply();
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
        // The next knob move (or New seed) arms a fresh preview from the new source.
        _resultText.Text = $"{_source.Count} notes · applied";
        _resultText.Foreground = TextSecondary;
        PaintApply();
    }

    private void Revert()
    {
        _roll.CancelPreview();
        Resync();
        _resultText.Text = "—";
        _resultText.Foreground = TextTertiary;
        PaintApply();
    }

    /// <summary>Apply reads as available only while something is actually pending.</summary>
    private void PaintApply()
    {
        if (_applyButton is null) return;
        bool pending = _roll.Previewing;
        _applyButton.Opacity = pending ? 1 : 0.45;
        _applyButton.Cursor = new Cursor(pending ? StandardCursorType.Hand : StandardCursorType.Arrow);
    }

    private void SelectTool(MidiTool? tool)
    {
        _roll.CancelPreview();
        Resync();
        _tool = tool;
        _blurb.Text = tool?.Blurb ?? "";
        RebuildToolList();
        RebuildParams();
        Preview();
    }

    // --- tool list --------------------------------------------------------

    private Control KindTabs()
    {
        _generateTab = KindTab("Generate", MidiToolKind.Generate);
        _transformTab = KindTab("Transform", MidiToolKind.Transform);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        g.Children.Add(_generateTab);
        Grid.SetColumn(_transformTab, 1);
        g.Children.Add(_transformTab);
        return g;
    }

    private Border KindTab(string label, MidiToolKind kind)
    {
        var t = new TextBlock { Text = label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border
        {
            Height = 24, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand), Child = t, Tag = kind,
        };
        b.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (_kind == kind) return;
            _kind = kind;
            PaintKindTabs();
            SelectTool(null);
        };
        return b;
    }

    private void PaintKindTabs()
    {
        foreach (var b in new[] { _generateTab, _transformTab })
        {
            bool active = (MidiToolKind)b.Tag! == _kind;
            b.Background = active ? AccentSubtle : Raised;
            b.BorderBrush = active ? Brass : BorderStrong;
            ((TextBlock)b.Child!).Foreground = active ? AccentBright : TextSecondary;
        }
    }

    private void RebuildToolList()
    {
        PaintKindTabs();
        _toolList.Children.Clear();
        foreach (var tool in MidiToolCatalog.All.Where(t => t.Kind == _kind))
        {
            bool active = _tool?.Id == tool.Id;
            var text = new TextBlock
            {
                Text = tool.Name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = active ? AccentBright : TextPrimary, Margin = new Thickness(8, 0, 0, 0),
            };
            var row = new Border
            {
                Height = 24, CornerRadius = new CornerRadius(4),
                Background = active ? AccentSubtle : Brushes.Transparent,
                BorderBrush = active ? Brass : Brushes.Transparent, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), Child = text,
            };
            var captured = tool;
            row.PointerPressed += (_, e) => { e.Handled = true; SelectTool(captured); };
            _toolList.Children.Add(row);
        }
    }

    // --- parameter rail ---------------------------------------------------

    private void RebuildParams()
    {
        _paramList.Children.Clear();
        if (_tool is null)
        {
            _paramList.Children.Add(new TextBlock { Text = "Pick a tool.", FontSize = 9, Foreground = TextTertiary });
            return;
        }
        var settings = SettingsFor(_tool);
        foreach (var p in _tool.Params) _paramList.Children.Add(ParamRow(p, settings));
    }

    private Control ParamRow(MidiToolParam p, MidiToolSettings settings)
    {
        var value = Mono(p.Display(settings[p.Id]));
        value.Foreground = TextSecondary;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.Margin = new Thickness(0);

        Control control;
        switch (p.Kind)
        {
            case MidiParamKind.Choice:
            {
                value.Text = p.Display(settings[p.Id]) + "  \u25be";
                var chip = Chip(value);
                chip.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    var flyout = new MenuFlyout();
                    for (int i = 0; i < (p.Choices?.Length ?? 0); i++)
                    {
                        int idx = i;
                        var item = new MenuItem { Header = p.Choices![i] };
                        if (Math.Abs(settings[p.Id] - i) < 0.5) item.Icon = new TextBlock { Text = "·", Foreground = AccentBright };
                        item.Click += (_, _) =>
                        {
                            settings.Set(p.Id, idx);
                            value.Text = p.Display(settings[p.Id]) + "  \u25be";
                            Preview();
                        };
                        flyout.Items.Add(item);
                    }
                    flyout.ShowAt(chip);
                };
                // The chip already carries the read-out, so the row is label + chip.
                return TwoColumn(p.Name, chip);
            }

            case MidiParamKind.Toggle:
            {
                var chip = Chip(value);
                void Paint()
                {
                    bool on = settings.Flag(p.Id);
                    chip.Background = on ? AccentSubtle : Raised;
                    chip.BorderBrush = on ? Brass : BorderStrong;
                    value.Foreground = on ? AccentBright : TextSecondary;
                    value.Text = p.Display(settings[p.Id]);
                }
                Paint();
                chip.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    settings.Set(p.Id, settings.Flag(p.Id) ? 0 : 1);
                    Paint();
                    Preview();
                };
                return TwoColumn(p.Name, chip);
            }

            default:
            {
                double span = Math.Max(1e-9, p.Max - p.Min);
                var fader = new MiniFader((settings[p.Id] - p.Min) / span, 1.0)
                {
                    Accent = true,
                    Default = (p.Default - p.Min) / span,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                fader.ValueChanged += v =>
                {
                    double raw = p.Min + v * span;
                    if (p.Kind is MidiParamKind.Int or MidiParamKind.Note) raw = Math.Round(raw);
                    if (Math.Abs(raw - settings[p.Id]) < 1e-9) return;
                    settings.Set(p.Id, raw);
                    value.Text = p.Display(settings[p.Id]);
                    Preview();
                };
                control = fader;
                break;
            }
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("72,*,50"), ColumnSpacing = 6, Height = 16 };
        grid.Children.Add(new TextBlock { Text = p.Name, FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        Grid.SetColumn(value, 2); value.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(value);
        return grid;
    }

    private static Control TwoColumn(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("72,*"), ColumnSpacing = 6 };
        grid.Children.Add(new TextBlock { Text = label, FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        return grid;
    }

    // --- seed / blend / actions -------------------------------------------

    private Control SeedRow()
    {
        var box = FieldBox(22);
        _seedText.Text = _seed.ToString();
        box.Child = _seedText;

        var reroll = TextChip("New seed");
        reroll.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _seed = Random.Shared.Next(1, 9999);
            _seedText.Text = _seed.ToString();
            Preview();
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        grid.Children.Add(box);
        Grid.SetColumn(reroll, 1); grid.Children.Add(reroll);
        return Section("SEED", grid);
    }

    private Control BlendRow()
    {
        _replaceChip = TextChip("Replace");
        _addChip = TextChip("Add");
        void Paint()
        {
            foreach (var (chip, blend) in new[] { (_replaceChip, MidiToolBlend.Replace), (_addChip, MidiToolBlend.Add) })
            {
                bool active = _blend == blend;
                chip.Background = active ? AccentSubtle : Raised;
                chip.BorderBrush = active ? Brass : BorderStrong;
                ((TextBlock)chip.Child!).Foreground = active ? AccentBright : TextSecondary;
            }
        }
        _replaceChip.PointerPressed += (_, e) => { e.Handled = true; _blend = MidiToolBlend.Replace; Paint(); Preview(); };
        _addChip.PointerPressed += (_, e) => { e.Handled = true; _blend = MidiToolBlend.Add; Paint(); Preview(); };
        Paint();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        grid.Children.Add(_replaceChip);
        Grid.SetColumn(_addChip, 1); grid.Children.Add(_addChip);
        return grid;
    }

    private Control ResultRow()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
        grid.Children.Add(new TextBlock { Text = "Result", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        _resultText.HorizontalAlignment = HorizontalAlignment.Right;
        _resultText.Margin = new Thickness(0);
        Grid.SetColumn(_resultText, 1); grid.Children.Add(_resultText);
        return grid;
    }

    private Control ActionRow()
    {
        var apply = new Border
        {
            Height = 26, Background = AccentSubtle, BorderBrush = Brass, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "Apply", FontSize = 11, Foreground = AccentBright, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        apply.PointerPressed += (_, e) => { e.Handled = true; Apply(); };
        _applyButton = apply;
        PaintApply();

        var revert = TextChip("Revert");
        revert.Height = 26;
        revert.PointerPressed += (_, e) => { e.Handled = true; Revert(); };

        var reset = TextChip("Defaults");
        reset.Height = 26;
        reset.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (_tool is null) return;
            SettingsFor(_tool).Reset();
            RebuildParams();
            Preview();
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        top.Children.Add(revert);
        Grid.SetColumn(reset, 1); top.Children.Add(reset);

        return new StackPanel
        {
            Spacing = 6, Margin = new Thickness(0, 2, 0, 6),
            Children =
            {
                apply, top,
                new TextBlock
                {
                    Text = "Every change is previewed in the roll — Apply keeps it as one undo step.",
                    FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap,
                },
            },
        };
    }

    // --- helpers ----------------------------------------------------------

    private static TextBlock Mono(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 10, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        t.BindResource(FontFamilyProperty, "Font.Mono");
        return t;
    }

    private static Control Section(string title, Control content) => new StackPanel
    {
        Spacing = 5,
        Children =
        {
            new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TextTertiary },
            content,
        },
    };

    private static Control Rule() => new Border { Height = 1, Background = BorderDef };

    private static Border FieldBox(double h) => new()
    {
        Height = h, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
    };

    private static Border Chip(Control child) => new()
    {
        Height = 20, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
        Cursor = new Cursor(StandardCursorType.Hand), Child = child, Padding = new Thickness(0, 0, 6, 0),
    };

    private static Border TextChip(string text) => new()
    {
        Height = 22, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
        Cursor = new Cursor(StandardCursorType.Hand), Padding = new Thickness(8, 0),
        Child = new TextBlock { Text = text, FontSize = 10, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };
}
