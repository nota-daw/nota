// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Owns the app-wide interface scale (spec 027, #38). Avalonia has no global scale, so every
// window's root sits in a LayoutTransformControl: children measure and arrange in logical
// units (frame ÷ factor) and draw through the transform, so text stays vector-sharp and hits
// land where things are drawn. Popups (menus, tooltips, flyouts, dropdowns) open in their own
// PopupRoot; Popup.InheritsTransform (set by an app-wide style) makes each one copy its
// target's scale.
//
// What lives outside the transform is scaled by hand:
//   • window sizes — a window's Width/Height/MinWidth/MinHeight are set in logical (design)
//     units by its constructor; on the first show they are multiplied by the factor and
//     capped at the screen. Later changes keep each open window's frame and scale only its
//     minimum, so contents reflow instead of the window jumping — except a window the user
//     can't resize, whose frame follows the scale so its content never clips;
//   • the extended title bar's drag height (ExtendClientAreaTitleBarHeightHint);
//   • insets for the OS caption buttons (traffic lights / min-max-close), which the OS draws
//     at system size: those stay fixed on screen, so in logical units they shrink as the
//     factor grows — see Track.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Nota.Application;

namespace Nota.App;

internal static class NotaUiScale
{
    private sealed class Entry
    {
        public required LayoutTransformControl Root;
        public double TitleBar;          // design height of the extended title bar; 0 = none
        public Size DesignMin;           // captured on the first show
        public Size DesignSize;          // likewise (NaN = unset)
        public bool Opened;
    }

    private static readonly ConditionalWeakTable<Window, Entry> Windows = new();
    private static int _percent = InterfaceScale.Default;
    private static bool _popups;

    /// <summary>The scale in percent (one of <see cref="InterfaceScale.Presets"/>).</summary>
    public static int Percent => _percent;

    /// <summary>The layout factor (1.25 at 125 %).</summary>
    public static double Factor => _percent / 100.0;

    /// <summary>Raised after the scale changed and every window was re-applied.</summary>
    public static event Action? Changed;

    /// <summary>Make every popup take its target's scale. Once, before any window opens.</summary>
    /// <remarks>A style, not a default override (Popup owns that metadata, so it can't be
    /// overridden): ToolTip, ContextMenu and the flyouts create their Popup in code, but each
    /// gives it a logical parent (SetPopupParent) before opening it, which styles it.</remarks>
    public static void InitPopups()
    {
        if (_popups || Avalonia.Application.Current is not { } app) return;
        _popups = true;
        app.Styles.Add(new Style(x => x.OfType<Popup>())
        {
            Setters = { new Setter(Popup.InheritsTransformProperty, true) },
        });
    }

    /// <summary>Wear <paramref name="percent"/> (normalised) in every window, live.</summary>
    public static void Set(int percent)
    {
        int p = InterfaceScale.Normalize(percent);
        if (p == _percent) return;
        _percent = p;
        foreach (var (window, entry) in Windows) Apply(window, entry);
        Changed?.Invoke();
    }

    /// <summary>Put <paramref name="root"/> under the scale for <paramref name="window"/> and
    /// return what to use as the window's content. <paramref name="titleBar"/> is the design
    /// height of the extended title bar (0 when the window keeps the native frame).</summary>
    public static Control Wrap(Window window, Control root, double titleBar)
    {
        var entry = new Entry { Root = new LayoutTransformControl { Child = root }, TitleBar = titleBar };
        Windows.AddOrUpdate(window, entry);
        // Raised at the very start of Window.Show, before the frame size is read — the
        // constructor's design sizes are final by then, and nothing has been drawn yet.
        window.AddHandler(Window.WindowOpenedEvent, (_, _) => OnOpening(window, entry));
        window.Closed += (_, _) => Windows.Remove(window);   // a closed window's platform is gone
        SetTransform(entry);
        SetTitleBar(window, entry);
        return entry.Root;
    }

    /// <summary>Run <paramref name="apply"/> with the factor now and on every change while
    /// <paramref name="control"/> is in a window — for values that must stay fixed on screen,
    /// like the room left for the OS caption buttons (logical = screen px ÷ factor).</summary>
    public static void Track(Control control, Action<double> apply)
    {
        void OnChanged() => apply(Factor);
        apply(Factor);
        control.AttachedToVisualTree += (_, _) => { Changed -= OnChanged; Changed += OnChanged; apply(Factor); };
        control.DetachedFromVisualTree += (_, _) => Changed -= OnChanged;
    }

