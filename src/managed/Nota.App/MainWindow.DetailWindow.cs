// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Pop the Devices/Clip detail panel out into its own window (piano roll on top, device
// chain on the bottom — no tabs). While floating, the detail-panel routing sends the clip
// editor and device chain to the window's two hosts instead of the docked DetailBody; the
// docked panel is hidden and the tick loop keeps updating whichever control is visible
// (keyed off IsEffectivelyVisible). Closing the window re-docks.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public partial class MainWindow
{
    private DetailWindow? _detailWindow;
    private Control? _lastClipEditor;   // most recently shown clip editor (MIDI or audio)
    private bool _shuttingDown;

    private bool DetailFloating => _detailWindow is not null;
    // Where the two detail contents go: separate hosts while floating, the shared tabbed
    // DetailBody while docked.
    private ContentControl DeviceHost => _detailWindow?.ChainHost ?? DetailBody;
    private ContentControl ClipDetailHost => _detailWindow?.ClipHost ?? DetailBody;

    // Assign a control to a ContentControl host, detaching it from any previous host first
    // (a control can have only one parent).
    private static void SetHost(ContentControl host, Control? content)
    {
        if (content?.Parent is ContentControl prev && !ReferenceEquals(prev, host)) prev.Content = null;
        if (!ReferenceEquals(host.Content, content)) host.Content = content;
    }

    private void OnToggleDetailWindow(object? sender, RoutedEventArgs e)
    {
        if (_detailWindow is not null) { _detailWindow.Close(); return; }   // → DockDetail

        var win = new DetailWindow(this);
        win.DockRequested += DockDetail;
        win.EnableMidiLearn(_learn);   // so the popped-out chain/clip editor can be mapped
        _detailWindow = win;

        // Move current content across: device chain → bottom pane, current clip editor → top.
        if (_deviceChain is not null) SetHost(win.ChainHost, _deviceChain);
        Control? clip = DetailBody.Content is ClipEditorView or AudioClipEditorView
            ? (Control)DetailBody.Content! : _lastClipEditor;
        if (clip is not null) SetHost(win.ClipHost, clip);
        else win.ClipHost.Content = ClipPlaceholder();

        // Hide the docked panel — state is preserved, it just lives in the window now.
        DetailPanel.IsVisible = false;
        DetailSplitter.IsVisible = false;
        var row = BodyGrid.RowDefinitions[2];
        row.MinHeight = 0;
        row.Height = new GridLength(0);

        win.Show(this);
    }

    private void DockDetail()
    {
        _detailWindow = null;
        if (_shuttingDown) return;
        // Re-open docked on the Devices tab; the clip editor stays reachable via the Clip tab.
        int t = Timeline.SelectedTrackId > 0 ? Timeline.SelectedTrackId : _lastInstrumentTrackId;
        if (t > 0) ShowDevices(t);
        SyncClipTab();
    }

    // Top-pane hint shown when the window opens with no clip selected.
    private Control ClipPlaceholder() => new Border
    {
        Child = new TextBlock
        {
            Text = "Select a clip in the arrangement to edit it here",
            FontSize = 12,
            Foreground = Avalonia.Application.Current?.FindResource("Brush.TextTertiary") as IBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    // Called from OnMainWindowClosing: drop the floating window without re-docking.
    private void CloseFloatingDetail()
    {
        _shuttingDown = true;
        _detailWindow?.Close();
    }
}
