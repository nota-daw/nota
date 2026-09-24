// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Materializes the factory kits on disk. Nothing ships as audio: the first time Nota
// runs (and after any change to a recipe or to the renderer) the kit's WAVs are
// synthesized into the per-user data folder, once, in the background.
//
// Each kit folder carries a stamp file holding the renderer version plus a hash of the
// kit's recipes, so a kit is re-rendered exactly when its sound would change and is
// skipped otherwise. Rendering is idempotent and safe to call from anywhere.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nota.Infrastructure.Kits;

public static class KitLibrary
{
    /// <summary>Bump when the synthesis or the post chain changes in a way that should
    /// re-render every shipped kit. Recipe edits are caught by the per-kit hash.</summary>
    private const int RendererVersion = 2;

    private const string StampFile = ".kit-stamp";

    private static readonly object Gate = new();

    /// <summary>Where the rendered kits live: a managed folder under the Nota data dir,
    /// deliberately not the user's own samples folder — these are regenerable, and they
    /// should not appear in (or disappear with) a folder the user curates.</summary>
    public static string Root => NotaPaths.SubDir("kits");

    public static string DirOf(KitDefinition kit) => Path.Combine(Root, kit.Id);

    /// <summary>File name of a pad's one-shot. The index prefix keeps a kit folder in
    /// pad order in any file browser.</summary>
    public static string FileNameOf(KitDefinition kit, KitPad pad)
    {
        int i = IndexOf(kit, pad);
        return i < 0 ? $"{Sanitize(pad.Name)}.wav" : $"{i + 1:00} {Sanitize(pad.Name)}.wav";
    }

    // Pads are compared by identity first (the catalog is built once, so instances are
    // stable) and by trigger note as a fallback, so a copy of a pad still resolves.
    private static int IndexOf(KitDefinition kit, KitPad pad)
    {
        for (int i = 0; i < kit.Pads.Count; i++) if (ReferenceEquals(kit.Pads[i], pad)) return i;
        for (int i = 0; i < kit.Pads.Count; i++) if (kit.Pads[i].Note == pad.Note) return i;
        return -1;
    }

    public static string PathOf(KitDefinition kit, KitPad pad)
        => Path.Combine(DirOf(kit), FileNameOf(kit, pad));

    /// <summary>True when every pad of the kit is on disk and current.</summary>
    public static bool IsRendered(KitDefinition kit)
    {
        var dir = DirOf(kit);
        if (!Directory.Exists(dir)) return false;
        try
        {
            var stamp = Path.Combine(dir, StampFile);
            if (!File.Exists(stamp) || File.ReadAllText(stamp).Trim() != StampFor(kit)) return false;
        }
        catch { return false; }
        foreach (var pad in kit.Pads) if (!File.Exists(PathOf(kit, pad))) return false;
        return true;
    }

    /// <summary>Renders the kit if it is missing or stale. Returns the number of files
    /// written (0 when it was already current).</summary>
    public static int Ensure(KitDefinition kit, CancellationToken ct = default)
    {
        // One renderer at a time: two windows (or the MCP server and the app) starting
        // together must not race on the same folder.
        lock (Gate)
        {
            if (IsRendered(kit)) return 0;
            var dir = DirOf(kit);
            Directory.CreateDirectory(dir);

            int written = 0;
            Parallel.For(0, kit.Pads.Count, new ParallelOptions { CancellationToken = ct }, i =>
            {
                var pad = kit.Pads[i];
                KitRenderer.RenderToFile(pad, SeedFor(kit, pad), PathOf(kit, pad));
                Interlocked.Increment(ref written);
            });

            // Drop stale files from an older revision of the kit (renamed or removed pads).
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StampFile };
            foreach (var pad in kit.Pads) keep.Add(FileNameOf(kit, pad));
            foreach (var f in Directory.EnumerateFiles(dir))
                if (!keep.Contains(Path.GetFileName(f)))
                    try { File.Delete(f); } catch { /* best-effort */ }

            File.WriteAllText(Path.Combine(dir, StampFile), StampFor(kit));
            return written;
        }
    }

    /// <summary>Renders every shipped kit that is missing or stale.</summary>
    public static int EnsureAll(CancellationToken ct = default)
    {
        int n = 0;
        foreach (var kit in KitCatalog.All)
        {
            ct.ThrowIfCancellationRequested();
            n += Ensure(kit, ct);
        }
        return n;
    }

    /// <summary>Per-pad render seed — stable across runs and distinct per pad, so the
    /// "analog drift" of a kit is fixed character rather than render-to-render noise.</summary>
    private static uint SeedFor(KitDefinition kit, KitPad pad) => Fnv($"{kit.Id}/{pad.Name}/{pad.Note}");

    // The stamp covers the renderer version and every recipe field: KitPad is a record,
    // so its ToString() names them all and a new parameter can't silently skip a rebuild.
    private static string StampFor(KitDefinition kit)
    {
        var sb = new StringBuilder();
        sb.Append(kit.Id).Append('|').Append(kit.Swing).Append('|').Append(kit.Humanize);
        foreach (var pad in kit.Pads) sb.Append('|').Append(pad);
        return $"v{RendererVersion}-{Fnv(sb.ToString()):x8}";
    }

    private static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(Path.GetInvalidFileNameChars().AsSpan().Contains(c) ? '-' : c);
        return sb.ToString();
    }
}
