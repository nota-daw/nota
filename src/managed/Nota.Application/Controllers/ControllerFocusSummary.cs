// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What the lead controller is on, in one value (specs/107): the title-bar readout, Nota Remote's
// focus line and the MCP tools all read it, so they never disagree. In Device mode the focused
// track, device and page; in Volume / Pan / Send the bank of tracks the knobs move.

namespace Nota.Application.Controllers;

public sealed record ControllerFocusSummary(
    string Controller,
    KnobMode Mode,
    int TrackId,
    string TrackName,
    string? Device,
    DeviceAddress? Address,
    int Page,
    int PageCount,
    string PageName,
    int BankFirst,
    int BankLast)
{
    /// <summary>A key that changes whenever anything shown changes (Remote sends on change).</summary>
    public string Key => $"{Controller}|{Mode}|{TrackId}|{TrackName}|{Device}|{Address}|{Page}|{PageCount}|{PageName}|{BankFirst}|{BankLast}";
}
