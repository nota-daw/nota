// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The panel that replaces Modular's long parameter menus (specs/101, #656): a CV cable dropped
// on a node or on another track's header, and the "Modulate from…" items, open it at the
// pointer. A search field has focus straight away; the list under it is a virtualizing ListBox
// so a plug-in with thousands of parameters opens and scrolls at once, and the panel never
// grows past 320 × 420. Typing filters, ↑ ↓ / PageUp / PageDown move the highlight, Enter picks,
// Esc (or a click outside) closes. The filtering and keyboard rules live in ParamPickerModel.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Localization;
using Nota.Presentation;

namespace Nota.App;

internal static class ParamPickerPanel
{
    internal const double PanelW = 320, PanelMaxH = 420, RowH = 22;
    private const double ChromeH = 76;   // title + search field + spacing + padding, above the list

    /// <summary>The picker shown last (tests drive it through this).</summary>
    internal static (ParamPickerModel Model, Action<object> Pick, Flyout Flyout)? Last { get; private set; }

    /// <summary>Closes the open picker without picking — its list may name a device that is gone.</summary>
    public static void CloseOpen() => Last?.Flyout.Hide();

    /// <summary>Opens the picker at the pointer (or under <paramref name="anchor"/>) and calls
    /// <paramref name="onPick"/> with the chosen entry's payload.</summary>
    public static Flyout Show(Control anchor, string title, ParamPickerModel model, Action<object> onPick, bool atPointer = true)
    {
        var sw = Environment.GetEnvironmentVariable("NOTA_DEBUG_PICKER") == "1" ? Stopwatch.StartNew() : null;
        var flyout = new Flyout
        {
            Placement = atPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard,
        };

        var heading = new TextBlock
        {
            Text = title, FontSize = NotaType.Name, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var search = new TextBox
        {
            PlaceholderText = Loc.T("modular.picker_search"), FontSize = NotaType.Body,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var empty = new TextBlock
        {
            FontSize = NotaType.Body, Foreground = NotaPalette.TextDisabled, Margin = new Thickness(8, 4),
            IsVisible = false,
        };
        bool done = false;   // picked already (a click and Enter can't both link)
        var list = new ListBox
        {
            Classes = { "browser" },
            MaxHeight = PanelMaxH - ChromeH,
            SelectionMode = SelectionMode.Single,
            Focusable = false,
            ItemTemplate = new FuncDataTemplate<ParamPickerEntry>((e, _) => Hoverable(Row(e, model.Grouped && model.Query.Length > 0), e)),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

        // Hovering a row moves the highlight there, so Enter picks what the pointer is on; a click
        // on it picks it. The click is the row's own press, not a selection change: the hovered
        // (or first) row is already selected, so clicking it changes nothing to listen to.
        int IndexOf(ParamPickerEntry e)
        {
            for (int k = 0; k < model.Visible.Count; k++) if (ReferenceEquals(model.Visible[k], e)) return k;
            return -1;
        }
        Control Hoverable(Control row, ParamPickerEntry? e)
        {
            if (e is null || e.IsHeading) return row;
            row.PointerEntered += (_, _) => model.SetHighlight(IndexOf(e));
            row.AddHandler(InputElement.PointerPressedEvent, (_, pe) =>
            {
                if (!pe.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                pe.Handled = true;
                model.SetHighlight(IndexOf(e));
                Commit();
            }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            return row;
        }

        bool syncing = false;
        void Sync()
        {
            syncing = true;
            list.ItemsSource = new List<ParamPickerEntry>(model.Visible);
            list.SelectedIndex = model.Highlight;
            if (model.Highlight >= 0) list.ScrollIntoView(model.Highlight);
            syncing = false;
            string? key = model.EmptyKey;
            empty.IsVisible = key is not null;
            if (key is not null)
                empty.Text = key == ParamPickerModel.NoParamsKey
                    ? model.NoParamsText ?? Loc.T("modular.picker_no_params")
                    : Loc.T("modular.picker_no_matches");
        }
        void Highlighted()
        {
            syncing = true;
            list.SelectedIndex = model.Highlight;
            if (model.Highlight >= 0) list.ScrollIntoView(model.Highlight);
            syncing = false;
        }

        void Commit()
        {
            if (done || model.Pick() is not { } payload) return;
            done = true;
            flyout.Hide();
            onPick(payload);
        }

        string lastQuery = "";
        model.Changed += () => { if (model.Query != lastQuery) { lastQuery = model.Query; Sync(); } else Highlighted(); };
        search.TextChanged += (_, _) => model.Query = search.Text ?? "";
        search.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down: model.MoveHighlight(1); e.Handled = true; break;
                case Key.Up: model.MoveHighlight(-1); e.Handled = true; break;
                case Key.PageDown: model.MoveHighlight(model.PageSize); e.Handled = true; break;
                case Key.PageUp: model.MoveHighlight(-model.PageSize); e.Handled = true; break;
                case Key.Enter: Commit(); e.Handled = true; break;
                case Key.Escape: flyout.Hide(); e.Handled = true; break;
            }
        };
        // The selection only mirrors the highlight; if anything else moves it (a click on a
        // heading, say), put it back.
        list.SelectionChanged += (_, _) =>
        {
            if (syncing || list.SelectedIndex == model.Highlight) return;
            Highlighted();
        };

        var panel = new StackPanel
        {
            Width = PanelW - 16, Spacing = 8,
            Children = { heading, search, empty, list },
        };
        flyout.Content = panel;
        flyout.Opened += (_, _) =>
        {
            search.Focus();
            if (sw is not null) Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Console.Error.WriteLine($"[picker] {model.Entries.Count} entries shown in {sw.Elapsed.TotalMilliseconds:0.0}\u2009ms"),
                Avalonia.Threading.DispatcherPriority.Background);
        };
        flyout.Closed += (_, _) => { if (Last is { } l && l.Flyout == flyout) Last = null; };
        Sync();
        Last = (model, p => { done = true; flyout.Hide(); onPick(p); }, flyout);
        flyout.ShowAt(anchor, showAtPointer: atPointer);
        return flyout;
    }

    // A 22px row: a parameter (with its group in ink 4 while a grouped search mixes devices), or
    // a group heading (sentence case, ink 3) that can't be picked.
    private static Control Row(ParamPickerEntry? e, bool showGroup)
    {
        if (e is null) return new Border();
        if (e.IsHeading)
        {
            return new Border
            {
                Height = RowH, Padding = new Thickness(8, 6, 8, 0), IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = NotaText.Label(e.Label), FontSize = NotaType.Caption, FontWeight = FontWeight.SemiBold,
                    Foreground = NotaPalette.TextTertiary, TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
        }
        var grid = new Grid { Height = RowH, ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var label = new TextBlock
        {
            Text = e.Label, FontSize = NotaType.Body, Foreground = NotaPalette.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        grid.Children.Add(label);
        if (showGroup && e.Group is { Length: > 0 } g)
        {
            var group = new TextBlock
            {
                Text = g, FontSize = NotaType.Caption, Foreground = NotaPalette.TextDisabled, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120,
            };
            Grid.SetColumn(group, 1);
            grid.Children.Add(group);
        }
        // The padded border is the row, so its whole width takes the hover and the click.
        var row = new Border { Padding = new Thickness(8, 0), Background = Brushes.Transparent, Child = grid };
        ToolTip.SetTip(row, e.Label);
        return row;
    }
}
