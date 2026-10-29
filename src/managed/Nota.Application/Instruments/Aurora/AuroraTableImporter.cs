// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Importing a file as a Nota Aurora wavetable (specs/053): the decode and the table build
// run on a worker (they can take a moment for a long recording), then the table goes into
// the oscillator on the caller's thread as one undo step. The file picker and a drop both
// come here, so each says the same thing about a file it could not use.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Nota.Application;

public enum AuroraImportOutcome { Ok, Shortened, FirstFrames, Unreadable, Format, Silent, NotAurora }

public readonly record struct AuroraImportResult(AuroraImportOutcome Outcome, AuroraTableInfo Info)
{
    public bool Loaded => Outcome is AuroraImportOutcome.Ok or AuroraImportOutcome.Shortened or AuroraImportOutcome.FirstFrames;

    /// <summary>The one line the card shows; "" for a plain success.</summary>
    public string Message => Outcome switch
    {
        AuroraImportOutcome.Shortened => "Shortened to 60 s",
        AuroraImportOutcome.FirstFrames => "First 256 frames",
        AuroraImportOutcome.Format => "Couldn't read this file — use WAV, FLAC or MP3",
        AuroraImportOutcome.Unreadable => "Couldn't read this file — use WAV, FLAC or MP3",
        AuroraImportOutcome.Silent => "This file is silent",
        AuroraImportOutcome.NotAurora => "This track has no Aurora",
        _ => "",
    };
}

public static class AuroraTableImporter
{
    /// <summary>The file types a table can come from.</summary>
    public static readonly string[] Extensions = { ".wav", ".flac", ".mp3" };

    public static bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path ?? "");
        foreach (var e in Extensions) if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Builds the table on a worker, then (back on the calling thread) puts it into
    /// oscillator <paramref name="osc"/> of the Aurora on <paramref name="trackId"/> as one undo
    /// step. Call from the UI thread so the hand-over runs there.</summary>
    public static async Task<AuroraImportResult> ImportAsync(IAudioEngine engine, int trackId, int osc, string path)
    {
        if (!IsSupported(path)) return new(AuroraImportOutcome.Format, default);
        var (code, table) = await Task.Run(() =>
        {
            int r = engine.AuroraTableBuild(path, out var h);
            return (r, h);
        });
        if (code != AuroraTableResult.Ok)
            return new(code switch
            {
                AuroraTableResult.ErrSilent => AuroraImportOutcome.Silent,
                AuroraTableResult.ErrFormat => AuroraImportOutcome.Format,
                _ => AuroraImportOutcome.Unreadable,
            }, default);
        try
        {
            var info = engine.AuroraTableInfoOf(table);
            if (!engine.AuroraSetTable(trackId, osc, table, undoable: true)) return new(AuroraImportOutcome.NotAurora, info);
            var outcome = info.TruncatedFrames ? AuroraImportOutcome.FirstFrames
                : info.TruncatedLength ? AuroraImportOutcome.Shortened : AuroraImportOutcome.Ok;
            return new(outcome, info);
        }
        finally { engine.AuroraTableFree(table); }
    }
}
