// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Where groups sit in the Arrangement's header column (specs/094). The header rows are
// separate cards, one per lane, so a group can't be one control around its members; each
// row instead draws its slice of every group it sits in. This works the slices out from
// the visible rows alone — which trays a row is in, which one it heads, how many close on
// it — so the view only paints, and the rules can be checked without a window. The new
// look draws trays; the classic look draws 0.59.2's staircase of each row's own spine.

namespace Nota.Presentation;

/// <summary>One visible header row, in display order (a group before its members).</summary>
/// <param name="ParentId">The enclosing group's id, −1 at the top level.</param>
/// <param name="HiddenTracks">A group's tracks, nested groups' tracks included and the
/// nested groups themselves not counted; 0 for a track.</param>
public readonly record struct TrayRow(int Id, int ParentId, bool IsGroup, bool Folded, int ColorIndex, int HiddenTracks);

/// <summary>A tray a row is drawn in. Its edge sits at <c>Level × Step</c>.</summary>
public readonly record struct TrayRef(int GroupId, int ColorIndex, int Level);

/// <summary>One row's share of the group drawing.</summary>
/// <param name="Chain">The trays the row is in, outer → inner; a group's own row ends with its own tray.</param>
/// <param name="Heads">The row is the header row of the last tray in <paramref name="Chain"/>.</param>
/// <param name="Closings">Trays that end on this row (their closing strips, innermost on top).</param>
/// <param name="ContentLeft">Where the row's own content starts (its colour bar, or a group's chevron).</param>
/// <param name="SpineX">The row's own colour stripe: a track's bar (new look) or the 0.59.2 spine (classic).</param>
/// <param name="FoldedCount">A folded group's hidden tracks; null otherwise.</param>
public sealed record TraySlice(int Depth, TrayRef[] Chain, bool Heads, int Closings, double ContentLeft, double SpineX, int? FoldedCount);

public static class GroupTrayLayout
{
    /// <summary>New look: one level = a tray edge plus the gap before the next thing in.</summary>
    public const double EdgeW = 3, Gap = 3, Step = EdgeW + Gap;

    /// <summary>New look: the strip that closes the innermost tray under its last row, taken out
    /// of that row; each further tray closing on the same row adds a thinner one, so even a
    /// Compact row closing four trays keeps its 20 px name row.</summary>
    public const double ClosingH = 4, ClosingNestH = 2;

    /// <summary>The height at the bottom of a row taken by <paramref name="closings"/> closing strips.</summary>
    public static double ClosingBand(int closings) => closings <= 0 ? 0 : ClosingH + (closings - 1) * ClosingNestH;

    /// <summary>The height of the <paramref name="k"/>-th closing strip from the inside (0 = innermost).</summary>
    public static double ClosingStrip(int k) => k == 0 ? ClosingH : ClosingNestH;

    /// <summary>New look: member cards stop this far from the column's right edge so the tray shows.</summary>
    public const double TrayInsetR = 4;

    /// <summary>Classic look (0.59.2): the spine steps in this far per nesting level.</summary>
    public const double ClassicStep = 10, ClassicSpineW = 3;

    public static TraySlice[] Compute(IReadOnlyList<TrayRow> rows, bool classic)
    {
        var at = new Dictionary<int, int>(rows.Count);
        for (int i = 0; i < rows.Count; i++) at[rows[i].Id] = i;

        // Ancestors, outer → inner. A parent always precedes its children, so walking up
        // stays inside the visible rows; the guard stops a malformed chain from hanging.
        var chains = new TrayRef[rows.Count][];
        var depth = new int[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var up = new List<int>();
            for (int p = rows[i].ParentId, guard = 0; p >= 0 && guard < 32 && at.TryGetValue(p, out int pi); guard++)
            {
                up.Add(pi);
                p = rows[pi].ParentId;
            }
            depth[i] = up.Count;
            up.Reverse();
            if (rows[i].IsGroup) up.Add(i);
            var chain = new TrayRef[up.Count];
            for (int k = 0; k < up.Count; k++) chain[k] = new TrayRef(rows[up[k]].Id, rows[up[k]].ColorIndex, k);
            chains[i] = chain;
        }

        // A tray closes on the last row whose chain holds it.
        var lastRow = new Dictionary<int, int>();
        for (int i = 0; i < rows.Count; i++)
            foreach (var t in chains[i]) lastRow[t.GroupId] = i;
        var closings = new int[rows.Count];
        foreach (var last in lastRow.Values) closings[last]++;

        var slices = new TraySlice[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int? folded = r.IsGroup && r.Folded ? r.HiddenTracks : null;
            double spine, content;
            if (classic)
            {
                spine = depth[i] * ClassicStep;
                content = spine + ClassicSpineW;
            }
            else
            {
                content = chains[i].Length * Step;
                // A group's own stripe is its tray's edge; a track's bar starts where its card does.
                spine = r.IsGroup ? (chains[i].Length - 1) * Step : content;
            }
            slices[i] = new TraySlice(depth[i], chains[i], r.IsGroup, closings[i], content, spine, folded);
        }
        return slices;
    }
}
