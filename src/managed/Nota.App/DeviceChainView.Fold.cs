// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — folding a card into a narrow strip (specs/022), so a long chain fits
// the panel. A double-click on a card's header (or "Fold" in its menu) folds it; the strip
// keeps the bypass switch on top and the name, rotated to read top to bottom, and still
// selects, opens the device menu and drags like the card. A double-click on it unfolds.
// The fold lives on the engine's device (beside bypass), so it follows moves and undo, and
// it is saved with the project. A folded card's body is not built.

using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    // The strip's width: the "+" key's, just room for the bypass switch and a rotated name.
    internal const double FoldedW = 28;

    private bool IsFolded(ChainKind k, int di)
        => k == ChainKind.Midi ? _engine.MidiEffectFolded(_trackId, di) : _engine.DeviceFolded(_trackId, di);

    private void SetFolded(ChainKind k, int di, bool folded)
    {
        if (k == ChainKind.Midi) _engine.SetMidiEffectFolded(_trackId, di, folded);
        else _engine.SetDeviceFolded(_trackId, di, folded);
        Rebuild();
    }

    // True when the pointer event started on `control` or inside it (a header control the
    // fold double-click must leave alone).
    private static bool Within(object? source, Control? control)
        => control is not null && source is Visual v && (ReferenceEquals(v, control) || v.GetVisualAncestors().Contains<Visual>(control));

    private Control FoldedStrip(ChainKind k, int di, string name, bool bypassable, bool bypassed)
    {
        bool movable = k != ChainKind.Instrument;   // the instrument is the track's, not a chain slot
        var power = new Border { Height = SwitchTrack.H, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 6) };
        if (bypassable)
        {
            var bypass = new SwitchTrack { IsOn = !bypassed, Cursor = new Cursor(StandardCursorType.Hand) };
            ToolTip.SetTip(bypass, bypassed ? "Bypassed — click to enable" : "Active — click to bypass");
            bypass.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(bypass).Properties.IsLeftButtonPressed) return;
                e.Handled = true;   // not a drag, not a selection click, not a fold toggle
                if (k == ChainKind.Midi) _engine.SetMidiEffectBypassed(_trackId, di, !bypassed);
                else _engine.SetDeviceBypassed(_trackId, di, !bypassed);
                Rebuild(); Changed?.Invoke();
            };
            power.Child = bypass;
        }
        var label = new TextBlock
        {
            Text = name, FontSize = NotaType.DeviceName, FontWeight = FontWeight.SemiBold,
            Foreground = bypassed ? TextTertiary : TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Rotated a quarter turn clockwise, so the name reads top to bottom down the strip.
        var rotated = new LayoutTransformControl
        {
            LayoutTransform = new RotateTransform(90), Child = label,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        };
        DockPanel.SetDock(power, Dock.Top);
        var strip = new Border
        {
            Background = ShellHdr, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Body, ClipToBounds = true,
            Padding = new Thickness(0, 0, 0, 8),
            Child = new DockPanel { LastChildFill = true, Children = { power, rotated } },
            Cursor = movable ? new Cursor(StandardCursorType.SizeAll) : null,
        };
        strip.BindResource(Border.BoxShadowProperty, "Shadow.Raised");
        ToolTip.SetTip(strip, $"{name} — double-click to unfold");

        const double RingGap = 3;   // the same focus ring as the full card (BuildCardShell)
        var selRing = new Border
        {
            BorderBrush = Brass, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(NotaRadius.BodyValue + RingGap), Margin = new Thickness(-RingGap),
            IsHitTestVisible = false, IsVisible = IsSelected(k, di),
        };
        var card = new Border { Width = FoldedW, Height = CardH, Child = new Panel { Children = { strip, selRing } } };

        void Menu(MenuFlyout flyout)
        {
            flyout.Items.Add(MenuKit.Item("Unfold", null, () => SetFolded(k, di, false)));
            flyout.Items.Add(new Separator());
        }
        strip.PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(strip).Properties;
            if (pt.IsRightButtonPressed) { ShowDeviceContextMenu(strip, k, di, Menu); return; }
            if (!pt.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { e.Handled = true; SetFolded(k, di, false); return; }
            SelectDevice(k, di);
        };
        if (movable) HookDeviceDrag(strip, card, k, di);
        return card;
    }
}
