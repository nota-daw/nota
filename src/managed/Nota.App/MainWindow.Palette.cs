// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette host (⌘⇧P / Ctrl+Shift+P). Keeps the index current (CP-23: actions once,
// devices and presets when the browser re-lists them — prepared off the UI thread — tracks
// on every open), snapshots the context the moment the palette opens (CP-2), shows it in the
// window it was called from (CP-17), and applies the chosen item through the command registry
// or the shared insert service (CP-3/CP-25), with a toast that can undo it.

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.Application.Palette;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private readonly PaletteIndex _paletteIndex = new();
    private PaletteUsage? _paletteUsage;
    private PaletteSearch? _paletteSearch;
    private CommandPaletteView? _palette;
    private readonly PaletteToast _paletteToast = new();
    private IInputElement? _paletteReturnFocus;
    private int _presetBuild;

    private bool PaletteOpen => _palette?.IsOpen == true;

    /// <summary>Called once the view model is attached: fills the index and keeps it current.</summary>
    private void InitPalette()
    {
        if (_vm is null) return;
        _paletteUsage = new PaletteUsage(_vm.Settings.Current);
        _paletteSearch = new PaletteSearch(_paletteIndex, _paletteUsage);
        _paletteIndex.Set("modular", new ModularPaletteProvider().Build());
        _paletteIndex.Set("actions", new ActionPaletteProvider(_commands).Build());
        RefreshPaletteTracks();
        RefreshPaletteDevices();
        _vm.Browser.DevicesRebuilt += RefreshPaletteDevices;
        _vm.Browser.PresetsRebuilt += RefreshPalettePresets;
        _vm.Browser.LibraryChanged += RefreshPaletteDevices;   // favourites and tags
    }

    private void RefreshPaletteTracks() => _paletteIndex.Set("tracks", new TrackPaletteProvider(Engine).Build());

    private void RefreshPaletteDevices()
    {
        if (_vm is null) return;
        _paletteIndex.Set("devices", new DevicePaletteProvider(_vm.Browser).Build());
        RefreshPalettePresets();
    }

    // Presets are the bulk of the index: list them here (they come from the browser's trees),
    // prepare them on a worker, swap the slice in on the UI thread. A palette opened meanwhile
    // searches what is there and shows the progress (CP-23).
    private void RefreshPalettePresets()
    {
        if (_vm is null) return;
        var items = new PresetPaletteProvider(_vm.Browser).Build();
        int build = ++_presetBuild;
        _paletteIndex.ReportProgress("presets", 0.1f);
        Task.Run(() => PaletteIndex.Prepare(items)).ContinueWith(t =>
        {
            if (t.IsFaulted || build != _presetBuild) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (build != _presetBuild) return;
                _paletteIndex.Set("presets", t.Result);
                _paletteIndex.ReportProgress("presets", 1f);
            });
        });
    }

    // ---- open / close ------------------------------------------------------------------

    /// <summary>Opens the palette over <paramref name="source"/> (the window the key came from),
    /// or closes it when open (CP-1).</summary>
    internal void TogglePalette(Window? source)
    {
        if (_vm is null || _paletteSearch is null) return;
        if (PaletteOpen) { _palette!.Close(); return; }
        var ctx = SnapshotContext(source);
        RefreshPaletteTracks();

        _palette ??= CreatePalette();
        // A docked or detached panel / the Mixer get it in their own window; a small device
        // window doesn't have the room, so the main window hosts it.
        Panel layer = source is NotaWindow nw and (MixerWindow or DetailWindow) ? nw.OverlayLayer : PaletteLayer;
        var host = source is MixerWindow or DetailWindow ? source : this;
        if (_palette.Parent is Panel old && !ReferenceEquals(old, layer)) { old.Children.Remove(_palette); old.Children.Remove(_paletteToast); }
        if (_palette.Parent is null) { layer.Children.Add(_palette); layer.Children.Add(_paletteToast); }
        _paletteReturnFocus = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();
        if (!ReferenceEquals(host, source) || source is null) Activate();
        _palette.Open(ctx with { Window = host }, it => PaletteTargets.Check(it, ctx, id => ActionAvailability(id, ctx)),
            id => ActionAvailability(id, ctx));
    }

    private CommandPaletteView CreatePalette()
    {
        var p = new CommandPaletteView(_paletteSearch!, _paletteIndex) { Apply = ApplyPaletteItem };
        p.Closed += () =>
        {
            // Esc / click outside / applied: focus goes back where it was (CP-20).
            var f = _paletteReturnFocus;
            _paletteReturnFocus = null;
            if (f is InputElement el && el.IsAttachedToVisualTree() && el.IsEffectivelyVisible) el.Focus();
        };
        return p;
    }

    private Availability ActionAvailability(string id, PaletteContext ctx)
        => _commands.Get(id) is { } c ? c.CanExecute(ctx) : Availability.No("Not available");

    // ---- context (CP-2, CP-3) ---------------------------------------------------------------

    /// <summary>Where the palette was called from and what it would act on, frozen at open.</summary>
    internal PaletteContext SnapshotContext(Window? source)
    {
        string view = _session?.IsVisible == true ? "Session" : _modular?.IsVisible == true ? "Modular" : "Arrangement";
        var focused = (source ?? this).FocusManager?.GetFocusedElement() as Visual;
        bool Within(Control? c) => c is not null && focused is not null && focused.GetSelfAndVisualAncestors().Contains(c);

        PaletteOrigin origin;
        int track;
        if (source is MixerWindow) { origin = PaletteOrigin.Mixer; track = Timeline.SelectedTrackId; }
        else if (source is DetailWindow dw)
        {
            origin = Within(dw.ClipHost) ? PaletteOrigin.PianoRoll : PaletteOrigin.Devices;
            track = origin == PaletteOrigin.PianoRoll ? EditedTrack() : _deviceChain?.TrackId ?? -1;
        }
        else if (source is DeviceWindow devw) { origin = PaletteOrigin.DeviceWindow; track = devw.ContextTrackId > 0 ? devw.ContextTrackId : _deviceChain?.TrackId ?? -1; }
        else if (DetailPanel.IsVisible && (Within(DetailPanel) || _detailWasLastFocused))
        {
            origin = DetailBody.Content is ClipEditorView or DrumPatternView ? PaletteOrigin.PianoRoll : PaletteOrigin.Devices;
            track = origin == PaletteOrigin.PianoRoll ? EditedTrack() : _deviceChain?.TrackId ?? -1;
        }
        else if (Within(Browser))
        {
            origin = PaletteOrigin.Browser;
            track = SelectedTrackForView(view);
        }
        else
        {
            origin = view switch { "Session" => PaletteOrigin.Session, "Modular" => PaletteOrigin.Modular, _ => PaletteOrigin.Arrangement };
            track = origin == PaletteOrigin.Modular ? _modular?.TrackId ?? -1 : SelectedTrackForView(view);
        }

        var type = PaletteTrackType.None;
        int instKind = -1; string instName = "";
        if (track > 0 && Insertion.IsMaster(track)) type = PaletteTrackType.Master;
        else if (Insertion.TryTrackInfo(track, out var ti))
        {
            type = ti.IsGroup ? PaletteTrackType.Group : ti.IsReturn ? PaletteTrackType.Return : ti.IsInstrument ? PaletteTrackType.Instrument : PaletteTrackType.Audio;
            if (ti.IsInstrument && !ti.IsGroup) { instKind = Engine.TrackInstrumentKind(track); instName = Engine.DeviceName(track, -1); }
        }
        else track = -1;

        string trackName = track <= 0 ? "" : type == PaletteTrackType.Master ? "Master" : Engine.GetTrackName(track);
        var insert = origin == PaletteOrigin.Devices && _deviceChain is { } dc && dc.TrackId == track ? dc.CurrentInsertPoint() : InsertPoint.None;
        return new PaletteContext
        {
            Origin = origin, View = view, TrackId = track, TrackName = trackName, TrackType = type,
            InstrumentKind = instKind, InstrumentName = instName, Insert = insert,
            SelectedClips = Timeline.SelectionCount, HasTimeSelection = Timeline.HasTimeSelection,
            HasOpenMidiClip = _editorRoll is not null && _clipEditor is { IsEffectivelyVisible: true },
            SessionScene = _session?.SelectedSlot.Scene ?? -1,
            SelectedTrackCount = Timeline.SelectedTrackIds().Count,
            Window = source ?? this,
        };
    }

    private int EditedTrack() => _editorTrackId > 0 ? _editorTrackId : _sessionSlotTrack;

    private int SelectedTrackForView(string view)
    {
        if (view == "Session" && _session?.SelectedSlot is { TrackId: > 0 } slot) return slot.TrackId;
        var ids = Timeline.SelectedTrackIds();
        return ids.Count > 0 ? ids[0] : Timeline.SelectedTrackId;
    }

    // ---- apply ---------------------------------------------------------------------------

    private PaletteContext? ApplyPaletteItem(PaletteItem item, ApplyMode mode, PaletteContext ctx)
    {
        if (_vm is null) return null;
        RecordUse(item.Id, fromPalette: true);
        switch (item.Kind)
        {
            case PaletteKind.Action:
                if (item.Payload is string id) _commands.Run(id, ctx, mode, onlyIfAvailable: true);
                return null;

            case PaletteKind.Track when item.Payload is int t:
                if ((mode & ApplyMode.NewTrack) != 0 || Insertion.IsMaster(t)) { ShowDevices(t); _vm.StatusText = $"Opened {item.Name} in Devices"; }
                else
                {
                    Timeline.Select(t, -1);
                    if (_modular?.IsVisible == true) _modular.Show(t);
                    _vm.StatusText = $"Selected {item.Name}";
                }
                return null;

            case PaletteKind.Modular when item.Payload is int kind:
            {
                if (_modular is null || ctx.TrackId <= 0) return null;
                if (_modular.TrackId != ctx.TrackId) _modular.Show(ctx.TrackId);
                int modId = _modular.AddModulatorAt(kind);
                if (modId < 0) { _vm.StatusText = $"Couldn't add {item.Name}."; return null; }
                Timeline.Refresh();
                string msg = $"{item.Name} added to the {ctx.TrackName} canvas — connect it to a parameter";
                _vm.StatusText = msg;
                _paletteToast.Show(msg, () => _commands.Run("edit.undo"));
                return ctx;
            }

            case PaletteKind.Device or PaletteKind.Preset when item.Payload is BrowserItem bi:
            {
                var target = PaletteInsert.TargetFor(ctx, item, mode);
                var r = Insertion.Insert(bi, target);
                // From Modular the canvas stays on screen; elsewhere the chain is revealed, as a drop does.
                ApplyInsertResult(r, revealDevices: ctx.Origin != PaletteOrigin.Modular);
                if (!r.Ok) { _paletteToast.Show(r.Status, null); return ctx; }
                _paletteToast.Show(r.Status, () => _commands.Run("edit.undo"));
                // ⇧Enter: the next device goes after this one (CP-5).
                if (r.Placed is { } p && r.TrackId == ctx.TrackId)
                {
                    string name = p.Midi ? Engine.MidiEffectName(r.TrackId, p.Final) : Engine.DeviceName(r.TrackId, p.Final);
                    return ctx with { Insert = new InsertPoint(p.Midi ? ChainSection.Midi : ChainSection.Audio, p.Final, name) };
                }
                return ctx;
            }
        }
        return null;
    }

    // ---- frecency -----------------------------------------------------------------------

    /// <summary>The palette id of a browser row, so the browser's use counts toward frecency too.</summary>
    private static string PaletteIdOf(BrowserItem item)
        => item.LibraryKey.Length > 0 ? "d:" + item.LibraryKey
         : item.Kind == BrowserItemKind.Preset ? "p:" + item.Path : "";

    private void RecordUse(string id, bool fromPalette)
    {
        if (_paletteUsage is null || _vm is null || id.Length == 0) return;
        _paletteUsage.Record(id, fromPalette);
        _vm.Settings.Save();
    }
}
