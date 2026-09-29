// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Save / Don't Save / Cancel modal shown before discarding unsaved project edits.
// ShowDialog<SaveChoice> returns Cancel when the window is closed without a choice.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public enum SaveChoice { Cancel, Save, DontSave }

public sealed class SaveChangesWindow : NotaWindow
{
    public SaveChangesWindow(string title, string message)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        var save = new Button { Content = "Save", Classes = { "primary" } };
        save.Click += (_, _) => Close(SaveChoice.Save);
        var dontSave = new Button { Content = "Don't Save", Classes = { "ghost" } };
        dontSave.Click += (_, _) => Close(SaveChoice.DontSave);
        var cancel = new Button { Content = "Cancel", Classes = { "ghost" } };
        cancel.Click += (_, _) => Close(SaveChoice.Cancel);

        SetBody(new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new DockPanel
                {
                    LastChildFill = false,
                    Children =
                    {
                        dontSave,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 8,
                            [DockPanel.DockProperty] = Dock.Right,
                            Children = { cancel, save },
                        },
                    },
                },
            },
        });
    }
}
