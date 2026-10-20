// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The clip strip of the multi-clip editor (specs/029-multi-clip-editing): a row under the
// ruler, on the roll's own beat axis, with one bar per open clip over its span. Clips of
// different tracks that overlap stack in rows. The active clip is the chosen one: a solid
// track-colour bar with derived ink and an accent outline. The others are panel-toned with a
// track-colour stripe; a reference (locked) clip's name steps down an ink and its lock is
// shut. A click makes a clip active; the lock on a non-active bar toggles reference. Shown
// only while two or more clips are open.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

public sealed partial class PianoRollView
{
    private sealed class ClipStrip : Control
    {
        public const double RowH = 18;
        private const double BarInset = 2, LockW = 16, Stripe = 3;
        private readonly PianoRollView _o;
        private readonly List<int> _rowOf = new();   // per slot: its row
        private int _rows = 1;

        public ClipStrip(PianoRollView o)
        {
            _o = o;
            Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(this, "Click a clip to edit it · the lock keeps a clip for reference");
        }

        /// <summary>Re-stacks the bars (clips added, removed, moved) and sizes the strip.</summary>
        public void Relayout()
        {
            var set = _o._set;
            IsVisible = set.IsMulti;
            _rowOf.Clear();
            var rowEnds = new List<double>();
            for (int k = 0; k < set.Count; k++)
            {
                double s = set.EditorStart(k), e = set.EditorEnd(k);
                int r = rowEnds.FindIndex(end => end <= s + 1e-9);
                if (r < 0) { r = rowEnds.Count; rowEnds.Add(e); } else rowEnds[r] = e;
                _rowOf.Add(r);
            }
            _rows = Math.Max(1, rowEnds.Count);
            Height = _rows * RowH;
            InvalidateVisual();
        }

        private Rect BarRect(int slot)
        {
            var set = _o._set;
            double x0 = _o.BeatToX(set.EditorStart(slot)), x1 = _o.BeatToX(set.EditorEnd(slot));
            int row = slot < _rowOf.Count ? _rowOf[slot] : 0;
            return new Rect(x0 + 1, row * RowH + BarInset, Math.Max(2, x1 - x0 - 2), RowH - 2 * BarInset);
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            ctx.FillRectangle(KeysBg, new Rect(0, 0, w, h));
            ctx.DrawLine(new Pen(Divider, 1), new Point(0, h - 0.5), new Point(w, h - 0.5));
            var set = _o._set;
            if (!set.IsMulti) return;
            for (int k = 0; k < set.Count; k++)
            {
                var r = BarRect(k);
                if (r.Right < 0 || r.X > w) continue;
                bool active = k == set.Active, locked = !set.IsEditable(k);
                var track = (k < _o._slotBrushes.Count ? _o._slotBrushes[k] : _o._trackBrush) as SolidColorBrush ?? NotaPalette.TextSecondary;
                using (ctx.PushClip(r))
                {
                    if (active)
                    {
                        ctx.FillRectangle(track, r, (float)NotaRadius.ClipValue);
                        ctx.DrawRectangle(null, new Pen(NotaPalette.Accent, 1), r.Deflate(0.5), NotaRadius.ClipValue, NotaRadius.ClipValue);
                    }
                    else
                    {
                        ctx.FillRectangle(NotaPalette.SurfaceRaised, r, (float)NotaRadius.ClipValue);
                        ctx.FillRectangle(track, new Rect(r.X, r.Y, Stripe, r.Height));
                    }
                    IBrush ink = active ? NotaPalette.InkOn(track) : locked ? NotaPalette.TextTertiary : NotaPalette.TextSecondary;
                    var clip = set.Clips[k];
                    string name = _o.ClipNameOf?.Invoke(clip.TrackId, clip.ClipIndex) ?? $"Track {clip.TrackId}";
                    var ft = new FormattedText(name, NotaNum.Culture, FlowDirection.LeftToRight, NotaFonts.SansMedium, 9, ink);
                    double textX = r.X + (active ? 6 : Stripe + 5);
                    double room = r.Width - (textX - r.X) - (active ? 6 : LockW + 2);
                    if (room > 12)
                    {
                        ft.MaxTextWidth = room;
                        ft.Trimming = TextTrimming.CharacterEllipsis;
                        ft.MaxLineCount = 1;
                        ctx.DrawText(ft, new Point(textX, r.Y + (r.Height - ft.Height) / 2));
                    }
                    if (!active && r.Width >= LockW + Stripe + 4)
                        Glyph.Draw(ctx, locked ? GlyphKind.Lock : GlyphKind.LockOpen,
                            new Rect(r.Right - LockW + 2, r.Y + (r.Height - 11) / 2, 11, 11),
                            locked ? NotaPalette.TextSecondary : NotaPalette.TextTertiary);
                }
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            var set = _o._set;
            // Topmost row first is irrelevant: bars in one row never overlap.
            for (int k = 0; k < set.Count; k++)
            {
                var r = BarRect(k);
                if (!r.Contains(p)) continue;
                if (k != set.Active && p.X >= r.Right - LockW) _o.SetReference(k, set.IsEditable(k));
                else _o.SetActiveClip(k);
                e.Handled = true;
                return;
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            _o.RouteWheel(e, e.GetPosition(this).X);
            base.OnPointerWheelChanged(e);
        }
    }
}
