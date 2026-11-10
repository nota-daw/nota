// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Appearance → Language (specs 068 and 077, contracts/language-row.md). The interface
// language is fixed for a run, so choosing one saves it and says so on the row itself, with no
// dialog: a note beside the list and, where Nota can start itself again, a small Restart button
// that closes the main window by its usual path (the unsaved-changes prompt included).
//
// Nota ships English only. The list holds English, the installed languages and — once the list
// from nota-translations is in (LanguageDownloads) — the ones that can be downloaded, each with
// how complete it is for this Nota. Choosing one that is not installed downloads it in place
// (progress, then the restart note; a failure says why and changes nothing). Beside the list:
// Update when a newer version of the chosen language is published, Remove, and Load file… for a
// translator's file, whose problems are listed under the row. Each language is written in its
// own name, so the row can be found and read whichever language is running.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Infrastructure;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private enum LanguageKind { English, Installed, Available }

    private sealed record LanguageItem(string Code, string Name, LanguageKind Kind, string Detail,
        InstalledLanguage? Installed, CatalogLanguage? Offered);

    // Settings → General → Language (specs/088): its own page, off Appearance (#427).
    private Control LanguagePane() => Sections(
        Section(Loc.T("preferences.language"), 10,
            Row(Loc.T("preferences.language"), LanguagePicker()),
            Row("", Caption(Loc.T("preferences.the_language_of_menus_panels")))));

    private Control LanguagePicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var installed = App.Services.GetRequiredService<InstalledLanguages>();
        var downloads = LanguageDownloads.Shared;

        var combo = Combo(160);
        combo.HorizontalContentAlignment = HorizontalAlignment.Left;   // a panel, not a string: keep it where text sits
        var items = new List<LanguageItem>();

        var canRelaunch = AppRestart.CanRelaunch;
        var pending = new TextBlock
        {
            Text = canRelaunch ? Loc.T("preferences.language.applies_after_restart") : Loc.T("preferences.language.applies_the_next_time_nota"),
            FontSize = 11, Foreground = Brass, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };
        var restart = SmallBtn(Loc.T("preferences.language.restart"));
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

        var update = SmallBtn(Loc.T("preferences.language.update"));
        var remove = SmallBtn(Loc.T("preferences.language.remove"));
        var load = SmallBtn(Loc.T("preferences.language.load_file"));
        var show = SmallBtn(Loc.T("preferences.language.show_available_languages"));
        var fetchStored = SmallBtn("");
        var status = new TextBlock { FontSize = 11, Foreground = TextMuted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var progress = new ProgressBar { Width = 90, Height = 4, MinWidth = 0, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        progress.BindResource(ProgressBar.ForegroundProperty, "Brush.Accent");
        var problems = new TextBlock { FontSize = 11, LineHeight = 17, Foreground = TextMuted, TextWrapping = TextWrapping.Wrap, IsVisible = false };

        string Stored() => (settings.Current.Language ?? UiLanguage.Default).Trim().ToLowerInvariant();

        void Save(string code)
        {
            settings.Current.Language = code;
            settings.Save();
            MacSystemLanguage.Set(code);   // macOS's own text follows from the next start
        }

        IReadOnlyList<LanguageItem> Build()
        {
            var list = new List<LanguageItem> { new(UiLanguage.Default, Loc.English.Name, LanguageKind.English, "", null, null) };
            var index = downloads.Index;
            var mine = installed.List().Where(l => l.Code != UiLanguage.Default).ToList();
            foreach (var l in mine)
            {
                var offered = index?.Languages.FirstOrDefault(o => o.Code == l.Code);
                string detail = l.Table is null ? Loc.T("preferences.language.unreadable")
                    : l.Meta.IsLocal ? Loc.T("preferences.language.local_file")
                    // The published coverage describes the published file: use it only when that
                    // is the file installed, else measure the installed one.
                    : index is not null && offered is not null && offered.Sha256 == l.Meta.Sha256
                      && LanguageDownloads.CoverageOf(index, offered) is { } c ? NotaNum.Unit(c, "0", "%")
                    : NotaNum.Unit(l.Table.ValidateAgainst(Loc.English).Coverage, "0", "%");
                list.Add(new(l.Code, l.Table?.Name ?? l.Code, LanguageKind.Installed, detail, l, offered));
            }
            if (index is not null)
                foreach (var o in index.Languages.Where(o => o.Code != UiLanguage.Default && mine.All(m => m.Code != o.Code)))
                {
                    var c = LanguageDownloads.CoverageOf(index, o);
                    list.Add(new(o.Code, o.Name, LanguageKind.Available, (c is { } cc ? NotaNum.Unit(cc, "0", "%") + " · " : "") + Loc.T("preferences.language.download"), null, o));
                }
            return list;
        }

        var syncing = false;
        void Refresh()
        {
            var fresh = Build();
            // Rebuild the list only when it changed: the combo's items are not replaced under an
            // open drop-down or a selection change for nothing.
            bool same = fresh.Count == items.Count && fresh.Zip(items).All(p => p.First.Code == p.Second.Code
                && p.First.Kind == p.Second.Kind && p.First.Detail == p.Second.Detail && p.First.Name == p.Second.Name);
            items.Clear();
            items.AddRange(fresh);
            syncing = true;
            if (!same) combo.Items.Clear();
            foreach (var it in same ? (IEnumerable<LanguageItem>)Array.Empty<LanguageItem>() : items)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
                row.Children.Add(new TextBlock { Text = it.Name, VerticalAlignment = VerticalAlignment.Center });
                if (it.Detail.Length > 0)
                    row.Children.Add(new TextBlock { Text = it.Detail, FontSize = 11, Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center });
                // A line above the first language to download sets them apart from the installed
                // ones; drawn on the item itself, so the items stay one per language.
                bool firstAvailable = it.Kind == LanguageKind.Available && items.First(i => i.Kind == LanguageKind.Available) == it;
                Control content = row;
                if (firstAvailable)
                {
                    var rule = new Border { BorderThickness = new Avalonia.Thickness(0, 1, 0, 0), Padding = new Avalonia.Thickness(0, 6, 0, 0), Child = row, HorizontalAlignment = HorizontalAlignment.Stretch };
                    rule.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
                    content = rule;
                }
                combo.Items.Add(new ComboBoxItem { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            var stored = Stored();
            int at = items.FindIndex(i => i.Code == stored && i.Kind != LanguageKind.Available);
            combo.SelectedIndex = Math.Max(0, at);
            syncing = false;

            var chosen = items[combo.SelectedIndex];
            bool storedInstalled = at >= 0;
            note.IsVisible = storedInstalled && (stored != Loc.Language || downloads.UpdatedThisRun.Contains(stored));
            // One choice at a time: while a language downloads, the list and Load file… wait.
            combo.IsEnabled = load.IsEnabled = downloads.Downloading is null;
            update.IsVisible = chosen.Kind == LanguageKind.Installed && chosen.Installed is { } inst && chosen.Offered is { } off
                               && LanguageCatalog.UpdateAvailable(inst, off) && downloads.Downloading is null;
            remove.IsVisible = chosen.Kind == LanguageKind.Installed && downloads.Downloading is null;
            show.IsVisible = downloads.Index is null && !LanguageDownloads.MayCheck && storedInstalled;
            // The stored language is not installed (Nota used to ship French) and Nota does not go
            // online on its own: one click fetches the list and the language (FR-021).
            fetchStored.IsVisible = !storedInstalled && stored != UiLanguage.Default && !LanguageDownloads.MayCheck && downloads.Downloading is null;
            fetchStored.Content = Loc.F("preferences.language.download_language", NameOf(stored));

            progress.IsVisible = downloads.Downloading is not null;
            progress.IsIndeterminate = downloads.Progress < 0;
            if (downloads.Progress >= 0) progress.Value = downloads.Progress;
            status.Foreground = TextMuted;
            if (downloads.Downloading is { } dl)
                status.Text = Loc.F("preferences.language.downloading", NameOf(dl));
            else if (downloads.Failed is { } f)
            {
                status.Text = Loc.F("preferences.language.couldnt_download", NameOf(f.Code), f.Message);
                status.Foreground = NotaPalette.DangerBright;
            }
            else if (!storedInstalled && stored != UiLanguage.Default)
                status.Text = LanguageDownloads.MayCheck
                    ? Loc.F("preferences.language.will_be_downloaded_when_online", NameOf(stored))
                    : Loc.F("preferences.language.is_not_installed", NameOf(stored));
            else if (chosen.Installed is { Table: null } broken)
                status.Text = Loc.F("preferences.language.cannot_be_used", broken.Error ?? "");
            else if (downloads.Index is null && downloads.IndexError is { } why)
                status.Text = Loc.F("preferences.language.couldnt_get_the_list", why);
            else status.Text = "";
            status.IsVisible = status.Text.Length > 0;
        }

        // Download progress moves only the bar: nothing is re-read for it.
        void Progress()
        {
            progress.IsIndeterminate = downloads.Progress < 0;
            if (downloads.Progress >= 0) progress.Value = downloads.Progress;
        }

        string NameOf(string code)
            => items.FirstOrDefault(i => i.Code == code)?.Name
               ?? downloads.Index?.Languages.FirstOrDefault(l => l.Code == code)?.Name ?? OwnName(code);

        async void Download(CatalogLanguage language, bool choose)
        {
            // Saved only if nothing else was chosen meanwhile (the list is locked, but a remove
            // or a change from another window may still land).
            var before = Stored();
            if (await downloads.DownloadAsync(language) && choose && Stored() == before) Save(language.Code);
            downloads.NoteReady(language.Code, language.Name);
            Refresh();
        }

        // Handled after the selection change has finished: Refresh may replace the combo's items,
        // which Avalonia does not allow from inside its own SelectionChanged.
        combo.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(Choose);
        };
        void Choose()
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= items.Count) return;
            var it = items[combo.SelectedIndex];
            problems.IsVisible = false;
            if (it.Kind == LanguageKind.Available)
            {
                if (it.Offered is { } o && downloads.Downloading is null) Download(o, choose: true);
                Refresh();   // back to the stored language until the download is in
                return;
            }
            if (Stored() == it.Code) { Refresh(); return; }
            Save(it.Code);
            downloads.NoteReady(it.Code, it.Name);
            Refresh();
        }
        update.Click += (_, _) =>
        {
            var it = items[combo.SelectedIndex];
            if (it.Offered is { } o) Download(o, choose: false);
        };
        remove.Click += (_, _) =>
        {
            var it = items[combo.SelectedIndex];
            installed.Remove(it.Code);
            // Removing the chosen language goes back to English from the next start (and keeps
            // the start-up download from fetching it again); a running one stays until then.
            if (Stored() == it.Code) Save(UiLanguage.Default);
            downloads.ClearReady();
            Refresh();
        };
        show.Click += async (_, _) => { await downloads.FetchAsync(refresh: true); Refresh(); };
        fetchStored.Click += async (_, _) =>
        {
            var code = Stored();
            await downloads.FetchAsync(refresh: true);
            if (downloads.Index?.Languages.FirstOrDefault(l => l.Code == code) is { } offered) Download(offered, choose: true);
            Refresh();
        };
        load.Click += async (_, _) =>
        {
            var files = await this.PickFilesAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("preferences.language.load_a_language_file"), AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(Loc.T("preferences.language.language_file")) { Patterns = new[] { "*.json" } } },
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            TranslationTable? table;
            string? error;
            try { table = TranslationTable.Parse(File.ReadAllText(path), out error); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { table = null; error = e.Message; }
            if (table is null || table.Code == UiLanguage.Default || !InstalledLanguages.IsSafeCode(table.Code))
            {
                problems.Text = Loc.F("preferences.language.not_a_language_file", Path.GetFileName(path), error ?? table?.Code ?? "");
                problems.Foreground = NotaPalette.DangerBright;
                problems.IsVisible = true;
                return;
            }
            installed.InstallLocal(path, table.Code);
            Save(table.Code);
            downloads.NoteReady(table.Code, table.Name);
            var (found, coverage) = table.ValidateAgainst(Loc.English);
            var log = App.Services.GetRequiredService<ILogSink>();
            log.Info($"Interface language file {path}: {table.Code}, covers {coverage}% of the English keys, {found.Count} entries show English");
            foreach (var p in found) log.Warn($"Interface language file {path}: {p.Key} — {p.Problem}");
            var lines = new List<string> { Loc.F("preferences.language.loaded_file", table.Name, NotaNum.Unit(coverage, "0", "%"), found.Count) };
            lines.AddRange(found.Take(10).Select(p => $"{p.Key} — {p.Problem}"));
            if (found.Count > 10) lines.Add(Loc.F("preferences.language.and_more_see_the_log", found.Count - 10));
            problems.Text = string.Join("\n", lines);
            problems.Foreground = TextMuted;
            problems.IsVisible = true;
            Refresh();
        };


        // The list: the cache at once, the network when Nota may ask on its own.
        downloads.LoadCached();
        Refresh();
        if (LanguageDownloads.MayCheck) _ = downloads.FetchAsync();

        var line = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        combo.Margin = new Avalonia.Thickness(0, 0, 12, 0);
        line.Children.Add(combo);
        foreach (var c in new Control[] { note, update, remove, fetchStored, load, show, progress, status })
        {
            c.Margin = new Avalonia.Thickness(0, 2, 10, 2);
            line.Children.Add(c);
        }
        // Listen only while the row is on screen: Settings builds a new row each time the page is
        // shown, and a hidden one must not keep re-reading the installed languages.
        var panel = new StackPanel { Spacing = 6, Children = { line, problems } };
        panel.AttachedToVisualTree += (_, _) => { downloads.Changed += Refresh; downloads.ProgressChanged += Progress; };
        panel.DetachedFromVisualTree += (_, _) => { downloads.Changed -= Refresh; downloads.ProgressChanged -= Progress; };
        return panel;
    }

    // A language's own name before the list is in ("fr" → "Français"), from the OS's culture data.
    private static string OwnName(string code)
    {
        try
        {
            var name = System.Globalization.CultureInfo.GetCultureInfo(code).NativeName;
            return name.Length > 0 && !name.StartsWith("Unknown", StringComparison.Ordinal) ? char.ToUpperInvariant(name[0]) + name[1..] : code;
        }
        catch (System.Globalization.CultureNotFoundException) { return code; }
    }

    private static Button SmallBtn(string text)
    {
        var b = Btn(text);
        b.Height = 24;
        b.Padding = new Avalonia.Thickness(10, 0);
        return b;
    }
}
