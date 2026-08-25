// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
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
    /// and the shell wraps it with no inset. Default false = the shell pads the body 8px.</summary>
    bool FullBleed => false;

    Control Build(DeviceCardContext ctx, int deviceIndex);
}
