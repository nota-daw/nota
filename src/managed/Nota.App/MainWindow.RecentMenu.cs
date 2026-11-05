// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// File → Open recent (#443): the welcome screen's recent-project list, reachable while working.
// Rebuilt from the settings whenever the list changes (every open / save records a project), so
// it never needs to be read at menu-open time. Picking one asks about unsaved changes first,
// as Open… does.

using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private NativeMenu? _recentMenu;

    private void InitRecentMenu()
    {
        if (NativeMenu.GetMenu(this) is not { } menu) return;
        foreach (var top in menu.Items)
        {
            if (top is not NativeMenuItem { Menu: { } file }) continue;
            for (int i = 0; i < file.Items.Count; i++)
            {
                if (file.Items[i] is not NativeMenuItem item || Commands.GetId(item) != "file.open") continue;
                _recentMenu = new NativeMenu();
                file.Items.Insert(i + 1, new NativeMenuItem(Loc.T("main.recent_menu.open_recent")) { Menu = _recentMenu });
                RefreshRecentMenu();
                return;
            }
        }
    }

    private void RefreshRecentMenu()
    {
        if (_recentMenu is null) return;
        _recentMenu.Items.Clear();
        var recent = LoadRecentItems(App.Services.GetRequiredService<ISettingsService>());
        foreach (var r in recent)
        {
            string path = r.Path;
            // The in-window menu bar (Windows / Linux) reads "_" as an access key; a name keeps its own.
            string header = OperatingSystem.IsMacOS() ? r.Name : r.Name.Replace("_", "__");
            var item = new NativeMenuItem(header) { ToolTip = path };
            item.Click += (_, _) => _ = OpenProjectConfirmedAsync(path);
            _recentMenu.Items.Add(item);
        }
        if (recent.Count == 0)
            _recentMenu.Items.Add(new NativeMenuItem(Loc.T("main.recent_menu.no_recent_projects")) { IsEnabled = false });
    }
}
