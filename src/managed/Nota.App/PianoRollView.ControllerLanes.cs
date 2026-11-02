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
        var divider = new Border { Height = 6, Background = KeysBg,
            BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0),
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
        ToolTip.SetTip(divider, Loc.T("Drag to resize lane"));
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

    private Control ControllerLaneTools()
    {
        var add = LaneButton(GlyphKind.Plus, Loc.T("Add controller lane"));
        add.Name = "AddControllerLane";
        add.Click += (_, _) => AddControllerLane();
        return new Border
        {
            Background = KeysBg, BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(4, 0),
                Children = { add, new TextBlock { Text = Loc.T("CC lanes"), FontSize = 9,
                    Foreground = LabelText, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
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
        double space = Math.Max(0, Bounds.Height - RulerH - (_strip.IsVisible ? ClipStrip.RowH : 0) - 22 - 10 - 64 - 6);
        _laneRow.Height = Math.Min(LaneHeight(PrimaryLaneKey, _laneMode == LaneMode.Velocity ? VelH : ExprH),
            Math.Max(MidiLaneHeight.Minimum, space));
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
        var picker = new Button { Classes = { "chip" }, FontSize = 9, Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        void Select(int id)
        {
            lane.ResetGesture();
            lane.Controller = id;
            picker.Content = MidiController.ShortName(id);
            ToolTip.SetTip(picker, ControllerLabel(id));
            lane.InvalidateVisual();
            FitControllerLanes();
        }
        Select(ctl);
        var remove = LaneButton(GlyphKind.Minus, Loc.T("Hide controller lane (keep its data)"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{KeysW},*"), Height = ExprH };
        int slot = Enumerable.Range(0, _controllerLanes.Count + 1).First(s => _controllerLanes.All(l => l.Slot != s));
        var container = ResizableLane(row, () => $"extra:{slot}:cc:{lane.Controller ?? MidiController.Sustain}");
        var item = new ExtraControllerLane(lane, row, container, slot);
        picker.Click += (_, _) =>
        {
            int generation = _controllerCacheGeneration;
            ShowControllerPicker(picker, id =>
            {
                if (_controllerLanes.Contains(item) && generation == _controllerCacheGeneration) Select(id);
            });
        };
        remove.Click += (_, _) => RemoveControllerLane(item);
        row.Children.Add(new Border
        {
            Background = KeysBg, BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 1, 0),
            Child = new StackPanel { Margin = new Thickness(2), Spacing = 3, Children = { picker, remove } },
        });
        var body = new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0), Child = lane };
        Grid.SetColumn(body, 1); row.Children.Add(body);
        _keyColumns.Add(row.ColumnDefinitions[0]);
        _controllerLanes.Add(item); _controllerRows.Children.Add(container);
        FitControllerLanes();
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
        foreach (var item in _controllerLanes) item.Lane.InvalidateVisual();
    }
}
