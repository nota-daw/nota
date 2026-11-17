// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The codes binding tokens travel as between the app and plug-in editor windows (#221). A
// character key is its character ('A'–'Z' upper-case, '0'–'9', punctuation); a named key is
// the code AppKit gives it (NSF1FunctionKey = 0xF704, NSUpArrowFunctionKey = 0xF700, …), so the
// macOS window passes what it reads straight on and the JUCE window on Windows / Linux maps its
// key codes to the same numbers. Every bare key a binding can name has a code (F2 included).

using System;
using System.Collections.Generic;

namespace Nota.Application.Input;

public static class EditorKeyCodes
{
    private const int FunctionBase = 0xF704; // F1; F2 = F1 + 1, … F24

    private static readonly Dictionary<string, int> Named = new(StringComparer.Ordinal)
    {
        ["Space"] = ' ', ["Return"] = '\r', ["Tab"] = '\t', ["Esc"] = 0x1B, ["⌫"] = 0x7F, ["⌦"] = 0xF728,
        ["↑"] = 0xF700, ["↓"] = 0xF701, ["←"] = 0xF702, ["→"] = 0xF703,
        ["Home"] = 0xF729, ["End"] = 0xF72B, ["PgUp"] = 0xF72C, ["PgDn"] = 0xF72D,
    };

    private static readonly Dictionary<int, string> Reverse = BuildReverse();

    private static Dictionary<int, string> BuildReverse()
    {
        var r = new Dictionary<int, string>();
        foreach (var (t, c) in Named) r[c] = t;
        for (int f = 1; f <= 24; f++) r[FunctionBase + f - 1] = "F" + f;
        return r;
    }

    /// <summary>The code for a binding token, or 0 when it has none.</summary>
    public static int Of(string token)
    {
        if (string.IsNullOrEmpty(token)) return 0;
        if (Named.TryGetValue(token, out int c)) return c;
        if (token.Length >= 2 && token[0] == 'F' && int.TryParse(token.AsSpan(1), out int f) && f is >= 1 and <= 24)
            return FunctionBase + f - 1;
        if (token.Length == 1) return char.ToUpperInvariant(token[0]);
        return 0;
    }

    /// <summary>The binding token a code stands for, or null.</summary>
    public static string? TokenOf(int code)
    {
        if (Reverse.TryGetValue(code, out var t)) return t;
        if (code is > 0x20 and < 0x7F) return char.ToUpperInvariant((char)code).ToString();
        return null;
    }
}
