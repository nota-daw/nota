// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// M7-4c: a tiny modal that asks for a single line of text (e.g. a preset name).
// ShowDialog<string?> returns the trimmed text, or null if cancelled.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;

namespace Nota.App;

public sealed class TextPromptWindow : NotaWindow
{
    private readonly TextBox _input;

    public TextPromptWindow(string title, string prompt, string initial = "")
    {
        Title = title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = this.TryFindResource("Brush.BgApp", out var bg) && bg is IBrush b ? b : Brushes.Magenta;

        _input = new TextBox { Text = initial, PlaceholderText = prompt };
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Accept(); else if (e.Key == Key.Escape) Close(null); };

        var ok = new Button { Content = "OK", Classes = { "primary" } };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", Classes = { "ghost" } };
        cancel.Click += (_, _) => Close(null);

        SetBody(new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = prompt, Classes = { "Caption" } },
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        });
    }

    private void Accept()
    {
        var text = _input.Text?.Trim();
        Close(string.IsNullOrEmpty(text) ? null : text);
    }
}
