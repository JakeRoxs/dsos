/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license. 
 * You should have received a copy of the license along with this program. 
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include <windows.h>
#include <filesystem>

#include "Shared/Core/Utils/Logging.h"

#include "Injector/Injector.h"

void main();

// DllMain can request injector shutdown when the DLL is unloaded.
// This uses a shared shutdown signal rather than global singleton state.

// Scope that allocates a console and redirects stdin/stdout/stderr so logs
// and printf output are visible during injection.
struct ConsoleScope
{
    ConsoleScope()
    {
        FILE* dummy;
        if (!AllocConsole())
        {
            // Continue without a console; output may not be visible.
            OutputDebugStringA("rekindled-server: failed to allocate console\n");
            return;
        }

        if (errno_t Err = freopen_s(&dummy, "CONIN$", "r", stdin); Err != 0)
        {
            Error("Failed to redirect stdin to console (error=%d)", Err);
            OutputDebugStringA("rekindled-server: failed to redirect stdin to console\n");
        }
        if (errno_t Err = freopen_s(&dummy, "CONOUT$", "w", stderr); Err != 0)
        {
            Error("Failed to redirect stderr to console (error=%d)", Err);
            OutputDebugStringA("rekindled-server: failed to redirect stderr to console\n");
        }
        if (errno_t Err = freopen_s(&dummy, "CONOUT$", "w", stdout); Err != 0)
        {
            Error("Failed to redirect stdout to console (error=%d)", Err);
            OutputDebugStringA("rekindled-server: failed to redirect stdout to console\n");
        }
    }

    ~ConsoleScope()
    {
        FreeConsole();
    }
};

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpReserved)
{
    switch (fdwReason)
    {
    case DLL_PROCESS_ATTACH:
        CreateThread(NULL, 0, (LPTHREAD_START_ROUTINE)main, NULL, 0, NULL);
        break;

    case DLL_PROCESS_DETACH:
        // If the injector is still running when the DLL is unloaded (e.g. game exit),
        // request a clean shutdown so we can uninstall hooks gracefully.
        InjectorShutdown::RequestShutdown();
        break;
    }

    return TRUE;  // Successful DLL_PROCESS_ATTACH.
}

void main()
{
    // Allocate a console for output (useful when running in a GUI process).
    ConsoleScope consoleScope;

    Log(R"--(    ____             __      _____             __    )--");
    Log(R"--(   / __ \____ ______/ /__   / ___/____  __  __/ /____)--");
    Log(R"--(  / / / / __ `/ ___/ //_/   \__ \/ __ \/ / / / / ___/)--");
    Log(R"--( / /_/ / /_/ / /  / ,<     ___/ / /_/ / /_/ / (__  ) )--");
    Log(R"--(/_____/\__,_/_/  /_/|_|   /____/\____/\__,_/_/____/  )--");
    Log(R"--(  / __ \____  ___  ____     / ___/___  ______   _____  _____  )--");
    Log(R"--( / / / / __ \/ _ \/ __ \    \__ \/ _ \/ ___/ | / / _ \/ ___/  )--");
    Log(R"--(/ /_/ / /_/ /  __/ / / /   ___/ /  __/ /   | |/ /  __/ /      )--");
    Log(R"--(\____/ .___/\___/_/ /_/   /____/\___/_/    |___/\___/_/       )--");
    Log(R"--(    /_/                                                       )--");
    Log("");
    Log("https://github.com/jakeroxs/rekindled-server");
    Log("");

    struct PlatformScope
    {
        bool Initialized;
        PlatformScope() : Initialized(PlatformInit()) {}
        ~PlatformScope() { if (Initialized) PlatformTerm(); }
    } platformScope;

    if (!platformScope.Initialized)
    {
        Error("Failed to initialize platform specific functionality.");
        MessageBoxA(nullptr, "Failed to initialize Rekindled Server platform components.\n\nGame will now be terminated to avoid playing on a partially patched game which may trigger bans.", "Rekindled Server Error", MB_OK|MB_ICONEXCLAMATION);
        std::abort();
    }

    Injector InjectorInstance;

    if (!InjectorInstance.Init())
    {
        Error("Injector failed to initialize.");
        MessageBoxA(nullptr, "Failed to initialize Rekindled Server.\n\nGame will now be terminated to avoid playing on a partially patched game which may trigger bans.", "Rekindled Server Error", MB_OK|MB_ICONEXCLAMATION);
        std::abort();
    }

    // DllMain may already have requested shutdown before we start the loop.
    // InjectorShutdown is a global signal; no further action is required here.

    InjectorInstance.RunUntilQuit();
    if (!InjectorInstance.Term())
    {
        Error("Injector failed to terminate.");
        return;
    }
}
