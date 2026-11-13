// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — a device body strategy: builds the inner content of one device
// card (everything below the shared header) for a given built-in kind or a hosted
// plugin. The shell (bypass dot, reorder/remove, drag handle) stays in the view and
// wraps whatever Build returns. Strategies are stateless singletons; per-rebuild
// state is carried by DeviceCardContext.

using Avalonia.Controls;

namespace Nota.App;

internal interface IDeviceBody
{
    /// <summary>Fixed card width for this device kind (ignored when <see cref="AutoWidth"/>).</summary>
    double Width { get; }

    /// <summary>When true, the card sizes to its content width instead of the fixed
    /// <see cref="Width"/> — so a body that collapses part of itself shrinks the card.</summary>
    bool AutoWidth => false;

    /// <summary>When true, the body manages its own padding (e.g. a full-width LIVE strip)
    /// and the shell wraps it with no inset. Default false = the shell pads the body 6px (NotaSpace.DeviceInset).</summary>
    bool FullBleed => false;

    /// <summary>The processing type, shown as the mono caps badge on the right of the header
    /// (e.g. "HYBRID", "DYNAMICS"); null shows no badge.</summary>
    string? Subtitle => null;

    /// <summary>The card width for this device as it is set now — a body with an S / L size
    /// (DeviceView) answers per device; the rest keep <see cref="Width"/>.</summary>
    double WidthFor(Nota.Application.IAudioEngine engine, int trackId, int deviceIndex) => Width;

    /// <summary>True when the device opens in XL: its L body moved into the panel above the
    /// chain and stretched to it (specs/085). The body must lay out to any size its host gives it.</summary>
    bool SupportsXl => false;

    /// <summary>Extra controls for the card header (the S / L toggle), or null.</summary>
    Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => null;

    Control Build(DeviceCardContext ctx, int deviceIndex);
}
