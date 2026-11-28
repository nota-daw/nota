// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"
#include "nota/io/midi/IMidiDriver.h"
#include "nota/io/midi/IMidiOutput.h"
#include "nota/io/midi/IMidiInput.h"
#include "nota/io/midi/MidiTypes.h"

#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <string_view>
#include <vector>

namespace nota::io::midi {

/// Thread-safe registry of MIDI drivers and MIDI configuration storage.
class NOTA_IO_API MidiDriverRegistry {
  public:
    static MidiDriverRegistry& instance();

    void registerDriver(std::shared_ptr<const IMidiDriver> driver);
    std::shared_ptr<const IMidiDriver> find(std::string_view id) const;
    std::shared_ptr<const IMidiDriver> defaultDriver() const;
    std::vector<std::shared_ptr<const IMidiDriver>> list() const;

  private:
    MidiDriverRegistry() = default;
    mutable std::mutex mutex_;
    std::vector<std::shared_ptr<const IMidiDriver>> drivers_;
};

class NOTA_IO_API MidiConfigStore {
  public:
    static MidiConfig load();
    static void save(const MidiConfig& cfg);
};

NOTA_IO_API void registerBuiltinMidiDrivers();

[[nodiscard]] NOTA_IO_API std::unique_ptr<IMidiInput> createMidiInput();
[[nodiscard]] NOTA_IO_API std::unique_ptr<IMidiOutput> createMidiOutput();

NOTA_IO_API std::vector<MidiDeviceInfo> enumerateMidiInputs();
NOTA_IO_API std::string midiUidForEndpoint(uint32_t endpoint);
NOTA_IO_API std::string midiEndpointName(uint32_t endpoint);
NOTA_IO_API uint32_t midiPortsGeneration();

NOTA_IO_API MidiConfig loadMidiConfig();
NOTA_IO_API void saveMidiConfig(const MidiConfig& cfg);

} // namespace nota::io::midi
