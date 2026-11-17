// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Background indexing must never slow the rest of the machine down (#358). On Linux a .NET
// thread priority barely touches the CPU scheduler and not the disk at all, so the sample
// index's threads also ask the kernel for the lowest CPU niceness and the idle I/O class:
// they read the disk only when nothing else wants it. Elsewhere the BelowNormal thread
// priority they already have is what the system offers.

using System;
using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

internal static partial class BackgroundIo
{
    private const int PrioProcess = 0;                 // setpriority: `who` is a thread id on Linux
    private const int IoprioWhoProcess = 1;
    private const int IoprioClassIdle = 3;
    private const int IoprioClassShift = 13;

    /// <summary>Lowers the calling thread's CPU and disk priority (Linux; a no-op elsewhere).</summary>
    public static void Lower()
    {
        if (!OperatingSystem.IsLinux()) return;
        long gettid, ioprioSet;
        switch (RuntimeInformation.ProcessArchitecture)
        {
            case Architecture.X64: gettid = 186; ioprioSet = 251; break;
            case Architecture.Arm64: gettid = 178; ioprioSet = 30; break;
            default: return;
        }
        try
        {
            long tid = Syscall(gettid, 0, 0, 0);
            if (tid <= 0) return;
            SetPriority(PrioProcess, (uint)tid, 19);
            Syscall(ioprioSet, IoprioWhoProcess, tid, IoprioClassIdle << IoprioClassShift);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [LibraryImport("libc", EntryPoint = "syscall")]
    private static partial long Syscall(long number, long a1, long a2, long a3);

    [LibraryImport("libc", EntryPoint = "setpriority")]
    private static partial int SetPriority(int which, uint who, int prio);
}
