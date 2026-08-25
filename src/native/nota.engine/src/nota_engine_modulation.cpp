// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// C ABI — CV modulation (Phase 3, Modular editor). Thin wrappers over the
// Engine modulator/CvLink surface (see Engine_Modulation.cpp).

#include "nota_engine_internal.h"

extern "C" {

int32_t nota_engine_modulation_selftest(NotaEngine* e) {
    return (e && ENG(e)->modulationSelfTest()) ? 1 : 0;
}

int32_t nota_track_add_modulator(NotaEngine* e, int32_t track_id, int32_t kind) {
    return e ? ENG(e)->addModulator(track_id, kind) : -1;
}
NotaResult nota_track_remove_modulator(NotaEngine* e, int32_t track_id, int32_t mod_id) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->removeModulator(track_id, mod_id) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_track_modulator_count(const NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->modulatorCount(track_id) : 0;
}
int32_t nota_track_modulator_id_at(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->modulatorIdAt(track_id, index) : -1;
}
int32_t nota_track_modulator_kind(const NotaEngine* e, int32_t track_id, int32_t mod_id) {
    return e ? CENG(e)->modulatorKind(track_id, mod_id) : -1;
}
float nota_track_modulator_get(const NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t field) {
    return e ? CENG(e)->modulatorGet(track_id, mod_id, field) : 0.0f;
}
NotaResult nota_track_modulator_set(NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t field, float value) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->modulatorSet(track_id, mod_id, field, value);
    return NOTA_OK;
}
float nota_track_modulator_value(const NotaEngine* e, int32_t track_id, int32_t mod_id) {
    return e ? CENG(e)->modulatorValue(track_id, mod_id) : 0.0f;
}
int32_t nota_track_modulator_scope(const NotaEngine* e, int32_t track_id, int32_t mod_id, float* out, int32_t cap) {
    return e ? CENG(e)->modulatorScope(track_id, mod_id, out, cap) : 0;
}

int32_t nota_track_add_cv_link(NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t device_index, int32_t param_index) {
    return e ? ENG(e)->addCvLink(track_id, mod_id, device_index, param_index) : -1;
}
int32_t nota_track_add_cv_link_to(NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t target_track, int32_t device_index, int32_t param_index) {
    return e ? ENG(e)->addCvLinkTo(track_id, mod_id, target_track, device_index, param_index) : -1;
}
int32_t nota_track_add_cv_link_from_param(NotaEngine* e, int32_t track_id, int32_t src_device, int32_t src_param, int32_t target_track, int32_t target_device, int32_t target_param) {
    return e ? ENG(e)->addCvLinkFromParam(track_id, src_device, src_param, target_track, target_device, target_param) : -1;
}
int32_t nota_track_add_cv_link_to_target(NotaEngine* e, int32_t track_id, int32_t mod_id, int32_t target_kind, int32_t target_track, int32_t target_device, int32_t target_param) {
    return e ? ENG(e)->addCvLinkToTarget(track_id, mod_id, target_kind, target_track, target_device, target_param) : -1;
}
int32_t nota_track_add_cv_link_from_param_target(NotaEngine* e, int32_t track_id, int32_t src_device, int32_t src_param, int32_t target_kind, int32_t target_track, int32_t target_device, int32_t target_param) {
    return e ? ENG(e)->addCvLinkFromParamToTarget(track_id, src_device, src_param, target_kind, target_track, target_device, target_param) : -1;
}
int32_t nota_track_cv_link_target_kind(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkTargetKind(track_id, index) : 0;
}
int32_t nota_track_cv_link_source_kind(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkSourceKind(track_id, index) : 0;
}
int32_t nota_track_cv_link_source_device(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkSourceDevice(track_id, index) : -1;
}
int32_t nota_track_cv_link_source_param(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkSourceParam(track_id, index) : -1;
}
NotaResult nota_track_remove_cv_link(NotaEngine* e, int32_t track_id, int32_t index) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    return ENG(e)->removeCvLink(track_id, index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}
int32_t nota_track_cv_link_count(const NotaEngine* e, int32_t track_id) {
    return e ? CENG(e)->cvLinkCount(track_id) : 0;
}
int32_t nota_track_cv_link_source(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkSource(track_id, index) : -1;
}
int32_t nota_track_cv_link_target_track(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkTargetTrack(track_id, index) : -1;
}
int32_t nota_track_cv_link_device(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkDevice(track_id, index) : -1;
}
int32_t nota_track_cv_link_param(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkParam(track_id, index) : -1;
}
float nota_track_cv_link_depth(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkDepth(track_id, index) : 0.0f;
}
int32_t nota_track_cv_link_mode(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkMode(track_id, index) : 0;
}
NotaResult nota_track_set_cv_link_depth(NotaEngine* e, int32_t track_id, int32_t index, float depth) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setCvLinkDepth(track_id, index, depth);
    return NOTA_OK;
}
NotaResult nota_track_set_cv_link_mode(NotaEngine* e, int32_t track_id, int32_t index, int32_t mode) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setCvLinkMode(track_id, index, mode);
    return NOTA_OK;
}
float nota_track_cv_link_base(const NotaEngine* e, int32_t track_id, int32_t index) {
    return e ? CENG(e)->cvLinkBase(track_id, index) : 0.0f;
}
NotaResult nota_track_set_cv_link_base(NotaEngine* e, int32_t track_id, int32_t index, float base) {
    if (!e) return NOTA_ERR_INVALID_ARG;
    ENG(e)->setCvLinkBase(track_id, index, base);
    return NOTA_OK;
}
int32_t nota_track_device_param_modulated(const NotaEngine* e, int32_t track_id, int32_t device_index, int32_t param_index) {
    return (e && CENG(e)->deviceParamModulated(track_id, device_index, param_index)) ? 1 : 0;
}
int32_t nota_track_param_modulated(const NotaEngine* e, int32_t target_kind, int32_t track_id, int32_t device_index, int32_t param_index) {
    return (e && CENG(e)->paramModulated(target_kind, track_id, device_index, param_index)) ? 1 : 0;
}

} // extern "C"
