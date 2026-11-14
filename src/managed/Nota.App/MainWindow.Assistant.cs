// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Assistant panel in the main window (specs/098): a column on the right of the work area,
// full height beside the body (browser and main view over the Mixer and Detail panels). Opened
// and closed by the toolbar key, View ▸ Assistant and view.assistant (⌘⇧H); its width and
// whether it was open are remembered. The panel's view is built the first time it opens, and
// only then does the manual index load (on a background thread) — startup is untouched.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Assistant;
using Nota.Application.Localization;
using Nota.Infrastructure;

namespace Nota.App;

public partial class MainWindow
{
    private const double AssistantMinWidth = 280, AssistantMaxWidth = 560, AssistantDefaultWidth = 360;

    private AssistantView? _assistant;
    private ShowMeController? _showMe;

    private static ISettingsService AssistantSettings => App.Services.GetRequiredService<ISettingsService>();

    internal bool IsAssistantShown => AssistantPanel.IsVisible;

    private void OnToggleAssistantButton(object? sender, RoutedEventArgs e)
    {
        _commands.Run("view.assistant");
        SyncAssistantToggle();
    }

    private void OnAssistantClose(object? sender, RoutedEventArgs e) => HideAssistant();

    private void OnAssistantClear(object? sender, RoutedEventArgs e)
    {
        _showMe?.Stop();
        _assistant?.Clear();
    }

    private void SyncAssistantToggle() => AssistantToggle.IsChecked = IsAssistantShown;

    internal void ToggleAssistant()
    {
        if (IsAssistantShown) HideAssistant();
        else ShowAssistant();
    }

    internal void ShowAssistant(bool focus = true)
    {
        EnsureAssistant();
        var col = WorkGrid.ColumnDefinitions[2];
        col.MinWidth = AssistantMinWidth;
        col.MaxWidth = AssistantMaxWidth;
        col.Width = new GridLength(Math.Clamp(AssistantSettings.Current.AssistantWidth, AssistantMinWidth, AssistantMaxWidth));
        AssistantPanel.IsVisible = true;
        AssistantSplitter.IsVisible = true;
        FitAssistant();
        RememberAssistant(shown: true);
        SyncAssistantToggle();
        if (focus) _assistant!.FocusInput();
    }

    internal void HideAssistant()
    {
        _showMe?.Stop();
        // The width is remembered when the user drags the edge, not here: the column may be
        // narrowed for the moment by a narrow window (FitAssistant).
        var col = WorkGrid.ColumnDefinitions[2];
        AssistantPanel.IsVisible = false;
        AssistantSplitter.IsVisible = false;
        col.MinWidth = 0;
        col.Width = new GridLength(0);
        RememberAssistant(shown: false);
        SyncAssistantToggle();
        Timeline.Focus();
    }

    // The main view keeps a usable width (MainViewMinWidth) beside the browser: on a narrow
    // window the panel gives way first, down to its own minimum (T056). The window's own
    // minimum (1100) always leaves room for both.
    private const double MainViewMinWidth = 360;

    private void FitAssistant()
    {
        if (!AssistantPanel.IsVisible) return;
        var col = WorkGrid.ColumnDefinitions[2];
        double browser = BrowserGrid.ColumnDefinitions[0].ActualWidth;
        double room = WorkGrid.Bounds.Width - browser - MainViewMinWidth;
        if (room <= 0 || WorkGrid.Bounds.Width <= 0) return;
        double want = Math.Clamp(AssistantSettings.Current.AssistantWidth, AssistantMinWidth, AssistantMaxWidth);
        col.Width = new GridLength(Math.Max(AssistantMinWidth, Math.Min(want, room)));
    }

    private void RestoreAssistant()
    {
        WorkGrid.SizeChanged += (_, e) => { if (e.WidthChanged) FitAssistant(); };
        AssistantSplitter.DragCompleted += (_, _) =>
        {
            var w = WorkGrid.ColumnDefinitions[2].Width;
            if (w.IsAbsolute) RememberAssistantWidth(w.Value);
        };
        AssistantSplitter.DoubleTapped += (_, _) =>
        {
            WorkGrid.ColumnDefinitions[2].Width = new GridLength(AssistantDefaultWidth);
            RememberAssistantWidth(AssistantDefaultWidth);
        };
        if (AssistantSettings.Current.AssistantShown) ShowAssistant(focus: false);
        SyncAssistantToggle();
    }

    private void EnsureAssistant()
    {
        if (_assistant is not null) return;
        var meaning = ManualMeaning.Shared(App.Services.GetRequiredService<IModelStore>());
        var engine = Engine;
        ManualMeaning.Busy = () => engine.IsRecording;   // no background embedding while recording
        var responder = new ManualResponder(
            async () => await ManualIndexSource.LoadAsync(),
            meaning,
            () => Loc.Language);
        _assistant = new AssistantView(responder);
        _assistant.LinkClicked += OnAssistantLink;
        _assistant.FocusOut += () => Timeline.Focus();
        _showMe = new ShowMeController(this, _assistant);
        AssistantHost.Content = _assistant;
        _ = ManualIndexSource.LoadAsync();          // warm up while the user types
        meaning.Warm();
    }

    private void OnAssistantLink(ChatMessage message, string url)
    {
        if (url.StartsWith("nota:showme/", StringComparison.Ordinal))
        {
            if (int.TryParse(url["nota:showme/".Length..], out int n) && n >= 0 && n < message.Walkthroughs.Count)
                _showMe?.Start(message.Walkthroughs[n]);
            return;
        }
        string target = url;
        if (url.StartsWith("nota-manual:", StringComparison.Ordinal))
        {
            string rest = url["nota-manual:".Length..];
            string frag = "";
            int hash = rest.IndexOf('#');
            if (hash >= 0) { frag = rest[(hash + 1)..]; rest = rest[..hash]; }
            int slash = rest.IndexOf('/');
            string lang = slash < 0 ? rest : rest[..slash];
            string page = slash < 0 ? "" : rest[(slash + 1)..];
            target = NotaDocs.Url(lang, page, frag);
        }
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            _ = Launcher.LaunchUriAsync(uri);
    }

    private static void RememberAssistant(bool shown)
    {
        var settings = AssistantSettings;
        if (settings.Current.AssistantShown == shown) return;
        settings.Current.AssistantShown = shown;
        settings.Save();
    }

    private static void RememberAssistantWidth(double width)
    {
        var settings = AssistantSettings;
        double w = Math.Round(Math.Clamp(width, AssistantMinWidth, AssistantMaxWidth));
        if (Math.Abs(settings.Current.AssistantWidth - w) < 0.5) return;
        settings.Current.AssistantWidth = w;
        settings.Save();
    }
}
