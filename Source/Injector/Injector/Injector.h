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

#include "Shared/Platform/Platform.h"
#include "Shared/Game/GameType.h"
#include "Config/RuntimeConfig.h"

#include "Injector/HookManager.h"
#include "Injector/ShutdownSignal.h"

#include <memory>
#include <vector>
#include <filesystem>
#include <queue>
#include <unordered_map>
#include <optional>
#include <mutex>
#include <condition_variable>

// Core of this application, manages all the 
// network services that ds3 uses. 

class Injector
{
public:
    Injector();
    ~Injector();

    bool Init(const std::filesystem::path& configPath = {});
    bool Term();
    void RunUntilQuit();

    const std::string& GetLastInitError() const { return LastInitError; }

    void RequestShutdown();
    bool IsShutdownRequested() const;

    void SaveConfig();

    const RuntimeConfig& GetConfig()    { return Config; }
    GameType GetGameType()              { return CurrentGameType; }

    intptr_t GetBaseAddress();
    
    using AOBByte = std::optional<uint8_t>;
    std::vector<intptr_t> SearchAOB(const std::vector<AOBByte>& pattern);

    std::vector<intptr_t> SearchString(const std::string& input);
    std::vector<intptr_t> SearchString(const std::wstring& input);

private:

    bool QuitRequested = false;
    mutable std::mutex QuitMutex;
    std::condition_variable QuitCv;


    std::filesystem::path DllPath;

    GameType CurrentGameType;

    std::pair<intptr_t, size_t> ModuleRegion;

    RuntimeConfig Config;

    HookManager Hooks;

    std::string LastInitError;
};