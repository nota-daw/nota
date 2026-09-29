// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the MIDI-effect cards (shown before the instrument), wrapped by the
// same shared shell as instruments and effects (BuildCardShell with ChainKind.Midi):
// bypass dot, name, "MIDI" tag, in-header preset picker + A/B + meter (when wide),
// reorder ◀▶, remove ✕ and a drag handle. The body comes from MidiDeviceCardFactory.

using Avalonia;
using Avalonia.Controls;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    private Control MidiDeviceCard(int index, int count)
    {
        bool bypassed = _engine.MidiEffectBypassed(_trackId, index);
        string name = _engine.MidiEffectName(_trackId, index);
        int kind = _engine.MidiEffectKind(_trackId, index);
        var body = _midiFactory.Resolve(kind);
        var ctx = NewCardContext();
        double width = body.WidthFor(_engine, _trackId, index);
        var spec = new ShellSpec(
            Name: name, Subtitle: "MIDI", DeviceIndex: index, Count: count, Bypassed: bypassed, Bypassable: true,
            CanMove: true, CanDelete: true, PresetKind: kind, IsInstrument: false, Width: width, Kind: ChainKind.Midi,
            HeaderExtra: body.HeaderAccessory(ctx, index), Compact: width < 300);
        var content = body.Build(ctx, index);
        return BuildCardShell(spec, body.FullBleed ? content : new Border { Padding = new Thickness(NotaSpace.DeviceInset), Child = content });
    }
}
