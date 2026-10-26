// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Linux desktop robustness: file drops that arrive as text / URI lists (#300), when a
// system file dialog counts as broken (#297), and an engine that starts without a working
// audio output (#243). Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --linux-desktop`.

using Nota.Application;
using Nota.Application.Input;
using Nota.Infrastructure;
using V = Nota.Application.FileDialogFallback.Verdict;

namespace Nota.SmokeTest;

internal static class LinuxDesktopTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        // Dropped paths.
        var uri = DroppedFilePaths.Parse("file:///home/a/My%20Kick.wav\r\nfile:///home/a/b.flac\r\n");
        yield return (uri.SequenceEqual(new[] { "/home/a/My Kick.wav", "/home/a/b.flac" }), "drop: a uri-list gives decoded local paths");
        yield return (DroppedFilePaths.Parse("copy\nfile:///x/y.wav").SequenceEqual(new[] { "/x/y.wav" }),
            "drop: gnome-copied-files skips its copy header");
        yield return (DroppedFilePaths.Parse("/x/y.wav\0").SequenceEqual(new[] { "/x/y.wav" }), "drop: a plain path, NUL-terminated, is read");
        yield return (DroppedFilePaths.Parse("# comment\nhttps://e.com/a.wav\nfile://otherhost/a.wav\nrel/a.wav\nhello").Count == 0,
            "drop: comments, web URLs, remote hosts, relative paths and text are ignored");
        yield return (DroppedFilePaths.Parse("file://localhost/x/a.wav\nfile:///x/a.wav").SequenceEqual(new[] { "/x/a.wav" }),
            "drop: localhost is local, and duplicates collapse");
        yield return (DroppedFilePaths.Parse("file:///C:/Samples/a.wav").SequenceEqual(new[] { "C:/Samples/a.wav" }),
            "drop: a Windows drive URI keeps its drive");
        var utf16 = System.Text.Encoding.Unicode.GetBytes("file:///x/a.wav\nKick");
        yield return (DroppedFilePaths.Parse(DroppedFilePaths.Decode(utf16)).SequenceEqual(new[] { "/x/a.wav" }),
            "drop: a UTF-16 text/x-moz-url decodes, its title line ignored");

        // File dialog fallback: a dialog that took the focus is real.
        yield return (FileDialogFallback.Decide(finished: true, empty: false, shown: false, windowActive: true) == V.Keep,
            "dialogs: a picked file is kept");
        yield return (FileDialogFallback.Decide(true, true, shown: true, windowActive: true) == V.Keep,
            "dialogs: a cancelled dialog that showed is a cancel, not a failure");
        yield return (FileDialogFallback.Decide(true, true, shown: false, windowActive: true) == V.Fallback,
            "dialogs: an empty answer that never showed falls back");
        yield return (FileDialogFallback.Decide(false, false, shown: false, windowActive: true) == V.Fallback,
            "dialogs: no dialog within the timeout falls back");
        yield return (FileDialogFallback.Decide(false, false, shown: true, windowActive: false) == V.Wait,
            "dialogs: an open dialog is waited for");

        // The engine starts on Linux whatever the audio setup (a device, or the silent stand-in).
        if (OperatingSystem.IsLinux())
        {
            using var eng = new NotaEngine();
            bool started;
            try { eng.Start(); started = eng.IsRunning; } catch (NotaEngineException) { started = false; }
            yield return (started && eng.SampleRate > 0,
                $"audio: the engine starts ({(eng.AudioSilentFallback ? "silent stand-in" : "device")}, {eng.SampleRate:0} Hz)");
            eng.Stop();
        }
    }
}
