// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Chord result keyboard (mockup 3b): a two-octave stylised keyboard, one bar per
// semitone (white keys tall, black keys short), lighting the played root bright and each
// added chord note amber — a live preview of what the current voicing does to one key.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ChordKeysViz : Control
{
    private static readonly IBrush Well = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush WhiteKey = new SolidColorBrush(Color.Parse("#26231E"));
    private static readonly IBrush BlackKey = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush Added = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush Played = new SolidColorBrush(Color.Parse("#F0C060"));

    private int _root = 60, _low = 60;
    private const int Span = 25;   // two octaves + 1
    private bool[] _added = new bool[Span];

    public ChordKeysViz() { MinHeight = 34; }

    public void Set(int root, System.Collections.Generic.IEnumerable<int> added)
    {
        _root = root;
        _low = root - ((root % 12) + 12) % 12;   // octave floor at/below root
        _added = new bool[Span];
        foreach (var p in added) { int s = p - _low; if (s >= 0 && s < Span) _added[s] = true; }
        InvalidateVisual();
    }

    private static bool IsBlack(int pc) => pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Well, null, new Rect(0, 0, w, h), 3, 3);
        double bw = w / Span;
        for (int s = 0; s < Span; s++)
        {
            int pitch = _low + s, pc = ((pitch % 12) + 12) % 12;
            bool black = IsBlack(pc);
            double bh = black ? h * 0.62 : h;               // black keys shorter
            var rect = new Rect(s * bw, h - bh, Math.Max(1, bw - 0.6), bh);
            IBrush fill = pitch == _root ? Played : _added[s] ? Added : black ? BlackKey : WhiteKey;
            ctx.DrawRectangle(fill, null, rect, 1, 1);
            ctx.DrawLine(new Pen(Line, 0.5), new Point(s * bw, h - bh), new Point(s * bw, h));
        }
    }
}
