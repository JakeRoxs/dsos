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

#include <Windows.h>
#include "Injector/InjectorContext.h"

// A strongly-typed error code for hook installation.
//
// This lets hook implementations communicate rich failure details without
// relying on global state (e.g. GetLastError()).
enum class HookError : DWORD
{
    Success = ERROR_SUCCESS,
    NotFound = ERROR_NOT_FOUND,
    DetourFailed = ERROR_INVALID_FUNCTION,
    InvalidState = ERROR_INVALID_DATA,
    GeneralFailure = ERROR_GEN_FAILURE,
};

inline const char* HookErrorToString(HookError error)
{
    switch (error)
    {
        case HookError::Success: return "Success";
        case HookError::NotFound: return "NotFound";
        case HookError::DetourFailed: return "DetourFailed";
        case HookError::InvalidState: return "InvalidState";
        case HookError::GeneralFailure: return "GeneralFailure";
        default: return "Unknown";
    }
}

// Base class for all detour hooks.
class Hook
{
public:

    // Installs the hook.
    // Returns HookError::Success on success, otherwise a non-zero error.
    virtual HookError Install(const InjectorContext& context) = 0;

    // Uninstalls the hook.
    virtual void Uninstall() = 0;

    // Gets a descriptive name for what this hook is doing.
    virtual const char* GetName() = 0;

};
