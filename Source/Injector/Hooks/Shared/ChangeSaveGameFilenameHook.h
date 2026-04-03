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

// Hooks CreateFile and changes the savegame extension from .sl2 to .rds
class ChangeSaveGameFilenameHook : public Hook {
public:
  virtual HookError Install(const InjectorContext& context) override;
  virtual void Uninstall() override;
  virtual const char* GetName() override;
};
