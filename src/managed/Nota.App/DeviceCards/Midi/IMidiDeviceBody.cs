// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — a MIDI-effect body strategy: the inner content of a MIDI-effect
// card (shown before the instrument). The MIDI shell (bypass dot, reorder, remove)
// stays in the view and wraps whatever Build returns.

using Avalonia.Controls;

namespace Nota.App;

internal interface IMidiDeviceBody
{
    double Width { get; }

    /// <summary>The card width for this device instance — a body with a size toggle (Nota
    /// Arp S / L) reads its own state. Default: <see cref="Width"/>.</summary>
    double WidthFor(Nota.Application.IAudioEngine engine, int trackId, int index) => Width;

    /// <summary>Optional controls shown in the shell header, left of the bypass switch
    /// (Nota Arp: MIDI Learn · tempo · the S / L size toggle). Default: none.</summary>
    Control? HeaderAccessory(DeviceCardContext ctx, int index) => null;

    /// <summary>When true the body manages its own padding (e.g. a full-width LIVE strip)
    /// and the shell wraps it with no inset. Default false = the shell pads the body 8px.</summary>
    bool FullBleed => false;

    Control Build(DeviceCardContext ctx, int index);
}
