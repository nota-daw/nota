// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M7-7: a tiny themed yes/no modal. ShowDialog<bool> returns true if the
// affirmative button was chosen, false otherwise (incl. window close).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed class ConfirmWindow : NotaWindow
{
    public ConfirmWindow(string title, string message, string affirmative, string negative)
    {
        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        var yes = new Button { Content = affirmative, Classes = { "primary" } };
        yes.Click += (_, _) => Close(true);
        var no = new Button { Content = negative, Classes = { "ghost" } };
        no.Click += (_, _) => Close(false);

        SetBody(new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, yes },
                },
            },
        });
    }
}
