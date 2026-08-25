// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// M7-4c: a single-device/instrument preset. C# owns serialization (like
// ProjectModel). A preset captures either a built-in device (kind + param
// values) or a plugin (stable id + opaque state blob), so it can be recalled
// onto any track.

using System.Text.Json.Serialization;

namespace Nota.Infrastructure;

public sealed class PresetDocument
{
    public int FormatVersion { get; set; } = 1;
    public string DisplayName { get; set; } = "";

    /// <summary>"builtin-effect" | "builtin-instrument" | "plugin-effect" | "plugin-instrument".</summary>
    public string Type { get; set; } = "";

    /// <summary>Device/instrument display name at save time (e.g. "Nota Volt", "Serum"),
    /// used to group presets under their device in the browser. "" for older presets.</summary>
    public string DeviceName { get; set; } = "";

    // Built-in device/instrument: kind (effects 0=EQ…5=Rack · instruments 0=Synth,2=Physical,5=Aurora).
    public int BuiltinKind { get; set; } = -1;

    // Legacy positional values (user-saved built-in effect presets, format v1).
    public float[] Params { get; set; } = System.Array.Empty<float>();

    /// <summary>Parameter values keyed by name (effects) or id (instruments). Preferred
    /// over <see cref="Params"/> — robust to param reordering. Effect values are in real
    /// units; instrument values are normalized 0..1.</summary>
    public System.Collections.Generic.Dictionary<string, float>? NamedParams { get; set; }

    // Plugin: stable identity + base64-encoded state blob.
    public string PluginId { get; set; } = "";
    public string StateBase64 { get; set; } = "";

    /// <summary>Forward-compat: unknown fields from newer versions round-trip.</summary>
    [JsonExtensionData] public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}