    private static void OnOpening(Window window, Entry entry)
    {
        double s = Factor;
        if (!entry.Opened)
        {
            entry.Opened = true;
            entry.DesignMin = new Size(window.MinWidth, window.MinHeight);
            entry.DesignSize = new Size(window.Width, window.Height);
            var area = WorkingArea(window);
            if (!window.SizeToContent.HasFlag(SizeToContent.Width) && !double.IsNaN(window.Width))
                window.Width = Math.Min(window.Width * s, area.Width);
            if (!window.SizeToContent.HasFlag(SizeToContent.Height) && !double.IsNaN(window.Height))
                window.Height = Math.Min(window.Height * s, area.Height);
        }
        SetMinimum(window, entry);
    }

    private static void Apply(Window window, Entry entry)
    {
        SetTransform(entry);
        SetTitleBar(window, entry);
        if (!entry.Opened) return;
        SetMinimum(window, entry);
        if (!window.CanResize) SetFixedSize(window, entry);
        // A frame now below its minimum grows (Avalonia clamps the platform size to the new
        // minimum); keep it on its screen.
        if (window.IsVisible) KeepOnScreen(window);
    }

    private static void SetTransform(Entry entry)
    {
        double s = Factor;
        entry.Root.LayoutTransform = new ScaleTransform(s, s);
    }

    private static void SetTitleBar(Window window, Entry entry)
    {
        if (entry.TitleBar > 0 && window.ExtendClientAreaToDecorationsHint)
            window.ExtendClientAreaTitleBarHeightHint = entry.TitleBar * Factor;
    }

    // A dialog the user can't resize would clip its content once the scale grows under it:
    // its frame follows the scale instead (the one exception to "open windows keep their
    // size"). Axes sized to content follow it on their own.
    private static void SetFixedSize(Window window, Entry entry)
    {
        double s = Factor;
        var area = WorkingArea(window);
        if (!window.SizeToContent.HasFlag(SizeToContent.Width) && !double.IsNaN(entry.DesignSize.Width))
            window.Width = Math.Min(entry.DesignSize.Width * s, area.Width);
        if (!window.SizeToContent.HasFlag(SizeToContent.Height) && !double.IsNaN(entry.DesignSize.Height))
            window.Height = Math.Min(entry.DesignSize.Height * s, area.Height);
    }

    private static void SetMinimum(Window window, Entry entry)
    {
        double s = Factor;
        var area = WorkingArea(window);
        window.MinWidth = Math.Min(entry.DesignMin.Width * s, area.Width);
        window.MinHeight = Math.Min(entry.DesignMin.Height * s, area.Height);
        // A maximised or fullscreen frame is the OS's; leave it. (NaN sizes compare false, so
        // an unset size is left alone.)
        if (window.IsVisible && window.WindowState != WindowState.Normal) return;
        if (!window.SizeToContent.HasFlag(SizeToContent.Width) && window.Width < window.MinWidth) window.Width = window.MinWidth;
        if (!window.SizeToContent.HasFlag(SizeToContent.Height) && window.Height < window.MinHeight) window.Height = window.MinHeight;
    }

    // The screen's working area in window units (DIPs); unbounded when there is no screen
    // (headless).
    private static Size WorkingArea(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        return screen is null
            ? new Size(double.PositiveInfinity, double.PositiveInfinity)
            : new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling);
    }

    private static void KeepOnScreen(Window window)
    {
        if (window.WindowState != WindowState.Normal) return;
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return;
        var wa = screen.WorkingArea;
        int w = (int)Math.Ceiling(window.Width * screen.Scaling), h = (int)Math.Ceiling(window.Height * screen.Scaling);
        int x = Math.Clamp(window.Position.X, wa.X, Math.Max(wa.X, wa.Right - w));
        int y = Math.Clamp(window.Position.Y, wa.Y, Math.Max(wa.Y, wa.Bottom - h));
        if (x != window.Position.X || y != window.Position.Y) window.Position = new PixelPoint(x, y);
    }
}
