// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What a control hands to ValueEntry so its value can be typed (specs/035-typed-value-entry):
// a ValueField per typeable value, and — for controls with several handles (graphs,
// envelopes, matrices) — an IValueEntryHost that says which fields sit under the pointer.

using System;
using System.Collections.Generic;
using Avalonia;

namespace Nota.App;

/// <summary>One typeable value. Positions are normalised 0..1; the owner maps them to the parameter.</summary>
/// <param name="Name">Shown in the menu header ("Cutoff").</param>
/// <param name="Get">The current position.</param>
/// <param name="TextAt">The read-out at a position — pure: it formats, it never writes the engine
/// (unless <see cref="Probes"/> says otherwise).</param>
/// <param name="Set">Writes a position through the same setter a drag uses.</param>
/// <param name="Begin">Opens the gesture (automation write / undo step).</param>
/// <param name="End">Closes it.</param>
/// <param name="Default">The 0..1 position of the default; null = no reset.</param>
internal sealed record ValueField(string Name, Func<double> Get, Func<double, string> TextAt, Action<double> Set,
    Action? Begin = null, Action? End = null, double? Default = null)
{
    /// <summary>Resets by action instead of by position (a slider row's own reset).</summary>
    public Action? Reset { get; init; }
    /// <summary>Removes instead of resetting (a matrix route, a cable): ⌥-click and the menu's Clear.</summary>
    public Action? Clear { get; init; }
    /// <summary>Repaints the owner after a commit or a reset.</summary>
    public Action? After { get; init; }
    /// <summary><see cref="TextAt"/> reads back through <see cref="Set"/> (legacy rows, plug-in
    /// parameters whose text is only readable at the current value); the position is restored
    /// before the gesture opens.</summary>
    public bool Probes { get; init; }

    public bool CanReset => Reset is not null || Default is not null;
}

/// <summary>A control with several typeable handles: double-click / ⌥-click / right-click ask it
/// which fields sit under the pointer.</summary>
internal interface IValueEntryHost
{
    /// <summary>The fields of the handle at <paramref name="p"/>, in Tab order; null = not on a
    /// handle, so the event passes through to the control (an empty-space double-click keeps
    /// its meaning).</summary>
    IReadOnlyList<ValueField>? FieldsAt(Point p);

    /// <summary>Where the box sits for <paramref name="field"/>, in the host's coordinates.</summary>
    Rect AnchorOf(ValueField field);

    /// <summary>Every field, in order, for the Tab chain.</summary>
    IReadOnlyList<ValueField> AllFields { get; }
}
