// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The 232px inspector beside the piano roll (nota-design "Nota Clip Editor" 1a): CLIP
// (Start / Length in bars.beats.16ths, the Loop switch and its length), QUANTIZE (its step,
// Quantize, strength — the snap grid itself is the header chip), TRANSPOSE (− value +) and SELECTION (the selected note's
// pitch / start / length / velocity, or the range of several), ending in one hint line.
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
    private readonly TextBlock _loopText = Mono("", 10, NotaPalette.TextStrong);
    private readonly TextBlock _transposeText = Mono("");
    private readonly TextBlock _gridText = Mono("");
    private readonly TextBlock _selCount = Meta("—");
    private readonly StackPanel _selRows = new();
    private readonly Border _selCard;
    private readonly Border _selEmpty;
    private int _transpose;
    private double _strength = 0.8;

    public ClipPropsView(PianoRollView roll, double startBeat)
    {
        _roll = roll;
        _startText.Text = Position(startBeat);
        _gridText.Text = roll.Grids.Quantize.Label;

        // CLIP — clips always loop in this model; the switch waits for a per-clip loop.
        var loop = SwitchRow("Loop", () => true, () => { }, out _, _loopText);
        loop.IsEnabled = false;
        ToolTip.SetTip(loop, "Clips always loop");
        var clip = Section("Clip",
            Pair(Labeled("Start", Field(_startText)), Labeled("Length", Field(_lengthText))),
            loop);

        // QUANTIZE — its own step (not the snap grid, which follows the zoom), one-tap
        // Quantize at the strength below.
        var grid = Section("Quantize",
            Pair(Dropdown(_gridText, a => ShowMenu(a, Array.ConvertAll(QuantizeSteps, q => q.Label),
                    Array.IndexOf(QuantizeSteps, _roll.Grids.Quantize), i =>
                {
                    _roll.Grids.SetQuantize(QuantizeSteps[i]);
                    _gridText.Text = QuantizeSteps[i].Label;
                })),
                ClipEditorKit.Button("Quantize", () => _roll.Quantize(_strength))),
            SliderRow("Strength", 64, () => _strength, v => _strength = v,
                () => string.Format(NotaNum.Culture, "{0:0}\u2009%", _strength * 100), out _, reset: () => _strength = 0.8, valueW: 34));

        // TRANSPOSE — − value +; the value turns Brass Light while it is off zero.
        _transposeText.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        var tRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        tRow.Children.Add(GlyphButton(GlyphKind.Minus, () => Transpose(-1)));
        var tField = Field(_transposeText);
        Grid.SetColumn(tField, 1); tRow.Children.Add(tField);
        var plus = GlyphButton(GlyphKind.Plus, () => Transpose(1));
        Grid.SetColumn(plus, 2); tRow.Children.Add(plus);
        var transpose = Section("Transpose", tRow);

        // SELECTION — a card of read-outs, or one empty-state line in the same frame.
        _selCard = Card(_selRows);
        _selCard.Padding = new Thickness(0);
        _selEmpty = new Border
        {
            Height = 104, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control,
            Child = new TextBlock
            {
                Text = "No notes selected", FontSize = NotaType.Body, Foreground = NotaPalette.TextTertiary,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        var selection = Section(SectionTitle("Selection", _selCount), 8, _selCard, _selEmpty);

        Content = Inspector(Sections(clip, grid, transpose, selection),
            Hint("Double-click — add or delete · edge — length · Shift — add to selection"));

        PaintTranspose();
        OnRollChanged();
        _roll.Changed += OnRollChanged;
    }

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
        _selCount.Text = sel.Count > 0 ? sel.Count.ToString(NotaNum.Culture) : "—";
        _selCard.IsVisible = sel.Count > 0;
        _selEmpty.IsVisible = sel.Count == 0;
        _selRows.Children.Clear();
        if (sel.Count == 1)
        {
            var n = sel[0];
            Row("Pitch", PianoRollView.NoteName(n.Pitch));
            Row("Start", Position(n.StartBeat));
            Row("Length", string.Format(NotaNum.Culture, "{0:0.##}/16", n.LengthBeats * 4));
            Row("Velocity", Vel(n.Velocity).ToString(NotaNum.Culture), last: true);
        }
        else if (sel.Count > 1)
        {
            int lo = sel.Min(n => n.Pitch), hi = sel.Max(n => n.Pitch);
            int vLo = sel.Min(n => Vel(n.Velocity)), vHi = sel.Max(n => Vel(n.Velocity));
            Row("Notes", sel.Count.ToString(NotaNum.Culture));
            Row("Range", lo == hi ? PianoRollView.NoteName(lo) : $"{PianoRollView.NoteName(lo)}–{PianoRollView.NoteName(hi)}");
            Row("Velocity", vLo == vHi ? vLo.ToString(NotaNum.Culture) : $"{vLo}–{vHi}", last: true);
        }

        void Row(string key, string value, bool last = false)
        {
            var v = Mono(value);
            _selRows.Children.Add(new Border
            {
                Padding = new Thickness(10, 0), BorderBrush = NotaPalette.GraphBorder,
                BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = KeyValue(key, v),
            });
        }
    }

    private static int Vel(float v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 127);
}
