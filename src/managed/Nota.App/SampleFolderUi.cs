// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The words and dialogs the sample-folder list (spec 092, #346 / #268) shares between the
// browser's Files tab and Settings → Library: why a folder wasn't added, the rename prompt,
// and the confirmation before the default folder is removed — so both places say the same.

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

internal static class SampleFolderUi
{
    /// <summary>A short note for a folder that wasn't added; null when it was.</summary>
    public static string? Refusal(SampleFolderResult r, IReadOnlyList<ResolvedSampleFolder> folders)
    {
        string Other() => r.Other >= 0 && r.Other < folders.Count ? folders[r.Other].Label : "";
        return r.Outcome switch
        {
            SampleFolderOutcome.Added => null,
            SampleFolderOutcome.AlreadyPresent => Loc.T("browser.folder_already_added"),
            SampleFolderOutcome.Inside => Loc.F("browser.folder_inside", Other()),
            SampleFolderOutcome.Contains => Loc.F("browser.folder_contains", Other()),
            _ => Loc.T("browser.not_a_folder"),
        };
    }

    /// <summary>Adds each folder, returning the note for the last one refused (or null).</summary>
    public static string? AddAll(ISettingsService settings, IEnumerable<string> paths)
    {
        string? note = null;
        foreach (var p in paths)
        {
            var r = settings.AddSampleFolder(p);
            note = Refusal(r, settings.ResolvedSampleFolders()) ?? note;
        }
        return note;
    }

    /// <summary>Asks for the label a folder shows; empty restores the folder's own name.</summary>
    public static async Task RenameAsync(Window owner, ISettingsService settings, ResolvedSampleFolder folder)
    {
        var name = await new TextPromptWindow(Loc.T("browser.rename_folder"), Loc.T("browser.folder_label"), folder.Label)
            .ShowDialog<string?>(owner);
        if (name is null) return;
        settings.RenameSampleFolder(folder.Index, name);
    }

    /// <summary>Forgets a folder (asks first when it is the default). Files on disk stay.</summary>
    public static async Task RemoveAsync(Window owner, ISettingsService settings, ResolvedSampleFolder folder)
    {
        var all = settings.ResolvedSampleFolders();
        if (all.Count <= 1) return;
        if (folder.IsDefault)
        {
            int next = folder.Index < all.Count - 1 ? folder.Index + 1 : folder.Index - 1;
            bool ok = await new ConfirmWindow(Loc.T("browser.remove_default_title"),
                Loc.F("browser.remove_default_confirm", all[next].Label),
                Loc.T("browser.remove_from_browser"), Loc.T("common.cancel")).ShowDialog<bool>(owner);
            if (!ok) return;
        }
        settings.RemoveSampleFolder(folder.Index);
    }
}
