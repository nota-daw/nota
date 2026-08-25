// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Marks a control that exists on the mockup but whose feature isn't implemented
// yet (HANDOFF §4). The badge itself is a visible chip with a tooltip; the
// feature control it sits on is left disabled so the layout doesn't shift when
// the feature ships. Two variants: "N/A" (not implemented) and "M7+" (planned
// for a later milestone).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public enum NaBadgeKind { NA, Future }

public sealed class NaBadge : Border
{
    public static readonly StyledProperty<NaBadgeKind> KindProperty =
        AvaloniaProperty.Register<NaBadge, NaBadgeKind>(nameof(Kind));

    public NaBadgeKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private readonly TextBlock _label = new()
    {
        FontSize = 9,
        FontWeight = FontWeight.Bold,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public NaBadge()
    {
        CornerRadius = new CornerRadius(8);      // Radius.Pill
        Padding = new Thickness(6, 1);
        Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
        Child = _label;
        _label.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == KindProperty) Apply();
    }

    private void Apply()
    {
        if (Kind == NaBadgeKind.Future)
        {
            _label.Text = "N/A";
            this.BindResource(BackgroundProperty, "Brush.AccentSubtle");
            _label.BindResource(TextBlock.ForegroundProperty, "Brush.Warning");
            ToolTip.SetTip(this, "Coming in future");
        }
        else
        {
            _label.Text = "N/A";
            this.BindResource(BackgroundProperty, "Brush.SurfaceRaised");
            _label.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
            ToolTip.SetTip(this, "Not implemented yet");
        }
    }
}
