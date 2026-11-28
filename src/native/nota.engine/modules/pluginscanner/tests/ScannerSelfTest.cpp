// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// nota-scanner-selftest — OutOfProcessScanner against misbehaving workers.
// The binary is its own stub worker: run as `<self> STUB <mode>` it plays a
// plugin that hangs, answers then hangs at exit, prints noise, or dies without
// answering. Run with no arguments it drives OutOfProcessScanner at those stubs
// and checks every probe ends cleanly and on time. Exit code 0 = all passed.

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/OutOfProcessScanner.h"
#include "nota/pluginscanner/wire/ScanWireCodec.h"

#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <string>
#include <system_error>
#include <thread>
#include <vector>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <cerrno>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>
#if defined(__APPLE__)
#include <mach-o/dyld.h>
#endif
#endif

namespace {

using nota::pluginscanner::OutOfProcessScanner;
using Outcome = OutOfProcessScanner::Outcome;

std::string currentExecutablePath(const char* argv0) {
#if defined(_WIN32)
    wchar_t buf[32768];
    const DWORD n = ::GetModuleFileNameW(nullptr, buf, static_cast<DWORD>(std::size(buf)));
    if (n > 0 && n < std::size(buf)) {
        const int utf8Len = ::WideCharToMultiByte(CP_UTF8, 0, buf, static_cast<int>(n), nullptr, 0, nullptr, nullptr);
        if (utf8Len > 0) {
            std::string out(static_cast<size_t>(utf8Len), '\0');
            ::WideCharToMultiByte(CP_UTF8, 0, buf, static_cast<int>(n), out.data(), utf8Len, nullptr, nullptr);
            return out;
        }
    }
#elif defined(__APPLE__)
    uint32_t size = 0;
    (void)::_NSGetExecutablePath(nullptr, &size);
    if (size > 0) {
        std::string buf(size, '\0');
        if (::_NSGetExecutablePath(buf.data(), &size) == 0) {
            std::error_code ec;
            const auto canon = std::filesystem::weakly_canonical(std::filesystem::path(buf.c_str()), ec);
            return ec ? std::string(buf.c_str()) : canon.string();
        }
    }
#else
    std::error_code ec;
    const auto self = std::filesystem::read_symlink("/proc/self/exe", ec);
    if (!ec && !self.empty()) return self.string();
#endif
    if (argv0 != nullptr) {
        std::error_code ec;
        const auto abs = std::filesystem::weakly_canonical(std::filesystem::path(argv0), ec);
        return ec ? std::string(argv0) : abs.string();
    }
    return {};
}

void printAnswer() {
    nota::dsp::PluginDescriptor d;
    d.name = "Stub";
    d.descriptiveName = "Stub";
    d.formatName = "VST3";
    d.format = nota::dsp::PluginFormatKind::Vst3;
    d.fileOrIdentifier = "stub.vst3";
    d.uniqueId = 42;
    std::cout << nota::pluginscanner::encodeScanResults({d}) << std::endl;
}

[[noreturn]] void hang() {
    for (;;) std::this_thread::sleep_for(std::chrono::hours(1));
}

int runStub(const std::string& mode) {
    if (mode == "hang") hang();
    if (mode == "answer-then-hang") {
        printAnswer();
        hang();
    }
    if (mode == "noisy") {
        for (int i = 0; i < 400; ++i) std::cout << "plugin log line " << i << " ........................\n";
        printAnswer();
        return 0;
    }
    if (mode == "die") std::_Exit(7);
    if (mode == "ok") {
        printAnswer();
        return 0;
    }
    return 3;
}

int g_failures = 0;

void check(bool ok, const std::string& label) {
    std::cout << "  [" << (ok ? "PASS" : "FAIL") << "] " << label << "\n";
    if (!ok) ++g_failures;
}

void probe(const OutOfProcessScanner& scanner, const char* mode, Outcome want, size_t wantTypes, int maxMs) {
    const auto t0 = std::chrono::steady_clock::now();
    Outcome got = Outcome::StartFailed;
    const auto descs = scanner.scanDescriptions("STUB", mode, &got);
    const int ms = static_cast<int>(
        std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - t0).count());
    check(got == want && descs.size() == wantTypes && ms <= maxMs,
        std::string(mode) + ": outcome " + std::to_string(static_cast<int>(got)) + " (want " +
            std::to_string(static_cast<int>(want)) + "), " + std::to_string(descs.size()) + " types (want " +
            std::to_string(wantTypes) + "), " + std::to_string(ms) + " ms (max " + std::to_string(maxMs) + ")");
}

} // namespace

int main(int argc, char** argv) {
    if (argc >= 3 && std::strcmp(argv[1], "STUB") == 0) return runStub(argv[2]);

    const std::string self = currentExecutablePath(argc > 0 ? argv[0] : nullptr);
    constexpr int kTimeoutMs = 1500;
    const OutOfProcessScanner scanner(self, kTimeoutMs);
    constexpr int kSlack = 3000;

    std::cout << "-- out-of-process scanner --\n";
    probe(scanner, "ok", Outcome::Ok, 1, kTimeoutMs);
    probe(scanner, "hang", Outcome::Timeout, 0, kTimeoutMs + kSlack);
    probe(scanner, "answer-then-hang", Outcome::Ok, 1, kTimeoutMs);
    probe(scanner, "noisy", Outcome::Ok, 1, kTimeoutMs);
    probe(scanner, "die", Outcome::NoAnswer, 0, kTimeoutMs);

    const OutOfProcessScanner missing("/nonexistent/nota-scanworker", kTimeoutMs);
    Outcome missingOutcome = Outcome::Ok;
    const auto missingDescs = missing.scanDescriptions("STUB", "ok", &missingOutcome);
    check(missingDescs.empty() && (missingOutcome == Outcome::StartFailed || missingOutcome == Outcome::NoAnswer),
        "missing worker: no types, no hang");

    {
        const char* modes[] = {"ok", "die", "hang", "answer-then-hang", "noisy", "die", "ok", "hang"};
        std::vector<std::thread> pool;
        std::vector<std::string> bad(8);
        const auto t0 = std::chrono::steady_clock::now();
        for (int i = 0; i < 8; ++i) {
            pool.emplace_back([&, i] {
                Outcome got = Outcome::StartFailed;
                const auto d = scanner.scanDescriptions("STUB", modes[i], &got);
                const std::string m = modes[i];
                const Outcome want = m == "hang" ? Outcome::Timeout : m == "die" ? Outcome::NoAnswer : Outcome::Ok;
                if (got != want || d.size() != (want == Outcome::Ok ? 1u : 0u)) bad[static_cast<size_t>(i)] = m;
            });
        }
        for (auto& t : pool) t.join();
        const int ms = static_cast<int>(
            std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - t0).count());
        std::string wrong;
        for (const auto& b : bad) {
            if (!b.empty()) wrong += " " + b;
        }
        check(wrong.empty() && ms <= kTimeoutMs + kSlack, "8 parallel probes end right and on time (" +
                                                              std::to_string(ms) + " ms)" +
                                                              (wrong.empty() ? "" : ", wrong:" + wrong));
    }

#if !defined(_WIN32)
    int status = 0;
    const pid_t left = ::waitpid(-1, &status, WNOHANG);
    check(left == -1 && errno == ECHILD, "no unreaped worker processes left");
#endif

    std::cout << (g_failures == 0 ? "SCANNER SELFTEST PASSED" : "SCANNER SELFTEST FAILED") << "\n";
    return g_failures == 0 ? 0 : 1;
}
