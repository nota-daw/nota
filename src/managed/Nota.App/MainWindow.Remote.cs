// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Remote in the window: the top-bar Remote button (off / the number of phones, a dot that
// flashes on every touch, like the MIDI dot), the Connect Phone popup it opens, and the
// window's side of the hub — it is the IRemoteHost, and it ticks the hub on the UI clock.
// A phone is one more controller: transport through the transport view-model, mapped controls
// through MIDI Learn, notes into the engine on the socket thread (see RemoteHub).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Remote;

namespace Nota.App;

public partial class MainWindow : IRemoteHost
{
    private RemoteService? _remote;
    private int _remoteSeenActivity;
    private DateTime _remoteLastActivity;

    private void InitRemote()
    {
        if (_vm is null) return;
        _remote = App.Services.GetRequiredService<RemoteService>();
        _remote.Hub.Host = this;
        _remote.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(SyncRemoteButton);
        Timeline.PlayerBadge = id => _remote?.Hub.PlayersOf(id) is { Count: > 0 } p ? string.Join(", ", p) : null;
        _vm.PlayheadUpdated += _ =>
        {
            _remote?.Hub.Tick();
            SyncRemoteDot();
        };
        // Learn armed / a control picked: the phones show the banner and dashed outlines.
        SyncRemoteButton();
        _ = _remote.ApplyAsync();
    }

    private void OnRemoteClick(object? sender, RoutedEventArgs e) => ShowConnectPhone();

    private void OnMenuConnectPhone(object? sender, EventArgs e) => ShowConnectPhone();

