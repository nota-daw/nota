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

    /// <summary>When true the body manages its own padding (e.g. a full-width LIVE strip)
    /// and the shell wraps it with no inset. Default false = the shell pads the body 8px.</summary>
    bool FullBleed => false;

    Control Build(DeviceCardContext ctx, int index);
}
