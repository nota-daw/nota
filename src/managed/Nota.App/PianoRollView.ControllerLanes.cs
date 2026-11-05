// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PianoRollView
{
    // Extra lanes own only their view/gesture state. Curves stay in the active clip and
    // use the primary controller editor's cache, time mapping and undo boundary.
    private sealed record ExtraControllerLane(ControllerLane Lane, Grid Row, Control Container, int Slot)
    {
        public string HeightKey => $"extra:{Slot}:cc:{Lane.Controller ?? MidiController.Sustain}";
    }
    private readonly List<ExtraControllerLane> _controllerLanes = new();
    private readonly StackPanel _controllerRows = new();
    private readonly ScrollViewer _controllerScroll = new();
    private readonly Dictionary<string, double> _laneHeights = new();
    private ISettingsService? _laneSettings;
    private const double ControllerHeaderH = 20, AddLaneH = 20;
    private const double PrimaryLaneMinimum = VelH + AddLaneH;
    private readonly Control _primaryControllerBody;
    private readonly Button _primaryControllerPicker;
    private readonly Control _primaryControllerScale;

    public ISettingsService? LaneSettings
    {
        get => _laneSettings;
        set
        {
            _laneSettings = value;
            _laneHeights.Clear();
            if (value?.Current.PianoRollLaneHeights is { } saved)
                foreach (var pair in saved) _laneHeights[pair.Key] = pair.Value;
            FitControllerLanes();
        }
    }

    private string PrimaryLaneKey => _laneMode == LaneMode.Controller ? ControllerKey(_laneCtl)
        : _laneMode.ToString();
    private static string ControllerKey(int controller) => $"cc:{controller}";
    private double LaneHeight(string key, double fallback)
        => MidiLaneHeight.Normalize(_laneHeights.GetValueOrDefault(key, fallback), fallback);

    // The divider captures its own pointer; no note/controller gesture is started.
    // Saving happens once at release, never on every UI frame of a resize.
    private Control ResizableLane(Grid row, Func<string> key)
    {
        var divider = new Border { Height = 6, Background = Brushes.Transparent,
            BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0),
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
        ToolTip.SetTip(divider, Loc.T("piano_roll.lanes.drag_to_resize"));
        double startY = 0, startHeight = 0;
        string? dragging = null;
        void Finish()
        {
            if (dragging is null) return;
            dragging = null;
            if (_laneSettings is { } settings)
            {
                settings.Current.PianoRollLaneHeights = new Dictionary<string, double>(_laneHeights);
                settings.Save();
            }
        }
        divider.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(divider).Properties.IsLeftButtonPressed) return;
            dragging = key(); startY = e.GetPosition(this).Y; startHeight = row.Height;
            e.Pointer.Capture(divider); e.Handled = true;
        };
        divider.PointerMoved += (_, e) =>
        {
            if (dragging is null) return;
            ResizeLane(dragging, startHeight + startY - e.GetPosition(this).Y);
            e.Handled = true;
        };
        divider.PointerReleased += (_, e) =>
        {
            if (dragging is null) return;
            Finish(); e.Pointer.Capture(null); e.Handled = true;
        };
        divider.PointerCaptureLost += (_, _) => Finish();
        var container = new DockPanel();
        DockPanel.SetDock(divider, Dock.Top);
        container.Children.Add(divider); container.Children.Add(row);
        return container;
    }

    private void ResizeLane(string key, double height)
    {
        _laneHeights[key] = MidiLaneHeight.Normalize(height, ExprH);
        FitControllerLanes();
    }

    private Control AddControllerLaneButton()
    {
        var add = new Button
        {
            Name = "AddControllerLane", Classes = { "ghost" }, Height = AddLaneH,
            Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2,
                Children = { new Glyph(GlyphKind.Plus, 8), new TextBlock
                    { Text = Loc.T("piano_roll.lanes.lane"), FontFamily = NotaFonts.Sans.FontFamily, FontSize = 8 } },
            },
        };
        ToolTip.SetTip(add, Loc.T("piano_roll.lanes.add"));
        add.Click += (_, _) => AddControllerLane();
        return add;
    }

    private Control ControllerLaneRows()
    {
        _controllerScroll.Name = "ControllerLanes";
        _controllerScroll.Content = _controllerRows;
        _controllerScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _controllerScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        SizeChanged += (_, _) => FitControllerLanes();
        FitControllerLanes();
        return _controllerScroll;
    }

    private void FitControllerLanes()
    {
        // Leave room for notes even in the normal short docked detail panel.
        double available = Math.Max(0, Bounds.Height - RulerH - 10 - 64 - 6);
        // A large stack remains scrollable instead of consuming the entire note area.
        _clipStripScroll.MaxHeight = Math.Max(ClipStrip.RowH, (available - PrimaryLaneMinimum) / 2);
        double strip = _strip.IsVisible ? Math.Min(_strip.Height, _clipStripScroll.MaxHeight) : 0;
        double space = Math.Max(0, available - strip);
        // Five tabs and the sidebar action fit even when restoring an older saved height.
        _laneRow.Height = Math.Max(PrimaryLaneMinimum, Math.Min(
            LaneHeight(PrimaryLaneKey, _laneMode == LaneMode.Velocity ? VelH : ExprH), space));
        foreach (var item in _controllerLanes)
            item.Row.Height = LaneHeight(item.HeightKey, ExprH);
        _controllerScroll.MaxHeight = Math.Max(0, space - _laneRow.Height);
    }

    private static Button LaneButton(GlyphKind glyph, string tip)
    {
        var button = new Button { Classes = { "icon" }, Width = 22, Height = 22,
            Padding = new Thickness(3), Content = new Glyph(glyph, 12) };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private void AddControllerLane()
    {
        int ctl = ControllersWithData().Concat(CommonControllers)
            .Concat(Enumerable.Range(0, 128).Where(c => !MidiController.IsReserved(c)))
            .FirstOrDefault(c => !_controllerLanes.Any(l => l.Lane.Controller == c)
                && (_laneMode != LaneMode.Controller || c != _laneCtl), MidiController.Sustain);
        var lane = new ControllerLane(this) { Controller = ctl, ClipToBounds = true, Focusable = true };
        var scale = new ControllerValueScale(lane);
        Button picker = null!;
        void Select(int id)
        {
            lane.ResetGesture();
            lane.Controller = id;
            UpdateControllerPicker(picker, id);
            lane.InvalidateVisual();
            scale.InvalidateVisual();
            FitControllerLanes();
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{(_rowsArePads ? FoldedPadKeysW : KeysW)},*"), Height = ExprH };
        int slot = Enumerable.Range(0, _controllerLanes.Count + 1).First(s => _controllerLanes.All(l => l.Slot != s));
        var container = ResizableLane(row, () => $"extra:{slot}:cc:{lane.Controller ?? MidiController.Sustain}");
        var item = new ExtraControllerLane(lane, row, container, slot);
        row.Children.Add(new Border
        {
            Background = KeysBg, BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 1, 0),
            Child = scale,
        });
        var body = ControllerLaneBody(lane, Select, () => RemoveControllerLane(item),
            () => _controllerLanes.Contains(item), out picker);
        Grid.SetColumn(body, 1); row.Children.Add(body);
        _keyColumns.Add(row.ColumnDefinitions[0]);
        _controllerLanes.Add(item); _controllerRows.Children.Add(container);
        FitControllerLanes();
    }

    // Controls are siblings of the graph, so they cannot start a drawing gesture or
    // cover a curve point. Both the primary lane and extra lanes use this one shell.
    private Control ControllerLaneBody(ControllerLane lane, Action<int> select,
        Action remove, Func<bool> active, out Button picker)
    {
        var button = new Button
        {
            Name = "ControllerPicker", Classes = { "ghost" }, Height = ControllerHeaderH,
            Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 260,
        };
        UpdateControllerPicker(button, lane.ControllerId);
        button.Click += (_, _) =>
        {
            int generation = _controllerCacheGeneration;
            ShowControllerPicker(button, id =>
            {
                if (active() && generation == _controllerCacheGeneration) select(id);
            });
        };
        var close = LaneButton(GlyphKind.Close, Loc.T("piano_roll.lanes.hide_keep_data"));
        close.Name = "RemoveControllerLane";
        close.Height = ControllerHeaderH;
        close.Click += (_, _) => remove();
        var tools = new DockPanel { Background = KeysBg,
            HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 282 };
        // Reserve the close button before measuring the name, including narrow editors.
        DockPanel.SetDock(close, Dock.Right); tools.Children.Add(close);
        tools.Children.Add(button);
        var body = new Grid { RowDefinitions = new RowDefinitions($"{ControllerHeaderH},*"),
            Background = VelBg, ClipToBounds = true };
        body.Children.Add(tools);
        Grid.SetRow(lane, 1); body.Children.Add(lane);
        picker = button;
        return body;
    }

    private static void UpdateControllerPicker(Button picker, int controller)
    {
        string label = ControllerLabel(controller);
        var caption = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        caption.Children.Add(new TextBlock { Text = label, FontFamily = NotaFonts.Sans.FontFamily,
            FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis });
        var caret = new Glyph(GlyphKind.CaretDown, 8) { Margin = new Thickness(4, 0, 0, 0) };
        Grid.SetColumn(caret, 1); caption.Children.Add(caret);
        picker.Content = caption;
        ToolTip.SetTip(picker, label);
    }

    private sealed class ControllerValueScale : Control
    {
        private readonly ControllerLane _lane;
        public ControllerValueScale(ControllerLane lane)
        {
            _lane = lane; Name = "ControllerValueScale";
            IsHitTestVisible = false; ClipToBounds = true;
            lane.SizeChanged += (_, _) => InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (_lane.Bounds.Height <= 0) return;
            bool wheel = _lane.ControllerId == MidiController.PitchWheel;
            var (lo, hi) = MidiController.Range(_lane.ControllerId);
            Draw(wheel ? "1" : "127", hi);
            Draw(wheel ? "0" : "64", wheel ? 0 : 64f / 127);
            Draw(wheel ? "−1" : "0", lo);

            void Draw(string label, float value)
            {
                var text = new NotaFormattedText(label, NotaNum.Culture, FlowDirection.LeftToRight,
                    NumFace, _lane.Bounds.Height < 40 ? 7 : 8, LabelText);
                if (_lane.TranslatePoint(new Point(0, _lane.ValueToY(value)), this) is { } point)
                    context.DrawText(text, new Point(Bounds.Width - text.Width - 3, point.Y - text.Height / 2));
            }
        }
    }

    private void RemoveControllerLane(ExtraControllerLane item)
    {
        item.Lane.ResetGesture();
        _controllerRows.Children.Remove(item.Container);
        _keyColumns.Remove(item.Row.ColumnDefinitions[0]);
        _controllerLanes.Remove(item);
        FitControllerLanes();
    }

    private void InvalidateControllerLanes()
    {
        _ctlLane.InvalidateVisual();
        _primaryControllerScale.InvalidateVisual();
        foreach (var item in _controllerLanes) item.Lane.InvalidateVisual();
        foreach (var item in _controllerLanes) ((Border)item.Row.Children[0]).Child!.InvalidateVisual();
    }
}
