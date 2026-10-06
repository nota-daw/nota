// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Remote;

/// <summary>The phone's mappable controls, in one id space so a MIDI Learn mapping needs only a
/// number. Stable: the ids are stored in the project's MIDI map. A mapping belongs to the control,
/// not to a phone, so every connected phone drives the same targets. The XY pad and tilt are
/// mapped per track — each track's XY plays its own sound — the macro knobs project-wide.</summary>
public static class PhoneControls
{
    public const int Macro1 = 1;          // Macro 1..8 = 1..8
    public const int XyX = 20, XyY = 21;
    public const int TiltX = 30, TiltY = 31;

    public static bool IsMacro(int id) => id is >= 1 and <= 8;

    /// <summary>A control whose mappings belong to the track the phone plays.</summary>
    public static bool PerTrack(int id) => id is XyX or XyY or TiltX or TiltY;

    public static string Name(int id) => id switch
    {
        >= 1 and <= 8 => $"Macro {id}",
        XyX => "XY ←→",
        XyY => "XY ↑↓",
        TiltX => "Tilt ←→",
        TiltY => "Tilt ↑↓",
        _ => $"Control {id}",
    };
}

/// <summary>What a phone control's move did in MIDI Learn.</summary>
public enum PhoneControlResult
{
    /// <summary>No mapping: the control drives its default target.</summary>
    None,
    /// <summary>It bound the control Nota was waiting for.</summary>
    Bound,
    /// <summary>It drove a learned mapping.</summary>
    Mapped,
}

/// <summary>The app side of Nota Remote: everything that lives above the engine (the transport
/// view-model, MIDI Learn, the arrangement's sections, track colours, redraws). Implemented by
/// Nota.App. Every member is called on the UI thread, from <see cref="RemoteHub.Tick"/>.</summary>
public interface IRemoteHost
{
    /// <summary>The computer's name, as the phone shows it ("Reconnecting to Studio Mac…").</summary>
    string HostName { get; }
    /// <summary>Nota's palette, which the phone follows unless set to follow its own theme.</summary>
    bool DarkTheme { get; }
    /// <summary>The track's colour as Nota draws it, "#RRGGBB".</summary>
    string TrackColor(int trackId);
    /// <summary>The track selected in Nota (0 = none), for "Follow Nota's selection".</summary>
    int SelectedTrackId { get; }
    /// <summary>The project key as a <c>MusicalKey.Code</c>, −1 = none.</summary>
    int ProjectKeyCode { get; }
    /// <summary>The arrangement's sections (Intro, Verse…), in time order.</summary>
    IReadOnlyList<(string Name, double StartBeat)> Sections { get; }

    bool Recording { get; }
    bool LoopOn { get; }
    bool MetronomeOn { get; }
    void Play();
    void Stop();
    void SetRecord(bool on);
    void SetLoop(bool on);
    void SetLoopRange(double startBeat, double endBeat);
    void SetMetronome(bool on);
    void SetBpm(double bpm);
    /// <summary>Master gain, linear 0..1.5 — through the transport view-model so the bar's
    /// fader follows.</summary>
    double MasterVolume { get; set; }
    /// <summary>Undo one step, redrawing what it changed. False when there was nothing to undo.</summary>
    bool Undo();

    /// <summary>A phone changed something Nota draws (a mute, a macro, a launched clip): redraw.</summary>
    void Refresh();

    /// <summary>Nota's MIDI Learn is on.</summary>
    bool LearnArmed { get; }
    /// <summary>The control Nota is waiting to bind ("Reverb · Decay"), or null.</summary>
    string? LearnPendingName { get; }
    /// <summary>A phone control moved to <paramref name="norm"/> (0..1) on a phone playing
    /// <paramref name="trackId"/> (it scopes the per-track controls, see <see cref="PhoneControls.PerTrack"/>).</summary>
    PhoneControlResult PhoneControl(int controlId, int trackId, double norm);
    /// <summary>The name of what a phone control is mapped to on that track, or null when it isn't.</summary>
    string? PhoneMappingName(int controlId, int trackId);

    /// <summary>A phone connected, left, renamed itself or switched track.</summary>
    void DevicesChanged();
    /// <summary>A phone sent something (the Remote button flashes, like the gamepad dot).</summary>
    void Activity();
}

/// <summary>Who may do what from a phone (Settings → Remote).</summary>
public enum RemoteAccess
{
    /// <summary>Pads and keys only. Mixer, transport and mappings are read-only.</summary>
    PlayNotes = 0,
    /// <summary>Also mixer, transport, macros, XY and Session.</summary>
    Control = 1,
}
