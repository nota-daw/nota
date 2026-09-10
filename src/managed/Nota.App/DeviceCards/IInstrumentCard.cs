// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — a built-in instrument card strategy: builds the full editor card
// for the track's instrument (there is no shared shell here — the instrument card owns
// its whole frame). Stateless singleton; per-rebuild state lives in DeviceCardContext.

using Avalonia.Controls;

namespace Nota.App;

internal interface IInstrumentCard
{
    Control Build(DeviceCardContext ctx);

    /// <summary>True if Build returns body content only (no header/border), to be wrapped
    /// by the shared DeviceCardShell. False (default) = the card owns its whole frame.</summary>
    bool BodyOnly => false;

    /// <summary>Small uppercase tag shown next to the name in the shared shell header
    /// (e.g. "SUBTRACTIVE", "GRANULAR"). Only used when BodyOnly.</summary>
    string Subtitle => "";

    /// <summary>Card width when wrapped by the shared shell (bodies keep their natural
    /// width; the chrome is what's unified). Only used when BodyOnly.</summary>
    double CardWidth => 700;

    /// <summary>Voice readout for the shell header (e.g. "3/5"), or null for the default
    /// "active/16". Called on the live-refresh tick. Only used when BodyOnly.</summary>
    string? VoiceLabel(Nota.Application.IAudioEngine engine, int trackId, int active) => null;
}
