/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#pragma once

#include "Shared/Game/GameType.h"
#include "Injector/Config/RuntimeConfig.h"

#include <cstdint>
#include <functional>
#include <optional>
#include <vector>

// A minimal context object containing the runtime values and helpers that hooks need.
// This intentionally avoids exposing the full Injector instance.
struct InjectorContext
{
    using AOBByte = std::optional<uint8_t>;

    const RuntimeConfig& Config;
    GameType GameType;
    intptr_t BaseAddress;

    // Search helpers used by hooks for pattern and string scanning.
    std::function<std::vector<intptr_t>(const std::vector<AOBByte>&)> SearchAOB;
    std::function<std::vector<intptr_t>(const std::string&)> SearchString;
    std::function<std::vector<intptr_t>(const std::wstring&)> SearchWString;
};
