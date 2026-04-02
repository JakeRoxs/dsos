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

#include "Injector/Hooks/Hook.h"

// Hooks part of the STL to monitor for the server address and replace it when
// its found. Adding it to the STL adds some constant overhead but should be reliable
// for all game versions as its non-obfuscated and unlikely to ever be obfuscated due to 
// performance concerns.
class DS2_ReplaceServerAddressHook : public Hook
{
public:
    virtual HookError Install(const InjectorContext& context) override;
    virtual void Uninstall() override;
    virtual const char* GetName() override;

private:
    HookError PatchKey(const InjectorContext& context);
    HookError PatchHostname(const InjectorContext& context);

};
