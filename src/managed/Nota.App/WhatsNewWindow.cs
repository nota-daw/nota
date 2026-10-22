// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// "What's new" — shown once after the first launch on a newer app version (see
// MainWindow.OnOpenedWhatsNew). Lists the versions the user hasn't seen yet, newest
// first: a release's curated notes (the GitHub release text) where it has them,
// otherwise its changelog section. Curated notes show their summary, then "All changes"
// (the Added / Changed / Fixed groups, each item with its commit link), then the
// contributors, all expanded. Links open in the browser. Reachable any time from the
// Help menu too.

using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed class WhatsNewWindow : NotaWindow
{
    public WhatsNewWindow(IReadOnlyList<ChangelogEntry> entries)
    {
        Title = "What's new in Nota";
        Width = 460;
        Height = 556;   // + title-bar band
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Brush.BgApp");

        // Fixed content width so long bullets wrap to the window instead of
        // overflowing (the window is a fixed 460 and non-resizable): 460 − 28px
        // side padding × 2 − room for the vertical scrollbar.
        var stack = new StackPanel { Spacing = 20, Width = 396 };
        foreach (var entry in entries)
            stack.Children.Add(EntryBlock(entry));

        if (entries.Count == 0)
            stack.Children.Add(new TextBlock
            {
                Classes = { "Caption" },
                Text = "No release notes available.",
            });

        var scroll = new ScrollViewer
        {
            Content = stack,
            Padding = new Thickness(28, 8, 28, 8),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var header = new StackPanel { Margin = new Thickness(28, 24, 28, 4), Spacing = 2 };
        header.Children.Add(new TextBlock
        {
            Text = "What's new",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("Brush.TextPrimary"),
        });
        header.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = $"Nota {AppInfo.Version}",
        });

        var ok = new Button
        {
            Content = "Got it",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(28, 8, 28, 24),
            Classes = { "primary" },
        };
        ok.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { Close(); e.Handled = true; } };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(header, 0);
        Grid.SetRow(scroll, 1);
        Grid.SetRow(ok, 2);
        root.Children.Add(header);
        root.Children.Add(scroll);
        root.Children.Add(ok);
        SetBody(root);
    }

    private Control EntryBlock(ChangelogEntry entry)
    {
        var panel = new StackPanel { Spacing = 8 };

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock
        {
            Text = "Version " + entry.Version,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("Brush.TextHeading"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrWhiteSpace(entry.Date))
            title.Children.Add(new TextBlock
            {
                Classes = { "Caption" },
                Text = entry.Date,
                VerticalAlignment = VerticalAlignment.Center,
            });
        panel.Children.Add(title);

        if (entry.IsReleaseNotes)
            panel.Children.Add(Paragraph(entry.Lede, "Brush.TextPrimary"));

        bool listStarted = false;
        foreach (var section in entry.Sections)
        {
            // The summary's features read as headings; the full list and the
            // contributors use the changelog's quieter group labels under one
            // "All changes" heading, after a seam.
            bool feature = entry.IsReleaseNotes && section.Kind == SectionKind.Feature;
            if (entry.IsReleaseNotes && section.Kind == SectionKind.Changes && !listStarted)
            {
                listStarted = true;
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Background = Brush("Brush.BorderDefault"),
                    Margin = new Thickness(0, 10, 0, 2),
                });
                panel.Children.Add(FeatureTitle("All changes"));
            }
            panel.Children.Add(feature
                ? FeatureTitle(section.Title)
                : new TextBlock
                {
                    Classes = { "SectionLabel" },
                    Text = section.Title,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            if (section.Text.Length > 0)
                panel.Children.Add(Paragraph(section.Text, "Brush.TextSecondary"));
            foreach (var item in section.Items)
                panel.Children.Add(Bullet(item));
        }
        return panel;
    }

    private TextBlock FeatureTitle(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush("Brush.TextPrimary"),
        Margin = new Thickness(0, 6, 0, 0),
    };

    private Control Paragraph(string text, string brush)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(brush),
        };
        AppendInlineMarkdown(block, text);
        return block;
    }

    private Control Bullet(string text)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*") };
        var dot = new TextBlock
        {
            Text = "•",
            Foreground = Brush("Brush.TextTertiary"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("Brush.TextSecondary"),
        };
        AppendInlineMarkdown(body, text);
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(body, 1);
        row.Children.Add(dot);
        row.Children.Add(body);
        return row;
    }

    // Renders the inline Markdown the changelog and release notes use: **bold**, *italic*,
    // `code` and [links](url) — the link text in accent colour, clickable (see
    // AttachLinks). Underscores are left alone — they show up inside identifiers like
    // `get_rhythm_macros`.
    private void AppendInlineMarkdown(TextBlock block, string text)
    {
        var inlines = block.Inlines ??= new InlineCollection();
        var buf = new StringBuilder();
        bool bold = false, italic = false;
        int pos = 0;   // characters laid out so far — where the next run starts
        var links = new List<Link>();

        void Flush()
        {
            if (buf.Length == 0) return;
            var run = new Run(buf.ToString());
            if (bold)
            {
                run.FontWeight = FontWeight.SemiBold;
                run.Foreground = Brush("Brush.TextPrimary");
            }
            if (italic) run.FontStyle = FontStyle.Italic;
            inlines.Add(run);
            pos += buf.Length;
            buf.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && "\\`*_[]()#".IndexOf(text[i + 1]) >= 0)
            {
                buf.Append(text[++i]);
            }
            else if (c == '`' && text.IndexOf('`', i + 1) is var end and > 0)
            {
                Flush();
                var code = new Run(text[(i + 1)..end]) { Foreground = Brush("Brush.TextPrimary") };
                if (this.TryFindResource("Font.Mono", out var mono) && mono is FontFamily ff)
                    code.FontFamily = ff;
                inlines.Add(code);
                pos += end - i - 1;
                i = end;
            }
            else if (c == '*' && i + 1 < text.Length && text[i + 1] == '*'
                     && (bold || text.IndexOf("**", i + 2, StringComparison.Ordinal) > 0))
            {
                Flush();
                bold = !bold;
                i++;
            }
            else if (c == '*' && (italic || text.IndexOf('*', i + 1) > 0))
            {
                Flush();
                italic = !italic;
            }
            else if (c == '[' && text.IndexOf("](", i + 1, StringComparison.Ordinal) is var mid and > 0
                     && text.IndexOf(')', mid + 2) is var close and > 0)
            {
                Flush();
                AppendLink(text[(i + 1)..mid], text[(mid + 2)..close]);
                i = close;
            }
            else buf.Append(c);
        }
        Flush();
        if (links.Count > 0) AttachLinks(block, links);

        void AppendLink(string label, string url)
        {
            var run = new Run(label) { Foreground = Brush("Brush.Accent") };
            if (bold) run.FontWeight = FontWeight.SemiBold;
            if (italic) run.FontStyle = FontStyle.Italic;
            inlines.Add(run);
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                links.Add(new Link(pos, label.Length, uri, run));
            pos += label.Length;
        }
    }

    private sealed record Link(int Start, int Length, Uri Uri, Run Run);

    // A Run can't take pointer input, so the block hit-tests the pointer against its
    // laid-out text and finds the link under it: hand cursor and underline on hover,
    // a click opens the URL in the default browser. The text keeps wrapping through
    // the link as ordinary text.
    private void AttachLinks(TextBlock block, List<Link> links)
    {
        Link? hovered = null;

        Link? At(PointerEventArgs e)
        {
            // HitTestPoint snaps to the nearest character even past a line's end, so
            // check the pointer is over that character's box (a click right of a line
            // that ends in a link must not open it).
            var p = e.GetPosition(block);
            int at = block.TextLayout.HitTestPoint(p).TextPosition;
            var link = links.Find(l => at >= l.Start && at < l.Start + l.Length);
            if (link == null) return null;
            var box = block.TextLayout.HitTestTextPosition(at);
            return p.Y >= box.Top && p.Y < box.Bottom && p.X >= box.Left - 1 && p.X <= box.Right + 1 ? link : null;
        }

        void Hover(Link? link)
        {
            if (ReferenceEquals(link, hovered)) return;
            if (hovered != null) hovered.Run.TextDecorations = null;
            hovered = link;
            if (link != null) link.Run.TextDecorations = TextDecorations.Underline;
            block.Cursor = link != null ? new Cursor(StandardCursorType.Hand) : null;
        }

        block.PointerMoved += (_, e) => Hover(At(e));
        block.PointerExited += (_, _) => Hover(null);
        block.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(block).Properties.IsLeftButtonPressed || At(e) is not { } link) return;
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(link.Uri);
            e.Handled = true;
        };
    }

    private IBrush Brush(string key)
        => (IBrush?)NotaPalette.ByKey(key) ?? NotaPalette.TextPrimary;
}
