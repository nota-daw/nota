// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Local file paths from the text forms a file drag can arrive in (#300): a text/uri-list,
// GNOME's x-special/gnome-copied-files ("copy" then URIs), KDE's uri list, or plain text
// holding file:// URIs or absolute paths, one per line. Used when a drop carries no file
// list Avalonia could read. Pure: the smoke test drives it directly.

using System;
using System.Collections.Generic;

namespace Nota.Application.Input;

public static class DroppedFilePaths
{
    /// <summary>The drag formats besides Avalonia's file list that can name files.</summary>
    public static readonly IReadOnlyList<string> UriListMimeTypes = new[]
    {
        "x-special/gnome-copied-files", "application/x-kde4-urilist", "text/x-moz-url",
    };

    /// <summary>The local paths <paramref name="text"/> names, in order, without duplicates.
    /// Lines that are comments, the gnome "copy" / "cut" header, other URI schemes, remote
    /// hosts or relative paths are skipped.</summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        var paths = new List<string>();
        if (string.IsNullOrEmpty(text)) return paths;
        foreach (var raw in text.Split(new[] { '\r', '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line is "copy" or "cut") continue;
            string? path = line.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? FromFileUri(line)
                         : IsAbsolute(line) ? line
                         : null;
            if (path is not null && !paths.Contains(path)) paths.Add(path);
        }
        return paths;
    }

    /// <summary>The text of a drag format's raw bytes: UTF-16 when it looks like it
    /// (text/x-moz-url is), otherwise UTF-8.</summary>
    public static string Decode(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return "";
        bool utf16 = bytes.Length >= 2 && bytes[1] == 0 && bytes[0] != 0;
        return (utf16 ? System.Text.Encoding.Unicode : System.Text.Encoding.UTF8).GetString(bytes);
    }

    private static string? FromFileUri(string line)
    {
        if (!Uri.TryCreate(line, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
        // file://host/… names a file on another machine unless the host is this one.
        if (!string.IsNullOrEmpty(uri.Host) && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return null;
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        // file:///C:/x → /C:/x: drop the slash before a Windows drive.
        if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':') path = path[1..];
        return path;
    }

    private static bool IsAbsolute(string line)
        => line[0] == '/' || (line.Length >= 3 && char.IsLetter(line[0]) && line[1] == ':' && (line[2] == '\\' || line[2] == '/'));
}
