// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Edit the pages a controller's knobs walk on one device type (specs/051 FR-018, T074): opened
// from a device card's menu, "Edit controller pages…". Pages on the left (add, remove, rename,
// move), the eight knobs of the chosen page on the right, each picked from the device's
// parameter list. A reference that no longer resolves (a renamed parameter) is shown struck
// through, so it can be fixed. Save writes the user's pages and the open project's copy; Reset
// removes the type's pages so the shipped ones (or a plug-in's default) come back.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Controllers;
using Nota.Application.Localization;

namespace Nota.App.Controllers;

public sealed class DevicePageEditor : NotaWindow
{
    private readonly DevicePagesStore _store;
    private readonly string _typeKey;
    private readonly IReadOnlyList<(string Ref, string Name)> _params;
    private readonly List<(string? Name, string?[] Refs)> _pages;
    private readonly ListBox _list = new() { Width = 180, MinHeight = 220 };
    private readonly TextBox _pageName = new();
    private readonly ComboBox[] _knobs = new ComboBox[DevicePage.Knobs];
    private bool _filling;

    public DevicePageEditor(IAudioEngine engine, DevicePagesStore store, DeviceAddress device, IDevicePageSource? current)
    {
        _store = store;
        _typeKey = FocusedDevice.TypeKeyOf(engine, device) ?? "";
        _params = FocusedDevice.Parameters(engine, device);
        var focused = FocusedDevice.Build(engine, device, current);
        Title = Loc.F("controllers.pages.title", focused?.DeviceName is { Length: > 0 } n ? n : _typeKey);
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        _pages = (focused?.Pages ?? Array.Empty<DevicePage>())
            .Select(p => (p.Name, Enumerable.Range(0, DevicePage.Knobs).Select(k => k < p.Params.Count ? p.Params[k] : null).ToArray()))
            .ToList();
        if (_pages.Count == 0) _pages.Add((null, new string?[DevicePage.Knobs]));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 6 };
        for (int k = 0; k < DevicePage.Knobs; k++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = Loc.F("controllers.pages.knob", k + 1), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = NotaPalette.TextSecondary };
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            int knob = k;
            combo.SelectionChanged += (_, _) => KnobPicked(knob);
            _knobs[k] = combo;
            Grid.SetRow(label, k + 2); Grid.SetRow(combo, k + 2); Grid.SetColumn(combo, 1);
            grid.Children.Add(label); grid.Children.Add(combo);
        }
        var nameLabel = new TextBlock { Text = Loc.T("controllers.pages.page_name"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = NotaPalette.TextSecondary };
        Grid.SetColumn(_pageName, 1);
        grid.Children.Add(nameLabel); grid.Children.Add(_pageName);
        _pageName.TextChanged += (_, _) =>
        {
            if (_filling || _list.SelectedIndex < 0) return;
            int i = _list.SelectedIndex;
            _pages[i] = (string.IsNullOrWhiteSpace(_pageName.Text) ? null : _pageName.Text!.Trim(), _pages[i].Refs);
            FillList(i);
        };

        Button Small(string text, Action click) { var b = new Button { Content = text, Classes = { "ghost" } }; b.Click += (_, _) => click(); return b; }
        var pageButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4,
            Children =
            {
                Small(Loc.T("controllers.pages.add"), () => { _pages.Insert(Sel() + 1, (null, new string?[DevicePage.Knobs])); FillList(Sel() + 1); }),
                Small(Loc.T("controllers.pages.remove"), () => { if (_pages.Count > 1) { int i = Sel(); _pages.RemoveAt(i); FillList(Math.Min(i, _pages.Count - 1)); } }),
                Small("↑", () => Move(-1)),
                Small("↓", () => Move(1)),
            },
        };
        _list.SelectionChanged += (_, _) => { if (!_filling) ShowPage(); };

        var reset = new Button { Content = Loc.T("controllers.pages.reset"), Classes = { "ghost" }, IsEnabled = store.IsEdited(_typeKey) };
        reset.Click += (_, _) => { _store.Reset(_typeKey); Close(); };
        var save = new Button { Content = Loc.T("controllers.pages.save"), Classes = { "primary" } };
        save.Click += (_, _) => { Save(); Close(); };
        var cancel = new Button { Content = Loc.T("common.cancel"), Classes = { "ghost" } };
        cancel.Click += (_, _) => Close();

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(cancel, 2); Grid.SetColumn(save, 3);
        bottom.Children.Add(reset); bottom.Children.Add(cancel); bottom.Children.Add(save);

        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        var left = new StackPanel { Spacing = 6, Children = { _list, pageButtons } };
        Grid.SetColumn(grid, 1);
        main.Children.Add(left); main.Children.Add(grid);

        SetBody(new StackPanel
        {
            Margin = new Thickness(20), Spacing = 14,
            Children =
            {
                new TextBlock { Text = Loc.T("controllers.pages.caption"), Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap },
                main, bottom,
            },
        });
        FillList(0);
    }

    private int Sel() => Math.Clamp(_list.SelectedIndex, 0, _pages.Count - 1);

    private void Move(int delta)
    {
        int i = Sel(), j = i + delta;
        if (j < 0 || j >= _pages.Count) return;
        (_pages[i], _pages[j]) = (_pages[j], _pages[i]);
        FillList(j);
    }

    private void FillList(int select)
    {
        _filling = true;
        _list.Items.Clear();
        for (int i = 0; i < _pages.Count; i++)
            _list.Items.Add(new ListBoxItem { Content = _pages[i].Name is { Length: > 0 } n ? Loc.F("controllers.pages.page_named", i + 1, n) : Loc.F("controllers.pages.page", i + 1) });
        _list.SelectedIndex = Math.Clamp(select, 0, _pages.Count - 1);
        _filling = false;
        ShowPage();
    }

    // The chosen page's name and eight knobs. A reference that resolves to no parameter stays
    // selected, struck through.
    private void ShowPage()
    {
        _filling = true;
        var (name, refs) = _pages[Sel()];
        _pageName.Text = name ?? "";
        for (int k = 0; k < DevicePage.Knobs; k++)
        {
            var combo = _knobs[k];
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = Loc.T("controllers.pages.empty"), Tag = null });
            int pick = 0;
            foreach (var (r, n) in _params)
            {
                combo.Items.Add(new ComboBoxItem { Content = n, Tag = r });
                if (r == refs[k]) pick = combo.Items.Count - 1;
            }
            if (refs[k] is { } missing && pick == 0)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = new TextBlock { Text = missing, TextDecorations = TextDecorations.Strikethrough, Foreground = NotaPalette.TextSecondary },
                    Tag = missing,
                });
                pick = combo.Items.Count - 1;
            }
            combo.SelectedIndex = pick;
        }
        _filling = false;
    }

    private void KnobPicked(int knob)
    {
        if (_filling) return;
        var tag = (_knobs[knob].SelectedItem as ComboBoxItem)?.Tag as string;
        _pages[Sel()].Refs[knob] = tag;
    }

    private void Save()
    {
        var pages = _pages.Select(p => new DevicePage(p.Name, TrimNulls(p.Refs))).ToList();
        _store.Edit(_typeKey, pages);
    }

    private static string?[] TrimNulls(string?[] refs)
    {
        int n = refs.Length;
        while (n > 0 && refs[n - 1] is null) n--;
        return refs.Take(n).ToArray();
    }
}
