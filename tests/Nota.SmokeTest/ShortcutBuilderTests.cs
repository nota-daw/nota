// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Verification of the Shortcut builder, canonical glyph ordering, platform spelling,
// and Avalonia KeyGesture generation (spec 023-shortcut-builder).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Nota.App.Input;
using Nota.Application.Input;

namespace Nota.SmokeTest;

internal static class ShortcutBuilderTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        // ---- 1. Factory methods and canonical glyph ordering -----------------------------
        var cmdN = Shortcut.Cmd('N');
        yield return (cmdN.ToGlyphs() == "⌘N", $"Cmd('N') formats as ⌘N (got \"{cmdN.ToGlyphs()}\")");

        var cmdShiftS = Shortcut.CmdShift('S');
        yield return (cmdShiftS.ToGlyphs() == "⌘⇧S", $"CmdShift('S') formats as ⌘⇧S (got \"{cmdShiftS.ToGlyphs()}\")");

        var cmdAltS = Shortcut.CmdAlt('S');
        yield return (cmdAltS.ToGlyphs() == "⌥⌘S", $"CmdAlt('S') formats as ⌥⌘S (got \"{cmdAltS.ToGlyphs()}\")");

        // Fluent chaining produces identical canonical ordering regardless of call order
        var chained1 = Shortcut.Cmd('S').WithAlt();
        var chained2 = Shortcut.Alt('S').WithCmd();
        yield return (chained1.ToGlyphs() == "⌥⌘S" && chained2.ToGlyphs() == "⌥⌘S",
            "fluent WithAlt/WithCmd produces canonical ⌥⌘S order");

        var fnKey = Shortcut.None("F11");
        yield return (fnKey.ToGlyphs() == "F11", "None(\"F11\") formats as F11");

        // ---- 2. Implicit string conversion -----------------------------------------------
        string str = Shortcut.Cmd('A');
        yield return (str == "⌘A", "implicit string conversion produces canonical glyphs");

        // ---- 3. Parsing ------------------------------------------------------------------
        var parsedShiftCmd = Shortcut.Parse("⌘⇧Z");
        yield return (parsedShiftCmd.Modifiers == (ShortcutModifiers.Primary | ShortcutModifiers.Shift) && parsedShiftCmd.Key == "Z",
            "Parse(\"⌘⇧Z\") extracts Primary + Shift modifiers and key Z");

        var parsedAltCmd = Shortcut.Parse("⌥⌘S");
        yield return (parsedAltCmd.Modifiers == (ShortcutModifiers.Primary | ShortcutModifiers.Alt) && parsedAltCmd.Key == "S",
            "Parse(\"⌥⌘S\") extracts Primary + Alt modifiers and key S");

        var parsedBare = Shortcut.Parse("Space");
        yield return (parsedBare.Modifiers == ShortcutModifiers.None && parsedBare.Key == "Space",
            "Parse(\"Space\") extracts None modifiers and key Space");

        // ---- 4. Platform string and key caps (both macOS and PC branches) -----------------
        var macCaps = Shortcut.CmdShift('P').ToKeyCaps(mac: true);
        yield return (macCaps.SequenceEqual(["⌘", "⇧", "P"]), "ToKeyCaps(mac: true) returns [⌘, ⇧, P]");
        yield return (Shortcut.CmdShift('P').ToPlatformString(mac: true) == "⌘⇧P",
            "ToPlatformString(mac: true) returns ⌘⇧P");

        var pcCaps = Shortcut.CmdShift('P').ToKeyCaps(mac: false);
        yield return (pcCaps.SequenceEqual(["Ctrl", "Shift", "P"]), "ToKeyCaps(mac: false) returns [Ctrl, Shift, P]");
        yield return (Shortcut.CmdShift('P').ToPlatformString(mac: false) == "Ctrl+Shift+P",
            "ToPlatformString(mac: false) returns Ctrl+Shift+P");

        // Special keys in platform strings
        yield return (Shortcut.None(KeyGlyphs.Return).ToPlatformString(mac: false) == "Enter",
            "ToPlatformString(mac: false) converts ↵ to Enter");
        yield return (Shortcut.None(KeyGlyphs.Backspace).ToPlatformString(mac: false) == "Del",
            "ToPlatformString(mac: false) converts ⌫ to Del");

        // ---- 5. Text spelling (both macOS and PC branches) --------------------------------
        string textWithGlyphs = "Show automation (⌘⇧A) · hold ⌥ to drag · ⌫ delete · ↵ launch";
        string macSpelled = Shortcut.SpellText(textWithGlyphs, mac: true);
        yield return (macSpelled == textWithGlyphs, "SpellText(mac: true) preserves macOS glyphs");

        string pcSpelled = Shortcut.SpellText(textWithGlyphs, mac: false);
        yield return (pcSpelled == "Show automation (Ctrl+Shift+A) · hold Alt to drag · Del delete · Enter launch",
            "SpellText(mac: false) converts glyphs to PC key names");

        // ---- 6. Chord and Menu gesture validation -----------------------------------------
        yield return (Shortcut.Cmd('Z').IsChord, "Cmd('Z') is a chord");
        yield return (!Shortcut.None("F11").IsChord, "None(\"F11\") is not a chord");
        yield return (Shortcut.None("F11").CanMenuGesture, "None(\"F11\") can be a menu gesture");
        yield return (Shortcut.Shift("F5").CanMenuGesture, "Shift(\"F5\") can be a menu gesture");
        yield return (!Shortcut.None("0").CanMenuGesture, "Bare \"0\" cannot be a menu gesture (would swallow typing)");
        yield return (Shortcut.Cmd('S').CanMenuGesture, "Cmd('S') can be a menu gesture");

        // ---- 7. Avalonia KeyGesture generation --------------------------------------------
        var commaMac = Shortcut.Cmd(',').ToGesture(mac: true);
        yield return (commaMac is { Key: Key.OemComma, KeyModifiers: KeyModifiers.Meta },
            "Cmd(',') on macOS converts to Key.OemComma + Meta");

        var commaPc = Shortcut.Cmd(',').ToGesture(mac: false);
        yield return (commaPc is { Key: Key.OemComma, KeyModifiers: KeyModifiers.Control },
            "Cmd(',') on PC converts to Key.OemComma + Control");

        var zeroGesture = Shortcut.None('0').ToGesture();
        yield return (zeroGesture is { Key: Key.D0, KeyModifiers: KeyModifiers.None },
            "None('0') converts to Key.D0 with KeyModifiers.None");

        var returnGesture = Shortcut.None("Return").ToGesture();
        yield return (returnGesture is { Key: Key.Return }, "None(\"Return\") converts to Key.Return");

        var escGesture = Shortcut.None("Esc").ToGesture();
        yield return (escGesture is { Key: Key.Escape }, "None(\"Esc\") converts to Key.Escape");

        var optCmdSMac = Shortcut.Parse("⌥⌘S").ToGesture(mac: true);
        yield return (optCmdSMac is { Key: Key.S, KeyModifiers: KeyModifiers.Alt | KeyModifiers.Meta },
            "⌥⌘S on macOS converts to Key.S + Alt + Meta");

        var optCmdSPc = Shortcut.Parse("⌥⌘S").ToGesture(mac: false);
        yield return (optCmdSPc is { Key: Key.S, KeyModifiers: KeyModifiers.Alt | KeyModifiers.Control },
            "⌥⌘S on PC converts to Key.S + Alt + Control");
    }
}
