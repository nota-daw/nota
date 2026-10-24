// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The piano roll's settings strip (style guide § Editor settings strip): every setting in one
// row above the notes, so the notes get the full width — Clip (Start / Length in
// bars.beats.16ths, the Loop switch and its length), Quantize (its step, Quantize, strength —
// the snap grid itself is the header chip), Transpose (− value +) and Selection (the selected
// note's pitch, start, length and velocity, or the range of several, on one line).
// Edits drive the PianoRollView; read-outs follow its Changed event.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nota.Application.Editing;
using static Nota.App.ClipEditorKit;

namespace Nota.App;

public sealed class ClipPropsView : UserControl
{
    // Quantize steps (the app-wide Settings.QuantizeStep, independent of the snap grid and
    // the zoom): straight divisions coarse to fine, then triplets. No bar steps.
    private static readonly GridStep[] QuantizeSteps =
    {
        GridStep.Of(4, false), GridStep.Of(3, false), GridStep.Of(2, false), GridStep.Of(1, false), GridStep.Of(0, false),
        GridStep.Of(4, true), GridStep.Of(3, true), GridStep.Of(2, true), GridStep.Of(1, true), GridStep.Of(0, true),
    };

    private readonly PianoRollView _roll;
    private readonly TextBlock _startText = Mono("");
    private readonly TextBlock _lengthText = Mono("");
    private readonly TextBlock _loopText = Mono("", NotaType.Body, NotaPalette.TextDisabled);
    private readonly TextBlock _transposeText = Mono("");
    private readonly TextBlock _gridText = Mono("");
    private readonly TextBlock _selText = StripLabel("No notes selected");
    private int _transpose;
    private double _strength = 0.8;

    public ClipPropsView(PianoRollView roll, double startBeat)
    {
        _roll = roll;
        _startText.Text = Position(startBeat);
        _gridText.Text = roll.Grids.Quantize.Label;

        // Clip — clips always loop in this model; the switch waits for a per-clip loop, so it
        // is drawn off (ink 4), not faded.
        var loop = SwitchRow("Loop", () => false, () => { }, out _, _loopText, StripField);
        loop.IsEnabled = false;
        ToolTip.SetTip(loop, "Clips always loop");
        var clip = StripGroup("Clip",
            StripLabel("Start"), Sized(StripValue(_startText), 58),
            StripLabel("Length"), Sized(StripValue(_lengthText), 58),
            loop);

        // Quantize — its own step (not the snap grid, which follows the zoom), one-tap
        // Quantize at the strength beside it.
        var step = Dropdown(_gridText, a => ShowMenu(a, Array.ConvertAll(QuantizeSteps, q => q.Label),
            Array.IndexOf(QuantizeSteps, _roll.Grids.Quantize), i =>
            {
                _roll.Grids.SetQuantize(QuantizeSteps[i]);
                _gridText.Text = QuantizeSteps[i].Label;
            }));
        step.Height = StripField;
        step.MinWidth = 64;
        var qButton = ClipEditorKit.Button("Quantize", () => _roll.Quantize(_strength));
        qButton.Height = StripField;
        qButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        // Strength: the shell slider, its value inside (style guide § Slider: quantize strength).
        static string Pct(double v) => string.Format(NotaNum.Culture, "{0:0}%", v * 100);
        var strength = new MiniFader(_strength, 1) { Width = 92, Default = 0.8, Text = Pct };
        strength.ValueChanged += v => _strength = v;
        ValueEntry.Attach(strength, strength.EntryField("Strength", Pct));
        var grid = StripGroup("Quantize", step, qButton, StripLabel("Strength"), strength);

        // Transpose — − value +; the value is ink 1 while it is off zero.
        _transposeText.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        var minus = GlyphButton(GlyphKind.Minus, () => Transpose(-1));
        var plus = GlyphButton(GlyphKind.Plus, () => Transpose(1));
        minus.Height = plus.Height = StripField;
        minus.Width = plus.Width = StripField;
        var transpose = StripGroup("Transpose", minus, Sized(StripValue(_transposeText), 52), plus);

        // Selection — one line of read-outs, or "No notes selected".
        _selText.TextTrimming = TextTrimming.CharacterEllipsis;
        var selection = StripGroup("Selection", _selText);

        Content = Strip(clip, grid, transpose, selection);

        PaintTranspose();
        OnRollChanged();
        _roll.Changed += OnRollChanged;
    }

    private const double StripField = 26;

    private static Border StripValue(TextBlock value)
    {
        var f = Field(value);
        f.Height = StripField;
        return f;
    }

    private static T Sized<T>(T c, double w) where T : Control { c.Width = w; return c; }

    /// <summary>Re-points the START read-out after the clip moved or was trimmed from the
    /// left in the arrangement. LENGTH / LOOP follow the roll's own Changed event.</summary>
    public void SetStart(double startBeat) => _startText.Text = Position(startBeat);

    private void OnRollChanged()
    {
        double len = _roll.ActiveLengthBeats;   // the active clip's own length, several clips open or one
        _lengthText.Text = Duration(len);
        _loopText.Text = string.Format(NotaNum.Culture, "{0:0.##} {1}", len, Math.Abs(len - 1) < 1e-9 ? "beat" : "beats");
        PaintSelection();
    }

    private void Transpose(int d)
    {
        _transpose += d;
        PaintTranspose();
        _roll.TransposeBy(d);
    }

    private void PaintTranspose()
    {
        _transposeText.Text = Semis(_transpose);
        _transposeText.Foreground = _transpose != 0 ? NotaPalette.TextPrimary : NotaPalette.TextSecondary;
    }

    private void PaintSelection()
    {
        var sel = _roll.SelectedNotes();
        _selText.Foreground = sel.Count > 0 ? NotaPalette.TextPrimary : NotaPalette.TextTertiary;
        if (sel.Count == 0) { _selText.Text = "No notes selected"; return; }
        if (sel.Count == 1)
        {
            var n = sel[0];
            _selText.Text = string.Format(NotaNum.Culture, "{0} · {1} · {2:0.##}/16 · velocity {3}",
                PianoRollView.NoteName(n.Pitch), Position(n.StartBeat), n.LengthBeats * 4, Vel(n.Velocity));
            return;
        }
        int lo = sel.Min(n => n.Pitch), hi = sel.Max(n => n.Pitch);
        int vLo = sel.Min(n => Vel(n.Velocity)), vHi = sel.Max(n => Vel(n.Velocity));
        string range = lo == hi ? PianoRollView.NoteName(lo) : $"{PianoRollView.NoteName(lo)}–{PianoRollView.NoteName(hi)}";
        string vel = vLo == vHi ? vLo.ToString(NotaNum.Culture) : $"{vLo}–{vHi}";
        _selText.Text = $"{sel.Count.ToString(NotaNum.Culture)} notes · {range} · velocity {vel}";
    }

    private static int Vel(float v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 127);
}
