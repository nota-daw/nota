// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The macOS application menu has a few items that Avalonia adds itself, in English, below the ones
// App.axaml declares: Hide <app>, Hide Others, Show All and Quit <app> (Services is the same word
// in French). App.axaml's own items are translated from code (App.TranslateAppMenu); these four
// are not reachable from Avalonia, so they are found by their title and
// retitled through the Objective-C runtime (all of them have the same action, so they are
// recognised by the English title Avalonia gave them). Avalonia sets the menu again when a window is
// activated, so this runs on every activation. Best-effort: if anything is missing the menu stays
// English. Nothing happens unless the running language differs from English.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Nota.Application.Localization;

namespace Nota.App;

internal static class MacAppMenu
{
    private const string AppName = "Nota";
    private const string ObjC = "/usr/lib/libobjc.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_getName(IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr sel, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendAt(IntPtr target, IntPtr sel, nint index);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendCount(IntPtr target, IntPtr sel);

    /// <summary>Retitle Hide / Hide Others / Show All / Quit in the language that is running.</summary>
    public static void Translate()
    {
        if (!OperatingSystem.IsMacOS() || Loc.Language == "en") return;
        try
        {
            foreach (var (item, _) in Items())
            {
                // Avalonia gives all of them one action (didSelectItem:), so they are told apart by
                // the English title it set; a title that is already translated matches nothing.
                string current = TitleOf(item);
                string? title = current switch
                {
                    "Hide Others" => Loc.T("menu.mac.hide_others"),
                    "Show All" => Loc.T("menu.mac.show_all"),
                    var t when t.StartsWith("Hide ", StringComparison.Ordinal) => Loc.F("menu.mac.hide", AppName),
                    var t when t.StartsWith("Quit", StringComparison.Ordinal) => Loc.F("menu.mac.quit", AppName),
                    _ => null,
                };
                if (title is null || TitleOf(item) == title) continue;
                var ns = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), Marshal.StringToCoTaskMemUTF8(title));
                Send(item, sel_registerName("setTitle:"), ns);
            }
        }
        catch { /* best-effort: the menu stays English */ }
    }

    /// <summary>The titles of the application menu's items, for checking from a test run.</summary>
    internal static string[] Titles()
    {
        var list = new List<string>();
        foreach (var (item, action) in Items()) list.Add(TitleOf(item) + " <" + action + ">");
        return list.ToArray();
    }

    private static IEnumerable<(IntPtr Item, string Action)> Items()
    {
        var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        var main = Send(app, sel_registerName("mainMenu"));
        if (main == IntPtr.Zero || SendCount(main, sel_registerName("numberOfItems")) < 1) yield break;
        var submenu = Send(SendAt(main, sel_registerName("itemAtIndex:"), 0), sel_registerName("submenu"));
        if (submenu == IntPtr.Zero) yield break;
        nint n = SendCount(submenu, sel_registerName("numberOfItems"));
        for (nint i = 0; i < n; i++)
        {
            var item = SendAt(submenu, sel_registerName("itemAtIndex:"), i);
            var sel = Send(item, sel_registerName("action"));
            yield return (item, sel == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(sel_getName(sel)) ?? "");
        }
    }

    private static string TitleOf(IntPtr item)
        => Marshal.PtrToStringUTF8(Send(Send(item, sel_registerName("title")), sel_registerName("UTF8String"))) ?? "";
}
