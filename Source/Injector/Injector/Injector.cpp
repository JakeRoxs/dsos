/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Injector/Injector.h"
#include "Injector/InjectorContext.h"
#include "Injector/Config/BuildConfig.h"
#include "Shared/Core/Utils/Logging.h"
#include "Shared/Core/Utils/File.h"
#include "Shared/Core/Utils/Strings.h"
#include "Shared/Core/Utils/Random.h"
#include "Shared/Core/Utils/DebugObjects.h"
#include "Shared/Core/Network/NetUtils.h"
#include "Shared/Core/Network/NetHttpRequest.h"
#include "Shared/Core/Utils/WinApi.h"
#include "Shared/Core/Utils/Strings.h"

#include "Injector/Hooks/DarkSouls3/DS3_ReplaceServerAddressHook.h"
#include "Injector/Hooks/DarkSouls2/DS2_ReplaceServerAddressHook.h"
#include "Injector/Hooks/DarkSouls2/DS2_LogProtobufsHook.h"
#include "Injector/Hooks/Shared/ReplaceServerPortHook.h"
#include "Injector/Hooks/Shared/ChangeSaveGameFilenameHook.h"

#include <thread>
#include <chrono>
#include <fstream>
#include <cassert>

#include "ThirdParty/nlohmann/json.hpp"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef _WINSOCKAPI_
#define _WINSOCKAPI_
#endif

#include <Windows.h>

// Use different save file.
// add checkbox to ui to show debug window.

namespace {
void dummyFunction() {
}
}; // namespace

Injector::Injector() {
}

Injector::~Injector() {
}

bool Injector::Init(const std::filesystem::path& configPathOverride) {
  Log("Initializing injector ...");
  LastInitError.clear();

  // Grab the dll path based on the location of static function.
  HMODULE moduleHandle = nullptr;
  if (GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                        reinterpret_cast<LPCTSTR>(&dummyFunction),
                        &moduleHandle) == 0) {
    DWORD err = GetLastError();
    Error("Failed to get dll handle, GetLastError=%u", err);
    LastInitError = "Failed to get dll handle: " + std::to_string(err);
    return false;
  }

  // GetModuleFileNameW can silently truncate when the path is longer than MAX_PATH.
  // Retry with a larger buffer to support long paths.
  std::wstring modulePathWide;
  size_t bufferSize = MAX_PATH;
  while (true) {
    modulePathWide.resize(bufferSize);
    DWORD len = GetModuleFileNameW(moduleHandle, modulePathWide.data(), static_cast<DWORD>(bufferSize));
    if (len == 0) {
      DWORD err = GetLastError();
      Error("Failed to get dll path, GetLastError=%u", err);
      LastInitError = "Failed to get dll path: " + std::to_string(err);
      return false;
    }

    if (len < bufferSize) {
      modulePathWide.resize(len);
      break;
    }

    // Buffer was too small; grow and retry.
    bufferSize *= 2;
    if (bufferSize > 32768) {
      Error("DLL path is unexpectedly long.");
      LastInitError = "DLL path is unexpectedly long.";
      return false;
    }
  }

  DllPath = modulePathWide;
  DllPath = DllPath.parent_path();

  Log("DLL Path: %s", NarrowString(DllPath.generic_wstring()).c_str());

  // Use RuntimeConfig helpers to determine the config file location.
  // This keeps file path logic centralized and enables overriding for tests.
  const auto cfgPath = (configPathOverride.empty() ? RuntimeConfig::GetConfigPath(DllPath) : configPathOverride);

  // Load configuration.
  if (!Config.Load(cfgPath)) {
    Error("Failed to load configuration file: %s", cfgPath.string().c_str());
    LastInitError = "Failed to load configuration file: " + cfgPath.string();
    return false;
  }

  if (!ParseGameType(Config.ServerGameType.c_str(), CurrentGameType)) {
    Error("Unknown game type in configuration file: %s", Config.ServerGameType.c_str());
    LastInitError = "Unknown game type: " + Config.ServerGameType;
    return false;
  }

  Log("Server Name: %s", Config.ServerName.c_str());
  Log("Server Hostname: %s", Config.ServerHostname.c_str());
  Log("Server Port: %i", Config.ServerPort);
  Log("Server Game Type: %s", Config.ServerGameType.c_str());

  switch (CurrentGameType) {
  case GameType::DarkSouls3: {
    ModuleRegion = GetModuleBaseRegion("DarkSoulsIII.exe");
    break;
  }
  case GameType::DarkSouls2: {
    ModuleRegion = GetModuleBaseRegion("DarkSoulsII.exe");
    break;
  }
  }

  Log("Base Address: 0x%p", ModuleRegion.first);
  Log("Base Size: 0x%08x", ModuleRegion.second);
  Log("");

  if (ModuleRegion.first == 0) {
    Error("Failed to get module region for DarkSoulsIII.exe.");
    LastInitError = "Failed to locate game module in process.";
    return false;
  }

  // Add hooks we need to use based on configuration.
  switch (CurrentGameType) {
  case GameType::DarkSouls3: {
    if (!BuildConfig::DO_NOT_REDIRECT) {
      Hooks.AddHook(std::make_unique<DS3_ReplaceServerAddressHook>());
    }
    break;
  }
  case GameType::DarkSouls2: {
    if (!BuildConfig::DO_NOT_REDIRECT) {
      Hooks.AddHook(std::make_unique<DS2_ReplaceServerAddressHook>());
    }

#ifdef _DEBUG
    Hooks.AddHook(std::make_unique<DS2_LogProtobufsHook>());
#endif
    break;
  }
  }

  if (!BuildConfig::DO_NOT_REDIRECT) {
    Hooks.AddHook(std::make_unique<ReplaceServerPortHook>());
  }

  if (Config.EnableSeparateSaveFiles) {
    if (!BuildConfig::DO_NOT_REDIRECT) {
      Hooks.AddHook(std::make_unique<ChangeSaveGameFilenameHook>());
    }
  }

  Log("Installing hooks ...");

  InjectorContext ctx{
      Config,
      CurrentGameType,
      ModuleRegion.first,
      [this](const std::vector<AOBByte>& pattern) { return SearchAOB(pattern); },
      [this](const std::string& s) { return SearchString(s); },
      [this](const std::wstring& s) { return SearchString(s); },
  };

  DWORD installResult = Hooks.InstallAll(ctx);
  if (installResult != ERROR_SUCCESS) {
    return false;
  }

  return true;
}

