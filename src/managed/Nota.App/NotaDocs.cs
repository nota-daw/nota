// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Links into the user manual (nota-daw/nota-docs, published on GitHub Pages): Help ▸
// Documentation and each device card's "Documentation" item. The manual is English with a
// Russian translation under /ru/; a Russian UI culture opens that one. The page slugs mirror
// the manual's src/content/docs tree — keep them in step when a device is added there.

using System;
using System.Globalization;
using Avalonia.Controls;

namespace Nota.App;

internal static class NotaDocs
{
    private const string BaseUrl = "https://nota-daw.github.io/nota-docs/";

    // Engine kind ids → page slug (devices/<group>/<slug>/).
    private static readonly string[] Instruments =
    {
        "instruments/synth", "instruments/sampler", "instruments/physical", "racks/instrument-rack",
        "racks/drum-rack", "instruments/aurora", "instruments/volt", "instruments/bass",
        "instruments/pendulum", "instruments/operator", "instruments/grain", "instruments/flux",
        "instruments/rhythm", "instruments/monolith", "instruments/pentad", "instruments/consort",
        "instruments/keys", "instruments/mosaic",
    };
    private static readonly string[] AudioEffects =
    {
        "eq-8", "compressor", "reverb", "delay", "utility", "", "valve", "auto-filter", "vintage",
        "orbit", "auto-shift", "beat-repeat", "crush", "dynamic-eq", "ceiling", "strata", "eq-3",
        "forge", "level", "shutter", "chamber", "prism", "lens", "flanger", "phaser", "chorus",
    };
    private static readonly string[] MidiEffects = { "arp", "chord", "scale", "length", "velocity", "random" };

    /// <summary>The manual's address for a page ("" = the home page), in the UI's language.</summary>
    public static string Url(string page = "")
    {
        bool ru = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        return BaseUrl + (ru ? "ru/" : "") + (page.Length > 0 ? page.TrimEnd('/') + "/" : "");
    }

    /// <summary>The page for a built-in device, or the plug-ins page for anything else.</summary>
    public static string DevicePage(DeviceChainView.ChainKind kind, int engineKind)
    {
        string slug = kind switch
        {
            DeviceChainView.ChainKind.Instrument => Pick(Instruments, engineKind),
            DeviceChainView.ChainKind.Midi => Pick(MidiEffects, engineKind) is { Length: > 0 } m ? "midi-effects/" + m : "",
            _ => engineKind == 5 ? "racks/audio-effect-rack"
                : Pick(AudioEffects, engineKind) is { Length: > 0 } a ? "audio-effects/" + a : "",
        };
        return slug.Length > 0 ? "devices/" + slug : "plugins";
    }

    private static string Pick(string[] map, int i) => i >= 0 && i < map.Length ? map[i] : "";

    /// <summary>Open a manual page in the default browser.</summary>
    public static void Open(Control anchor, string page = "")
    {
        var launcher = TopLevel.GetTopLevel(anchor)?.Launcher;
        if (launcher != null) _ = launcher.LaunchUriAsync(new Uri(Url(page)));
    }
}
