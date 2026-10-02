// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The one running Downloads job (a plug-in, sample pack or AI model install / removal) and the
// outcome of the last one. It lives for the app, not for Preferences: closing the window leaves
// the download running, and the main window's status bar keeps showing it (MainWindow.Downloads.cs).
// Preferences → Downloads starts the jobs and shows the same state in its dock. Quitting the
// app cancels a running download.

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

internal sealed class DownloadJobs
{
    public static DownloadJobs Shared { get; } = new();

    /// <summary>Key of the item being installed / removed ("pack:<id>", "model:<id>", a plug-in
    /// id); null when idle.</summary>
    public string? BusyId { get; private set; }

    /// <summary>Which Downloads source the job belongs to: 0 plug-ins, 1 sample packs, 2 AI models.</summary>
    public int Source { get; private set; }

    public StoreProgress Progress { get; private set; }

    /// <summary>Bytes of the download in progress, 0 when unknown or past the download stage.</summary>
    public long Size { get; private set; }

    /// <summary>The last job's outcome; a success clears itself after a while, an error stays.</summary>
    public string? Result { get; private set; }

    public bool Busy => BusyId is not null;

    /// <summary>A Cancel is possible (the download stage of an install).</summary>
    public bool Cancellable => _job is not null;
    public bool Cancelling => _job is { IsCancellationRequested: true };

    /// <summary>A job started or ended, or the result changed: lists need re-rendering.</summary>
    public event Action? Changed;

    /// <summary>The running job reported progress.</summary>
    public event Action? ProgressChanged;

    private readonly CancellationTokenSource _app = new();   // app lifetime
    private CancellationTokenSource? _job;                   // the running download; null when it can't be cancelled
    private int _resultSeq;

    /// <summary>Install: <paramref name="download"/> runs cancellable, then <paramref name="finish"/>
    /// (no cancel) returns the line to show.</summary>
    public async void Install(string key, int source, string name, long size, StoreProgress first,
        Func<IProgress<StoreProgress>, CancellationToken, Task> download, Func<Task<string>> finish)
    {
        if (Busy) return;
        Begin(key, source, size, first);
        var job = _job = CancellationTokenSource.CreateLinkedTokenSource(_app.Token);
        // A report queued before Cancel must not overwrite "Cancelling…". Only the download has
        // a known byte count; a later stage's fraction is of something else.
        var progress = new Progress<StoreProgress>(r =>
        {
            if (job.IsCancellationRequested || BusyId != key) return;
            if (!r.Message.StartsWith("Downloading", StringComparison.Ordinal)) Size = 0;
            Progress = r;
            ProgressChanged?.Invoke();
        });
        string? result = null;
        bool sticky = false;
        try
        {
            await download(progress, job.Token);
            _job = null;   // the files are in place — too late to cancel
            Changed?.Invoke();
            result = await finish();
        }
        catch (OperationCanceledException)
        {
            // Quitting: nothing to show. Otherwise the user pressed Cancel.
            if (!_app.IsCancellationRequested) result = $"Installing {name} was cancelled.";
        }
        catch (StoreException e) { result = e.Message; sticky = true; }
        catch (Exception e)
        {
            App.Services.GetRequiredService<ILogSink>().Error($"Installing {key} failed", e);
            result = $"Installing {name} failed: {e.Message}";
            sticky = true;
        }
        finally
        {
            _job = null;
            job.Dispose();
        }
        End(result, sticky);
    }

    /// <summary>Removal: not cancellable; <paramref name="remove"/> returns the line to show.</summary>
    public async void Remove(string key, int source, string message, Func<Task<string>> remove)
    {
        if (Busy) return;
        Begin(key, source, 0, new StoreProgress(-1, message));
        string? result;
        bool sticky = false;
        try { result = await remove(); }
        catch (StoreException e) { result = e.Message; sticky = true; }
        catch (Exception e)
        {
            App.Services.GetRequiredService<ILogSink>().Error($"Removing {key} failed", e);
            result = e.Message;
            sticky = true;
        }
        End(result, sticky);
    }

    /// <summary>Progress from a job's own later stage (its finish step).</summary>
    public void Report(StoreProgress r)
    {
        if (!Busy) return;
        Size = 0;
        Progress = r;
        ProgressChanged?.Invoke();
    }

    public void Cancel()
    {
        if (_job is not { IsCancellationRequested: false } job) return;
        job.Cancel();
        Progress = Progress with { Message = "Cancelling…" };
        ProgressChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Hide the last outcome (the dock's / status bar's close button).</summary>
    public void Dismiss()
    {
        if (Result is null) return;
        Result = null;
        Changed?.Invoke();
    }

    /// <summary>App exit: stop a running download.</summary>
    public void CancelAll() => _app.Cancel();

    private void Begin(string key, int source, long size, StoreProgress first)
    {
        BusyId = key;
        Source = source;
        Size = size;
        Progress = first;
        Result = null;
        _resultSeq++;
        Changed?.Invoke();
    }

    private void End(string? result, bool sticky)
    {
        BusyId = null;
        Result = result;
        int seq = ++_resultSeq;
        if (result is not null && !sticky)
            DispatcherTimer.RunOnce(() => { if (seq == _resultSeq && !Busy) { Result = null; Changed?.Invoke(); } }, TimeSpan.FromSeconds(8));
        Changed?.Invoke();
    }
}
