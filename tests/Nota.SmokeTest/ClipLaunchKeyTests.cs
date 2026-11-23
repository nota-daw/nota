// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Clip launching ergonomics (specs/112, #364): what Return does on the selected slot (the
// click's record / stop-take path), and the Session's four launch keys — Return, ⇧Return,
// ⌘Return, ⌘⇧Return — as rebindable defaults that leave Return = Stop outside the Session.

using System.Collections.Generic;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Input;

namespace Nota.SmokeTest;

internal static class ClipLaunchKeyTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        // ---- Return on a track slot: the same as a click --------------------------------------
        yield return (SessionLaunchKey.Resolve(filled: true, armed: false, recording: false) == SlotLaunchAction.Launch,
            "Return on a clip launches it");
        yield return (SessionLaunchKey.Resolve(filled: true, armed: true, recording: false) == SlotLaunchAction.Launch,
            "Return on a clip of an armed track still launches it (no overdub take)");
        yield return (SessionLaunchKey.Resolve(filled: false, armed: true, recording: false) == SlotLaunchAction.Record,
            "Return on an armed track's empty slot records into it");
        yield return (SessionLaunchKey.Resolve(filled: false, armed: false, recording: false) == SlotLaunchAction.Launch,
            "Return on an unarmed track's empty slot keeps today's launch (stops the track)");
        yield return (SessionLaunchKey.Resolve(filled: false, armed: true, recording: true) == SlotLaunchAction.StopRecording
                      && SessionLaunchKey.Resolve(filled: true, armed: false, recording: true) == SlotLaunchAction.StopRecording,
            "Return on a recording slot ends the take");

        // ---- the keys: defaults, scope, Return = Stop elsewhere ------------------------------
        const ShortcutModifiers P = ShortcutModifiers.Primary, S = ShortcutModifiers.Shift;
        foreach (bool mac in new[] { true, false })
        {
            string os = mac ? "macOS" : "Windows/Linux";
            var kb = new KeyBindings(KeyActionCatalog.Create(mac), new Settings(), null, mac ? KeyPlatform.Mac : KeyPlatform.Windows);
            yield return (kb.Resolve(KeyScope.Session, Shortcut.None("Return"))?.Id == "session.launchSelection",
                $"{os}: Return in the Session launches the selection");
            yield return (kb.Resolve(KeyScope.Session, new Shortcut(S, "Return"))?.Id == "session.stopTrack",
                $"{os}: ⇧Return in the Session stops the selected track");
            yield return (kb.Resolve(KeyScope.Session, new Shortcut(P, "Return"))?.Id == "session.launchScene",
                $"{os}: ⌘Return in the Session launches the selected scene row");
            yield return (kb.Resolve(KeyScope.Session, new Shortcut(P | S, "Return"))?.Id == "session.stopAll",
                $"{os}: ⌘⇧Return in the Session stops all clips");
            yield return (kb.Resolve(KeyScope.Global, Shortcut.None("Return"))?.Id == "transport.stop"
                          && kb.Resolve(KeyScope.Global, new Shortcut(S, "Return")) is null
                          && kb.Resolve(KeyScope.Arrangement, new Shortcut(P, "Return")) is null,
                $"{os}: outside the Session Return is still Stop and the modified Returns stay free");
            foreach (var id in new[] { "session.stopTrack", "session.launchScene", "session.stopAll" })
                yield return (kb.Get(id) is { Scope: KeyScope.Session, Fixed: false }, $"{os}: {id} is a rebindable Session key");
        }
    }
}
