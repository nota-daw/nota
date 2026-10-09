// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// "Create multisample" — the auto-mapping preview (mockup 2a). Files or a folder go through
// MultisampleMapper: instruments by name stem on the left (each with its confidence), the
// octave convention (C4 or C3 = 60, the pitch detector's verdict explained) and the mapping
// options; on the right the chosen instrument's zone map with what needs a look — gaps where
// neighbours stretched, duplicates, notes that came from analysis — and every file with the
// note, layer, round-robin step and release mark it got. "Create N presets" saves one Nota
// Mosaic preset per checked instrument under Nota Mosaic → Packs → <folder>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Mosaic;
using static Nota.App.DeviceCardKit;
using Path = System.IO.Path;

namespace Nota.App;

public sealed class MosaicCreateWindow : NotaWindow
{
    /// <summary>The presets written (path + program), in instrument order; empty when cancelled.</summary>
    public IReadOnlyList<(string Path, MosaicProgram Program)> Created => _created;
    private readonly List<(string, MosaicProgram)> _created = new();

    private readonly IMosaicPacks _packs;
    private readonly IReadOnlyList<string> _inputs;
    private readonly string _folderName;
    private readonly MultisampleOptions _opt = new();
    private MultisampleProposal? _prop;
    private int _sel;

    private readonly StackPanel _instList = new() { Spacing = 3 };
    private readonly ContentControl _octave = new();
    private readonly ContentControl _mapping = new();
    private readonly TextBlock _instTitle = new() { FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _instSub = Mono("", TextTertiary, 8);
    private readonly MosaicZoneMap _map = new() { Interactive = false };
    private readonly WrapPanel _issues = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _headerInfo = Mono("", TextTertiary, 8);
    private readonly TextBlock _where = new() { FontSize = 8.5, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _create = new() { Classes = { "primary" } };

    public MosaicCreateWindow(IMosaicPacks packs, IReadOnlyList<string> inputs, string? breadcrumb = null)
    {
        _packs = packs;
        _inputs = inputs;
        _folderName = inputs.Count == 1 && Directory.Exists(inputs[0]) ? Path.GetFileName(inputs[0].TrimEnd('/', '\\'))
            : Path.GetFileName(Path.GetDirectoryName(inputs.FirstOrDefault() ?? "") ?? "Multisamples");
        if (string.IsNullOrWhiteSpace(_folderName)) _folderName = "Multisamples";
        Title = "Create multisample";
        Width = 860; Height = 520; MinWidth = 760; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(12, 8) };
        head.Children.Add(new TextBlock { Text = "Create multisample", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center });
        var crumb = Mono(breadcrumb ?? _folderName, TextTertiary, 8.5);
        Grid.SetColumn(crumb, 1); head.Children.Add(crumb);
        Grid.SetColumn(_headerInfo, 2); head.Children.Add(_headerInfo);

        // Left: instruments · octave · mapping.
        var left = new StackPanel
        {
            Spacing = 6, Width = 210,
            Children =
            {
                Panel("Instruments", new ScrollViewer { Content = _instList, MaxHeight = 190, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, "by name stem"),
                Panel("Octave", _octave),
                Panel("Mapping", _mapping),
            },
        };

        // Right: the chosen instrument.
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { LegendItem("stretched", NotaPalette.TextTertiary), LegendItem("next to gap", NotaPalette.Danger), LegendItem("low confidence", NotaPalette.Warning) },
        };
        var instHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 4) };
        instHead.Children.Add(_instTitle);
        Grid.SetColumn(_instSub, 1); instHead.Children.Add(_instSub);
        Grid.SetColumn(legend, 3); instHead.Children.Add(legend);
        _map.Height = 150;
        var tableHead = TableRow("FILE", "NOTE", "LAYER", "RR", "RELEASE", "CONF", "", TextTertiary, caps: true);
        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        table.Children.Add(tableHead);
        var scroll = new ScrollViewer { Content = _rows };
        Grid.SetRow(scroll, 1); table.Children.Add(scroll);
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 6 };
        right.Children.Add(instHead);
        Grid.SetRow(_map, 1); right.Children.Add(_map);
        Grid.SetRow(_issues, 2); right.Children.Add(_issues);
        Grid.SetRow(table, 3); right.Children.Add(table);
        var rightBox = Box(right);

        var mid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Margin = new Thickness(12, 0, 12, 8) };
        mid.Children.Add(new ScrollViewer { Content = left, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetColumn(rightBox, 1); mid.Children.Add(rightBox);

        var cancel = new Button { Content = "Cancel", Classes = { "ghost" } };
        cancel.Click += (_, _) => Close();
        _create.Click += (_, _) => Create();
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(12, 0, 12, 10) };
        foot.Children.Add(_where);
        Grid.SetColumn(cancel, 1); foot.Children.Add(cancel);
        Grid.SetColumn(_create, 2); foot.Children.Add(_create);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(head);
        Grid.SetRow(mid, 1); root.Children.Add(mid);
        Grid.SetRow(foot, 2); root.Children.Add(foot);
        SetBody(root);

        _instTitle.Text = "Reading the files…";
        _create.IsEnabled = false;
        _create.Content = "Create presets";
        _where.Text = $"Presets will appear in Nota Mosaic → Packs → {_folderName}";
        Opened += async (_, _) => await MapAsync();
    }

    // ---- mapping ---------------------------------------------------------------------------------
    private async Task MapAsync()
    {
        var opt = _opt;
        var inputs = _inputs;
        var prop = await Task.Run(() =>
        {
            var files = new List<string>();
            foreach (var p in inputs)
            {
                if (Directory.Exists(p))
                {
                    try { files.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Where(MultisampleMapper.IsAudio)); } catch { }
                }
                else if (MultisampleMapper.IsAudio(p)) files.Add(p);
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return MultisampleMapper.MapFiles(files, opt, _packs.DetectNote);
        });
        _prop = prop;
        if (_opt.OctaveShift is null) _opt.OctaveShift = prop.OctaveShift;   // keep the verdict when options change
        _sel = Math.Clamp(_sel, 0, Math.Max(0, prop.Instruments.Count - 1));
        Paint();
    }

    private void Paint()
    {
        if (_prop is not { } prop) return;
        int files = prop.Instruments.Sum(i => i.Files.Count);
        _headerInfo.Text = $"{files} files · {NotaNum.Bytes(prop.Bytes)}";

        // Instruments.
        _instList.Children.Clear();
        for (int i = 0; i < prop.Instruments.Count; i++)
        {
            int iv = i;
            var inst = prop.Instruments[i];
            bool sel = i == _sel;
            var check = new CheckBox { IsChecked = inst.Selected, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
            check.IsCheckedChanged += (_, _) => { inst.Selected = check.IsChecked == true; PaintFoot(); };
            string layers = inst.Layers.Count > 1 ? string.Join("/", inst.Layers) : $"{inst.Layers.Count} layer";
            var name = new TextBlock { Text = inst.Stem, FontSize = 9, FontWeight = sel ? FontWeight.SemiBold : FontWeight.Normal, Foreground = sel ? AccentBright : TextPrimary };
            var sub = Mono($"{inst.Files.Count} files · {inst.Roots} roots · {layers}{(inst.MaxRr > 1 ? $" · rr {inst.MaxRr}" : "")}", TextTertiary, 7.5);
            var conf = Mono(NotaNum.Unit(inst.Confidence, "0", "%"), inst.Confidence >= 85 ? TextPrimary : NotaPalette.Warning, 8.5);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
            g.Children.Add(check);
            var txt = new StackPanel { Spacing = 1, Children = { name, sub } };
            Grid.SetColumn(txt, 1); g.Children.Add(txt);
            Grid.SetColumn(conf, 2); g.Children.Add(conf);
            var b = new Border
            {
                Padding = new Thickness(6, 4), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                BorderBrush = sel ? NotaPalette.BorderBrass : Brushes.Transparent, Background = sel ? NotaPalette.AccentSubtle : Brushes.Transparent, Child = g,
            };
            b.PointerPressed += (_, e) => { if (e.Source is CheckBox) return; _sel = iv; Paint(); };
            _instList.Children.Add(b);
        }
        if (prop.Instruments.Count == 0) _instList.Children.Add(new TextBlock { Text = "No audio files with a note in their name or a clear pitch.", FontSize = 8.5, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap });

        // Octave.
        var oct = Segments(new[] { "C4 = 60", "C3 = 60" }, () => (_opt.OctaveShift ?? 0) == 12 ? 1 : 0, iv => { _opt.OctaveShift = iv == 1 ? 12 : 0; Remap(); }, out _, fill: true, fontSize: 8.5);
        _octave.Content = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                oct,
                new TextBlock { Text = prop.OctaveWhy.Length > 0 ? prop.OctaveWhy : "The names are taken as written (C4 = 60).", FontSize = 8, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap },
            },
        };

        // Mapping.
        var inst0 = prop.Instruments.Count > 0 ? prop.Instruments[Math.Clamp(_sel, 0, prop.Instruments.Count - 1)] : null;
        var stretch = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3,
            Children = { Step("−", () => { _opt.EdgeStretch = Math.Max(0, _opt.EdgeStretch - 1); Remap(); }), Mono($"±{_opt.EdgeStretch} st", TextPrimary, 9), Step("+", () => { _opt.EdgeStretch = Math.Min(48, _opt.EdgeStretch + 1); Remap(); }) },
        };
        var layerText = inst0 is null || inst0.Layers.Count <= 1 ? "one layer"
            : string.Join(" · ", inst0.Layers.Select(l => { var f = inst0.Used.First(x => !x.Release && x.Layer == l); return $"{l} {f.VelLo}–{f.VelHi}"; }));
        int rel = inst0?.Used.Count(f => f.Release) ?? 0;
        var rrSeg = Segments(new[] { "Seq", "Rnd", "No repeat" }, () => _opt.RrMode, iv => { _opt.RrMode = iv; }, out _, fill: true, fontSize: 8);
        _mapping.Content = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                KV("Edge zone stretch", stretch),
                new TextBlock { Text = "Velocity layers by dynamics", FontSize = 8.5, Foreground = TextSecondary },
                Mono(layerText, TextPrimary, 8.5),
                KV("Release samples → Release group", Mono(rel.ToString(NotaNum.Culture), TextPrimary, 9)),
                KV("Round-robin", Mono(inst0?.MaxRr > 1 ? $"rr {inst0.MaxRr}" : "none", TextPrimary, 9)),
                rrSeg,
            },
        };

        PaintInstrument(inst0);
        PaintFoot();
    }

    private void PaintInstrument(MappedInstrument? inst)
    {
        _rows.Children.Clear();
        _issues.Children.Clear();
        _map.Flagged.Clear();
        if (inst is null) { _instTitle.Text = "—"; _instSub.Text = ""; _map.Set(Array.Empty<MosaicZone>(), false, -1, "nothing to map"); return; }
        var prog = MultisampleMapper.Build(inst, _opt);
        var (lo, hi) = inst.Span;
        _instTitle.Text = inst.Stem;
        _instSub.Text = $"{MosaicNames.NoteName(lo)} – {MosaicNames.NoteName(hi)} · {prog.Zones.Count} zones";
        foreach (var g in inst.Gaps) _map.Flagged.Add(g);
        _map.Set(prog.Zones, false, -1);

        foreach (var g in inst.Gaps)
            _issues.Children.Add(Chip($"Gap at {MosaicNames.NoteName(g)} — neighbours stretched", NotaPalette.Danger));
        int dups = inst.Files.Count(f => f.Duplicate);
        if (dups > 0) _issues.Children.Add(Chip($"{dups} duplicate{(dups == 1 ? "" : "s")}", NotaPalette.Danger));
        int an = inst.Files.Count(f => f.NoteFrom == "analysis");
        if (an > 0) _issues.Children.Add(Chip($"{an} note{(an == 1 ? "" : "s")} from sample analysis", NotaPalette.Warning));
        int none = inst.Files.Count(f => f.Note < 0);
        if (none > 0) _issues.Children.Add(Chip($"{none} without a note — left out", NotaPalette.TextTertiary));

        foreach (var f in inst.Files.OrderBy(f => f.Duplicate).ThenBy(f => f.Note).ThenBy(f => f.Name, StringComparer.Ordinal))
        {
            string note = f.Note < 0 ? "—" : f.NoteFrom == "analysis" ? $"{f.NoteText} · analysis"
                : f.NamedNote != f.Note ? $"{MosaicNames.NoteName(f.NamedNote)} → {f.NoteText}"
                : f.NoteFrom == "midi" ? $"{f.NoteText} · MIDI" : f.NoteText;
            string action = f.Duplicate ? "duplicate" : f.Note < 0 ? "skipped" : f.LayerGuessed ? "layer?" : "";
            IBrush ink = f.Duplicate || f.Note < 0 ? TextTertiary : TextPrimary;
            var row = TableRow(f.Name, note, f.Release ? "—" : f.Layer.Length > 0 ? f.Layer : "—", f.Rr > 0 ? f.Rr.ToString(NotaNum.Culture) : "—",
                f.Release ? "rel" : "—", f.Confidence > 0 ? f.Confidence.ToString(NotaNum.Culture) : "—", action, ink, strike: f.Duplicate,
                confInk: f.Confidence > 0 && f.Confidence < 80 ? NotaPalette.Warning : null, actionInk: f.Duplicate ? NotaPalette.DangerBright : NotaPalette.AccentBright);
            if (f.Duplicate || f.NoteFrom == "analysis") row.Background = NotaPalette.Wash(NotaPalette.Danger, 0x14);
            _rows.Children.Add(row);
        }
    }

    private void PaintFoot()
    {
        int n = _prop?.Instruments.Count(i => i.Selected && i.Used.Any()) ?? 0;
        _create.Content = n == 1 ? "Create 1 preset" : $"Create {n} presets";
        _create.IsEnabled = n > 0;
    }

    private async void Remap()
    {
        _instTitle.Text = "Mapping…";
        var keep = _prop?.Instruments.Where(i => !i.Selected).Select(i => i.Stem).ToHashSet() ?? new HashSet<string>();
        await MapAsync();
        if (_prop is not null) foreach (var i in _prop.Instruments) i.Selected = !keep.Contains(i.Stem);
        Paint();
    }

    private void Create()
    {
        if (_prop is null) return;
        var chosen = _prop.Instruments.Where(i => i.Selected && i.Used.Any()).ToList();
        foreach (var inst in chosen)
        {
            string name = chosen.Count == 1 && _prop.Instruments.Count == 1 ? MultisampleMapper.Pretty(_folderName.Replace(' ', '_')) : MultisampleMapper.Pretty(inst.Stem);
            var prog = MultisampleMapper.Build(inst, _opt, _packs.ToRef, name);
            prog.SourceRef = _inputs.Count == 1 ? _packs.ToRef(_inputs[0]) : "";
            string path = _packs.Save(prog, name, _folderName);
            _created.Add((path, prog));
        }
        Close();
    }

    // ---- pieces ------------------------------------------------------------------------------------
    private static TextBlock Mono(string t, IBrush ink, double fs) => new() { Text = t, FontSize = fs, Foreground = ink, FontFamily = NotaFonts.MonoFamily, VerticalAlignment = VerticalAlignment.Center };

    private static Border Box(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
        Padding = new Thickness(8), Child = child,
    };

    private static Border Panel(string title, Control body, string? note = null)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 5) };
        head.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.SectionLabelTracking, Foreground = TextTertiary });
        if (note is not null) { var n = Mono(note, TextTertiary, 7.5); Grid.SetColumn(n, 1); head.Children.Add(n); }
        return Box(new StackPanel { Children = { head, body } });
    }

    private static Control KV(string k, Control v)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(new TextBlock { Text = k, FontSize = 8.5, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(v, 1); g.Children.Add(v);
        return g;
    }

    private static Control Step(string glyph, Action click)
    {
        var b = new Border
        {
            Width = 16, Height = 16, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = BorderDef, Background = NotaPalette.SurfaceRaised,
            Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = glyph, FontSize = 9, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.PointerPressed += (_, e) => { if (e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) { click(); e.Handled = true; } };
        return b;
    }

    private static Control Chip(string text, IBrush ink) => new Border
    {
        Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(7, 2), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = ink,
        Background = NotaPalette.Wash(NotaPalette.SurfaceRaised, 0xCC), Child = new TextBlock { Text = text, FontSize = 8, Foreground = TextPrimary },
    };

    private static Control LegendItem(string text, IBrush ink) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 4,
        Children = { new Border { Width = 8, Height = 8, BorderBrush = ink, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, VerticalAlignment = VerticalAlignment.Center }, Mono(text, TextTertiary, 7.5) },
    };

    private static Border TableRow(string file, string note, string layer, string rr, string rel, string conf, string action, IBrush ink,
        bool caps = false, bool strike = false, IBrush? confInk = null, IBrush? actionInk = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,48,30,48,40,60"), ColumnSpacing = 6, Height = caps ? 16 : 18 };
        string[] cells = { file, note, layer, rr, rel, conf, action };
        for (int i = 0; i < cells.Length; i++)
        {
            var tb = caps
                ? new TextBlock { Text = cells[i], FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.KnobLabelTracking, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center }
                : Mono(cells[i], i == 5 && confInk is not null ? confInk : i == 6 ? actionInk ?? TextTertiary : ink, 8.5);
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            if (strike && i == 0) tb.TextDecorations = TextDecorations.Strikethrough;
            if (i == 6) tb.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(tb, i); g.Children.Add(tb);
        }
        return new Border { Padding = new Thickness(6, 0), Child = g, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 0, 0, caps ? 1 : 0) };
    }
}
