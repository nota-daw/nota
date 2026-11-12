// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.SmokeTest;

internal static class TestFiles
{
    /// <summary>Makes a fixture hidden the way its platform means it: a leading dot hides it on
    /// macOS and Linux, Windows needs the Hidden attribute too (git sets it on .git there).</summary>
    public static void Hide(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
    }
}
