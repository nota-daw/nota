// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shared glue for the C ABI translation units (nota_engine_*.cpp).
// The public boundary is declared in <nota/nota_engine.h>; each TU below defines
// a domain slice of it. This header carries only the common handle-cast plumbing.

#pragma once

#include "nota/nota_engine.h"
#include "Engine.h"

using nota::Engine;

// Handle-cast shorthands used across every C ABI slice.
#define ENG(e) reinterpret_cast<Engine*>(e)
#define CENG(e) reinterpret_cast<const Engine*>(e)
