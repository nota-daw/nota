// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Links into the user manual (nota-daw/nota-docs, published on GitHub Pages): Help ▸
// Documentation and each device card's "Documentation" item. The manual is English with a
// translation under /<code>/ for each language ManualLanguage lists (French, Russian); the one
// that opens follows the language the interface runs in. Device pages come from DeviceSlugs
// (shared with the Assistant's Show me anchors).

using System;
using Avalonia.Controls;
using Nota.Application.Localization;
using Nota.Presentation.Assistant;

namespace Nota.App;

internal static class NotaDocs
{
    private const string BaseUrl = "https://nota-daw.github.io/nota-docs/";

    /// <summary>The manual's address for a page ("" = the home page), in the UI's language.</summary>
    public static string Url(string page = "")
        => BaseUrl + ManualLanguage.Folder(Loc.Language) + (page.Length > 0 ? page.TrimEnd('/') + "/" : "");

    /// <summary>A page in a given manual language ("en", "ru", …) with an optional heading
    /// fragment — the Assistant's quotes link to the language they were found in (specs/098).</summary>
    public static string Url(string lang, string page, string fragment)
        => BaseUrl + ManualLanguage.Folder(lang) + (page.Trim('/').Length > 0 ? page.Trim('/') + "/" : "")
           + (fragment.Length > 0 ? "#" + Uri.EscapeDataString(fragment.TrimStart('#')) : "");

    /// <summary>The page for a built-in device, or the plug-ins page for anything else.</summary>
    public static string DevicePage(DeviceChainView.ChainKind kind, int engineKind)
    {
        var group = kind switch
        {
            DeviceChainView.ChainKind.Instrument => DeviceGroup.Instrument,
            DeviceChainView.ChainKind.Midi => DeviceGroup.MidiEffect,
            _ => DeviceGroup.AudioEffect,
        };
        return DeviceSlugs.PageOf(group, engineKind) is { Length: > 0 } page ? page : "plugins";
    }

    /// <summary>Open a manual page in the default browser.</summary>
    public static void Open(Control anchor, string page = "")
    {
        var launcher = TopLevel.GetTopLevel(anchor)?.Launcher;
        if (launcher != null) _ = launcher.LaunchUriAsync(new Uri(Url(page)));
    }
}
