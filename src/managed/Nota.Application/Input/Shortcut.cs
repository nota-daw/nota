// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Strongly typed value object representing a keyboard shortcut and its modifiers.
// Handles canonical macOS glyph formatting, platform-specific spelling, and key cap generation.

using System;
using System.Collections.Generic;
using System.Text;

namespace Nota.Application.Input;

public readonly record struct Shortcut
{
    private readonly string? _key;

    public ShortcutModifiers Modifiers { get; init; }
    public string Key
    {
        get => _key ?? string.Empty;
        init => _key = value;
    }

    public Shortcut(ShortcutModifiers modifiers, string key)
    {
        Modifiers = modifiers;
        _key = key ?? string.Empty;
    }

    // ---- Static Factory Methods -----------------------------------------------------------

    public static Shortcut None(string key) => new(ShortcutModifiers.None, key);
    public static Shortcut None(char key) => new(ShortcutModifiers.None, key.ToString());
    public static Shortcut KeyName(string key) => new(ShortcutModifiers.None, key);

    public static Shortcut Cmd(string key) => new(ShortcutModifiers.Primary, key);
    public static Shortcut Cmd(char key) => new(ShortcutModifiers.Primary, key.ToString());

    public static Shortcut CmdShift(string key) => new(ShortcutModifiers.Primary | ShortcutModifiers.Shift, key);
    public static Shortcut CmdShift(char key) => new(ShortcutModifiers.Primary | ShortcutModifiers.Shift, key.ToString());

    public static Shortcut CmdAlt(string key) => new(ShortcutModifiers.Primary | ShortcutModifiers.Alt, key);
    public static Shortcut CmdAlt(char key) => new(ShortcutModifiers.Primary | ShortcutModifiers.Alt, key.ToString());

    public static Shortcut Shift(string key) => new(ShortcutModifiers.Shift, key);
    public static Shortcut Shift(char key) => new(ShortcutModifiers.Shift, key.ToString());

    public static Shortcut Alt(string key) => new(ShortcutModifiers.Alt, key);
    public static Shortcut Alt(char key) => new(ShortcutModifiers.Alt, key.ToString());

    public static Shortcut Ctrl(string key) => new(ShortcutModifiers.Ctrl, key);
    public static Shortcut Ctrl(char key) => new(ShortcutModifiers.Ctrl, key.ToString());

    // ---- Fluent Modifiers -----------------------------------------------------------------

    public Shortcut WithShift() => this with { Modifiers = Modifiers | ShortcutModifiers.Shift };
    public Shortcut WithAlt()   => this with { Modifiers = Modifiers | ShortcutModifiers.Alt };
    public Shortcut WithCtrl()  => this with { Modifiers = Modifiers | ShortcutModifiers.Ctrl };
    public Shortcut WithCmd()   => this with { Modifiers = Modifiers | ShortcutModifiers.Primary };

    // ---- Properties -----------------------------------------------------------------------

    /// <summary>True when this shortcut includes at least one primary/navigation modifier chord.</summary>
    public bool IsChord => (Modifiers & (ShortcutModifiers.Primary | ShortcutModifiers.Alt | ShortcutModifiers.Ctrl)) != 0;

    /// <summary>True for a chord or a bare function key (F1-F12) suitable for menu placement.</summary>
    public bool CanMenuGesture =>
        IsChord || (Key.Length > 1 && Key[0] == 'F'
            && int.TryParse(Key[1..], out int n) && n is >= 1 and <= 12);

    // ---- Output Formats -------------------------------------------------------------------

    /// <summary>Formats the shortcut in canonical macOS glyph notation ("⌥⌘S", "⌘⇧Z").</summary>
    public string ToGlyphs()
    {
        var sb = new StringBuilder(Key.Length + 4);
        if ((Modifiers & ShortcutModifiers.Ctrl) != 0) sb.Append(KeyGlyphs.Ctrl);
        if ((Modifiers & ShortcutModifiers.Alt) != 0) sb.Append(KeyGlyphs.Option);
        if ((Modifiers & ShortcutModifiers.Primary) != 0) sb.Append(KeyGlyphs.Cmd);
        if ((Modifiers & ShortcutModifiers.Shift) != 0) sb.Append(KeyGlyphs.Shift);
        sb.Append(Key);
        return sb.ToString();
    }

    /// <summary>Formats for display on the running OS: macOS glyphs on macOS, PC text elsewhere.</summary>
    public string ToPlatformString() => ToPlatformString(OperatingSystem.IsMacOS());

    /// <summary>Formats for display: macOS glyphs when mac is true, PC text elsewhere.</summary>
    public string ToPlatformString(bool mac)
    {
        if (mac) return ToGlyphs();

        var parts = new List<string>(4);
        if ((Modifiers & (ShortcutModifiers.Primary | ShortcutModifiers.Ctrl)) != 0)
            parts.Add("Ctrl");
        if ((Modifiers & ShortcutModifiers.Alt) != 0)
            parts.Add("Alt");
        if ((Modifiers & ShortcutModifiers.Shift) != 0)
            parts.Add("Shift");

        string normKey = NormalizePlatformKey(Key);
        if (!string.IsNullOrEmpty(normKey))
            parts.Add(normKey);

        return string.Join("+", parts);
    }

    /// <summary>Returns the individual visual tokens for key cap badges on the running OS.</summary>
    public IReadOnlyList<string> ToKeyCaps() => ToKeyCaps(OperatingSystem.IsMacOS());

    /// <summary>Returns the individual visual tokens for key cap badges.</summary>
    public IReadOnlyList<string> ToKeyCaps(bool mac)
    {
        var caps = new List<string>(4);
        if (mac)
        {
            if ((Modifiers & ShortcutModifiers.Ctrl) != 0) caps.Add(KeyGlyphs.Ctrl.ToString());
            if ((Modifiers & ShortcutModifiers.Alt) != 0) caps.Add(KeyGlyphs.Option.ToString());
            if ((Modifiers & ShortcutModifiers.Primary) != 0) caps.Add(KeyGlyphs.Cmd.ToString());
            if ((Modifiers & ShortcutModifiers.Shift) != 0) caps.Add(KeyGlyphs.Shift.ToString());
            if (!string.IsNullOrEmpty(Key)) caps.Add(Key);
        }
        else
        {
            if ((Modifiers & (ShortcutModifiers.Primary | ShortcutModifiers.Ctrl)) != 0) caps.Add("Ctrl");
            if ((Modifiers & ShortcutModifiers.Alt) != 0) caps.Add("Alt");
            if ((Modifiers & ShortcutModifiers.Shift) != 0) caps.Add("Shift");
            string normKey = NormalizePlatformKey(Key);
            if (!string.IsNullOrEmpty(normKey)) caps.Add(normKey);
        }
        return caps;
    }

    // ---- Implicit Conversions & Overrides -------------------------------------------------

    public static implicit operator string(Shortcut s) => s.ToGlyphs();
    public override string ToString() => ToGlyphs();

    // ---- Parsing & Text Translation -------------------------------------------------------

    /// <summary>Parses a glyph string ("⌘⇧P", "⌥⌘S", "Space") into a Shortcut.</summary>
    public static Shortcut Parse(string glyphs)
    {
        if (string.IsNullOrEmpty(glyphs)) return new Shortcut(ShortcutModifiers.None, string.Empty);

        var mods = ShortcutModifiers.None;
        int i = 0;
        for (; i < glyphs.Length; i++)
        {
            char c = glyphs[i];
            if (c == KeyGlyphs.Cmd) mods |= ShortcutModifiers.Primary;
            else if (c == KeyGlyphs.Ctrl) mods |= ShortcutModifiers.Ctrl;
            else if (c == KeyGlyphs.Option) mods |= ShortcutModifiers.Alt;
            else if (c == KeyGlyphs.Shift) mods |= ShortcutModifiers.Shift;
            else break;
        }

        string k = glyphs[i..];
        return new Shortcut(mods, k);
    }

    /// <summary>Spells macOS shortcut glyphs embedded in free text as this platform spells them.</summary>
    public static string SpellText(string text) => SpellText(text, OperatingSystem.IsMacOS());

    /// <summary>Spells macOS shortcut glyphs embedded in free text as macOS glyphs or PC text.</summary>
    public static string SpellText(string text, bool mac)
    {
        if (string.IsNullOrEmpty(text) || mac) return text;

        static bool IsMod(char c) => c is KeyGlyphs.Cmd or KeyGlyphs.Ctrl or KeyGlyphs.Option or KeyGlyphs.Shift;

        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length;)
        {
            if (!IsMod(text[i]))
            {
                if (text[i] == KeyGlyphs.Backspace) sb.Append("Del");
                else if (text[i] == KeyGlyphs.Return) sb.Append("Enter");
                else sb.Append(text[i]);
                i++;
                continue;
            }

            bool ctrl = false, alt = false, shift = false;
            for (; i < text.Length && IsMod(text[i]); i++)
            {
                ctrl |= text[i] is KeyGlyphs.Cmd or KeyGlyphs.Ctrl;
                alt |= text[i] == KeyGlyphs.Option;
                shift |= text[i] == KeyGlyphs.Shift;
            }

            var names = new List<string>(3);
            if (ctrl) names.Add("Ctrl");
            if (alt) names.Add("Alt");
            if (shift) names.Add("Shift");
            sb.Append(string.Join("+", names));

            if (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '-')
                sb.Append('+');
        }

        return sb.ToString();
    }

    private static string NormalizePlatformKey(string key) => key switch
    {
        "\u21B5" => "Enter", // ↵
        "\u232B" => "Del",   // ⌫
        _ => key,
    };
}
