// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>A saved device/instrument preset on disk, described for the browser
/// without exposing the on-disk DTO.</summary>
public readonly record struct PresetInfo(string Path, string DisplayName, string Type, string DeviceName = "");

/// <summary>Lists saved presets from the app presets folder (M7-4c).</summary>
public interface IPresetLibrary
{
    IReadOnlyList<PresetInfo> List(string folder);
}
