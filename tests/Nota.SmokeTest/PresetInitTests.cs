// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Init in the preset list (#57): after a factory preset, ResetInPlace puts every parameter of a
// built-in instrument, audio effect or MIDI effect back to its default, as on a freshly added
// device, and keeps the card size (the instrument's view param, a MIDI effect's "View").
// Nota Mosaic (#165): Init also empties its program, back to a fresh Mosaic, in one undo step.

using Nota.Application;
using Nota.Application.Mosaic;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class PresetInitTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        var cat = new FactoryPresetCatalog();
        using var e = new NotaEngine();

        // ---- instrument: Nota Synth after a factory preset ---------------------------------
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        int pc = e.PluginParamCount(t, -1);
        int view = InstrumentView.Index(e, t);
        var preset = cat.All().First(p => p.IsInstrument && p.BuiltinKind == 0 && p.DisplayName == "Warm Pad");
        yield return (cat.ApplyInPlace(e, preset.Id, t, -1).Length == 0, "a Synth factory preset applies in place");
        int moved = Enumerable.Range(0, pc).Count(i => i != view && Math.Abs(e.PluginParamGet(t, -1, i) - e.InstrumentParamDefault(t, i)) > 1e-4);
        if (view >= 0) e.PluginParamSet(t, -1, view, 1f - e.InstrumentParamDefault(t, view));   // the other card size
        float viewBefore = view >= 0 ? e.PluginParamGet(t, -1, view) : 0f;
        yield return (cat.ResetInPlace(e, t, -1, midiEffect: false).Length == 0, "Init on the instrument reports no warning");
        int off = Enumerable.Range(0, pc).Count(i => i != view && Math.Abs(e.PluginParamGet(t, -1, i) - e.InstrumentParamDefault(t, i)) > 1e-4);
        yield return (moved > 0 && off == 0, $"Init puts every Synth param back to its default ({moved} moved by the preset, {off} left off)");
        yield return (view < 0 || Math.Abs(e.PluginParamGet(t, -1, view) - viewBefore) < 1e-6, "Init keeps the instrument card's size");

        // ---- audio effect: a Delay after a factory preset ----------------------------------
        int d = e.AddBuiltinDevice(t, 3);
        var fx = cat.All().First(p => !p.IsInstrument && !p.IsMidiEffect && p.BuiltinKind == 3);
        cat.ApplyInPlace(e, fx.Id, t, d);
        int dc = e.DeviceParamCount(t, d);
        int fxMoved = Enumerable.Range(0, dc).Count(i => Math.Abs(e.DeviceGetParam(t, d, i) - e.DeviceParamDefault(t, d, i)) > 1e-4);
        cat.ResetInPlace(e, t, d, midiEffect: false);
        int fxOff = Enumerable.Range(0, dc).Count(i => Math.Abs(e.DeviceGetParam(t, d, i) - e.DeviceParamDefault(t, d, i)) > 1e-4);
        yield return (fxMoved > 0 && fxOff == 0, $"Init puts every Delay param back to its default ({fxMoved} moved by \"{fx.DisplayName}\", {fxOff} left off)");

        // ---- MIDI effect: every param but View --------------------------------------------
        int m = e.AddMidiEffect(t, 0);
        int mc = e.MidiEffectParamCount(t, m);
        int mView = Enumerable.Range(0, mc).FirstOrDefault(i => e.MidiEffectParamName(t, m, i) == "View", -1);
        for (int i = 0; i < mc; i++)
        {
            float def = e.MidiEffectParamDefault(t, m, i);
            e.MidiEffectSetParam(t, m, i, def == 0f ? 1f : 0f);
        }
        float mViewBefore = mView >= 0 ? e.MidiEffectGetParam(t, m, mView) : 0f;
        cat.ResetInPlace(e, t, m, midiEffect: true);
        int mOff = Enumerable.Range(0, mc).Count(i => i != mView && Math.Abs(e.MidiEffectGetParam(t, m, i) - e.MidiEffectParamDefault(t, m, i)) > 1e-4);
        yield return (mc > 0 && mOff == 0, $"Init puts every MIDI effect param back to its default ({mOff} of {mc} left off)");
        yield return (mView < 0 || Math.Abs(e.MidiEffectGetParam(t, m, mView) - mViewBefore) < 1e-6, "Init keeps the MIDI effect card's size (View)");

        // ---- Nota Mosaic: its sound is the program, so Init empties it (#165) ---------------
        int ms = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(ms, MosaicModel.Kind);
        int msc = e.PluginParamCount(ms, -1), msView = InstrumentView.Index(e, ms);
        e.MosaicSetProgram(ms, new MosaicProgram { Name = "Test Piano", SourceKind = "files" }.Serialize());
        for (int i = 0; i < msc; i++)
            if (i != msView) e.PluginParamSet(ms, -1, i, e.InstrumentParamDefault(ms, i) < 0.5f ? 0.9f : 0.1f);
        bool held = e.MosaicProgram(ms).Length > 0;
        yield return (cat.ResetInPlace(e, ms, -1, midiEffect: false).Length == 0, "Init on Mosaic reports no warning");
        int msOff = Enumerable.Range(0, msc).Count(i => i != msView && Math.Abs(e.PluginParamGet(ms, -1, i) - e.InstrumentParamDefault(ms, i)) > 1e-4);
        yield return (held && e.MosaicProgram(ms).Length == 0 && msOff == 0,
            $"Init empties Mosaic's program and resets its params (program held before: {held}, {msOff} params left off)");
        yield return (e.Undo() && MosaicProgram.Parse(e.MosaicProgram(ms)).Name == "Test Piano", "undo brings Mosaic's program back");
    }
}
