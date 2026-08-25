// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Progress plumbing for long operations. Two entry points:
//   • RunBlockingAsync — a modal ProgressWindow for work that takes over the engine or
//     the UI (export, MIDI convert).
//   • RunBackgroundAsync — the status-bar strip for work the user can keep playing over.
// Both hand the work an IProgress<ProgressReport> so an operation reports the same way
// regardless of which surface shows it.

using System;
using System.Threading.Tasks;

namespace Nota.App;

public partial class MainWindow
{
    /// <summary>Run <paramref name="work"/> behind a modal progress dialog over this window.</summary>
    private Task RunBlockingAsync(string title, string initial, Func<IProgress<ProgressReport>, Task> work)
        => ProgressWindow.RunAsync(this, title, initial, work);

    /// <summary>Run <paramref name="work"/> with progress shown in the status bar's background
    /// strip (non-modal). The strip appears for the duration and clears when the work ends.</summary>
    private async Task RunBackgroundAsync(string label, Func<IProgress<ProgressReport>, Task> work)
    {
        var progress = new Progress<ProgressReport>(ApplyBackground);
        if (_vm is not null)
        {
            _vm.BackgroundLabel = label;
            _vm.BackgroundIndeterminate = true;
            _vm.BackgroundProgress = 0;
            _vm.BackgroundBusy = true;
        }
        try { await work(progress); }
        finally { if (_vm is not null) { _vm.BackgroundBusy = false; _vm.BackgroundLabel = ""; } }
    }

    private void ApplyBackground(ProgressReport r)
    {
        if (_vm is null) return;
        if (!string.IsNullOrEmpty(r.Message)) _vm.BackgroundLabel = r.Message;
        if (r.Fraction < 0.0) _vm.BackgroundIndeterminate = true;
        else { _vm.BackgroundIndeterminate = false; _vm.BackgroundProgress = Math.Clamp(r.Fraction, 0.0, 1.0) * 100.0; }
    }
}
