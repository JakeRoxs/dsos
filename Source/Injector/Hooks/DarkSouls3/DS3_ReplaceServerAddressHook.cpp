/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Injector/Hooks/DarkSouls3/DS3_ReplaceServerAddressHook.h"
#include "Injector/InjectorContext.h"
#include "Shared/Core/Utils/Logging.h"
#include "Shared/Core/Utils/Strings.h"
#include "ThirdParty/detours/src/detours.h"

#include <vector>
#include <iterator>

namespace 
{
    using vector_insert_range_p = void(__fastcall*)(void* self, void* where, void* first, void* last);
    vector_insert_range_p s_original_vector_insert_range;

    // Instance pointer for the currently installed hook.
    // This is safe because we only install one hook instance at a time,
    // and we clear this pointer during uninstall.
    static ::DS3_ReplaceServerAddressHook* s_instance = nullptr;

    void __fastcall VectorInsertRangeHook(void* self, void* where, void* first, void* last)
    {
        if (!s_instance)
        {
            s_original_vector_insert_range(self, where, first, last);
            return;
        }

        const char* k_retail_key = "-----BEGIN RSA PUBLIC KEY-----\nMIIBCgKCAQEA1Nuliw8Rvkt40+0OKoW0JpuSIU/ErQwjzRicZV9JDrCikiTIqoAh\nvBj3DcHwGX1d6T5PY27E4SHa24eRxDetMPEYKeclUeJ0jB07lCtH9Y0zMWl1PMfo\nlIgcm5VKfz+Ua+Ny6klgx1y3ODxMS9g0k11t1WsFtccr464lfP4i1Fgz1/C2Jmgu\n7EV+YdIYkOqT+NJtJG5Z75guq/rTQ85/tVuBKa9dvGIaAqG+nTVlJ2+vzKhjPVXJ\n6AwzWdAbG802uzNC9pk+LEQ+YZXCZSHPMNKz6IwXjlagqDxl2w0rg6dEEFxRY0lm\nS0nqh01eO9pYZA2k0TmpeWHhrKJrnvrKFwIDAQAB\n-----END RSA PUBLIC KEY-----\n";

        const size_t k_key_length = 426;
        const size_t k_block_length = 520;
        const size_t k_hostname_offset = 432;

        char* first_c = reinterpret_cast<char*>(first);
        char* last_c = reinterpret_cast<char*>(last);

        size_t distance = std::distance(first_c, last_c);
        if (distance == k_key_length)
        {
            std::string str(first_c, distance);
            if (str.find(k_retail_key) == 0)
            {
                Log("Retail server address requested, patching to custom server address.");

                std::wstring UnicodeHostname = WidenString(s_instance->ServerHostname());

                memset(first_c, 0, k_block_length);
                memcpy(first_c, s_instance->ServerPublicKey().c_str(), s_instance->ServerPublicKey().size() + 1); // +1 as we want the null terminator.
                memcpy(first_c + k_hostname_offset, UnicodeHostname.c_str(), UnicodeHostname.size() * sizeof(wchar_t) + sizeof(wchar_t));  // +1 as we want the null terminator.
            }
        }

        s_original_vector_insert_range(self, where, first, last);
    }
};

HookError DS3_ReplaceServerAddressHook::Install(const InjectorContext& context)
{
    // Capture config values needed by the hook (called from detoured code).
    s_instance = this;
    ServerHostname() = context.Config.ServerHostname;
    ServerPublicKey() = context.Config.ServerPublicKey;

    // This matches std::vector::insert_range.
    std::vector<intptr_t> matches = context.SearchAOB({
        0x48, 0x89, 0x54, 0x24, 0x10, 0x48, 0x89, 0x4c, 0x24, 0x08, 0x53, 0x56, 0x57, 0x41, 0x54,
        0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x83, 0xec, 0x50, 0x48, 0xc7, 0x44, 0x24, 0x30,
        0xfe, 0xff, 0xff, 0xff, 0x4d, 0x8b, 0xf9, 0x4d, 0x8b, 0xe0, 0x48, 0x8b, 0xd9, 0x49, 0x8b,
        0xf9, 0x49, 0x2b, 0xf8, 0x0f, 0x84, 0x52, 0x01, 0x00, 0x00, 0x48, 0x8b, 0x71, 0x10, 0x4c,
        0x8b, 0x41, 0x08, 0x48, 0x8b, 0xc6, 0x49, 0x2b, 0xc0, 0x48, 0x3b, 0xc7
    });

    if (matches.empty())
    {
        Error("Failed to find injection point for modifying server address.");
        return HookError::NotFound;
    }

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    for (intptr_t func_ptr : matches)
    {
        // We end up detouring all the functions back to a single instance of the function.
        // As the functions are identical though, this makes no practical difference.
        s_original_vector_insert_range = reinterpret_cast<vector_insert_range_p>(func_ptr);
        DetourAttach(&(PVOID&)s_original_vector_insert_range, VectorInsertRangeHook);
    }
    LONG result = DetourTransactionCommit();

    return (result == NO_ERROR) ? HookError::Success : HookError::DetourFailed;
}

void DS3_ReplaceServerAddressHook::Uninstall()
{
    if (s_instance == this)
    {
        s_instance = nullptr;
    }
}

const char* DS3_ReplaceServerAddressHook::GetName()
{
    return "DS3 Replace Server Address";
}

