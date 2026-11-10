// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → Appearance, the user themes part (spec 087, contracts/ui-contract.md U1–U5):
// a user theme's card (its name in its own typeface, a "User theme" tag, a warning count),
// the card menu (Duplicate, Show in folder), the themes folder row (Open, Reload) and the
// list of files that could not be loaded. Problems never open a dialog: they are lines here.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Application.Themes;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    // The last thing the themes row has to say (a failed duplicate), shown until the next action.
    private static string _skinsStatus = "";

    /// <summary>The theme ids the settings ask for now: the single theme, or both follow-system slots.</summary>
    private static IEnumerable<string> StoredThemeIds(Settings s)
    {
        if (s.ColourTheme == Appearance.System)
        {
            yield return s.SystemDarkTheme;
            yield return s.SystemLightTheme;
        }
        else if (!string.IsNullOrEmpty(s.ColourTheme)) yield return s.ColourTheme;
    }

    // One card: the preview, the name (in the theme's own typeface), and for a user theme a
    // caption with its tag and, when it has any, the warning count with the details on hover.
    private static Control ThemeCard(NotaColourTheme t)
    {
        var name = new TextBlock { Text = t.Name, Classes = { "RowName" }, MaxWidth = ThemePreview.W, TextTrimming = TextTrimming.CharacterEllipsis };
        if (t.Geometry.FontFamily is { } family && NotaThemes.IsFontInstalled(family)) name.FontFamily = new FontFamily(family);
        var card = new StackPanel
        {
            Spacing = 6,
            Children = { new Border { Classes = { "ThemeFrame" }, Child = new ThemePreview(t) }, name },
        };

        if (t.IsUser)
        {
            var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, -4, 0, 0) };
            caption.Children.Add(new TextBlock { Text = Loc.T("preferences.skins.user_tag"), Classes = { "Caption" }, Foreground = TextMuted });
            if (t.Warnings.Count > 0)
            {
                var warn = new TextBlock
                {
                    Text = Loc.Plural("preferences.skins.warnings", t.Warnings.Count),
                    Classes = { "Caption" }, Foreground = NotaPalette.Warning,
                };
                ToolTip.SetTip(warn, WarningsTip(t.Warnings));
                caption.Children.Add(warn);
            }
            card.Children.Add(caption);
        }

        var menu = new MenuFlyout();
        menu.Items.Add(MenuKit.Item(Loc.T("preferences.skins.duplicate"), GlyphKind.Duplicate, () => DuplicateTheme(t)));
        if (t is { IsUser: true, FilePath: { } path })
            menu.Items.Add(MenuKit.Item(MenuKit.RevealLabel, GlyphKind.Folder, () => TryShell(() => MainWindow.RevealInFileManager(path))));
        card.ContextFlyout = menu;
        return card;
    }

    private static string WarningsTip(IReadOnlyList<ThemeWarning> warnings)
    {
        var lines = warnings.Take(5).Select(w => w.Message).ToList();
        if (warnings.Count > 5) lines.Add(Loc.F("preferences.skins.more", warnings.Count - 5));
        return string.Join("\n", lines);
    }

    // Open themes folder · Reload, with the hint (or the last failure) beside them.
    private static Control ThemesFolderRow()
    {
        var open = Btn(Loc.T("preferences.skins.open_folder"));
        var reload = Btn(Loc.T("preferences.skins.reload"));
        open.Click += (_, _) => TryShell(() =>
        {
            Directory.CreateDirectory(NotaThemes.ThemesFolder);
            MainWindow.OpenFolderInFileManager(NotaThemes.ThemesFolder);
        });
        reload.Click += (_, _) => { _skinsStatus = ""; NotaThemes.ReloadUser(); };
        var note = new TextBlock
        {
            Text = _skinsStatus.Length > 0 ? _skinsStatus : Loc.T("preferences.skins.hint"),
            Classes = { "Caption" }, Foreground = _skinsStatus.Length > 0 ? NotaPalette.Warning : TextMuted,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 560,
        };
        return new StackPanel
        {
            Spacing = 8, Margin = new Avalonia.Thickness(0, 6, 0, 0),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, reload } },
                note,
            },
        };
    }

    // The files in the folder that could not be loaded, one line each — or nothing at all.
    private static Control? ThemeProblems()
    {
        var problems = NotaThemes.UserLoadProblems;
        if (problems.Count == 0) return null;
        var list = new StackPanel { Spacing = 4 };
        list.Children.Add(new TextBlock { Text = Loc.T("preferences.skins.problems_heading"), FontSize = 11, Foreground = NotaPalette.Warning });
        foreach (var (file, reason) in problems)
            list.Children.Add(new TextBlock
            {
                Text = $"{file} — {reason}", Classes = { "Caption" }, Foreground = TextSecondary,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 560,
            });
        return list;
    }

    /// <summary>Wear <paramref name="id"/> and store it, exactly as a pick on a card does.</summary>
    internal static void PickTheme(string id, bool singleMode = false)
    {
        var theme = NotaThemes.Find(id);
        if (theme is null) return;
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var current = singleMode ? NotaThemeService.Choice with { FollowSystem = false } : NotaThemeService.Choice;
        var choice = Appearance.WithPick(current, theme.Id, theme.IsDark);
        var single = NotaThemes.Find(choice.Single) ?? NotaThemes.Default;
        Appearance.Store(settings.Current, choice, single.IsDark);
        settings.Save();
        NotaThemeService.Set(choice);
    }

    // A copy of any theme in the themes folder, under a free id and "<name> copy", then worn.
    private static void DuplicateTheme(NotaColourTheme t)
    {
        var source = NotaThemes.Find(t.Id) ?? t;   // the file's own colours, not the accented ones
        var name = Loc.F("preferences.skins.copy_name", source.Name);
        if (name.Length > ThemeFile.MaxNameLength) name = name[..ThemeFile.MaxNameLength].TrimEnd();
        var folder = NotaThemes.ThemesFolder;
        var id = ThemeFile.FreeId(name, x => NotaThemes.Find(x) is not null || File.Exists(Path.Combine(folder, x + ".json")));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, id + ".json"), NotaThemes.ToFile(source with { Id = id, Name = name, Source = ThemeSource.User }));
            _skinsStatus = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _skinsStatus = Loc.F("preferences.skins.cant_save", e.Message);
            NotaThemes.ReloadUser();   // rebuilds the page with the message
            return;
        }
        NotaThemes.ReloadUser();
        PickTheme(id);
    }

    private static void TryShell(Action run)
    {
        try { run(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _skinsStatus = Loc.F("main.browser.reveal_failed", e.Message);
            NotaThemes.ReloadUser();   // rebuilds the page with the message
        }
    }
}
