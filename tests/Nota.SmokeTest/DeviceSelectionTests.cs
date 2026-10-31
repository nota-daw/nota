// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Devices panel's multi-selection and what ⌘G makes of it (specs/044): Shift-click ranges
// across MIDI effects, the instrument and audio effects, ⌘-click toggles, and every row of the
// group-shape table (data-model.md). Headless: no window, no engine.

using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class DeviceSelectionTests
{
    private static ChainSlot M(int i) => new(DeviceSection.Midi, i);
    private static ChainSlot A(int i) => new(DeviceSection.Audio, i);
    private static ChainSlot I => ChainSlot.Instrument;

    private static string Show(DeviceSelection s) =>
        string.Join(",", s.Members.Select(m => m.Section switch { DeviceSection.Midi => $"m{m.Index}", DeviceSection.Instrument => "i", _ => $"a{m.Index}" }));

    public static IEnumerable<(bool ok, string label)> Run()
    {
        // A chain of 2 MIDI effects, the instrument and 3 audio effects.
        const int mc = 2, fc = 3;
        var s = new DeviceSelection();
        s.Click(M(1));
        s.ExtendTo(A(1), mc, true, fc);
        yield return (Show(s) == "m1,i,a0,a1", $"selection: Shift-click spans MIDI → instrument → audio ({Show(s)})");
        s.ExtendTo(M(0), mc, true, fc);
        yield return (Show(s) == "m0,m1", $"selection: Shift-click again re-spans from the same anchor ({Show(s)})");
        s.Click(A(0));
        s.Toggle(A(2));
        yield return (Show(s) == "a0,a2" && s.Anchor == A(2), $"selection: ⌘-click adds and becomes the anchor ({Show(s)})");
        s.Toggle(A(0));
        yield return (Show(s) == "a2", $"selection: ⌘-click on a selected device takes it out ({Show(s)})");
        s.Clear();
        yield return (s.IsEmpty && s.Anchor is null, "selection: Clear empties it");

        // Group shapes.
        GroupShape Shape(params ChainSlot[] slots) { var x = new DeviceSelection(); x.Set(slots); return x.Shape(mc, fc); }
        var g = Shape(A(1), A(2));
        yield return (g.Kind == GroupShapeKind.AudioRack && g.FxFirst == 1 && g.FxCount == 2, "shape: adjacent audio effects → Audio Effect Rack");
        g = Shape(A(0), A(2));
        yield return (g.Kind == GroupShapeKind.Refused && g.Reason!.Contains("next to each other"), "shape: a gap is refused");
        g = Shape(M(0), M(1));
        yield return (g.Kind == GroupShapeKind.MidiRack && g.MidiFirst == 0 && g.MidiCount == 2, "shape: MIDI effects → MIDI Effect Rack");
        g = Shape(M(1), A(0));
        yield return (g.Kind == GroupShapeKind.Refused && g.Reason!.Contains("instrument"), "shape: MIDI + audio without the instrument is refused");
        g = Shape(M(1), I, A(0), A(1));
        yield return (g.Kind == GroupShapeKind.InstrumentRack && g.MidiFirst == 1 && g.MidiCount == 1 && g.FxFirst == 0 && g.FxCount == 2,
            "shape: MIDI tail + instrument + audio head → Instrument Rack");
        g = Shape(I);
        yield return (g.Kind == GroupShapeKind.InstrumentRack && g.MidiCount == 0 && g.FxCount == 0, "shape: the instrument alone → Instrument Rack");
        g = Shape(M(0), I);
        yield return (g.Kind == GroupShapeKind.Refused, "shape: a MIDI run that doesn't reach the instrument is refused");
        g = Shape(I, A(1));
        yield return (g.Kind == GroupShapeKind.Refused, "shape: an audio run that doesn't start at the instrument is refused");
        var p = new DeviceSelection();
        p.Set(new[] { A(1), A(2) });
        p.Prune(mc, true, 2);
        yield return (Show(p) == "a1", $"selection: pruning drops devices past the chain's end ({Show(p)})");
    }
}
