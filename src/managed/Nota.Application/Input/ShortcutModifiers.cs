// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Modifier flags for cross-platform shortcut definitions.

using System;

namespace Nota.Application.Input;

[Flags]
public enum ShortcutModifiers : byte
{
    None    = 0,
    Primary = 1 << 0, // Command on macOS, Ctrl on Windows / Linux
    Shift   = 1 << 1, // Shift
    Alt     = 1 << 2, // Option on macOS, Alt on Windows / Linux
    Ctrl    = 1 << 3, // Control on macOS and Windows / Linux
}
