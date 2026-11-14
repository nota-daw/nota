// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Show me (specs/098, US3): walks a how-to's anchored steps one at a time. Entering a step shows
// the anchor's view and runs its reveal steps (MainWindow.ShowMe), then outlines the control and
// scrolls it into view; the panel's bar says "Step k of N" with the step's words and Back / Next /
// Done. When a step can't be shown as it is — no track selected for a Detail control, a control
// this build doesn't have, a command with no button — the bar says what to do instead, so a step
// never fails silently. Escape, Done, closing the panel or the project ends it; nothing in the
// project changes.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Nota.App.Input;
using Nota.Application.Assistant;
using Nota.Application.Localization;
using Nota.Presentation.Assistant;

namespace Nota.App;

internal sealed class ShowMeController
{
    private readonly MainWindow _window;
    private readonly AssistantView _view;
    private Walkthrough? _walk;
    private int _step;

    public ShowMeController(MainWindow window, AssistantView view)
    {
        _window = window;
        _view = view;
        _window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    public bool Running => _walk is not null;

    /// <summary>What the bar told the user instead of (or beside) the highlight, for the last
    /// step entered; null when it only highlighted (tests).</summary>
    public string? LastGuidance { get; private set; }

    public void Start(Walkthrough walk)
    {
        if (walk.Steps.Count == 0) return;
        _walk = walk;
        Go(0);
    }

    public void Stop()
    {
        HighlightAdorner.Hide();
        _walk = null;
        _view.WalkthroughBar.IsVisible = false;
        _view.WalkthroughBar.Child = null;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_walk is null || e.Key != Key.Escape) return;
        Stop();
        e.Handled = true;
    }

    private void Go(int step)
    {
        if (_walk is null) return;
        _step = Math.Clamp(step, 0, _walk.Steps.Count - 1);
        var s = _walk.Steps[_step];
        if (!_window.IsActive) _window.Activate();   // a plug-in or other window in front
        string? guidance = Enter(s.Anchor);
        LastGuidance = guidance;
        DrawBar(s, guidance);
    }

    // Shows the step's control; returns what to tell the user instead, or null.
    private string? Enter(string anchor)
    {
        HighlightAdorner.Hide();
        AnchorDefinition? def = null;
        string? commandId = null;
        if (anchor.StartsWith(AnchorRegistry.CommandPrefix, StringComparison.Ordinal))
        {
            commandId = anchor[AnchorRegistry.CommandPrefix.Length..];
            def = AnchorRegistry.ForCommand(commandId);
        }
        else def = AnchorRegistry.Find(anchor);

        if (def is null)
        {
            if (commandId is null) return Loc.T("assistant.control_missing");
            // A command with no button: open what it shows (a Settings page) and name it.
            string where = _window.CommandHint(commandId);
            string? key = BoundText(commandId);
            return where + (key is { Length: > 0 } ? (where.Length > 0 ? " · " : "") + KeyHint(key) : "");
        }

        _window.RevealView(def.View);
        if (_window.Unmet(def.Requires) is { } first)
        {
            if (first.Where is { } where) Outline(where);
            return Loc.F("assistant.first", first.Text);
        }
        foreach (var r in def.Reveal)
            if (!_window.Reveal(r))
            {
                Outline(_window.SelectTrackHint);
                return Loc.F("assistant.first", Loc.T("assistant.requires_track"));
            }

        // The layout settles after a reveal; find and outline the control once it has.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var target = _window.LocateAnchor(def.Locator);
            if (target is null) { LastGuidance = Loc.T("assistant.control_missing"); DrawBar(_walk!.Steps[_step], LastGuidance); return; }
            target = _window.ShedFallback(target) ?? target;
            target.BringIntoView();
            Outline(target);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
        string? key2 = commandId is not null ? BoundText(commandId) : null;
        return key2 is { Length: > 0 } ? KeyHint(key2) : null;
    }

    private static string KeyHint(string key) => $"<kbd>{key}</kbd>";

    // The user's current key for a command, spelled for this platform (Preferences ▸ Shortcuts).
    private static string? BoundText(string id) =>
        Keys.Bindings.Shown(id) is { } k ? Nota.Application.Input.Shortcut.SpellText(k.ToGlyphs()) : null;

    private static void Outline(Control c) => HighlightAdorner.Show(c);

    private void DrawBar(AnchoredStep step, string? guidance)
    {
        if (_walk is null) return;
        int n = _walk.Steps.Count;
        var caption = new TextBlock
        {
            Text = Loc.F("assistant.step_of", _step + 1, n),
            Classes = { "Caption" },
            Foreground = NotaPalette.TextTertiary,
        };
        var text = new MarkdownView { Markdown = step.Text + (guidance is null ? "" : "\n\n" + guidance) };
        var back = Btn(Loc.T("assistant.back"), () => Go(_step - 1), "ghost");
        back.IsEnabled = _step > 0;
        var next = Btn(Loc.T("assistant.next"), () => Go(_step + 1), "primary");
        next.IsVisible = _step < n - 1;
        var done = Btn(Loc.T("assistant.done"), Stop, _step < n - 1 ? "ghost" : "primary");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(back);
        buttons.Children.Add(next);
        buttons.Children.Add(done);
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(12, 10, 12, 10) };
        stack.Children.Add(caption);
        stack.Children.Add(text);
        stack.Children.Add(buttons);
        _view.WalkthroughBar.Child = stack;
        _view.WalkthroughBar.IsVisible = true;
        (next.IsVisible ? next : done).Focus();
    }

    private static Button Btn(string label, Action click, string cls)
    {
        var b = new Button { Content = label, Classes = { cls }, Height = 26, Padding = new Thickness(10, 0) };
        Avalonia.Automation.AutomationProperties.SetName(b, label);
        b.Click += (_, _) => click();
        return b;
    }
}
