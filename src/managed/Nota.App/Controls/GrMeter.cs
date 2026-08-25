// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

    // Vertical gain-reduction meter (red bar from the top). Fed live GrDb (dB, >=0)
    // on the UI tick; smooth attack + peak-hold decay so it reads like a hardware GR meter.
    internal sealed class GrMeter : Control
    {
        private static readonly IBrush Sunken = NotaPalette.BgSunken;
        private static readonly IBrush AccentBright = NotaPalette.AccentBright;
        private const double MaxDb = 24.0;   // full-scale reduction
        private static readonly IBrush Red = NotaPalette.Danger;
        private double _shown;               // smoothed displayed GR (dB)
        private double _hold; private int _holdAge;

        private double _gr;
        public double GrDb
        {
            get => _gr;
            set
            {
                _gr = value < 0 ? 0 : value;
                // Instant attack, slow release; short peak-hold on the max.
                _shown = _gr > _shown ? _gr : _shown * 0.80 + _gr * 0.20;
                if (_gr >= _hold) { _hold = _gr; _holdAge = 0; }
                else if (++_holdAge > 12) { _hold = _hold * 0.85; }
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            ctx.DrawRectangle(Sunken, null, new Rect(0, 0, w, h), 3, 3);
            double frac = System.Math.Clamp(_shown / MaxDb, 0, 1);
            if (frac > 0.001)
                ctx.DrawRectangle(Red, null, new Rect(0, 0, w, frac * h), 3, 3);   // from the top down
            double hy = System.Math.Clamp(_hold / MaxDb, 0, 1) * h;
            if (hy > 1)
                ctx.DrawLine(new Pen(AccentBright, 1), new Point(0, hy), new Point(w, hy));   // peak-hold tick
        }
    }
