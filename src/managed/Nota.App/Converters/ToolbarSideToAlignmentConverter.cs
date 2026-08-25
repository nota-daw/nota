// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Nota.Application;

namespace Nota.App;

/// <summary>Maps the domain-level <see cref="ToolbarSide"/> (exposed by the VM) to
/// an Avalonia <see cref="HorizontalAlignment"/> for the transport bar, keeping the
/// ViewModel free of Avalonia layout types.</summary>
public sealed class ToolbarSideToAlignmentConverter : IValueConverter
{
    public static readonly ToolbarSideToAlignmentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ToolbarSide.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is HorizontalAlignment.Right ? ToolbarSide.Right : ToolbarSide.Left;
}
