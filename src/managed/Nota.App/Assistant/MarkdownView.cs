// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Markdown in the Assistant panel (specs/098, research R8): Markdig parses, this walks the
// tree into Avalonia controls in the style guide's terms — one typeface, the palette's inks,
// the skin's radii — so a reply reads like the manual it quotes. Covers what the manual and the
// replies use (FR-012): headings, paragraphs, emphasis, strikethrough, inline code, code
// blocks, nested lists, quotes, GitHub alerts (the manual's note / tip / caution / danger
// asides), tables, rules, links and <kbd> key caps. Text is selectable (FR-014). Colours are the
// palette's live brushes, so a theme switch repaints without a rebuild (FR-013); a look or
// skin switch re-rounds through NotaSkin like every other control.
// Key caps and links are runs in the text, so a selected passage copies with them. A click on a
// link is hit-tested to its run; from the keyboard, Tab walks a paragraph's links and Return /
// Space runs one. LinkClicked hands the target to the panel, which opens nota-manual: and web
// links in the browser and runs nota: links (Show me).

using System;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class MarkdownView : StackPanel
{

    private const double BodySize = 12, CodeSize = 11.5;
    private static readonly double[] HeadingSizes = [15, 14, 13, 12, 12, 12];

    /// <summary>A link was clicked; the argument is its target.</summary>
    public event Action<string>? LinkClicked;

    private string _markdown = "";

    public MarkdownView()
    {
        Spacing = 8;
    }

    public string Markdown
    {
        get => _markdown;
        set { _markdown = value ?? ""; Build(); }
    }

    private void Build()
    {
        Children.Clear();
        var doc = Markdig.Markdown.Parse(_markdown, AssistantMarkdown.Pipeline);
        foreach (var block in doc) if (BlockControl(block) is { } c) Children.Add(c);
    }

    // ---- blocks ----------------------------------------------------------------------------

    private Control? BlockControl(Block block) => block switch
    {
        HeadingBlock h => Heading(h),
        ParagraphBlock p => Paragraph(p.Inline, NotaPalette.TextPrimary),
        ListBlock l => List(l),
        AlertBlock a => Alert(a),
        QuoteBlock q => Quote(q, NotaPalette.BorderStrong),
        FencedCodeBlock or CodeBlock => Code((LeafBlock)block),
        Table t => TableControl(t),
        ThematicBreakBlock => new Border { Height = 1, Background = NotaPalette.Rule, Margin = new Thickness(0, 4) },
        HtmlBlock html => Paragraph(null, NotaPalette.TextSecondary, Text(html)),
        _ => null,
    };

    private Control Heading(HeadingBlock h)
    {
        var tb = Paragraph(h.Inline, NotaPalette.TextHeading);
        tb.FontSize = HeadingSizes[Math.Clamp(h.Level - 1, 0, 5)];
        tb.FontWeight = FontWeight.SemiBold;
        tb.Margin = new Thickness(0, 4, 0, -2);
        return tb;
    }

    private SelectableTextBlock Paragraph(ContainerInline? inline, IBrush ink, string? plain = null)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = NotaFonts.UiFamily,
            FontSize = BodySize,
            LineHeight = BodySize * 1.45,
            Foreground = ink,
            SelectionBrush = NotaPalette.AccentSoft,
        };
        var inlines = tb.Inlines ??= new InlineCollection();
        if (plain is not null) inlines.Add(new Run(plain));
        else if (inline is not null) AddInlines(inlines, inline, Style.None);
        if (inlines.OfType<LinkRun>().Any()) HookLinks(tb);
        return tb;
    }

    private Control List(ListBlock list)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        int n = list.IsOrdered && int.TryParse(list.OrderedStart, out int start) ? start : 1;
        int row = 0;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var marker = new TextBlock
            {
                Text = list.IsOrdered ? $"{n++}." : "•",
                FontFamily = NotaFonts.UiFamily,
                FontSize = BodySize,
                LineHeight = BodySize * 1.45,
                Foreground = NotaPalette.TextTertiary,
                Margin = new Thickness(2, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = list.IsOrdered ? 16 : 10,
                TextAlignment = TextAlignment.Right,
            };
            var body = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var b in item) if (BlockControl(b) is { } c) body.Children.Add(c);
            Grid.SetRow(marker, row); Grid.SetRow(body, row); Grid.SetColumn(body, 1);
            grid.Children.Add(marker); grid.Children.Add(body);
            row++;
        }
        return grid;
    }

    private Control Quote(ContainerBlock q, IBrush edge, string? label = null)
    {
        var body = new StackPanel { Spacing = 6 };
        if (label is not null)
            body.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = NotaFonts.UiFamily,
                FontSize = BodySize - 1,
                FontWeight = FontWeight.SemiBold,
                Foreground = edge,
            });
        foreach (var b in q) if (BlockControl(b) is { } c) body.Children.Add(c);
        return new Border
        {
            BorderBrush = edge,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Background = NotaPalette.SurfaceInset,
            CornerRadius = NotaRadius.Cell,
            Padding = new Thickness(10, 8),
            Child = body,
        };
    }

    // The manual's asides: a coloured edge per kind (no info / warning roles in the palette).
    private Control Alert(AlertBlock a)
    {
        string kind = a.Kind.ToString().ToUpperInvariant();
        (IBrush edge, string label) = kind switch
        {
            "TIP" => ((IBrush)NotaPalette.Success, Loc.T("assistant.alert_tip")),
            "CAUTION" or "WARNING" => (NotaPalette.Accent, Loc.T("assistant.alert_caution")),
            "DANGER" => (NotaPalette.Danger, Loc.T("assistant.alert_danger")),
            _ => (NotaPalette.TextSecondary, Loc.T("assistant.alert_note")),
        };
        return Quote(a, edge, label);
    }

    private Control Code(LeafBlock code)
    {
        var text = new SelectableTextBlock
        {
            Text = code.Lines.ToString().TrimEnd('\n', '\r'),
            FontFamily = NotaFonts.MonoFamily,
            FontSize = CodeSize,
            Foreground = NotaPalette.TextPrimary,
            SelectionBrush = NotaPalette.AccentSoft,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 0, 0, 10),   // room for the overlay scroll bar
        };
        return new Border
        {
            Background = NotaPalette.SurfaceInset,
            BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Field,
            Padding = new Thickness(10, 8, 10, 4),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = text,
            },
        };
    }

    private Control TableControl(Table table)
    {
        var grid = new Grid();
        int cols = table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
        for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        int r = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int c = 0;
            foreach (var cell in row.OfType<TableCell>())
            {
                var stack = new StackPanel { Spacing = 4 };
                foreach (var b in cell)
                    if (BlockControl(b) is { } ctl)
                    {
                        if (row.IsHeader && ctl is SelectableTextBlock st) { st.FontWeight = FontWeight.SemiBold; st.Foreground = NotaPalette.TextSecondary; }
                        if (ctl is SelectableTextBlock w) w.MaxWidth = 320;
                        stack.Children.Add(ctl);
                    }
                var border = new Border
                {
                    Child = stack,
                    Padding = new Thickness(8, 5),
                    BorderBrush = NotaPalette.Rule,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = row.IsHeader ? NotaPalette.SurfaceInset : Brushes.Transparent,
                };
                Grid.SetRow(border, r); Grid.SetColumn(border, c);
                grid.Children.Add(border);
                c++;
            }
            r++;
        }
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border
            {
                Child = grid,
                BorderBrush = NotaPalette.BorderDefault,
                BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Field,
                ClipToBounds = true,
            },
        };
    }

    // ---- inlines ---------------------------------------------------------------------------

    [Flags]
    private enum Style { None = 0, Bold = 1, Italic = 2, Strike = 4 }

    private void AddInlines(InlineCollection into, ContainerInline container, Style style)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    into.Add(StyledRun(lit.Content.ToString(), style));
                    break;
                case EmphasisInline em:
                {
                    var s = style;
                    if (em.DelimiterChar == '~') s |= Style.Strike;
                    else if (em.DelimiterCount >= 2) s |= Style.Bold;
                    else s |= Style.Italic;
                    AddInlines(into, em, s);
                    break;
                }
                case CodeInline code:
                    into.Add(new Run(code.Content)
                    {
                        FontFamily = NotaFonts.MonoFamily,
                        FontSize = CodeSize,
                        Background = NotaPalette.SurfaceInset,
                        Foreground = NotaPalette.TextPrimary,
                    });
                    break;
                case LinkInline link when !link.IsImage:
                    into.Add(new LinkRun(PlainText(link), link.Url ?? ""));
                    break;
                case LinkInline image:
                    into.Add(new LinkRun(PlainText(image) is { Length: > 0 } alt ? alt : image.Url ?? "", image.Url ?? ""));
                    break;
                case AutolinkInline auto:
                    into.Add(new LinkRun(auto.Url, auto.IsEmail ? "mailto:" + auto.Url : auto.Url));
                    break;
                case HtmlInline html when html.Tag.StartsWith("<kbd", StringComparison.OrdinalIgnoreCase):
                {
                    // <kbd>X</kbd>: the literal between the tags becomes the key cap.
                    var sb = new StringBuilder();
                    var next = html.NextSibling;
                    while (next is not null && !(next is HtmlInline end && end.Tag.StartsWith("</kbd", StringComparison.OrdinalIgnoreCase)))
                    {
                        sb.Append(next is LiteralInline l ? l.Content.ToString() : "");
                        next = next.NextSibling;
                    }
                    into.Add(KeyRun(sb.ToString()));
                    inline = next ?? container.LastChild!;   // skip past </kbd>
                    break;
                }
                case HtmlInline:
                    break;                                    // other tags: drop the markup, keep the text
                case LineBreakInline br:
                    into.Add(br.IsHard ? new LineBreak() : new Run(" "));
                    break;
                case HtmlEntityInline ent:
                    into.Add(StyledRun(ent.Transcoded.ToString(), style));
                    break;
                case ContainerInline inner:
                    AddInlines(into, inner, style);
                    break;
            }
        }
    }

    private static Run StyledRun(string text, Style style)
    {
        var run = new Run(text);
        if (style.HasFlag(Style.Bold)) run.FontWeight = FontWeight.SemiBold;
        if (style.HasFlag(Style.Italic)) run.FontStyle = FontStyle.Italic;
        if (style.HasFlag(Style.Strike)) run.TextDecorations = TextDecorations.Strikethrough;
        return run;
    }

    private static string PlainText(ContainerInline c)
    {
        var sb = new StringBuilder();
        for (var i = c.FirstChild; i is not null; i = i.NextSibling)
            sb.Append(i switch { LiteralInline l => l.Content.ToString(), CodeInline code => code.Content, ContainerInline inner => PlainText(inner), _ => "" });
        return sb.ToString();
    }

    /// <summary>A link's words: accent, underlined, part of the paragraph's text — selectable and
    /// copied with the passage around them.</summary>
    private sealed class LinkRun : Run
    {
        public LinkRun(string text, string url) : base(text)
        {
            Url = url;
            Foreground = NotaPalette.Accent;
            TextDecorations = Avalonia.Media.TextDecorations.Underline;
        }
        public string Url { get; }
    }

    // A paragraph with links: the mouse runs the link under it (hit-tested to its run); from the
    // keyboard, Tab enters the paragraph on its first link and moves link to link (⇧Tab back),
    // the focused one tinted; Return or Space runs it; past the last link Tab moves on.
    private void HookLinks(SelectableTextBlock tb)
    {
        var links = tb.Inlines!.OfType<LinkRun>().ToList();
        int focused = -1;
        tb.Focusable = true;
        tb.IsTabStop = true;
        Avalonia.Automation.AutomationProperties.SetName(tb, string.Join(", ", links.Select(l => l.Text)));
        void Mark(int i)
        {
            if (focused >= 0) links[focused].Background = null;
            focused = i;
            if (focused >= 0) links[focused].Background = NotaPalette.AccentSoft;
        }
        tb.GotFocus += (_, e) => { if (e.NavigationMethod == NavigationMethod.Tab && focused < 0) Mark(0); };
        tb.LostFocus += (_, _) => Mark(-1);
        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab && focused >= 0)
            {
                int next = focused + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                if (next < 0 || next >= links.Count) { Mark(-1); return; }   // leave the paragraph
                Mark(next);
                e.Handled = true;
            }
            else if (e.Key is Key.Enter or Key.Space && focused >= 0)
            {
                e.Handled = true;
                LinkClicked?.Invoke(links[focused].Url);
            }
        };
        tb.PointerMoved += (_, e) =>
            tb.Cursor = RunAt(tb, e.GetPosition(tb)) is LinkRun ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        tb.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || tb.SelectionEnd != tb.SelectionStart) return;
            if (RunAt(tb, e.GetPosition(tb)) is LinkRun link)
            {
                e.Handled = true;
                LinkClicked?.Invoke(link.Url);
            }
        };
    }

    private static Avalonia.Controls.Documents.Inline? RunAt(SelectableTextBlock tb, Point p)
    {
        var hit = tb.TextLayout.HitTestPoint(p);
        if (!hit.IsInside || tb.Inlines is null) return null;
        int at = hit.TextPosition, pos = 0;
        foreach (var inline in tb.Inlines)
        {
            int len = inline switch { Run r => r.Text?.Length ?? 0, LineBreak => Environment.NewLine.Length, _ => 1 };
            if (at >= pos && at < pos + len) return inline;
            pos += len;
        }
        return null;
    }

    // A key as the manual writes it: the key's name on a raised ground, in the text's own line.
    private static Run KeyRun(string key) => new("\u2009" + key.Trim() + "\u2009")
    {
        Background = NotaPalette.SurfaceRaised,
        Foreground = NotaPalette.TextPrimary,
        FontWeight = FontWeight.Medium,
        FontSize = BodySize - 0.5,
    };

    private static string Text(LeafBlock block) => block.Lines.ToString();
}
