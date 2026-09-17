// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

    // Vertical gain-reduction meter, hanging from the top: more compression reads as more
    // bar. Brass — reduction is the device's data, not an alert. No animation (the value is
    // set as it arrives) and the peak mark holds until clicked (almanac § level meters).
    internal sealed class GrMeter : Control
    {
        private static readonly IBrush Ground = NotaPalette.BgSunken;
        private static readonly IBrush Bar = NotaPalette.Accent;
        private static readonly IBrush HoldInk = NotaPalette.AccentBright;
        private const double MaxDb = 24.0;   // full-scale reduction
        private double _gr, _hold;

        public GrMeter() => ToolTip.SetTip(this, "Clear the peak hold");

        public double GrDb
        {
            get => _gr;
            set
            {
                _gr = value < 0 ? 0 : value;
                if (_gr > _hold) _hold = _gr;
                InvalidateVisual();
            }
        }

        protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _hold = _gr; InvalidateVisual(); e.Handled = true;
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            ctx.DrawRectangle(Ground, null, new RoundedRect(new Rect(0, 0, w, h), NotaRadius.ClipValue));
            double frac = System.Math.Clamp(_gr / MaxDb, 0, 1);
            if (frac > 0.001)
                ctx.FillRectangle(Bar, new Rect(0, 0, w, frac * h));
            double hy = System.Math.Clamp(_hold / MaxDb, 0, 1) * h;
            if (hy > 1)
                ctx.FillRectangle(HoldInk, new Rect(0, hy - 1, w, 2));
        }
    }
