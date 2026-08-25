// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M7-9: project-format migration pipeline (NFR-8). Format v1 is frozen. When
// the schema next changes, bump ProjectService.CurrentFormatVersion and append
// ONE Migration(fromVersion, upgrade) to Registered — the runner chains them so
// any old file is transparently upgraded on Load. Migrations operate on the raw
// JSON tree (JsonObject), not the typed ProjectDocument, because a current DTO
// can't necessarily represent an older shape.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Nota.Infrastructure;

/// <summary>One format upgrade: reshape a document that is AT <see cref="FromVersion"/>
/// into version <c>FromVersion + 1</c>. The runner stamps the new formatVersion;
/// <see cref="Upgrade"/> only rewrites fields.</summary>
public sealed record Migration(int FromVersion, Action<JsonObject> Upgrade);

public static class ProjectMigrations
{
    /// <summary>Ordered upgrade steps. Append a Migration here (and bump
    /// CurrentFormatVersion) when the format changes.</summary>
    public static readonly IReadOnlyList<Migration> Registered = new[]
    {
        // v1 -> v2 (M9-A4): automation lanes added. A v1 document simply has no
        // "automation" arrays; the DTO defaults them to empty, so the upgrade is a
        // no-op over the tree — the runner just stamps formatVersion = 2.
        new Migration(1, _ => { }),
        // v2 -> v3 (M9-B4): plugin-param automation adds AutomationLane.pluginParamId.
        // A v2 document has no such lanes (target is never 3); no-op, stamp version.
        new Migration(2, _ => { }),
        // v3 -> v4 (M9-D): automation points gain `curve`. Absent in v3 -> DTO
        // defaults 0 (linear); no-op over the tree, stamp version.
        new Migration(3, _ => { }),
        // v4 -> v5: audio clips gain pitch + warp fields. Absent in v4 -> DTO
        // defaults 0 (no pitch, warp off); no-op over the tree, stamp version.
        new Migration(4, _ => { }),
        // v5 -> v6: audio clips gain warp markers. Absent in v5 -> warp loads as a
        // uniform stretch (two seeded end markers); no-op over the tree, stamp version.
        new Migration(5, _ => { }),
        // v6 -> v7: document gains master-volume automation. Absent in v6 -> DTO
        // defaults to an empty lane (no master automation); no-op, stamp version.
        new Migration(6, _ => { }),
        // v7 -> v8: audio clips gain a volume envelope. Absent in v7 -> null (no
        // envelope); no-op over the tree, stamp version.
        new Migration(7, _ => { }),
        // v8 -> v9: audio clips gain a pan envelope. Absent in v8 -> null; no-op.
        new Migration(8, _ => { }),
        // v9 -> v10: MIDI clips gain velocity + volume envelopes. Absent -> null; no-op.
        new Migration(9, _ => { }),
        // v10 -> v11: warped clips gain a trim window (WarpPlayStart/End). Absent -> 0/0
        // = whole warp; no-op.
        new Migration(10, _ => { }),
        // v11 -> v12: Instrument Rack (instrument kind 3). A v11 document has no rack
        // instruments; the rack's blob lives in the existing InstrumentDto.State, so
        // there's nothing to rewrite — no-op, stamp version.
        new Migration(11, _ => { }),
        // v12 -> v13: Audio Effect Rack (device builtinKind 5). A v12 document has no
        // rack devices; the rack's blob lives in the existing DeviceDto.State — no-op.
        new Migration(12, _ => { }),
        // v13 -> v14: Drum Rack (instrument kind 4) + rack blob v2 (per-chain trigger
        // note). Old blobs load as v1 (triggerNote -1) inside the engine — no-op here.
        new Migration(13, _ => { }),
        // v14 -> v15: per-device sidechain source (DeviceDto.SidechainSource, a track
        // index). Absent in v14 documents; default -1 (no sidechain) — no-op here.
        new Migration(14, _ => { }),
        // v15 -> v16: sidechain shaping (DeviceDto.SidechainGain/Mix/TapPre). Absent in
        // v15 documents; defaults (0 dB, fully wet, post-FX) match old behaviour — no-op.
        new Migration(15, _ => { }),
        // v16 -> v17: clip deactivate (Audio/MidiClipDto.Active, key 0). Absent in v16
        // documents; the default (true = plays) matches old behaviour — no-op.
        new Migration(16, _ => { }),
        // v17 -> v18: CV modulation (TrackDto.Modulators/CvLinks, Phase 3). Absent in v17
        // documents; the defaults (empty lists = no modulation) match old behaviour — no-op.
        new Migration(17, _ => { }),
    };

    /// <summary>Migrates <paramref name="root"/> up to <see cref="ProjectService.CurrentFormatVersion"/>
    /// using the registered steps.</summary>
    public static JsonObject Migrate(JsonObject root)
        => Migrate(root, ProjectService.CurrentFormatVersion, Registered);

    /// <summary>Migrates <paramref name="root"/> to <paramref name="targetVersion"/> by chaining
    /// <paramref name="steps"/> (a missing formatVersion is treated as the baseline, v1). Throws
    /// <see cref="NotSupportedException"/> if a version in the chain has no step. Pure — takes the
    /// step list as a parameter so the runner is testable with synthetic migrations.</summary>
    public static JsonObject Migrate(JsonObject root, int targetVersion, IReadOnlyList<Migration> steps)
    {
        int cur = root["formatVersion"]?.GetValue<int>() ?? 1;
        while (cur < targetVersion)
        {
            var step = steps.FirstOrDefault(m => m.FromVersion == cur)
                ?? throw new NotSupportedException($"Cannot migrate project from format v{cur} to v{cur + 1}.");
            step.Upgrade(root);
            cur++;
            root["formatVersion"] = cur;
        }
        return root;
    }
}
