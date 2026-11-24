// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The tracks under a hardware controller's bank of eight (specs/051, FR-009): a thin accent bar
// on the right edge of their headers, so it is clear which tracks the faders move.

using System.Collections.Generic;
using Avalonia.Controls;

namespace Nota.App;

public sealed partial class ArrangementView
{
    private HashSet<int> _bankTracks = new();
    private readonly List<Border> _bankMarks = new();

    public void SetControllerBank(HashSet<int> trackIds)
    {
        _bankTracks = trackIds;
        PlaceBankMarks();
    }

    private void PlaceBankMarks()
    {
        foreach (var m in _bankMarks) _headers.Children.Remove(m);
        _bankMarks.Clear();
        for (int i = 0; i < _tracks.Count; i++)
        {
            if (!_bankTracks.Contains(_tracks[i].Id)) continue;
            var mark = new Border
            {
                Width = 2, Height = System.Math.Max(2, RowHeightAt(i) - 2),
                Background = NotaPalette.Accent, IsHitTestVisible = false,
            };
            Canvas.SetLeft(mark, HeaderW - 3);
            Canvas.SetTop(mark, RowTop(i) + 1);
            _headers.Children.Add(mark);
            _bankMarks.Add(mark);
        }
    }
}
