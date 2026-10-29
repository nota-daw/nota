// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// When a Linux system file dialog counts as broken (#297), so Nota opens its built-in one.
// Pure: the smoke test drives it; Nota.App/LinuxFileDialogs.cs runs the pickers.

namespace Nota.Application;

public static class FileDialogFallback
{
    public enum Verdict { Keep, Wait, Fallback }

    /// <summary>What to do with a system picker: <paramref name="finished"/> whether it has
    /// answered (before the show timeout), <paramref name="empty"/> whether that answer picked
    /// nothing, <paramref name="shown"/> whether the window lost the focus to a dialog
    /// meanwhile, <paramref name="windowActive"/> whether the window still has the focus.
    /// A dialog that took the focus is real (even if the user then cancelled); one that
    /// answered "nothing" or stayed silent while the window kept the focus never showed.</summary>
    public static Verdict Decide(bool finished, bool empty, bool shown, bool windowActive)
    {
        bool showedSomething = shown || !windowActive;
        if (finished) return !empty || showedSomething ? Verdict.Keep : Verdict.Fallback;
        return showedSomething ? Verdict.Wait : Verdict.Fallback;
    }

    /// <summary>While waiting on a dialog that seemed to show (<see cref="Verdict.Wait"/>):
    /// the window losing the focus may have been the user switching apps, not a dialog. If
    /// the request is still open while the window has had the focus back for
    /// <paramref name="limit"/>, no dialog is in front of it and the request stalled.</summary>
    public static Verdict WhileWaiting(bool finished, System.TimeSpan activeFor, System.TimeSpan limit)
        => finished ? Verdict.Keep : activeFor >= limit ? Verdict.Fallback : Verdict.Wait;
}
