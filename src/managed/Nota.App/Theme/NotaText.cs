// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Labels are sentence case at letter-spacing 0 (no shouting). Views and device
// bodies still pass many labels as caps literals ("PRE-DELAY", "FREQ") — the strings double
// as names elsewhere — so every label builder routes them through Label(), which turns an
// all-caps label into sentence case and keeps acronyms and units in their own case.

using System;
using System.Collections.Generic;
using System.Text;

namespace Nota.App;

internal static class NotaText
{
    // Words that stay as written whatever their position. Upper-case acronyms, and the
    // units whose case carries meaning (dB, Hz, kHz, ms). Internal and kept as one
    // ["KEY"] = "Value" table because the smoke test's label check (RunLabels) reads this
    // file as text: a label made only of these words is not shouting.
    internal static readonly Dictionary<string, string> Fixed = new(StringComparer.Ordinal)
    {
        ["DB"] = "dB", ["HZ"] = "Hz", ["KHZ"] = "kHz", ["MS"] = "ms", ["BPM"] = "BPM",
        ["LFO"] = "LFO", ["EQ"] = "EQ", ["ADSR"] = "ADSR", ["AHDSR"] = "AHDSR", ["MIDI"] = "MIDI",
        ["MPE"] = "MPE", ["CV"] = "CV", ["FM"] = "FM", ["AM"] = "AM", ["PM"] = "PM", ["RM"] = "RM",
        ["HP"] = "HP", ["LP"] = "LP", ["BP"] = "BP", ["HPF"] = "HPF", ["LPF"] = "LPF", ["BPF"] = "BPF",
        ["XY"] = "XY", ["VST"] = "VST", ["VST3"] = "VST3", ["AU"] = "AU", ["CLAP"] = "CLAP",
        ["DC"] = "DC", ["RMS"] = "RMS", ["FX"] = "FX", ["AI"] = "AI", ["MCP"] = "MCP",
        ["PDC"] = "PDC", ["DSP"] = "DSP", ["CPU"] = "CPU", ["ID"] = "ID", ["UI"] = "UI",
        ["IR"] = "IR", ["FFT"] = "FFT", ["DJ"] = "DJ", ["OSC"] = "Osc", ["ENV"] = "Env",
        ["WAV"] = "WAV", ["MP3"] = "MP3", ["FLAC"] = "FLAC", ["AIFF"] = "AIFF", ["OGG"] = "OGG",
        ["ADT"] = "ADT", ["ST"] = "st", ["CT"] = "ct", ["LR"] = "LR", ["MS/LR"] = "MS/LR",
        ["SC"] = "SC", ["PWM"] = "PWM", ["SR"] = "SR", ["BIT"] = "bit",
        ["GR"] = "GR", ["OS"] = "OS", ["TP"] = "TP", ["AT"] = "AT", ["HF"] = "HF", ["LF"] = "LF",
        ["PW"] = "PW", ["VCA"] = "VCA", ["VCF"] = "VCF", ["VCO"] = "VCO", ["THD"] = "THD",
        ["BBD"] = "BBD", ["FET"] = "FET", ["RR"] = "RR", ["CC"] = "CC",
        ["LUFS"] = "LUFS", ["USB"] = "USB", ["OK"] = "OK", ["DAW"] = "DAW",
        ["ASIO"] = "ASIO", ["WASAPI"] = "WASAPI",
    };

    /// <summary>A label in sentence case: "PRE-DELAY" → "Pre-delay", "LFO RATE" → "LFO rate",
    /// "GAIN DB" → "Gain dB". A string that already has lower-case letters is returned as
    /// written — it was typed in the case it wants.</summary>
    public static string Label(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        bool hasLetter = false;
        foreach (var ch in text)
        {
            if (char.IsLower(ch)) return text;
            if (char.IsLetter(ch)) hasLetter = true;
        }
        if (!hasLetter) return text;

        var sb = new StringBuilder(text.Length);
        bool first = true;
        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetterOrDigit(text[i])) { sb.Append(text[i]); i++; continue; }
            int j = i;
            while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '#')) j++;
            sb.Append(Word(text[i..j], first));
            first = false;
            i = j;
        }
        return sb.ToString();
    }

    private static string Word(string w, bool first)
    {
        if (Fixed.TryGetValue(w, out var f))
            return first && char.IsLower(f[0]) && f is not ("dB" or "kHz" or "ms" or "st" or "ct") ? char.ToUpperInvariant(f[0]) + f[1..] : f;
        // One letter (A/B, L/R, M/S, X), a note name (C#3) or a code with digits (LFO2, 4X)
        // keeps its case: lower-casing it would change what it names.
        if (w.Length == 1 || HasDigit(w) || w.Contains('#'))
        {
            // …except a known acronym with a trailing index: "LFO2" stays, "OSC2" → "Osc2".
            var stem = w.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (stem.Length > 1 && stem.Length < w.Length && Fixed.TryGetValue(stem, out var fs)) return fs + w[stem.Length..];
            return w;
        }
        var lower = w.ToLowerInvariant();
        return first ? char.ToUpperInvariant(lower[0]) + lower[1..] : lower;
    }

    private static bool HasDigit(string w)
    {
        foreach (var ch in w) if (char.IsDigit(ch)) return true;
        return false;
    }
}
