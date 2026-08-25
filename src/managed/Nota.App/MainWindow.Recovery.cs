// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    // --- crash recovery (M7-7) ---------------------------------------------

    /// <summary>Current transport values as a persistence snapshot.</summary>
    private TransportState TransportSnapshot()
        => new((double)_vm!.Transport.Bpm, _vm.Transport.MasterVolume,
               _vm.Transport.MetronomeOn, _vm.Transport.LoopOn,
               _vm.Transport.TimeSigNumerator, _vm.Transport.TimeSigDenominator);

    private void OnAutosaveTick()
    {
        if (_vm is null) return;
        try { _recovery.Autosave(Engine, TransportSnapshot(), _projectPath); }
        catch { /* autosave is best-effort — never disrupt the session */ }
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        CloseFloatingDetail();   // tear down the popped-out Devices/Clip window, if any
        ShutdownGamepad();       // stop the IOKit pad thread before the engine dies
        _recovery.EndSessionClean();
    }

    private async void OnOpenedRecoveryCheck(object? sender, EventArgs e)
    {
        if (_vm is null) return;

        // What's New shows once per new app version, ahead of the launcher.
        await ShowWhatsNewIfNeeded();

        // The welcome launcher carries the crash-recovery prompt inline (a banner), so it's
        // the single entry point on startup. When the launcher is switched off, fall back to
        // the standalone recovery dialog so a crashed session is still offered.
        var settings = App.Services.GetRequiredService<ISettingsService>();
        if (settings.Current.ShowWelcomeOnStartup)
            await ShowWelcomeAsync();
        else if (PendingRecovery is { } info)
            await OfferRecoveryDialogAsync(info);
    }

    /// <summary>Prompt text shared by the welcome banner and the standalone recovery dialog.</summary>
    private static string RecoveryPromptText(RecoveryInfo info)
    {
        string when = info.SavedAt;
        if (DateTime.TryParse(info.SavedAt, out var dt)) when = dt.ToString("g");
        return $"Nota didn't shut down cleanly. Recover unsaved work from {when}?";
    }

    /// <summary>Load the crash snapshot into the engine and point the next Save at the real
    /// bundle. Clears the snapshot so it isn't offered again.</summary>
    private void ApplyPendingRecovery(RecoveryInfo info)
    {
        OpenProject(info.BundlePath);
        _projectPath = info.OriginalPath;   // next Save targets the real bundle (null → prompt)
        UpdateWindowTitle();
        if (_vm is not null) _vm.StatusText = "Recovered unsaved work.";
        try { _recovery.ClearRecovery(); } catch { }
    }

    // Fallback path when the welcome launcher is disabled: a modal Recover / Discard dialog.
    private async Task OfferRecoveryDialogAsync(RecoveryInfo info)
    {
        bool restore = await new ConfirmWindow("Recover project",
            RecoveryPromptText(info), "Recover", "Discard").ShowDialog<bool>(this);
        if (restore) ApplyPendingRecovery(info);
        else try { _recovery.ClearRecovery(); } catch { }
    }

    // Show the changelog once per new app version: compare the running version to
    // the last one the user acknowledged (persisted in settings). Reachable any time
    // from the Help menu via ShowWhatsNew().
    private async Task ShowWhatsNewIfNeeded()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var seen = settings.Current.LastSeenVersion;
        if (seen == AppInfo.Version) return; // already acknowledged this build

        var unseen = AppInfo.UnseenSince(seen);
        if (unseen.Count > 0)
            await new WhatsNewWindow(unseen).ShowDialog(this);

        // Mark this version acknowledged even when there were no notes, so a fresh
        // install with an empty changelog doesn't re-check on every launch.
        settings.Current.LastSeenVersion = AppInfo.Version;
        settings.Save();
    }

    /// <summary>Open the changelog on demand (Help ▸ What's New) — always shows the
    /// entry for the current version, regardless of what's already been seen.</summary>
    public void ShowWhatsNew()
    {
        var entries = AppInfo.UnseenSince(""); // "" → current version's entry
        new WhatsNewWindow(entries).Show(this);
    }
}
