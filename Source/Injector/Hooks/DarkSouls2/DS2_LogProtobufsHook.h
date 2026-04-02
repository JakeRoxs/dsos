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

// Writes the incoming and outgoing protobuf messages.
class DS2_LogProtobufsHook : public Hook
{
public:
    virtual HookError Install(const InjectorContext& context) override;
    virtual void Uninstall() override;
    virtual const char* GetName() override;

private:
    HookError Install_SerializeWithCachedSizesToArray(const InjectorContext& context);
    HookError Install_ParseFromArray(const InjectorContext& context);

};
