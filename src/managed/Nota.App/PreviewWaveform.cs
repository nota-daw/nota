// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The waveform strip in the browser's pinned preview footer (HANDOFF 1g). We
// don't have decoded peaks for an arbitrary browser file (it isn't loaded as an
// engine clip), so this draws a deterministic synthetic waveform seeded by the
// filename — a stable, decorative time diagram per file. Colour matches the
// mockup's sage preview accent.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

public sealed class PreviewWaveform : Control
{
    private static readonly IBrush Bar = NotaPalette.Sage;      // sage (Track4)
    private static readonly IBrush Baseline = NotaPalette.SurfaceRaised; // Brush.SurfaceRaised

    private string _seed = "";

    public void SetSample(string? name)
    {
        _seed = name ?? "";
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        if (_seed.Length == 0)
        {
            ctx.FillRectangle(Baseline, new Rect(0, h / 2 - 0.5, w, 1));
            return;
        }

        // FNV-1a hash → LCG, so the bars are stable for a given filename.
        uint s = 2166136261u;
        foreach (char c in _seed) { s ^= c; s *= 16777619u; }

        double mid = h / 2, maxAmp = h / 2 - 1;
        int n = (int)(w / 3);   // 2px bar + 1px gap
        for (int i = 0; i < n; i++)
        {
            s = s * 1664525u + 1013904223u;
            double r = ((s >> 8) & 0xFFFF) / 65535.0;
            // Gentle envelope so the middle reads louder, like a real hit.
            double env = 0.35 + 0.65 * System.Math.Sin(System.Math.PI * (i + 0.5) / n);
            double amp = (0.12 + 0.88 * r) * env * maxAmp;
            ctx.FillRectangle(Bar, new Rect(i * 3, mid - amp, 2, amp * 2));
        }
    }
}
