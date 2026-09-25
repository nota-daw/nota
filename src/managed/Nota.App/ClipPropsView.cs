// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The 232px inspector beside the piano roll (nota-design "Nota Clip Editor" 1a): CLIP
// (Start / Length in bars.beats.16ths, the Loop switch and its length), GRID (grid
// dropdown, Quantize, strength), TRANSPOSE (− value +) and SELECTION (the selected note's
// pitch / start / length / velocity, or the range of several), ending in one hint line.
// Edits drive the PianoRollView; read-outs follow its Changed event.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using static Nota.App.ClipEditorKit;

namespace Nota.App;

public sealed class ClipPropsView : UserControl
{
    // Grid steps in beats (4/4, one beat = a 1/4 note). Straight divisions then triplets.
    private static readonly double[] GridValues =
        { 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625, 2.0 / 3, 1.0 / 3, 1.0 / 6, 1.0 / 12 };
    private static readonly string[] GridLabels =
        { "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64", "1/4T", "1/8T", "1/16T", "1/32T" };

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
    private int _gridIndex = 4;   // 1/16
    private int _transpose;
    private double _strength = 0.8;

    public ClipPropsView(PianoRollView roll, double startBeat)
    {
        _roll = roll;
        _startText.Text = Position(startBeat);
        _gridText.Text = GridLabels[_gridIndex];

        // CLIP — clips always loop in this model; the switch waits for a per-clip loop.
        var loop = SwitchRow("Loop", () => true, () => { }, out _, _loopText);
        loop.IsEnabled = false;
        ToolTip.SetTip(loop, "Clips always loop");
        var clip = Section("CLIP",
            Pair(Labeled("Start", Field(_startText)), Labeled("Length", Field(_lengthText))),
            loop);

        // GRID — the grid step, one-tap Quantize at the strength below.
        var grid = Section("GRID",
            Pair(Dropdown(_gridText, a => ShowMenu(a, GridLabels, _gridIndex, i =>
                {
                    _gridIndex = i;
                    _gridText.Text = GridLabels[i];
                    _roll.Grid = GridValues[i];
                })),
                ClipEditorKit.Button("Quantize", () => _roll.Quantize(_strength))),
            SliderRow("STRENGTH", 64, () => _strength, v => _strength = v,
                () => string.Format(NotaNum.Culture, "{0:0}\u2009%", _strength * 100), out _, reset: () => _strength = 0.8, valueW: 34));

        // TRANSPOSE — − value +; the value turns Brass Light while it is off zero.
        _transposeText.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        var tRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        tRow.Children.Add(GlyphButton(GlyphKind.Minus, () => Transpose(-1)));
        var tField = Field(_transposeText);
        Grid.SetColumn(tField, 1); tRow.Children.Add(tField);
        var plus = GlyphButton(GlyphKind.Plus, () => Transpose(1));
        Grid.SetColumn(plus, 2); tRow.Children.Add(plus);
        var transpose = Section("TRANSPOSE", tRow);

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
        var selection = Section(SectionTitle("SELECTION", _selCount), 8, _selCard, _selEmpty);

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
        double len = _roll.LengthBeats;
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
        _transposeText.Foreground = _transpose != 0 ? NotaPalette.AccentBright : NotaPalette.TextPrimary;
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
