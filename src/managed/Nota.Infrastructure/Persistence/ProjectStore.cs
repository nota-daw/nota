// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Text.Json.Nodes;

namespace Nota.Infrastructure;

/// <summary>IProjectStore over ProjectService — hides the on-disk document and the
/// capture/apply mechanics behind the use-case surface.</summary>
public sealed class ProjectStore : IProjectStore
{
    public IReadOnlyList<string> Save(IAudioEngine engine, TransportState transport, string dir)
    {
        var warnings = new List<string>();
        var doc = ProjectService.Capture(engine, transport, warnings);
        ProjectService.Save(doc, dir, engine);
        return warnings;
    }

    public ProjectLoadResult Load(IAudioEngine engine, string dir)
    {
        var doc = ProjectService.Load(dir);
        var warnings = ProjectService.Apply(doc, engine, dir);
        var transport = new TransportState(
            doc.Transport.Bpm, doc.Transport.MasterVolume,
            doc.Transport.MetronomeOn, doc.Transport.LoopOn,
            doc.Transport.TimeSigNumerator, doc.Transport.TimeSigDenominator,
            Nota.Application.Samples.MusicalKey.Parse(doc.Transport.Key)?.Code ?? -1);
        return new ProjectLoadResult(transport, warnings) { MissingPlugins = MissingPlugins(doc) };
    }

    // Every "pluginId" in the document (instruments, effects on any track or bus) that the
    // catalog can't resolve — offered for install from the plugin registry after load.
    private static List<string> MissingPlugins(ProjectDocument doc)
    {
        var missing = new List<string>();
        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (key, value) in o)
                    {
                        if (key == "pluginId" && value is JsonValue v && v.TryGetValue(out string? id)
                            && id.Length > 0 && !missing.Contains(id) && NotaEngine.PluginIndexOfId(id) < 0)
                            missing.Add(id);
                        else Walk(value);
                    }
                    break;
                case JsonArray a:
                    foreach (var item in a) Walk(item);
                    break;
            }
        }
        Walk(JsonNode.Parse(ProjectService.SerializeManifest(doc)));
        return missing;
    }

    public string Fingerprint(IAudioEngine engine, TransportState transport)
        // Session names: no audio is read or hashed on this frequent path. Edits inside a
        // plugin's own state aren't seen here (as before) — only structural/param changes.
        => ProjectService.SerializeManifest(ProjectService.Capture(engine, transport, new List<string>(), contentNames: false));
}
