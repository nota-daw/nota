// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The "Locate ▸ In browser / In Finder" submenu of an audio clip (spec 092, #581), shared by
// the arrangement's clip menu and Session's slot menu. The views know the clip's sample id;
// MainWindow supplies what to do (it knows the open project and the browser).

using System;
using Avalonia.Controls;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

/// <summary>What a view needs to offer Locate: the file behind a sample and the two actions.</summary>
internal sealed class LocateHooks
{
    /// <summary>The clip's source file if known and on disk, else null.</summary>
    public required Func<long, string?> Source { get; init; }
    /// <summary>The project's copy of the sample's audio, if saved, else null.</summary>
    public required Func<long, string?> BundleCopy { get; init; }
    public required Action<string> InBrowser { get; init; }
    public required Action<string> InFileManager { get; init; }
}

internal static class LocateMenu
{
    /// <summary>"In Finder" in the platform's own words.</summary>
    public static string FileManagerLabel =>
        OperatingSystem.IsMacOS() ? Loc.T("clip.locate_in_finder")
        : OperatingSystem.IsWindows() ? Loc.T("clip.locate_in_explorer")
        : Loc.T("clip.locate_in_file_manager");

    public static MenuItem Build(LocateHooks hooks, long sampleId)
    {
        string? source = hooks.Source(sampleId);
        string? reveal = source ?? hooks.BundleCopy(sampleId);
        var menu = MenuKit.Item(Loc.T("clip.locate"), GlyphKind.Search);
        menu.Items.Add(MenuKit.Item(Loc.T("clip.locate_in_browser"), null,
            () => { if (source is not null) hooks.InBrowser(source); }, enabled: source is not null));
        menu.Items.Add(MenuKit.Item(FileManagerLabel, GlyphKind.Folder,
            () => { if (reveal is not null) hooks.InFileManager(reveal); }, enabled: reveal is not null));
        return menu;
    }
}
