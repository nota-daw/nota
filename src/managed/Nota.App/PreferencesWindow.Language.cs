// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Appearance → Language (spec 068). The interface language is fixed for a run, so
// choosing one saves it and says so on the row itself, with no dialog: a note beside the list
// and, where Nota can start itself again, a small Restart button that closes the main window by
// its usual path (the unsaved-changes prompt included) so Nota comes back in the new language.
// Leaving it alone is fine — the choice applies at the next start. Each choice is written in its
// own language ("English", "Français"), so the row can be found and read whichever language is
// running — and choosing the running language again cancels a pending change.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private Control LanguagePicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var codes = NotaLanguages.Codes;
        var combo = Combo(160);
        var languages = new List<string>();
        foreach (var language in NotaLanguages.Available)
        {
            languages.Add(language.Code);
            combo.Items.Add(new ComboBoxItem { Content = language.Name });
        }

        // Where Nota can start itself again the note comes with a button; in a development run
        // there is nothing to start, so the note says when the change lands.
        var canRelaunch = AppRestart.CanRelaunch;
        var pending = new TextBlock
        {
            Text = canRelaunch ? Loc.T("preferences.language.applies_after_restart") : Loc.T("preferences.language.applies_the_next_time_nota"),
            FontSize = 11, Foreground = Brass, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var restart = Btn(Loc.T("preferences.language.restart"));
        restart.Height = 24;
        restart.Padding = new Avalonia.Thickness(10, 0);
        restart.Click += (_, _) =>
        {
            // The usual close: it asks about unsaved changes, and cancelling that cancels the
            // restart (MainWindow.Recovery). The relaunch itself runs when the app exits.
            var main = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (main is null) return;
            AppRestart.Request();
            main.Close();
        };
        var note = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        note.Children.Add(pending);
        if (canRelaunch) note.Children.Add(restart);

        // The stored choice drives both the selected item and the note.
        var syncing = false;
        void ShowSaved()
        {
            var stored = UiLanguage.Normalize(settings.Current.Language, codes);
            syncing = true;
            combo.SelectedIndex = Math.Max(0, languages.IndexOf(stored));
            syncing = false;
            note.IsVisible = UiLanguage.IsPending(stored, Loc.Language, codes);
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (syncing || combo.SelectedIndex < 0 || combo.SelectedIndex >= languages.Count) return;
            var code = languages[combo.SelectedIndex];
            if (UiLanguage.Normalize(settings.Current.Language, codes) == code) return;
            settings.Current.Language = code;
            settings.Save();
            MacSystemLanguage.Set(code);   // macOS's own text follows from the next start
            ShowSaved();
        };
        ShowSaved();

        var line = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        combo.Margin = new Avalonia.Thickness(0, 0, 12, 0);
        line.Children.Add(combo);
        line.Children.Add(note);
        return line;
    }
}
