/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Injector/Hooks/Shared/ReplaceServerPortHook.h"
#include "Injector/InjectorContext.h"
#include "Shared/Core/Utils/Logging.h"
#include "Shared/Core/Utils/Strings.h"
#include "ThirdParty/detours/src/detours.h"

#include <atomic>
#include <vector>
#include <iterator>
#include <WinSock2.h>

namespace 
{
    using connect_p = int(WSAAPI*)(SOCKET s, const sockaddr* name, int namelen);
    connect_p s_original_connect;

    // The hook installs a single instance at a time.
    // This pointer is used by the detoured callback to read state.
    // Access is atomic to avoid races between Install/Uninstall and the hook callback.
    static std::atomic<ReplaceServerPortHook*> s_instance{nullptr};

    int WSAAPI ConnectHook(SOCKET s, const sockaddr* name, int namelen)
    {
        auto instance = s_instance.load(std::memory_order_acquire);
        if (!instance)
        {
            return s_original_connect(s, name, namelen);
        }

        sockaddr_in* addr = (sockaddr_in*)name;

        int redirect_port_number = 0;
        switch (instance->GetGameType())
        {
            case GameType::DarkSouls2:
                redirect_port_number = 50031;
                break;
            case GameType::DarkSouls3:
                redirect_port_number = 50050;
                break;
            default:
                // Unexpected game type: don't attempt to redirect.
                Log("ReplaceServerPortHook: unexpected GameType (%d), skipping port patch.", static_cast<int>(instance->GetGameType()));
                return s_original_connect(s, name, namelen);
        }

        if (addr->sin_port == htons(redirect_port_number))
        {
            Log("Attempt to connect to login server, patching port to '%i'.", instance->GetServerPort());
            addr->sin_port = ntohs(instance->GetServerPort());
        }

        return s_original_connect(s, name, namelen);
    }
};

HookError ReplaceServerPortHook::Install(const InjectorContext& context)
{
    GetGameType() = context.GameType;
    GetServerPort() = context.Config.ServerPort;

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    s_original_connect = reinterpret_cast<connect_p>(connect);
    DetourAttach(&(PVOID&)s_original_connect, ConnectHook);

    LONG result = DetourTransactionCommit();
    if (result == ERROR_SUCCESS)
    {
        s_instance.store(this, std::memory_order_release);
        return HookError::Success;
    }
    return HookError::DetourFailed;
}

void ReplaceServerPortHook::Uninstall()
{
    // Only clear the global instance if it still points at us.
    // This avoids clearing it if a newer instance was installed.
    auto current = s_instance.load(std::memory_order_acquire);
    if (current == this)
    {
        s_instance.store(nullptr, std::memory_order_release);
    }
}

const char* ReplaceServerPortHook::GetName()
{
    return "Replace Server Port";
}

