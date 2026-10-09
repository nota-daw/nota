// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette overlay (design "Nota Command Palette", CP-17…CP-20). A 640 px card a fifth
// of the way down the window it was called in — no scrim, so the target stays visible. The
// field carries the target pill (where the highlighted item goes, changing with ⌘ ⌥ ⇧); under
// it the type chips (synced with the > @ + # ~ prefixes, Tab cycles), an RU-layout hint, the
// result list with highlighted matches and "why" lines, and a footer of the keys that apply.
// The search, ranking and targeting live in Nota.Application.Palette; this file only draws
// and routes keys.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application.Palette;

namespace Nota.App;

internal sealed class CommandPaletteView : UserControl
{
    public const double CardWidth = 640, ListMax = 404;

    private static readonly Dictionary<string, string> Icons = new()
    {
        ["action"] = "M5 7 L10 12 L5 17 M12 17 H19",
        ["track"] = "M4 7 H20 M4 12 H20 M4 17 H13",
        ["device"] = "M4 5 H20 V19 H4 Z M9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12",
        ["plugin"] = "M9 3 V7 M15 3 V7 M6 7 H18 V11 A6 6 0 0 1 6 11 Z M12 17 V21",
        ["preset"] = "M7 4 H17 V20 L12 16 L7 20 Z",
        ["modular"] = "M4 7 A2.5 2.5 0 1 0 9 7 A2.5 2.5 0 1 0 4 7 M15 17 A2.5 2.5 0 1 0 20 17 A2.5 2.5 0 1 0 15 17 M8.5 8.5 L15.5 15.5",
    };
    private const string SearchIcon = "M10.5 4 A6.5 6.5 0 1 0 10.5 17 A6.5 6.5 0 1 0 10.5 4 M15.5 15.5 L21 21";

    private readonly PaletteSearch _search;
    private readonly PaletteIndex _index;
    private readonly TextBox _input;
    private readonly Border _card, _pill;
    private readonly TextBlock _pillText, _count, _status, _layoutText;
    private readonly Border _layoutRow;
    private readonly StackPanel _chips, _list, _hints;
    private readonly ScrollViewer _scroll;

    private PaletteContext _ctx = new();
    private Func<PaletteItem, Availability>? _avail;
    private Func<string, Availability>? _actionCheck;
    private PaletteResults? _results;
    private readonly List<Control> _rowControls = new();
    private int _sel;
    private ApplyMode _mods;
    private bool _suppressText;

    /// <summary>Applies an item; returns the context to continue with (⇧Enter keeps the
    /// palette open, the insert point moved on), or null to close.</summary>
    public Func<PaletteItem, ApplyMode, PaletteContext, PaletteContext?>? Apply { get; set; }
    /// <summary>Raised after the palette closed (Esc, click outside, applied).</summary>
    public event Action? Closed;

    public bool IsOpen => IsVisible;
    public PaletteContext Context => _ctx;

    public CommandPaletteView(PaletteSearch search, PaletteIndex index)
    {
        _search = search;
        _index = index;
        IsVisible = false;

        _input = new TextBox
        {
            Classes = { "search" }, FontSize = 14, PlaceholderText = "Search actions, tracks, devices, presets…",
            VerticalAlignment = VerticalAlignment.Center, AcceptsReturn = false, AcceptsTab = false,
            Foreground = NotaPalette.TextPrimary,
        };
        _input.TextChanged += (_, _) => { if (!_suppressText) { _sel = 0; Refresh(); } };
        _input.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);

