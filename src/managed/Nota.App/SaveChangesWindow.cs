// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Save / Don't Save / Cancel modal shown before discarding unsaved project edits, as drawn
// in nota-design/Nota Unsaved Dialog.html: the question, the project name with how long its
// changes have been waiting, and a footer strip — Don't Save alone on the left, Cancel and
// the brass Save on the right. Return saves, Escape cancels.
// ShowDialog<SaveChoice> returns Cancel when the window is closed without a choice.

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public enum SaveChoice { Cancel, Save, DontSave }

public sealed class SaveChangesWindow : NotaWindow
{
    private readonly Button _save, _dontSave, _cancel;
    private readonly Func<Task<bool>>? _saveInPlace;
    private bool _busy;

    /// <param name="project">The project's name, shown under the question.</param>
    /// <param name="detail">Mono meta beside the name ("unsaved changes · 14 min").</param>
    /// <param name="saveInPlace">When set, Save runs here with the dialog still up
    /// ("Saving…") and closes with <see cref="SaveChoice.Save"/> on success, or
    /// <see cref="SaveChoice.Cancel"/> if it fails. When null, Save just closes and the caller
    /// saves (an untitled project needs the file panel).</param>
    public SaveChangesWindow(string title, string message, string project, string detail,
                             Func<Task<bool>>? saveInPlace = null)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.Panel;
        BlendTitleBar();
        _saveInPlace = saveInPlace;

        var body = new StackPanel
        {
            Margin = new Thickness(20, 4, 20, 20),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    FontSize = NotaType.Heading,
                    FontWeight = FontWeight.SemiBold,
                    LineHeight = 18,
                    Foreground = NotaPalette.TextPrimary,
                    TextWrapping = TextWrapping.Wrap,
                },
                MetaRow(project, detail),
            },
        };

        _dontSave = new Button
        {
            Content = "Don't Save",
            Classes = { "ghost" },
            Padding = new Thickness(12, 0),
            Margin = new Thickness(-12, 0, 0, 0),   // the label, not the hover wash, lines up with the text
            Foreground = NotaPalette.TextSecondary,
        };
        _dontSave.Click += (_, _) => Choose(SaveChoice.DontSave);
        _cancel = new Button { Content = "Cancel" };
        _cancel.Click += (_, _) => Choose(SaveChoice.Cancel);
        _save = new Button { Content = "Save", Classes = { "primary" }, FontWeight = FontWeight.SemiBold, MinWidth = 64 };
        _save.Click += (_, _) => _ = SaveAsync();

        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_dontSave, Dock.Left);
        buttons.Children.Add(_dontSave);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _cancel, _save } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);

        var footer = new Border
        {
            Background = NotaPalette.SurfaceCard,
            BorderBrush = NotaPalette.GraphBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12),
            Child = buttons,
        };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        SetBody(root);
    }

    // Name (trimmed if long) with the mono meta right after it on the same baseline.
    private static Control MetaRow(string project, string detail)
    {
        var name = new TextBlock
        {
            Text = project,
            FontSize = NotaType.Name,
            FontWeight = FontWeight.Medium,
            Foreground = NotaPalette.TextStrong,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var meta = new TextBlock
        {
            Text = detail,
            FontFamily = NotaFonts.MonoFamily,
            FontSize = NotaType.Value,
            Foreground = NotaPalette.TextMuted,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 0, 1),
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
        name.MaxWidth = 250;
        Grid.SetColumn(meta, 1);
        row.Children.Add(name);
        row.Children.Add(meta);
        return row;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _ = SaveAsync(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Choose(SaveChoice.Cancel); e.Handled = true; }
        else base.OnKeyDown(e);
    }

    private void Choose(SaveChoice choice)
    {
        if (!_busy) Close(choice);
    }

    private async Task SaveAsync()
    {
        if (_busy) return;
        if (_saveInPlace is null) { Close(SaveChoice.Save); return; }

        _busy = true;
        _save.Content = "Saving…";   // the other buttons stay as drawn; Choose ignores them meanwhile
        bool ok;
        try { ok = await _saveInPlace(); }
        catch { ok = false; }
        _busy = false;
        Close(ok ? SaveChoice.Save : SaveChoice.Cancel);
    }
}
