// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

    // The "MIX SUM" bar under Volt's NOISE panel: Osc1 / Osc2 / Noise levels as
    // proportional amber segments (bright → faint).
    internal sealed class MixBar : Control
    {
        private static readonly IBrush Sunken = NotaPalette.BgSunken;
        private readonly IAudioEngine _e; private readonly int _t; private readonly int[] _ix;
        private static readonly IBrush S0 = NotaPalette.Accent;
        private static readonly IBrush S1 = new SolidColorBrush(Color.FromArgb(0x8C, 0xD8, 0xA0, 0x3D));
        private static readonly IBrush S2 = new SolidColorBrush(Color.FromArgb(0x47, 0xD8, 0xA0, 0x3D));
        public MixBar(IAudioEngine e, int t, int[] ix) { _e = e; _t = t; _ix = ix; Height = 6; }
        public void Refresh() => InvalidateVisual();
        private float V(int k) => _ix[k] >= 0 ? _e.PluginParamGet(_t, -1, _ix[k]) : 0f;
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.DrawRectangle(Sunken, null, new Rect(0, 0, w, h), 3, 3);
            float a = V(0), b = V(1), c = V(2), sum = a + b + c;
            if (sum <= 1e-4f) return;
            var cols = new[] { S0, S1, S2 }; var v = new[] { a, b, c };
            double x = 0;
            for (int i = 0; i < 3; i++) { double seg = w * v[i] / sum; if (seg > 0.5) ctx.FillRectangle(cols[i], new Rect(x, 0, seg, h)); x += seg; }
        }
    }
