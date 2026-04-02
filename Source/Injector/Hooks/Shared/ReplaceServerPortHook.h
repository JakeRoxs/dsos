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

// Hooks the connection call and swaps out the port number for the one we care about.
class ReplaceServerPortHook : public Hook
{
public:
    virtual HookError Install(const InjectorContext& context) override;
    virtual void Uninstall() override;
    virtual const char* GetName() override;

    const GameType& GetGameType() const { return m_gameType; }
    GameType& GetGameType() { return m_gameType; }

    int GetServerPort() const { return m_serverPort; }
    int& GetServerPort() { return m_serverPort; }

private:
    GameType m_gameType = GameType::Unknown;
    int m_serverPort = 0;
};
