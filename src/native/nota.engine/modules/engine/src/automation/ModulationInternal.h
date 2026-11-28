// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ModulationInternal — shared CV modulation target resolution and accessors.

#pragma once

#include "automation/Modulation.h"
#include "model/ParamResolve.h"
#include "model/Track.h"

#include <cstddef>
#include <cstdint>

namespace nota::detail {

inline ParamOwner targetOwner(Track& t, int32_t kind, int32_t dev, uint64_t uid, int32_t slot) {
    if (uid != 0) return resolveNested(t, dev, uid, slot);
    ParamOwner o;
    if (kind == static_cast<int32_t>(CvTargetKind::Instrument)) o.instrument = t.instrument.get();
    else if (kind == static_cast<int32_t>(CvTargetKind::MidiFx)) {
        if (dev >= 0 && dev < static_cast<int32_t>(t.midiEffects.size()))
            o.midi = t.midiEffects[static_cast<size_t>(dev)].get();
    } else if (dev >= 0 && dev < static_cast<int32_t>(t.devices.size()))
        o.device = t.devices[static_cast<size_t>(dev)].get();
    return o;
}

inline bool targetValid(Track& t, int32_t kind, int32_t dev, int32_t param, uint64_t uid = 0, int32_t slot = -1) {
    const ParamOwner o = targetOwner(t, kind, dev, uid, slot);
    if (param < 0) return false;
    if (kind == static_cast<int32_t>(CvTargetKind::Instrument))
        return o.instrument && param < o.instrument->pluginParamCount();
    if (kind == static_cast<int32_t>(CvTargetKind::MidiFx)) return o.midi && param < o.midi->paramCount();
    return o.device && param < o.device->paramCount() && !o.device->isLatencyConfigurationParam(param);
}

inline bool targetValidOrPending(
    Track& t, int32_t kind, int32_t dev, int32_t param, uint64_t uid = 0, int32_t slot = -1) {
    if (targetValid(t, kind, dev, param, uid, slot)) return true;
    if (param < 0 || kind != static_cast<int32_t>(CvTargetKind::Instrument)) return false;
    const ParamOwner o = targetOwner(t, kind, dev, uid, slot);
    return o.instrument && o.instrument->pluginLoadState() != 0;
}

inline float targetGet(
    Track& t, int32_t kind, int32_t dev, int32_t param, float& mn, float& mx, uint64_t uid = 0, int32_t slot = -1) {
    mn = 0.0f;
    mx = 1.0f;
    const ParamOwner o = targetOwner(t, kind, dev, uid, slot);
    if (kind == static_cast<int32_t>(CvTargetKind::Instrument))
        return o.instrument ? o.instrument->pluginParamGet(param) : 0.0f;
    if (kind == static_cast<int32_t>(CvTargetKind::MidiFx)) {
        if (!o.midi) return 0.0f;
        mn = o.midi->paramMin(param);
        mx = o.midi->paramMax(param);
        return o.midi->getParam(param);
    }
    if (!o.device) return 0.0f;
    mn = o.device->paramMin(param);
    mx = o.device->paramMax(param);
    return o.device->getParam(param);
}

inline void targetSet(
    Track& t, int32_t kind, int32_t dev, int32_t param, float v, uint64_t uid = 0, int32_t slot = -1) {
    const ParamOwner o = targetOwner(t, kind, dev, uid, slot);
    if (kind == static_cast<int32_t>(CvTargetKind::Instrument)) {
        if (o.instrument) o.instrument->pluginParamSet(param, v);
    } else if (kind == static_cast<int32_t>(CvTargetKind::MidiFx)) {
        if (o.midi) o.midi->setParam(param, v);
    } else if (o.device)
        o.device->setLiveParam(param, v);
}

} // namespace nota::detail
