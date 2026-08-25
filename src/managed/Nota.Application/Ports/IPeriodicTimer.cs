// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Application;

/// <summary>A recurring UI-thread timer. Abstracts Avalonia's DispatcherTimer so
/// ViewModels can drive their clocks without a dependency on the UI framework.</summary>
public interface IPeriodicTimer : IDisposable
{
    void Start();
    void Stop();
}

/// <summary>Creates UI-thread timers that fire <paramref name="onTick"/> every
/// <paramref name="interval"/>. Implemented in the App layer over DispatcherTimer.</summary>
public interface IPeriodicTimerFactory
{
    IPeriodicTimer Create(TimeSpan interval, Action onTick);
}
