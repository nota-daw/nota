// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Idle-engine CPU check for the CI regression gate (specs/034-engine-perf-gate).
// Creates an engine, leaves it idle (transport stopped, nothing rendering) and measures
// process CPU time over a few seconds. An idle engine must cost next to nothing: a
// worker pool that spins between jobs (#223) shows up here as whole cores.
//
//   nota_idle_cpu [limit_percent_of_one_core=5] [seconds=5]
//   exit 0 = within the limit, 1 = over it

#include "nota/nota_engine.h"

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <thread>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
static double processCpuSeconds() {
    FILETIME created, exited, kernel, user;
    GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user);
    auto secs = [](FILETIME f) { return (double)(((unsigned long long)f.dwHighDateTime << 32) | f.dwLowDateTime) * 1e-7; };
    return secs(kernel) + secs(user);
}
#else
#include <sys/resource.h>
static double processCpuSeconds() {
    rusage u{};
    getrusage(RUSAGE_SELF, &u);
    return u.ru_utime.tv_sec + u.ru_utime.tv_usec * 1e-6 + u.ru_stime.tv_sec + u.ru_stime.tv_usec * 1e-6;
}
#endif

int main(int argc, char** argv) {
    const double limit = argc > 1 ? std::atof(argv[1]) : 5.0;
    const double seconds = argc > 2 ? std::atof(argv[2]) : 5.0;

    NotaEngine* e = nota_engine_create();
    std::this_thread::sleep_for(std::chrono::seconds(1)); // let startup work settle

    const double c0 = processCpuSeconds();
    const auto t0 = std::chrono::steady_clock::now();
    std::this_thread::sleep_for(std::chrono::duration<double>(seconds));
    const double cpu = processCpuSeconds() - c0;
    const double wall = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    nota_engine_destroy(e);

    const double pct = 100.0 * cpu / wall;
    const bool ok = pct <= limit;
    std::printf("idle engine CPU: %.1f%% of one core over %.1f s (limit %.1f%%): %s\n", pct, wall, limit,
                ok ? "PASS" : "FAIL");
    return ok ? 0 : 1;
}
