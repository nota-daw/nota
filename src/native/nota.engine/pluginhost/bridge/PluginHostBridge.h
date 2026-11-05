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

namespace nota {

class Instrument;
class Device;

// Instantiate catalog entry `catalogIndex` as a hosted instrument. Returns null
// if the index is invalid, the plugin is not an instrument, or instantiation
// fails.
std::shared_ptr<Instrument> createPluginInstrument(int32_t catalogIndex, double sampleRate, int32_t maxBlock);

// Instantiate catalog entry `catalogIndex` as a hosted insert effect. Returns
// null on failure.
std::shared_ptr<Device> createPluginEffect(int32_t catalogIndex, double sampleRate, int32_t maxBlock);

// The audio device's IO-thread workgroup (an os_workgroup_t on macOS 11+, null for none),
// handed to hosted plug-ins that run real-time threads of their own: they join it so the OS
// schedules those threads against the device's deadline. Set after the device starts, cleared
// before it stops. Any thread; a no-op off macOS.
void setPluginAudioWorkgroup(void* osWorkgroup);

} // namespace nota
