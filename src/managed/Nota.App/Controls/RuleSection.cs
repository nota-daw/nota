// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A device-card section (style guide, spec 038): pressed into the card — the section grey,
// a 1px inner edge and a soft inner shadow along the top, corner 7 — so a card reads as one
// raised surface holding a few recessed areas. Sections sit 10 apart and 8 from the card edge;
// the gaps are the card layout's. Graph windows and wells keep their own frame.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Nota.App;

/// <summary>Which edges of a section carry the dividing rule.</summary>
[Flags]
internal enum SectionEdge
{
    None = 0,
    Top = 1,
    Left = 2,
    /// <summary>Find them: a rule on the top or left edge that faces a neighbouring section.</summary>
    Auto = 4,
}

internal sealed class RuleSection : Decorator
{
    // The inner edge line, kept clear of the content (inside any Padding).
    private static readonly Thickness Inset = new(1);

    /// <summary>Kept for older call sites: a pressed-in section has no dividing rules.</summary>
    public SectionEdge Edges { get; init; } = SectionEdge.Auto;

    public RuleSection() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        var pad = Padding + Inset;
        if (Child is not { } c) return new Size(pad.Left + pad.Right, pad.Top + pad.Bottom);
        c.Measure(availableSize.Deflate(pad));
        return c.DesiredSize.Inflate(pad);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize).Deflate(Padding + Inset));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        NotaDepth.Section(context, this, new RoundedRect(new Rect(Bounds.Size), NotaRadius.GroupValue));
    }
}
