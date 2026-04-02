/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Injector/HookManager.h"
#include "Shared/Core/Utils/Logging.h"
#include <Windows.h>

HookManager::~HookManager()
{
    UninstallAll();
}

void HookManager::AddHook(std::unique_ptr<Hook> hook)
{
    Hooks.push_back(std::move(hook));
}

DWORD HookManager::InstallAll(const InjectorContext& context)
{
    for (auto& hook : Hooks)
    {
        HookError error = hook->Install(context);
        if (error == HookError::Success)
        {
            Success("\t%s: Success", hook->GetName());
            InstalledHooks.push_back(hook.get());
        }
        else
        {
            Error("\t%s: Failed (%s)", hook->GetName(), HookErrorToString(error));
            UninstallAll();
            return static_cast<DWORD>(error);
        }
    }

    return static_cast<DWORD>(HookError::Success);
}

void HookManager::UninstallAll()
{
    for (auto* hook : InstalledHooks)
    {
        Log("\t%s", hook->GetName());
        hook->Uninstall();
    }
    InstalledHooks.clear();
}
