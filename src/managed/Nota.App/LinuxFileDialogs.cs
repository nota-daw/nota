// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// File dialogs that always open on Linux (#297). Avalonia asks xdg-desktop-portal first;
// when the portal is installed but has no working FileChooser backend (tiling WMs, minimal
// installs, mismatched portals) the request fails, returns nothing at once, or never
// answers — and no dialog appears. Here each picker tries the system dialog and falls back
// to Avalonia's built-in chooser when it throws, comes back empty without ever showing, or
// shows nothing within a few seconds. After one failure the session goes straight to the
// built-in chooser. NOTA_FILE_DIALOG=builtin forces it from the start. Every picker in the
// app goes through PickFilesAsync / PickSaveFileAsync / PickFoldersAsync.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Dialogs;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

internal static class LinuxFileDialogs
{
    /// <summary>How long a system picker may take to show before it counts as broken.</summary>
    internal static readonly TimeSpan ShowTimeout = TimeSpan.FromSeconds(4);

    // Set once a system picker failed: the rest of the session uses the built-in one.
    internal static bool SystemBroken { get; set; } =
        string.Equals(Environment.GetEnvironmentVariable("NOTA_FILE_DIALOG"), "builtin", StringComparison.OrdinalIgnoreCase);

    // On Linux TopLevel.StorageProvider is the built-in chooser (UseManagedSystemDialogs), and
    // the pickers below try the system one (the platform's own provider) first.
    private static bool _enabled;

    public static AppBuilder WithLinuxFileDialogFallback(this AppBuilder builder)
    {
        if (!OperatingSystem.IsLinux()) return builder;
        _enabled = true;
        return builder.UseManagedSystemDialogs();
    }

    /// <summary>Open-file picker that falls back to the built-in chooser on Linux.</summary>
    public static Task<IReadOnlyList<IStorageFile>> PickFilesAsync(this TopLevel top, FilePickerOpenOptions options)
        => Picker(top, p => p.OpenFilePickerAsync(options), r => r.Count == 0);

    /// <summary>Save-file picker that falls back to the built-in chooser on Linux.</summary>
    public static Task<IStorageFile?> PickSaveFileAsync(this TopLevel top, FilePickerSaveOptions options)
        => Picker(top, p => p.SaveFilePickerAsync(options), r => r is null);

    /// <summary>Folder picker that falls back to the built-in chooser on Linux.</summary>
    public static Task<IReadOnlyList<IStorageFolder>> PickFoldersAsync(this TopLevel top, FolderPickerOpenOptions options)
        => Picker(top, p => p.OpenFolderPickerAsync(options), r => r.Count == 0);

    // Runs the system picker; if it throws, answers "nothing" without ever having taken the
    // focus, or doesn't take the focus within ShowTimeout, the built-in one runs instead.
    private static async Task<T> Picker<T>(TopLevel top, Func<IStorageProvider, Task<T>> open, Func<T, bool> isEmpty)
    {
        var builtin = top.StorageProvider;
        if (!_enabled || top.PlatformImpl?.TryGetFeature<IStorageProvider>() is not { } system)
            return await open(builtin);   // not Linux: StorageProvider is the system dialog
        if (SystemBroken) return await open(builtin);
        var window = top as Window;
        bool shown = false;
        void OnDeactivated(object? s, EventArgs e) => shown = true;
        if (window is not null) window.Deactivated += OnDeactivated;
        try
        {
            var task = open(system);
            bool finished = await Task.WhenAny(task, Task.Delay(ShowTimeout)) == task;
            var result = finished ? await task : default!;   // rethrows a failed request
            bool active = window?.IsActive ?? false;          // no window to watch: trust the dialog
            return FileDialogFallback.Decide(finished, finished && isEmpty(result), shown, active) switch
            {
                FileDialogFallback.Verdict.Keep => result,
                FileDialogFallback.Verdict.Wait => await task,   // a dialog is up: wait for the user
                _ => await Broken(finished ? "the system file dialog answered without showing"
                                           : $"the system file dialog didn't open within {ShowTimeout.TotalSeconds:0} s"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await Broken($"the system file dialog failed: {ex.Message}");
        }
        finally
        {
            if (window is not null) window.Deactivated -= OnDeactivated;
        }

        Task<T> Broken(string why)
        {
            SystemBroken = true;
            App.Services?.GetService<ILogSink>()?.Warn(
                $"Using Nota's built-in file dialog: {why}. Check xdg-desktop-portal and its FileChooser backend.");
            return open(builtin);
        }
    }
}
