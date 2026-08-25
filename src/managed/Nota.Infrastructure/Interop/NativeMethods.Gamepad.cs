// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Gamepad input (live note source). Poll-driven: the UI ticks
/// PollGamepadEvents and turns each edge into noteOn/noteOff calls.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_gamepad_start")]
    internal static partial void GamepadStart(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_gamepad_stop")]
    internal static partial void GamepadStop(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_gamepad_count")]
    internal static partial int GamepadCount(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_gamepad_uid")]
    internal static partial IntPtr GamepadUid(IntPtr engine, int index);

    [LibraryImport(Lib, EntryPoint = "nota_gamepad_name")]
    internal static partial IntPtr GamepadName(IntPtr engine, int index);

    [LibraryImport(Lib, EntryPoint = "nota_gamepad_poll_events")]
    internal static partial int GamepadPollEvents(IntPtr engine, [Out] GamepadButtonEvent[] buf, int max);
}
