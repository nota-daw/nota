// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// After a project opens with hosted plugins that aren't installed, offer the ones the
// Nota plugin registry provides: install them (Get Plug-ins), rescan, and reopen the
// project so the devices come back with their saved state. Silent when offline or when
// the registry has none of them — the status bar already reports what was skipped.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public partial class MainWindow
{
    private async Task OfferMissingPluginsAsync(string dir, IReadOnlyList<string> missing)
    {
        if (_vm is null || missing.Count == 0) return;
        var store = App.Services.GetRequiredService<IPluginStore>();
        try { await store.FetchAsync(); }
        catch (PluginStoreException) { return; }

        var offers = missing.Select(store.FindProvider).OfType<StorePlugin>()
            .Where(p => p.Asset is not null).DistinctBy(p => p.Id)
            // Already installed yet still unresolved (the scan didn't match it): reinstalling won't help.
            .Where(p => !store.Installed.Any(i => i.Id == p.Id && i.Version == p.Version))
            .ToList();
        if (offers.Count == 0 || _projectPath != dir) return;

        bool one = offers.Count == 1;
        var names = string.Join(", ", offers.Select(p => p.Name));
        var ok = await new ConfirmWindow("Missing plugins",
            $"This project uses {names}, which {(one ? "isn't" : "aren't")} installed. " +
            $"{(one ? "It's" : "They're")} open source and available from the Nota plugin registry. " +
            "Install and reopen the project?",
            "Install", "Not now").ShowDialog<bool>(this);
        if (!ok || _vm is null) return;

        string? error = null;
        await RunBackgroundAsync("Installing plugins…", async prog =>
        {
            foreach (var p in offers)
            {
                try
                {
                    await store.InstallAsync(p, new Progress<StoreProgress>(r =>
                        prog.Report(r.Fraction < 0 ? ProgressReport.Indeterminate(r.Message) : ProgressReport.At(r.Fraction, r.Message))));
                }
                catch (PluginStoreException e) { error ??= e.Message; }
            }
            prog.Report(ProgressReport.Indeterminate("Scanning plugins…"));
            await PluginScan.RescanAsync(_vm);
        });

        if (error is not null) { _vm.StatusText = error; return; }
        // Reopen only when that's lossless: same project, nothing edited since it opened.
        if (_projectPath == dir && !HasUnsavedChanges()) OpenProject(dir);
        else _vm.StatusText = $"Installed {names} — reopen the project to use {(one ? "it" : "them")}.";
    }
}
