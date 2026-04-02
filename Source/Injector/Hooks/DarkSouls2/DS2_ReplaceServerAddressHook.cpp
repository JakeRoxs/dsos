/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Injector/Hooks/DarkSouls2/DS2_ReplaceServerAddressHook.h"
#include "Injector/InjectorContext.h"
#include "Shared/Core/Utils/Logging.h"
#include "Shared/Core/Utils/Strings.h"
#include "ThirdParty/detours/src/detours.h"

#include <vector>
#include <iterator>

// This is all much simpler than DS3, the key isn't encrypted or obfuscated, so we can just
// straight patch it in memory.

HookError DS2_ReplaceServerAddressHook::Install(const InjectorContext& context)
{   
    HookError result = PatchKey(context);
    if (result != HookError::Success)
    {
        return result;
    }

    return PatchHostname(context);
}

HookError DS2_ReplaceServerAddressHook::PatchHostname(const InjectorContext& context)
{
    const RuntimeConfig& Config = context.Config;
    std::wstring WideHostname = WidenString(Config.ServerHostname);

    char* WinePrefix = std::getenv("WINEPREFIX");

    std::vector<intptr_t> address_matches = context.SearchWString(L"frpg2-steam64-ope-login.fromsoftware-game.net");

    bool FoundKey = false;

    for (intptr_t key : address_matches)
    {
        // If the memory is not writable yet, modify its protection (steam drm fucks with the protection during boot).
        MEMORY_BASIC_INFORMATION info;
        if (VirtualQuery((void*)key, &info, sizeof(info)) == 0)
        {
            continue;
        }

        if (WinePrefix == nullptr)
        {
            if (((info.Protect & PAGE_READWRITE) == 0 && (info.Protect & PAGE_EXECUTE_READWRITE) == 0))
            {
                continue;
            }
        }

        wchar_t* ptr = (wchar_t*)key;
        for (size_t i = 0; i < WideHostname.size() + 1; i++)
        {
            wchar_t chr = WideHostname[i];

            // Flip endian
            char* source = (char*)&chr;
            std::swap(source[0], source[1]);

            memcpy(ptr, source, sizeof(wchar_t));
            ptr++;
        }
        FoundKey = true;
    }

    if (!FoundKey)
    {
        Error("Failed to find writable hostname string in memory.");
        return HookError::NotFound;
    }

    return HookError::Success;
}

HookError DS2_ReplaceServerAddressHook::PatchKey(const InjectorContext& context)
{
    char* WinePrefix = std::getenv("WINEPREFIX");

    const RuntimeConfig& Config = context.Config;
    size_t CopyLength = Config.ServerPublicKey.size() + 1;

    std::vector<intptr_t> key_matches = context.SearchString({
        "-----BEGIN RSA PUBLIC KEY-----\n"
        "MIIBCAKCAQEAxSeDuBTm3AytrIOGjDKpwJY+437i1F8leMBASVkknYdzM5HB4z8X\n"
        "YTXDylr/N6XAhgr/LcFFZ68yQNQ4AquriMONB+TWUiX0xu84ixYH3AqRtIVqLQbQ\n"
        "xKZsTfyCRC94n9EnvPeS+ueM495YhLIJQBf9T2aCeoHZBFDh2CghJQCdyd4dOT/E\n"
        "9ZxPImwj1t2fZkkKo4smpGk7GcCask2SGsnk/P2jUJxsOyFlCojaW1IldPxn+lXH\n"
        "dlgHSLjQvMlWiZ2SmOwvJqPWMv6XyUXYqsOdejRJJQjV7jeDzYG8trX+bSQxnTAw\n"
        "ENjvjslEcjBmzOCiqFTA/9H1jMjReZpI/wIBAw==\n"
        "-----END RSA PUBLIC KEY-----\n"
    });

    bool FoundKey = false;

    for (intptr_t key : key_matches)
    {
        // If the memory is not writable yet, modify its protection (steam drm fucks with the protection during boot).
        MEMORY_BASIC_INFORMATION info;
        
        if (WinePrefix != nullptr)
        {
            // Do the check because Wine doesn't emulate memory safe features of Windows
            if (VirtualQuery((void*)key, &info, sizeof(info)) == 0)
            {
                continue;
            }
        }
        else
        {
            if (VirtualQuery((void*)key, &info, sizeof(info)) == 0 ||
                ((info.Protect & PAGE_READWRITE) == 0 && (info.Protect & PAGE_EXECUTE_READWRITE) == 0))
            {
                continue;
            }
        }

        memcpy((char*)key, Config.ServerPublicKey.c_str(), CopyLength);
        FoundKey = true;
    }

    if (!FoundKey)
    {
        Error("Failed to find writable public key in memory.");
        return HookError::NotFound;
    }

    return HookError::Success;
}

void DS2_ReplaceServerAddressHook::Uninstall()
{

}

const char* DS2_ReplaceServerAddressHook::GetName()
{
    return "DS2 Replace Server Address";
}

