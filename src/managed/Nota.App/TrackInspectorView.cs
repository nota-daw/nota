// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The track inspector (specs/060, #186): the browser's Track inspector tab, showing the track
// the Devices panel shows — name, mute / solo / arm, record input + monitor or MIDI from,
// colour, row height and freeze — so those settings have a home besides the track menu. Every
// control calls what the header and the menu call, so undo and arming behave the same.
// Changes made elsewhere (header, menu, undo, keys) are picked up while the tab is on screen:
// a snapshot of what it shows — including every record-input and MIDI-from choice — is taken four
// times a second and the panel rebuilt when it differs. While freeze or export has suspended
// engine polling, the timer does not read the engine: that job owns it on another thread.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;

namespace Nota.App;

internal sealed class TrackInspectorView : UserControl
{
    private static readonly (string Label, double Height)[] Heights =
    {
        ("Compact", ArrangementView.CompactRowHeight), ("Standard", ArrangementView.StandardRowHeight), ("Large", ArrangementView.LargeRowHeight),
    };

    private readonly IAudioEngine _engine;
    private readonly ArrangementView _timeline;
    private readonly Func<int, IReadOnlyList<FreezeEntry>> _freezeEntries;
    private readonly Func<bool> _engineSuspended;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _trackId = -1;
    private string _shown = "";
    private TextBox? _nameBox;

    public TrackInspectorView(IAudioEngine engine, ArrangementView timeline, Func<int, IReadOnlyList<FreezeEntry>> freezeEntries,
        Func<bool> engineSuspended)
    {
        _engine = engine;
        _timeline = timeline;
        _freezeEntries = freezeEntries;
        _engineSuspended = engineSuspended;
        _poll.Tick += (_, _) => Poll();
        Rebuild();
    }

