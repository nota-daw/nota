// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Progress reporting for long operations. Two surfaces share one report type:
//   • ProgressWindow — a modal dialog for UI-blocking work (export, MIDI convert).
//   • the status bar's background strip — for work the user can keep playing over
//     (see MainWindow.RunBackgroundAsync + the StatusBar in MainWindow.axaml).
// Both consume ProgressReport through IProgress<ProgressReport>, so an operation is
// written once and can be surfaced either way.

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Nota.App;

/// <summary>One progress tick. <see cref="Fraction"/> is 0..1, or &lt; 0 for an
/// indeterminate state (the phase length isn't known yet). <see cref="Message"/> is the
/// label shown next to the bar.</summary>
public readonly record struct ProgressReport(double Fraction, string Message)
{
    public static ProgressReport Indeterminate(string message) => new(-1.0, message);
    public static ProgressReport At(double fraction, string message) => new(fraction, message);
}

/// <summary>Modal progress dialog for operations that take over the engine / block the UI
/// (export, MIDI conversion). A label over a thin bar, in the shared window chrome. Drive it
/// through <see cref="RunAsync"/>, which shows it for the lifetime of a unit of work.</summary>
public sealed class ProgressWindow : NotaWindow
{
    private readonly TextBlock _label;
    private readonly ProgressBar _bar;

    public ProgressWindow(string title, string initial)
    {
        Title = title;
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Height;
        Width = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _label = new TextBlock
        {
            Text = initial,
            Foreground = Brush("Brush.TextPrimary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 4,
            IsIndeterminate = true,
            Foreground = Brush("Brush.Accent"),
            Background = Brush("Brush.BorderDefault"),
        };
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(20) };
        body.Children.Add(_label);
        body.Children.Add(_bar);
        SetBody(body);
    }

    /// <summary>Apply a report to the bar/label. Safe to call from any thread.</summary>
    public void Report(ProgressReport r)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Report(r)); return; }
        if (!string.IsNullOrEmpty(r.Message)) _label.Text = r.Message;
        if (r.Fraction < 0.0) _bar.IsIndeterminate = true;
        else { _bar.IsIndeterminate = false; _bar.Value = Math.Clamp(r.Fraction, 0.0, 1.0) * 100.0; }
    }

    /// <summary>Show a modal progress dialog over <paramref name="owner"/> for the duration of
    /// <paramref name="work"/>, which reports through the supplied progress sink. The dialog
    /// closes when the work finishes; an exception from the work propagates after it closes.</summary>
    public static async Task RunAsync(Window owner, string title, string initial,
                                      Func<IProgress<ProgressReport>, Task> work)
    {
        var win = new ProgressWindow(title, initial);
        var progress = new Progress<ProgressReport>(win.Report);
        var opened = new TaskCompletionSource();
        win.Opened += (_, _) => opened.TrySetResult();
        var dialog = win.ShowDialog(owner);   // modal; completes when the window closes
        await opened.Task;                    // don't close before it's actually on screen
        try { await work(progress); }
        finally { win.Close(); await dialog; }
    }

    private static IBrush Brush(string key) => (IBrush?)NotaPalette.ByKey(key) ?? Brushes.Gray;
}