        _pillText = Mono(11, NotaPalette.TextStrong);
        _pillText.TextTrimming = TextTrimming.CharacterEllipsis;
        _pill = new Border
        {
            Height = 24, MaxWidth = 310, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Control,
            VerticalAlignment = VerticalAlignment.Center, Child = _pillText, IsVisible = false,
        };
        var fieldGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        fieldGrid.Children.Add(Icon(SearchIcon, 14, NotaPalette.TextSecondary, 2));
        Grid.SetColumn(_input, 1); fieldGrid.Children.Add(_input);
        Grid.SetColumn(_pill, 2); fieldGrid.Children.Add(_pill);
        var field = new Border
        {
            Height = 38, Padding = new Thickness(10, 0, 6, 0), Background = NotaPalette.BgSunken,
            BorderBrush = NotaPalette.Accent, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
            Child = fieldGrid, Margin = new Thickness(10, 10, 10, 8),
        };

        _chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _count = Mono(10, NotaPalette.TextTertiary);
        _count.VerticalAlignment = VerticalAlignment.Center;
        var chipRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 0, 10, 8) };
        chipRow.Children.Add(_chips);
        Grid.SetColumn(_count, 1); chipRow.Children.Add(_count);

        _layoutText = new TextBlock { FontSize = 11, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        _layoutRow = new Border
        {
            Height = 24, Padding = new Thickness(12, 0), Background = NotaPalette.Panel, IsVisible = false,
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Child = _layoutText,
        };

        _list = new StackPanel { Margin = new Thickness(0, 4) };
        _scroll = new ScrollViewer
        {
            Content = _list, MaxHeight = ListMax, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var listWrap = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Child = _scroll };

        _hints = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        _status = Mono(10, NotaPalette.TextMuted);
        _status.VerticalAlignment = VerticalAlignment.Center;
        var footGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footGrid.Children.Add(_hints);
        Grid.SetColumn(_status, 1); footGrid.Children.Add(_status);
        var footer = new Border
        {
            Height = 32, Padding = new Thickness(12, 0), Background = NotaPalette.Panel,
            BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(0, 1, 0, 0), Child = footGrid,
        };

        var body = new DockPanel();
        DockPanel.SetDock(field, Dock.Top); body.Children.Add(field);
        DockPanel.SetDock(chipRow, Dock.Top); body.Children.Add(chipRow);
        DockPanel.SetDock(_layoutRow, Dock.Top); body.Children.Add(_layoutRow);
        DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        body.Children.Add(listWrap);

        _card = new Border
        {
            Width = CardWidth, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Body, ClipToBounds = true, Child = body,
        };
        // Clicks outside the card close the palette; nothing dims the window (CP-17).
        var catcher = new Border { Background = Brushes.Transparent };
        catcher.PointerPressed += (_, e) => { e.Handled = true; Close(); };
        Content = new Panel { Children = { catcher, _card } };

        SizeChanged += (_, e) => PlaceCard(e.NewSize.Height);
        AddHandler(KeyDownEvent, (_, e) => TrackMods(e.KeyModifiers), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, (_, e) => TrackMods(e.KeyModifiers), RoutingStrategies.Tunnel, handledEventsToo: true);
        _index.Changed += () => { if (IsVisible) Dispatcher.UIThread.Post(() => { if (IsVisible) Refresh(); }, DispatcherPriority.Background); };
    }

    private void PlaceCard(double hostHeight) => _card.Margin = new Thickness(0, Math.Max(24, hostHeight * 0.2), 0, 0);

    // ---- open / close ------------------------------------------------------------------

    public void Open(PaletteContext ctx, Func<PaletteItem, Availability> availability, Func<string, Availability> actionCheck)
    {
        _ctx = ctx;
        _avail = availability;
        _actionCheck = actionCheck;
        _mods = ApplyMode.Default;
        _sel = 0;
        _suppressText = true;
        _input.Text = "";
        _suppressText = false;
        IsVisible = true;
        PlaceCard(Bounds.Height > 0 ? Bounds.Height : 600);
        Refresh();
        Dispatcher.UIThread.Post(() => _input.Focus(), DispatcherPriority.Input);
    }

    public void Close()
    {
        if (!IsVisible) return;
        IsVisible = false;
        _results = null;
        _list.Children.Clear();
        _rowControls.Clear();
        Closed?.Invoke();
    }

    // ---- results -------------------------------------------------------------------------

    private void Refresh()
    {
        string q = _input.Text ?? "";
        _results = _search.Run(q, _ctx, _avail, PaletteTargets.Suggested(_ctx));
        _sel = Math.Clamp(_sel, 0, Math.Max(0, _results.Items.Count - 1));
        BuildChips(_results.Query);
        BuildRows();
        _count.Text = _results.Query.IsEmpty ? "" : _results.Items.Count == 1 ? "1 result" : $"{_results.Items.Count} results";
        _layoutRow.IsVisible = _results.LayoutHint.Length > 0;
        if (_layoutRow.IsVisible)
        {
            _layoutText.Inlines = new InlineCollection
            {
                new Run("Showing results for "),
                new Run(_results.LayoutHint) { FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextPrimary },
                new Run(" · typed in the other keyboard layout"),
            };
        }
        _status.Text = _index.Status;
        UpdateTarget();
    }

    private void BuildChips(ParsedQuery q)
    {
        _chips.Children.Clear();
        foreach (var (label, prefix) in ChipList())
        {
            bool on = prefix == q.Prefix;
            var chip = new Border
            {
                Height = 22, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Control, Cursor = new Cursor(StandardCursorType.Hand),
                Background = on ? NotaPalette.BorderDefault : Brushes.Transparent,
                BorderBrush = on ? NotaPalette.BorderStrong : Brushes.Transparent, BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center,
                                        Foreground = on ? NotaPalette.TextPrimary : NotaPalette.TextSecondary },
                        Mono(10, NotaPalette.TextTertiary, prefix),
                    },
                },
            };
            string p = prefix;
            chip.PointerPressed += (_, e) => { e.Handled = true; SetPrefix(p); };
            _chips.Children.Add(chip);
        }
    }

    private IEnumerable<(string Label, string Prefix)> ChipList()
    {
        yield return ("All", "");
        foreach (var (p, k, label) in ParsedQuery.Prefixes)
            if (k != PaletteKind.Modular || _ctx.Origin == PaletteOrigin.Modular) yield return (label, p.ToString());
    }

    private void SetPrefix(string prefix)
    {
        var q = ParsedQuery.Parse(_input.Text ?? "");
        _input.Text = prefix + q.Rest;
        _input.CaretIndex = _input.Text.Length;
        _sel = 0;
        _input.Focus();
    }

    private void BuildRows()
    {
        _list.Children.Clear();
        _rowControls.Clear();
        var r = _results!;
        if (!r.Query.IsEmpty && r.Items.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "Nothing matches. Try a role (reverb, comp), a character (warm, dark) or a source (vocals, drums).",
                FontSize = 12, Foreground = NotaPalette.TextMuted, Margin = new Thickness(14, 18), TextWrapping = TextWrapping.Wrap,
            });
            return;
        }
        int i = 0;
        foreach (var row in r.Rows)
        {
            if (row.IsHeader) { _list.Children.Add(Header(row.Header!)); continue; }
            var c = Row(row.Hit, i, r.ReasonFor(row.Hit));
            _rowControls.Add(c);
            _list.Children.Add(c);
            i++;
        }
        PaintSelection();
    }

    private static Control Header(string label)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Height = 24, Margin = new Thickness(12, 0) };
        var t = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.Bold, LetterSpacing = 0.9, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        var line = new Border { Height = 1, Background = NotaPalette.GraphBorder, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7) };
        g.Children.Add(t);
        Grid.SetColumn(line, 1); g.Children.Add(line);
        return g;
    }

    private Control Row(PaletteHit hit, int index, string reason)
    {
        var it = hit.Item;
        string iconKey = it.Kind switch
        {
            PaletteKind.Device => it.IsPlugin ? "plugin" : "device",
            PaletteKind.Track => "track", PaletteKind.Preset => "preset", PaletteKind.Modular => "modular", _ => "action",
        };
        var icon = Icon(Icons[iconKey], 15, NotaPalette.TextMuted, 1.6);
        icon.Tag = "icon";

        var name = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        name.Inlines = NameRuns(it, hit.Mask, hit.Disabled);
        var sub = new TextBlock { Text = it.Sub, FontSize = 11, Foreground = NotaPalette.TextMuted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var line1 = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(name, Dock.Left);
        name.Margin = new Thickness(0, 0, 8, 0);
        line1.Children.Add(name);
        line1.Children.Add(sub);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { line1 } };
        if (reason.Length > 0) text.Children.Add(Mono(10, NotaPalette.AccentHover, reason));

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        if (hit.Disabled && hit.Why.Length > 0)
            right.Children.Add(new TextBlock { Text = hit.Why, FontSize = 10.5, Foreground = NotaPalette.DangerBright, VerticalAlignment = VerticalAlignment.Center });
        else if (it.Kind == PaletteKind.Action && it.Gesture.Length > 0)
            foreach (var cap in Caps(it.Gesture)) right.Children.Add(KeyCap(cap, 18, 10));
        else if (it.Tag.Length > 0)
        {
            var tag = Mono(10, NotaPalette.TextMuted, it.Tag);
            tag.Width = 64; tag.TextAlignment = TextAlignment.Right;
            right.Children.Add(tag);
        }

        var edge = new Border { Width = 2, CornerRadius = NotaRadius.Bar, Margin = new Thickness(0, 3), HorizontalAlignment = HorizontalAlignment.Left, Tag = "edge" };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(12, 5) };
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        Grid.SetColumn(right, 2); grid.Children.Add(right);
        var row = new Border
        {
            MinHeight = 32, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
            Child = new Panel { Children = { grid, edge } },
        };
        ToolTip.SetTip(row, it.Descriptor?.Description is { Length: > 0 } d ? d : null);
        row.PointerEntered += (_, _) => { if (_sel != index) { _sel = index; PaintSelection(); UpdateTarget(); } };
        row.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _sel = index;
            Activate(ModeOf(e.KeyModifiers));
        };
        return row;
    }

    // The name with the matched letters in brass; the "Nota" prefix quieter (as in the browser).
    private static InlineCollection NameRuns(PaletteItem it, ulong mask, bool disabled)
    {
        var runs = new InlineCollection();
        string n = it.Name;
        int prefix = n.StartsWith("Nota ", StringComparison.Ordinal) ? 5 : 0;
        int i = 0;
        while (i < n.Length)
        {
            int kind = Kind(i);
            int j = i + 1;
            while (j < n.Length && Kind(j) == kind) j++;
            runs.Add(new Run(n[i..j])
            {
                Foreground = kind switch
                {
                    2 => disabled ? NotaPalette.AccentDim : NotaPalette.AccentHover,
                    1 => NotaPalette.TextTertiary,
                    _ => disabled ? NotaPalette.TextMuted : NotaPalette.TextPrimary,
                },
            });
            i = j;
        }
        return runs;

        int Kind(int k) => k < 64 && (mask & (1UL << k)) != 0 ? 2 : k < prefix ? 1 : 0;
    }

    private void PaintSelection()
    {
        for (int i = 0; i < _rowControls.Count; i++)
        {
            bool on = i == _sel;
            var row = (Border)_rowControls[i];
            row.Background = on ? NotaPalette.SurfaceHover : Brushes.Transparent;
            var panel = (Panel)row.Child!;
            foreach (var c in panel.Children)
                if (c is Border { Tag: "edge" } edge) edge.Background = on ? NotaPalette.Accent : Brushes.Transparent;
            if (((Grid)panel.Children[0]).Children[0] is Path icon) icon.Stroke = on ? NotaPalette.Accent : NotaPalette.TextMuted;
        }
        if (_sel >= 0 && _sel < _rowControls.Count) _rowControls[_sel].BringIntoView();
    }

    private PaletteItem? Current => _results is { } r && _sel >= 0 && _sel < r.Items.Count ? r.Items[_sel].Item : null;

    // CP-6: the pill names where the highlighted item would go under the held modifiers.
    private void UpdateTarget()
    {
        var (text, bad) = PaletteTargets.Describe(Current, _ctx, _mods, _actionCheck);
        _pill.IsVisible = text.Length > 0;
        _pillText.Text = text;
        _pill.Background = bad ? NotaPalette.Wash(NotaPalette.Danger, 0x30) : NotaPalette.GraphBorder;
        _pillText.Foreground = bad ? NotaPalette.DangerBright : NotaPalette.TextStrong;
        BuildHints();
    }

    private void BuildHints()
    {
        _hints.Children.Clear();
        var cur = Current;
        if (cur is null) return;
        var hints = new List<(string Caps, string Label)>();
        switch (cur.Kind)
        {
            case PaletteKind.Action: hints.Add(("↵", "run")); break;
            case PaletteKind.Track: hints.Add(("↵", "select")); hints.Add(("⌘↵", "open in Devices")); break;
            case PaletteKind.Modular: hints.Add(("↵", "add to canvas")); hints.Add(("⇧↵", "add & stay")); break;
            default:
                if (cur.Role == PaletteDeviceRole.Instrument) { hints.Add(("↵", "load")); hints.Add(("⌘↵", "new track")); }
                else { hints.Add(("↵", "insert")); hints.Add(("⌥↵", "replace")); }
                hints.Add(("⇧↵", "add & stay"));
                break;
        }
        hints.Add(("Tab", "filter"));
        foreach (var (caps, label) in hints)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            var capRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            foreach (var c in caps == "Tab" ? ["Tab"] : Caps(caps)) capRow.Children.Add(KeyCap(c, 16, 9));
            row.Children.Add(capRow);
            row.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
            _hints.Children.Add(row);
        }
    }

    // ---- keys (CP-20) --------------------------------------------------------------------

    private void TrackMods(KeyModifiers m)
    {
        var mode = ModeOf(m);
        if (mode == _mods || !IsVisible) return;
        _mods = mode;
        UpdateTarget();
    }

    private static ApplyMode ModeOf(KeyModifiers m)
    {
        var mode = ApplyMode.Default;
        if (ArrangementView.IsPrimaryDown(m)) mode |= ApplyMode.NewTrack;
        if ((m & KeyModifiers.Alt) != 0) mode |= ApplyMode.Replace;
        if ((m & KeyModifiers.Shift) != 0) mode |= ApplyMode.KeepOpen;
        return mode;
    }

    private void OnInputKey(object? sender, KeyEventArgs e)
    {
        int n = _results?.Items.Count ?? 0;
        void Move(int d) { e.Handled = true; if (n == 0) return; _sel = Math.Clamp(_sel + d, 0, n - 1); PaintSelection(); UpdateTarget(); }
        switch (e.Key)
        {
            case Key.Down: Move(1); return;
            case Key.Up: Move(-1); return;
            case Key.PageDown: Move(10); return;
            case Key.PageUp: Move(-10); return;
            case Key.Home when e.KeyModifiers == KeyModifiers.None && n > 0: Move(-n); return;
            case Key.End when e.KeyModifiers == KeyModifiers.None && n > 0: Move(n); return;
            case Key.Escape: e.Handled = true; Close(); return;
            case Key.Enter:
                e.Handled = true;
                Activate(ModeOf(e.KeyModifiers));
                return;
            case Key.Tab:
            {
                e.Handled = true;
                var list = ChipList().Select(c => c.Prefix).ToList();
                int at = list.IndexOf(ParsedQuery.Parse(_input.Text ?? "").Prefix);
                int next = (at + ((e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1) + list.Count) % list.Count;
                SetPrefix(list[next]);
                return;
            }
        }
    }

    private void Activate(ApplyMode mode)
    {
        if (_results is null || _sel < 0 || _sel >= _results.Items.Count) return;
        var hit = _results.Items[_sel];
        if (hit.Disabled) return;   // the row says why; the palette stays (CP-4)
        var next = Apply?.Invoke(hit.Item, mode, _ctx);
        if (next is not null && (mode & ApplyMode.KeepOpen) != 0 && IsVisible)
        {
            _ctx = next;
            Refresh();
            _input.Focus();
        }
        else Close();
    }

    // ---- bits ------------------------------------------------------------------------------

    /// <summary>Glyph shortcut → key caps ("⌘⇧P" → ⌘ ⇧ P on macOS, Ctrl Shift P elsewhere).</summary>
    internal static List<string> Caps(string glyphs)
    {
        var caps = new List<string>();
        bool mac = OperatingSystem.IsMacOS();
        int i = 0;
        for (; i < glyphs.Length; i++)
        {
            string? m = glyphs[i] switch
            {
                '⌘' => mac ? "⌘" : "Ctrl", '⌃' => mac ? "⌃" : "Ctrl", '⌥' => mac ? "⌥" : "Alt", '⇧' => mac ? "⇧" : "Shift", _ => null,
            };
            if (m is null) break;
            caps.Add(m);
        }
        if (i < glyphs.Length) caps.Add(glyphs[i..]);
        return caps;
    }

    internal static Control KeyCap(string label, double size, double fontSize) => new Border
    {
        MinWidth = size, Height = size, Padding = new Thickness(3, 0), CornerRadius = NotaRadius.Badge,
        BorderBrush = NotaPalette.BorderStrong, BorderThickness = new Thickness(1, 1, 1, 2), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = label, FontSize = fontSize, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextStrong,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static TextBlock Mono(double size, IBrush ink, string text = "") => new()
    {
        Text = text, FontSize = size, FontFamily = NotaFonts.MonoFamily, Foreground = ink, VerticalAlignment = VerticalAlignment.Center,
    };

    private static Path Icon(string data, double size, IBrush stroke, double thickness) => new()
    {
        Data = Geometry.Parse(data), Width = size, Height = size, Stretch = Stretch.Uniform, Stroke = stroke, StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>The line that confirms what the palette did, with Undo (design: bottom-centre, 4 s).</summary>
internal sealed class PaletteToast : Border
{
    private readonly TextBlock _text = new() { FontSize = 11, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _undo;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private Action? _onUndo;

    public PaletteToast()
    {
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 48);
        Height = 32;
        Padding = new Thickness(12, 0, 6, 0);
        CornerRadius = NotaRadius.Panel;
        Background = NotaPalette.SurfaceHover;
        BorderBrush = NotaPalette.BorderStrong;
        BorderThickness = new Thickness(1);
        var undoLabel = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Undo", FontSize = 11, Foreground = NotaPalette.Accent, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = MenuKit.Keys("⌘Z"), FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        _undo = new Border
        {
            Height = 22, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Control, Background = NotaPalette.BorderDefault,
            Cursor = new Cursor(StandardCursorType.Hand), Child = undoLabel, VerticalAlignment = VerticalAlignment.Center,
        };
        _undo.PointerPressed += (_, e) => { e.Handled = true; var u = _onUndo; Hide(); u?.Invoke(); };
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _text, _undo } };
        _timer.Tick += (_, _) => Hide();
    }

    public void Show(string text, Action? undo)
    {
        _text.Text = text;
        _onUndo = undo;
        _undo.IsVisible = undo is not null;
        IsVisible = true;
        _timer.Stop();
        _timer.Start();
    }

    private void Hide() { _timer.Stop(); IsVisible = false; _onUndo = null; }
}
