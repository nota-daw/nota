// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Fresh built-in instances by kind (defined in Engine_Devices.cpp): the engine uses them to
// add / swap / clone devices and read factory defaults; the preset audition rig builds its
// standalone chain from them. nullptr = racks, plug-ins or an unknown kind.

#pragma once

#include <cstdint>
#include <memory>

namespace nota {

class Device;
class Instrument;
class MidiDevice;

std::shared_ptr<Device>     makeBuiltinDevice(int32_t kind);       // audio effects (builtinKind)
std::shared_ptr<Instrument> makeBuiltinInstrument(int32_t kind);   // instruments (kind), not racks
std::shared_ptr<MidiDevice> makeMidiDevice(int32_t kind);          // MIDI effects (midiKind)

} // namespace nota
