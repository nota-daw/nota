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

    Control Build(DeviceCardContext ctx, int deviceIndex);
}