    private void ShowConnectPhone()
    {
        if (_remote is null || _vm is null) return;
        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new ConnectPhoneView(_remote, _vm.Settings, Engine),
            FlyoutPresenterClasses = { "bare" },
        };
        flyout.ShowAt(RemoteBtn);
    }

    private void SyncRemoteButton()
    {
        if (_remote is null || _vm is null) return;
        bool on = _vm.Settings.Current.RemoteEnabled && _remote.Running;
        int n = _remote.Hub.Devices.Count;
        RemoteCount.Text = on ? n.ToString() : "Off";
        RemoteBtn.Classes.Set("lit", on);
        ToolTip.SetTip(RemoteBtn, !_vm.Settings.Current.RemoteEnabled ? "Play and control Nota from a phone"
            : _remote.Error ?? (n == 0 ? "Remote is on · scan the code with a phone" : $"{n} phone{(n == 1 ? "" : "s")} connected"));
        Timeline.Refresh();
    }

    private void SyncRemoteDot()
    {
        if (_remote is null) return;
        int a = _remote.Hub.ActivityCount;
        if (a != _remoteSeenActivity) { _remoteSeenActivity = a; _remoteLastActivity = DateTime.UtcNow; }
        bool connected = _remote.Running && _remote.Hub.Devices.Count > 0;
        bool lit = connected && (DateTime.UtcNow - _remoteLastActivity).TotalMilliseconds < 150;
        var want = lit ? NotaPalette.AccentBright : connected ? NotaPalette.Success : NotaPalette.BorderStrong;
        if (!ReferenceEquals(RemoteDot.Fill, want)) RemoteDot.Fill = want;
    }

    private void ShutdownRemote()
    {
        if (_remote is null) return;
        _remote.Hub.Host = null;
        _remote.ShutdownAsync().Wait(TimeSpan.FromSeconds(2));
    }

    // ---- IRemoteHost --------------------------------------------------------------

    string IRemoteHost.HostName => _remote?.Hub.HostName ?? Environment.MachineName;

    // The phone wears the theme the window wears, or under Follow phone one of the user's two
    // picks (spec 014). Built once per theme and once per change, so the hub's reference check
    // costs nothing on the ticks in between.
    private readonly Dictionary<string, RemoteTheme> _remoteThemes = new();
    private RemoteAppearance? _remoteLook;
    private (NotaColourTheme, AppearanceChoice)? _remoteLookFor;

    RemoteAppearance IRemoteHost.Appearance
    {
        get
        {
            var key = (NotaPalette.Theme ?? NotaThemes.Default, NotaThemeService.Choice);
            if (_remoteLook is null || !key.Equals(_remoteLookFor))
            {
                RemoteTheme Of(NotaColourTheme? t) => RemoteThemeOf(t ?? NotaThemes.Default);
                _remoteLook = new RemoteAppearance(Of(key.Item1), Of(NotaThemes.Find(key.Item2.Dark)), Of(NotaThemes.Find(key.Item2.Light)),
                    CssHex(NotaPalette.InkDark), CssHex(NotaPalette.InkLight));
                _remoteLookFor = key;
            }
            return _remoteLook;
        }
    }

    private RemoteTheme RemoteThemeOf(NotaColourTheme t)
    {
        if (!_remoteThemes.TryGetValue(t.Id, out var r))
            _remoteThemes[t.Id] = r = new RemoteTheme(t.Id, t.Name, t.IsDark, Enumerable.ToDictionary(t.Colors, c => c.Key, c => CssHex(c.Value)));
        return r;
    }

    // CSS order: #RRGGBB, or #RRGGBBAA when translucent (Avalonia writes #AARRGGBB).
    private static string CssHex(Avalonia.Media.Color c) =>
        c.A == 0xFF ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

    string IRemoteHost.TrackColor(int trackId)
    {
        var c = ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(Engine, trackId)).Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    int IRemoteHost.SelectedTrackId => Timeline.SelectedTrackId;
    int IRemoteHost.ProjectKeyCode => _vm?.Transport.KeyCode ?? -1;

    IReadOnlyList<(string Name, double StartBeat)> IRemoteHost.Sections =>
        Timeline.SectionList.Select((s, i) => (SectionNames.Display(s.Name, i), s.StartBeat)).ToList();

    bool IRemoteHost.Recording => _vm?.Transport.RecordOn ?? false;
    bool IRemoteHost.LoopOn => _vm?.Transport.LoopOn ?? false;
    bool IRemoteHost.MetronomeOn => _vm?.Transport.MetronomeOn ?? false;

    void IRemoteHost.Play() { if (_vm is { } vm && !Engine.IsPlaying) vm.Transport.PlayCommand.Execute(null); }

    // Stop as the transport bar does: a take ends first; pressed when stopped, back to the start.
    void IRemoteHost.Stop() => _vm?.Transport.StopCommand.Execute(null);

    void IRemoteHost.SetRecord(bool on) { if (_vm is { } vm) vm.Transport.RecordOn = on; }
    void IRemoteHost.SetLoop(bool on) { if (_vm is { } vm) vm.Transport.LoopOn = on; }

    void IRemoteHost.SetLoopRange(double startBeat, double endBeat)
    {
        Engine.SetLoop(Engine.LoopEnabled, startBeat, endBeat);
        _vm?.Transport.SyncLoop();
        Timeline.Refresh(rebuildHeaders: false);
    }

    void IRemoteHost.SetMetronome(bool on) { if (_vm is { } vm) vm.Transport.MetronomeOn = on; }
    void IRemoteHost.SetBpm(double bpm) { if (_vm is { } vm) vm.Transport.Bpm = (decimal)bpm; }

    double IRemoteHost.MasterVolume
    {
        get => _vm?.Transport.MasterVolume ?? 1.0;
        set { if (_vm is { } vm) vm.Transport.MasterVolume = value; }
    }

    bool IRemoteHost.Undo()
    {
        _editorRoll?.EndVelocityWheel();
        if (!Engine.Undo()) return false;
        RefreshAfterUndoRedo();
        if (_vm is not null) _vm.StatusText = "Undo (from a phone)";
        return true;
    }

    void IRemoteHost.Refresh()
    {
        Timeline.Refresh();
        _session?.Refresh();
        if (_mixer?.IsVisible == true) _mixer.Refresh();
        if (DetailBody.Content is MixerView mx && mx.IsEffectivelyVisible) mx.Refresh();
        if (_deviceChain is { } dc && dc.TrackId > 0) dc.Refresh();
    }

    bool IRemoteHost.LearnArmed => _learn?.Armed ?? false;
    string? IRemoteHost.LearnPendingName => _learn?.Pending?.Name;

    PhoneControlResult IRemoteHost.PhoneControl(int controlId, int trackId, double norm)
    {
        if (_learn is null) return PhoneControlResult.None;
        var r = _learn.HandlePhoneControl(controlId, trackId, norm);
        if (r == PhoneControlResult.Bound) { LearnOverlay.Refresh(); Browser.ShowMidiMap(); }
        return r;
    }

    string? IRemoteHost.PhoneMappingName(int controlId, int trackId) => _learn?.PhoneMappingFor(controlId, trackId)?.DisplayName;

    void IRemoteHost.DevicesChanged() => _remote?.RaiseChanged();

    void IRemoteHost.Activity() { }
}
