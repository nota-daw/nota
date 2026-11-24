// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Narrow C++ bridge between the JUCE-free core engine and the JUCE-based
// pluginhost module (ARCHITECTURE.md § Plugin hosting). No JUCE types cross this line:
// the factories instantiate a catalog plugin (in-process) and return core
// Instrument/Device objects the engine can drop into a track. Implemented in
// pluginhost/PluginHost.mm; called from Engine.cpp.
//
// Must be called on the main/message thread (JUCE plugin instantiation).

#pragma once

#include <cstdint>
#include <memory>
#include <string>

namespace nota {

class Instrument;
class Device;

// Catalog entry `catalogIndex` as a hosted instrument, returned at once and still loading
// (#979, specs/113: pluginLoadState() tells how far it got). Null if the index is invalid or
// the plugin is not an instrument.
std::shared_ptr<Instrument> createPluginInstrument(int32_t catalogIndex, double sampleRate, int32_t maxBlock);

// Catalog entry `catalogIndex` as a hosted insert effect, returned at once and still loading.
// Null if the index is invalid.
std::shared_ptr<Device> createPluginEffect(int32_t catalogIndex, double sampleRate, int32_t maxBlock);

// Attaches plug-ins whose binary has loaded and marks overdue loads "not responding". `changed`
// counts devices whose load state moved, `attached` those that now play.
struct PluginLoadsServiced {
    int32_t changed = 0;
    int32_t attached = 0;
};
PluginLoadsServiced servicePluginLoads();

// Hosted plug-ins still loading (neither loaded nor failed).
int32_t pendingPluginLoads();

// Project open: inside the scope a plug-in that froze Nota last time isn't loaded.
void setPluginAutoloadScope(bool on);

// The plug-in that was loading when Nota last ended (it froze Nota), once; else empty.
std::string takeFrozenPluginNotice();

// The audio device's IO-thread workgroup (an os_workgroup_t on macOS 11+, null for none),
// handed to hosted plug-ins that run real-time threads of their own: they join it so the OS
// schedules those threads against the device's deadline. Set after the device starts, cleared
// before it stops. Any thread; a no-op off macOS.
void setPluginAudioWorkgroup(void* osWorkgroup);

} // namespace nota
