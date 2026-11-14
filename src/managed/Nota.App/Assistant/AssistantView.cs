// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Assistant panel's body (specs/098, contracts/ui.md § Panel): the conversation, the input
// and the Show me bar. A question goes to an IAssistantResponder off the UI thread; its reply is
// Markdown (MarkdownView). The conversation lives for the session only. Typing here never
// reaches the app's shortcuts or the computer-keyboard notes (MainWindow.Input skips text
// boxes), and Escape hands focus back to the main view.

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application.Assistant;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class AssistantView : DockPanel
{
    private readonly Conversation _conversation = new();
    private readonly IAssistantResponder _responder;
    private readonly StackPanel _messages = new() { Spacing = 14, Margin = new Thickness(12, 12, 12, 16) };
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private readonly Button _send;
    private readonly Border _walkBar;
    private CancellationTokenSource? _pending;

    /// <summary>A link was clicked: nota-manual: and web links (open them), nota:showme/n.</summary>
    public event Action<ChatMessage, string>? LinkClicked;

    /// <summary>Escape in an empty input: give the keyboard back to the main view.</summary>
    public event Action? FocusOut;

    public Conversation Conversation => _conversation;

    /// <summary>The Show me bar's host, above the input (ShowMeController fills it).</summary>
    public Border WalkthroughBar => _walkBar;

    public AssistantView(IAssistantResponder responder)
    {
        _responder = responder;

        _input = new TextBox
        {
            Classes = { "field" },
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 120,
            MinHeight = 30,
            PlaceholderText = Loc.T("assistant.placeholder"),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(_input, Loc.T("assistant.placeholder"));
        _input.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);

        _send = new Button
        {
            Classes = { "primary" },
            Content = Loc.T("assistant.send"),
            Height = 30,
            Padding = new Thickness(12, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 0, 0),
        };
        Avalonia.Automation.AutomationProperties.SetName(_send, Loc.T("assistant.send"));
        _send.Click += (_, _) => Send();

        var inputRow = new DockPanel { Margin = new Thickness(10, 8, 10, 10) };
        DockPanel.SetDock(_send, Dock.Right);
        inputRow.Children.Add(_send);
        inputRow.Children.Add(_input);
        var bottom = new Border
        {
            BorderBrush = NotaPalette.Seam,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.Panel,
            Child = inputRow,
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        Children.Add(bottom);

        _walkBar = new Border { IsVisible = false, Background = NotaPalette.Panel, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(_walkBar, Dock.Bottom);
        Children.Add(_walkBar);

        _scroll = new ScrollViewer
        {
            Content = _messages,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        Children.Add(_scroll);

        _conversation.Changed += Rebuild;
        Rebuild();
    }

    public void FocusInput() => Dispatcher.UIThread.Post(() => _input.Focus(), DispatcherPriority.Input);

    public void Clear()
    {
        _pending?.Cancel();
        _conversation.Clear();
    }

    /// <summary>Ask a question as if typed (the example chips, tests).</summary>
    public void Ask(string question)
    {
        _input.Text = question;
        Send();
    }

    private void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            Send();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && string.IsNullOrEmpty(_input.Text))
        {
            FocusOut?.Invoke();
            e.Handled = true;
        }
    }

    private async void Send()
    {
        string q = (_input.Text ?? "").Trim();
        if (q.Length == 0) return;
        if (q.Length > 2000) q = q[..2000];   // pasted text: search on its start
        _input.Text = "";
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        _conversation.Add(ChatMessage.FromUser(q));
        ShowPending(true);
        ChatMessage reply;
        try { reply = await Task.Run(() => _responder.ReplyAsync(q, cts.Token), cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[assistant] reply failed: " + ex.Message);
            reply = AssistantReply.Unavailable();
        }
        if (cts.IsCancellationRequested) return;
        _pending = null;
        ShowPending(false);
        _conversation.Add(reply);
    }

    // ---- drawing ---------------------------------------------------------------------------

    private TextBlock? _pendingLabel;

    private void ShowPending(bool on)
    {
        if (on)
        {
            _pendingLabel ??= new TextBlock
            {
                Text = Loc.T("assistant.searching"),
                Foreground = NotaPalette.TextTertiary,
                FontFamily = NotaFonts.UiFamily,
                FontSize = 12,
            };
            if (!_messages.Children.Contains(_pendingLabel)) _messages.Children.Add(_pendingLabel);
            ScrollToEnd();
        }
        else if (_pendingLabel is not null) _messages.Children.Remove(_pendingLabel);
    }

    // Controls for the messages shown so far; a new message is appended, not the whole list
    // rebuilt (a long conversation stays quick). Clear starts over.
    private int _shown;

    private void Rebuild()
    {
        var msgs = _conversation.Messages;
        if (msgs.Count == 0 || msgs.Count < _shown)
        {
            _messages.Children.Clear();
            _shown = 0;
            if (msgs.Count == 0) { _messages.Children.Add(EmptyState()); return; }
        }
        if (_shown == 0) _messages.Children.Clear();   // the empty state goes with the first message
        if (_pendingLabel is not null) _messages.Children.Remove(_pendingLabel);
        for (; _shown < msgs.Count; _shown++)
        {
            var m = msgs[_shown];
            _messages.Children.Add(m.Role == ChatRole.User ? UserBubble(m) : ReplyBlock(m));
        }
        if (_pending is not null && _pendingLabel is not null) _messages.Children.Add(_pendingLabel);
        ScrollToEnd();
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);

    private Control EmptyState()
    {
        var stack = new StackPanel { Spacing = 10 };
        var intro = new MarkdownView { Markdown = Loc.T("assistant.intro") };
        stack.Children.Add(intro);
        foreach (var q in new[] { Loc.T("assistant.example_1"), Loc.T("assistant.example_2"), Loc.T("assistant.example_3") })
        {
            var chip = new Button
            {
                Classes = { "chip" },
                Content = new TextBlock { Text = q, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6),
                Height = double.NaN,
            };
            chip.Click += (_, _) => Ask(q);
            stack.Children.Add(chip);
        }
        return stack;
    }

    private static Control UserBubble(ChatMessage m) => new Border
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        MaxWidth = 300,
        Background = NotaPalette.SurfaceRaised,
        CornerRadius = NotaRadius.Group,
        Padding = new Thickness(10, 7),
        Child = new SelectableTextBlock
        {
            Text = m.Markdown,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = NotaFonts.UiFamily,
            FontSize = 12,
            Foreground = NotaPalette.TextPrimary,
            SelectionBrush = NotaPalette.AccentSoft,
        },
    };

    private Control ReplyBlock(ChatMessage m)
    {
        var md = new MarkdownView { Markdown = m.Markdown };
        md.LinkClicked += url => LinkClicked?.Invoke(m, url);
        var copy = new Button
        {
            Classes = { "ghost" },
            Height = 22,
            Padding = new Thickness(6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = NotaPalette.TextTertiary,
            Content = new Glyph(GlyphKind.Copy),
        };
        ToolTip.SetTip(copy, Loc.T("assistant.copy"));
        Avalonia.Automation.AutomationProperties.SetName(copy, Loc.T("assistant.copy"));
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(m.Markdown);
        };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(md);
        stack.Children.Add(copy);
        return stack;
    }
}
