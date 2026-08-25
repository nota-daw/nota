// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- gamepad input (live note source) ---

    /// <summary>Starts the gamepad poll thread (no-op if already running). Called once at startup.</summary>
    public void GamepadStart()
    { ThrowIfDisposed(); NativeMethods.GamepadStart(_handle); }

    /// <summary>Stops the gamepad poll thread. Called on shutdown / Dispose.</summary>
    public void GamepadStop()
    { ThrowIfDisposed(); NativeMethods.GamepadStop(_handle); }

    /// <summary>Hot-plug count for the UI tick (cheaper than rebuilding the list).</summary>
    public int GamepadCount
    { get { ThrowIfDisposed(); return NativeMethods.GamepadCount(_handle); } }

    /// <summary>Currently connected pads (macOS v1; empty on other platforms).</summary>
    public IReadOnlyList<GamepadDevice> Gamepads()
    {
        ThrowIfDisposed();
        int n = NativeMethods.GamepadCount(_handle);
        var list = new List<GamepadDevice>(n);
        for (int i = 0; i < n; i++)
            list.Add(new GamepadDevice(
                Marshal.PtrToStringUTF8(NativeMethods.GamepadUid(_handle, i)) ?? "",
                Marshal.PtrToStringUTF8(NativeMethods.GamepadName(_handle, i)) ?? ""));
        return list;
    }

    /// <summary>Drains queued button edges into <paramref name="buffer"/> and returns the
    /// count. Call from the UI tick (~30 Hz); each edge's <see cref="GamepadButtonEvent.Pad"/>
    /// indexes into the pad list from <see cref="Gamepads"/> at that moment.</summary>
    public int PollGamepadEvents(GamepadButtonEvent[] buffer)
    {
        ThrowIfDisposed();
        if (buffer is null || buffer.Length < 1) return 0;
        return NativeMethods.GamepadPollEvents(_handle, buffer, buffer.Length);
    }
}
