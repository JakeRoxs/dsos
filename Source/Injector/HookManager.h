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

#include <memory>
#include <vector>

class Injector;

// Manages the lifetime and installation/uninstallation of Hook instances.
// This provides a single place to handle error/rollback logic and keeps the
// Injector class focused on higher-level orchestration.
class HookManager
{
public:
    HookManager() = default;
    ~HookManager();

    HookManager(const HookManager&) = delete;
    HookManager& operator=(const HookManager&) = delete;

    void AddHook(std::unique_ptr<Hook> hook);

    // Installs all configured hooks. If installation fails for any hook,
    // already-installed hooks are uninstalled and this returns a Win32 error code (0=success).
    DWORD InstallAll(const InjectorContext& context);

    // Uninstalls all installed hooks.
    void UninstallAll();

private:
    std::vector<std::unique_ptr<Hook>> Hooks;
    std::vector<Hook*> InstalledHooks;
};