    /// <summary>Shows this track (≤ 0: none).</summary>
    public void Show(int trackId)
    {
        if (trackId == _trackId) return;
        _trackId = trackId;
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
        _poll.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _poll.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Poll()
    {
        // Freeze and export run on a worker and replace the session when they finish. The
        // progress dialog keeps this timer running, so leave the engine alone until the job
        // is done (the same SuspendEnginePolling the playhead clock honours).
        if (_engineSuspended()) return;
        // A folded browser keeps the tab attached but hidden; a name being typed must not be
        // swept away by a rebuild.
        if (!IsEffectivelyVisible || _nameBox?.IsFocused == true) return;
        if (Snapshot() != _shown) Rebuild();
    }

    private bool IsMaster => _trackId > 0 && _trackId == _engine.MasterTrackId;

    private bool TryInfo(out NotaTrackInfo info)
    {
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out info) && info.Id == _trackId) return true;
        info = default;
        return false;
    }

    private static bool IsAudio(in NotaTrackInfo ti) => !ti.IsInstrument && !ti.IsReturn && !ti.IsGroup;

    // Everything the panel shows, as one string: a difference means something changed elsewhere.
    private string Snapshot()
    {
        if (IsMaster) return "master|" + _engine.GetTrackName(_trackId);
        if (_trackId <= 0 || !TryInfo(out var ti)) return "none";
        int id = ti.Id;
        var sb = new StringBuilder();
        sb.Append(ti.IsInstrument).Append(ti.IsReturn).Append(ti.IsGroup).Append('|')
          .Append(TrackNames.Of(_engine, ti)).Append('|')
          .Append(_engine.GetTrackColorIndex(id)).Append(',').Append(ArrangementView.EffectiveColorIndex(_engine, id)).Append('|')
          .Append(ti.Muted).Append(ti.Soloed).Append(ti.Armed).Append('|')
          .Append(_timeline.TrackRowHeight(id)).Append('|');
        if (IsAudio(ti))
        {
            var pick = RecordInputPick.Of(_engine, id);
            sb.Append(pick).Append(_engine.GetTrackMonitor(id));
            if (pick.Source > 0) sb.Append(TrackNames.Of(_engine, pick.Source));
            foreach (var c in _timeline.RecordInputChoices(id))
                sb.Append('|').Append(c.Pick.Source).Append(':').Append((int)c.Pick.Channels).Append('=').Append(c.Label);
        }
        if (ti.IsInstrument)
        {
            int src = _engine.GetTrackMidiSource(id);
            sb.Append(src);
            if (src > 0) sb.Append(TrackNames.Of(_engine, src));
            foreach (var c in _timeline.MidiSourceChoices(id))
                sb.Append('|').Append(c.Id).Append('=').Append(c.Name);
        }
        foreach (var f in _freezeEntries(id)) sb.Append('|').Append(f.Label).Append(f.Switch);
        return sb.ToString();
    }

    private void Rebuild()
    {
        if (_engineSuspended()) return;
        _shown = Snapshot();
        _nameBox = null;
        var s = new StackPanel { Spacing = 18, Margin = new Thickness(12, 12, 12, 16) };
        if (IsMaster) BuildMaster(s);
        else if (_trackId > 0 && TryInfo(out var ti)) BuildTrack(s, ti);
        else
        {
            Content = new TextBlock
            {
                Text = "No track selected", FontSize = 11, Foreground = NotaPalette.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }
        Content = new ScrollViewer { Content = s, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // ---- sections -----------------------------------------------------------------------

    private void BuildMaster(StackPanel s)
    {
        string name = _engine.GetTrackName(_trackId) is { Length: > 0 } m ? m : "Master";
        s.Children.Add(Title(NotaPalette.Master, name, "MASTER"));
        s.Children.Add(Note("Every track ends up here. Its devices are in the Devices panel."));
    }

    private void BuildTrack(StackPanel s, NotaTrackInfo ti)
    {
        int id = ti.Id;
        bool audio = IsAudio(ti);
        bool frozen = (audio || ti.IsInstrument) && _engine.IsTrackFrozen(id);
        string kind = ti.IsReturn ? "Return" : ti.IsGroup ? "Group" : ti.IsInstrument ? "MIDI track" : "Audio track";
        s.Children.Add(Title(ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(_engine, id)), TrackNames.Of(_engine, ti),
            NotaText.Label(frozen ? kind + " · frozen" : kind)));

        s.Children.Add(Section("Name", NameBox(ti)));

        var state = new UniformGrid { Rows = 1, Columns = 3, ColumnSpacing = 6 };
        state.Children.Add(Toggle("Mute", ti.Muted != 0, NotaPalette.Mute, "Mute the track",
            (on, _) => { _engine.SetTrackMute(id, on); Edited(); }));
        if (!ti.IsReturn)
            state.Children.Add(Toggle("Solo", ti.Soloed != 0, NotaPalette.Solo, "Solo the track",
                (on, _) => { _engine.SetTrackSolo(id, on); Edited(); }));
        if (audio || ti.IsInstrument)
            state.Children.Add(Toggle("Arm", ti.Armed != 0, NotaPalette.Record, TrackArming.Tip,
                (on, mods) => { TrackArming.Set(_engine, id, on, TrackArming.IsAdditive(mods)); Edited(); }));
        s.Children.Add(state);

        if (audio) s.Children.Add(Section("Input", InputRows(id)));
        if (ti.IsInstrument) s.Children.Add(Section("Input", MidiFromRow(id)));

        s.Children.Add(Section("Color", Swatches(id)));

        if (!ti.IsReturn)
        {
            double h = _timeline.TrackRowHeight(id);
            int chosen = Array.FindIndex(Heights, p => Math.Abs(p.Height - h) < 0.5);
            s.Children.Add(Section("Height", SessionView.Segmented(Heights.Select(p => p.Label).ToList(), chosen,
                i => { _timeline.SetTrackHeights(new[] { id }, Heights[i].Height); Rebuild(); })));
        }

        var freeze = _freezeEntries(id);
        if (freeze.Count > 0) s.Children.Add(Section("Freeze", FreezeButtons(freeze)));
    }

    // Enter or leaving the box renames; Esc puts the name back. An empty name returns the
    // track to its automatic one (the instrument's, "Audio 3"…).
    private Control NameBox(NotaTrackInfo ti)
    {
        int id = ti.Id;
        string shown = TrackNames.Of(_engine, ti);
        var box = new TextBox { Text = shown, FontSize = 11, Height = NotaSize.Shell, MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center };
        // Leaving the box also happens when a rebuild (another track selected) drops it, so the
        // rename and the redraw it causes wait for the rebuild to finish.
        void Commit()
        {
            string text = (box.Text ?? "").Trim();
            if (text == shown) return;
            Dispatcher.UIThread.Post(() =>
            {
                _engine.SetTrackName(id, text);
                _timeline.RefreshAfterTrackEdit();
                Rebuild();
            });
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            if (e.Key == Key.Escape) box.Text = shown;
            e.Handled = true;
            TopLevel.GetTopLevel(box)?.FocusManager?.Focus(null);
        };
        box.LostFocus += (_, _) => Commit();
        _nameBox = box;
        return box;
    }

    private Control InputRows(int id)
    {
        var choices = _timeline.RecordInputChoices(id);
        var cur = RecordInputPick.Of(_engine, id);
        int sel = choices.FindIndex(c => c.Pick.Matches(cur));
        string label = sel >= 0 ? choices[sel].Label : RecordInputPick.Hardware[0].Label;
        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(SessionView.Row("Record input", SessionView.Chip(label, false, a =>
            SessionView.Pick(a, choices.Select(c => (c.Pick, c.Label)), p => p.Matches(cur),
                p => { p.ApplyTo(_engine, id); Edited(); }))));

        // Master can't be monitored (it would feed back), as on the header's monitor chip.
        bool canMonitor = cur.Source != -1;
        bool monitor = _engine.GetTrackMonitor(id);
        var chip = SessionView.Chip(monitor ? "On" : "Off", monitor, _ => { _engine.SetTrackMonitor(id, !monitor); Edited(); });
        chip.IsEnabled = canMonitor;
        ToolTip.SetTip(chip, canMonitor ? "Hear the input live through this track" : "Master can't be monitored (it would feed back)");
        rows.Children.Add(SessionView.Row("Monitor input", chip));
        return rows;
    }

    private Control MidiFromRow(int id)
    {
        int cur = _engine.GetTrackMidiSource(id);
        var choices = new List<(int Id, string Name)> { (-1, "None") };
        choices.AddRange(_timeline.MidiSourceChoices(id));
        string label = choices.FirstOrDefault(c => c.Id == cur).Name ?? "None";
        var chip = SessionView.Chip(label, false, a =>
            SessionView.Pick(a, choices.Select(c => (c.Id, c.Name)), v => v == cur, v => { _engine.SetTrackMidiSource(id, v); Edited(); }));
        ToolTip.SetTip(chip, "Play another instrument track's MIDI through this track's instrument");
        return SessionView.Row("MIDI from", chip);
    }

    // The track palette — each base in its normal, light and dark shade — and Auto, the
    // stored colour ringed, as in the track menu.
    private Control Swatches(int id)
    {
        int current = _engine.GetTrackColorIndex(id);
        int bases = ArrangementView.PaletteBases, shades = ArrangementView.PaletteShades;
        var grid = new UniformGrid { Columns = bases };
        for (int shade = 0; shade < shades; shade++)
            for (int b = 0; b < bases; b++)
            {
                int idx = b * shades + shade;
                grid.Children.Add(Swatch(ArrangementView.TrackBrush(idx), idx == current, () => _timeline.SetTrackColours(new[] { id }, idx)));
            }
        var auto = SessionView.Chip("Auto", current == -1, _ => _timeline.SetTrackColours(new[] { id }, -1));
        ToolTip.SetTip(auto, "The colour the track's position gives it");
        auto.HorizontalAlignment = HorizontalAlignment.Left;
        return new StackPanel { Spacing = 6, Children = { grid, auto } };
    }

    private Control Swatch(IBrush fill, bool on, Action pick)
    {
        var sw = new Border
        {
            Height = 18, Margin = new Thickness(2), CornerRadius = NotaRadius.Badge, Background = fill,
            BorderBrush = on ? NotaPalette.TextPrimary : Brushes.Transparent, BorderThickness = new Thickness(on ? 1.5 : 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        sw.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(sw).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            pick();
            Rebuild();
        };
        return sw;
    }

    private Control FreezeButtons(IReadOnlyList<FreezeEntry> entries)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var f in entries)
        {
            var ink = f.Danger ? NotaPalette.Danger : NotaPalette.TextPrimary;
            string text = f.Switch is { } on ? $"{f.Label} · {(on ? "on" : "off")}" : f.Label;
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { new Glyph(f.Icon, 12) { Foreground = ink, VerticalAlignment = VerticalAlignment.Center },
                             new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.Medium, Foreground = ink, VerticalAlignment = VerticalAlignment.Center } },
            };
            var b = new Button { Height = NotaSize.Shell, Padding = new Thickness(10, 0), Margin = new Thickness(0, 0, 6, 6), Content = content };
            var run = f.Run;
            b.Click += (_, _) => run();
            wrap.Children.Add(b);
        }
        return wrap;
    }

    // ---- pieces -------------------------------------------------------------------------

    private void Edited()
    {
        _timeline.RefreshAfterTrackEdit();
        Rebuild();
    }

    private static Control Title(IBrush color, string title, string sub)
    {
        var dot = new Border { Width = 10, Height = 10, CornerRadius = NotaRadius.Clip, Background = color, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        var head = new DockPanel { Children = { dot, name } };
        DockPanel.SetDock(dot, Dock.Left);
        dot.Margin = new Thickness(0, 0, 8, 0);
        var s = ClipEditorKit.Mono(sub, 10, NotaPalette.TextTertiary);
        s.TextTrimming = TextTrimming.CharacterEllipsis;
        return new StackPanel { Spacing = 4, Children = { head, s } };
    }

    private static Control Section(string head, Control body)
        => new StackPanel { Spacing = 8, Children = { SessionView.SectionHead(head), body } };

    private static TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 11, LineHeight = 16, Foreground = NotaPalette.TextTertiary, TextWrapping = TextWrapping.Wrap,
    };

    // A worded state key (Mute / Solo / Arm), filled in its colour while on — the header's
    // chips at inspector size.
    private static Control Toggle(string text, bool on, SolidColorBrush fill, string tip, Action<bool, KeyModifiers> set)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 10, FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Foreground = on ? NotaPalette.InkOn(fill) : NotaPalette.TextSecondary,
        };
        var chip = new Border
        {
            Height = 22, CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Child = tb,
            Background = on ? fill : NotaPalette.SurfaceRaised, BorderBrush = on ? fill : NotaPalette.Seam,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(chip, tip);
        chip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            set(!on, e.KeyModifiers);
        };
        return chip;
    }
}
