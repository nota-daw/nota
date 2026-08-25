// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// M7-7: crash recovery. A session marker is written at launch and cleared on a
// clean quit; if it survives to the next launch the previous session crashed.
// Meanwhile the app periodically autosaves a recovery snapshot (a full .nota
// bundle) here, but only when the project actually changed (manifest diff), so
// idle sessions cost no disk churn. On a detected crash the app offers to
// restore the snapshot. Reuses ProjectService for all serialization; no engine
// changes. Lives in Nota.Interop so it is unit-testable without the UI.

using System;
using System.IO;
using System.Text.Json;

namespace Nota.Infrastructure;

public sealed class RecoveryService : IRecoveryStore
{
    private sealed class Meta
    {
        public string? OriginalPath { get; set; }
        public string SavedAt { get; set; } = "";
    }

    private static readonly JsonSerializerOptions MetaOpts = new() { WriteIndented = true };

    private readonly string _dir;      // recovery folder
    private readonly string _bundle;   // recovery/project.nota
    private readonly string _meta;     // recovery/meta.json
    private readonly string _marker;   // recovery/session.active

    private string? _lastManifest;     // last autosaved manifest JSON (in-memory diff)

    /// <summary>Uses the default app-support recovery folder, or an override (tests).</summary>
    public RecoveryService(string? recoveryDir = null)
    {
        _dir = recoveryDir ?? NotaPaths.SubDir("recovery");
        _bundle = Path.Combine(_dir, "project.nota");
        _meta = Path.Combine(_dir, "meta.json");
        _marker = Path.Combine(_dir, "session.active");
    }

    /// <summary>True if the previous session left a marker behind (i.e. did not exit
    /// cleanly). Call BEFORE <see cref="BeginSession"/>.</summary>
    public bool CrashDetected() => File.Exists(_marker);

    /// <summary>The recoverable snapshot if a crash was detected and a snapshot exists.</summary>
    public RecoveryInfo? PendingRecovery()
    {
        if (!CrashDetected()) return null;
        if (!File.Exists(Path.Combine(_bundle, ProjectService.ManifestName))) return null;
        Meta? meta = null;
        try { if (File.Exists(_meta)) meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(_meta)); }
        catch { /* missing/corrupt meta -> treat as unknown origin */ }
        return new RecoveryInfo(_bundle, meta?.OriginalPath, meta?.SavedAt ?? "");
    }

    /// <summary>Marks this session as running. Call once at launch after the crash check.</summary>
    public void BeginSession()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_marker, DateTime.Now.ToString("o"));
    }

    /// <summary>Clears the marker on a clean shutdown so the next launch offers nothing.</summary>
    public void EndSessionClean()
    {
        try { if (File.Exists(_marker)) File.Delete(_marker); } catch { /* best-effort */ }
    }

    /// <summary>Removes the recovery snapshot (after the user restores or discards it).</summary>
    public void ClearRecovery()
    {
        try { if (Directory.Exists(_bundle)) Directory.Delete(_bundle, true); } catch { }
        try { if (File.Exists(_meta)) File.Delete(_meta); } catch { }
        _lastManifest = null;
    }

    /// <summary>Autosaves the snapshot if the project changed since the last autosave.
    /// Returns true if a write happened. <paramref name="originalPath"/> is the real
    /// bundle path (null if the session was never saved).</summary>
    public bool Autosave(ProjectDocument doc, IAudioEngine engine, string? originalPath)
    {
        string manifest = ProjectService.SerializeManifest(doc);
        if (manifest == _lastManifest) return false; // unchanged since last snapshot
        _lastManifest = manifest;

        ProjectService.Save(doc, _bundle, engine);
        var meta = new Meta { OriginalPath = originalPath, SavedAt = DateTime.Now.ToString("o") };
        File.WriteAllText(_meta, JsonSerializer.Serialize(meta, MetaOpts));
        return true;
    }

    /// <summary>IRecoveryStore entry point: captures the engine first, then autosaves.</summary>
    void IRecoveryStore.Autosave(IAudioEngine engine, TransportState transport, string? originalPath)
    {
        var doc = ProjectService.Capture(engine, transport, new List<string>());
        Autosave(doc, engine, originalPath);
    }
}
