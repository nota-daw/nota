// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System;
using Avalonia.Threading;
using Nota.Application;

namespace Nota.App;

/// <summary>IPeriodicTimerFactory realized with Avalonia's DispatcherTimer, so the
/// Presentation ViewModels can own UI-thread clocks without referencing Avalonia.</summary>
public sealed class AvaloniaPeriodicTimerFactory : IPeriodicTimerFactory
{
    public IPeriodicTimer Create(TimeSpan interval, Action onTick)
        => new AvaloniaPeriodicTimer(interval, onTick);

    private sealed class AvaloniaPeriodicTimer : IPeriodicTimer
    {
        private readonly DispatcherTimer _timer;

        public AvaloniaPeriodicTimer(TimeSpan interval, Action onTick)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, _) => onTick();
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose() => _timer.Stop();
    }
}