bool Injector::Term() {
  Log("Uninstalling hooks ...");
  Hooks.UninstallAll();

  Log("Terminating injector ...");

  return true;
}

void Injector::RunUntilQuit() {
  Log("");
  Success("Injector is now running.");

  // Prefer event-driven shutdown rather than tight polling.
  InjectorShutdown::RegisterNotifier(QuitMutex, QuitCv);

  std::unique_lock<std::mutex> Lock(QuitMutex);
  while (!QuitRequested && !InjectorShutdown::IsShutdownRequested()) {
    // TODO: Do any polling we need to do here ...
    QuitCv.wait_for(Lock, std::chrono::milliseconds(100));
  }

  InjectorShutdown::UnregisterNotifier();
}

void Injector::RequestShutdown() {
  InjectorShutdown::RequestShutdown();
}

bool Injector::IsShutdownRequested() const {
  return InjectorShutdown::IsShutdownRequested();
}

void Injector::SaveConfig() {
  const auto cfgPath = RuntimeConfig::GetConfigPath(DllPath);
  if (!Config.Save(cfgPath)) {
    Error("Failed to save configuration file: %s", cfgPath.string().c_str());
  }
}

intptr_t Injector::GetBaseAddress() {
  return ModuleRegion.first;
}

std::vector<intptr_t> Injector::SearchAOB(const std::vector<AOBByte>& pattern) {
  std::vector<intptr_t> Matches;

  if (pattern.empty()) {
    return Matches;
  }

  size_t ScanLength = ModuleRegion.second;
  if (ScanLength < pattern.size()) {
    return Matches;
  }

  // Allow matching at the very end of the module region.
  ScanLength = ScanLength - pattern.size() + 1;

  for (size_t Offset = 0; Offset < ScanLength; Offset++) {
    bool OffsetMatches = true;

    for (size_t PatternOffset = 0; PatternOffset < pattern.size(); PatternOffset++) {
      if (pattern[PatternOffset].has_value()) {
        intptr_t Address = ModuleRegion.first + Offset + PatternOffset;
        uint8_t Byte = *reinterpret_cast<uint8_t*>(Address);
        if (Byte != pattern[PatternOffset].value()) {
          OffsetMatches = false;
          break;
        }
      }
    }

    if (OffsetMatches) {
      Matches.push_back(ModuleRegion.first + Offset);
    }
  }

  return Matches;
}

std::vector<intptr_t> Injector::SearchString(const std::string& input) {
  std::vector<AOBByte> aob;
  aob.reserve(input.size());

  for (size_t i = 0; i < input.size(); i++) {
    aob.push_back(input[i]);
  }

  return SearchAOB(aob);
}

std::vector<intptr_t> Injector::SearchString(const std::wstring& input) {
  std::vector<AOBByte> aob;
  aob.reserve(input.size() * 2);

  for (size_t i = 0; i < input.size(); i++) {
    wchar_t val = input[i];
    uint8_t upper = (val >> 8) & 0xFF;
    uint8_t lower = (val >> 0) & 0xFF;
    aob.push_back(upper);
    aob.push_back(lower);
  }

  return SearchAOB(aob);
}