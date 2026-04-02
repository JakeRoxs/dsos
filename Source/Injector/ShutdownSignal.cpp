/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Injector/ShutdownSignal.h"

namespace InjectorShutdown
{
    static std::atomic<bool> g_shutdownRequested{false};
    static std::mutex* g_mutex = nullptr;
    static std::condition_variable* g_cv = nullptr;
    static std::mutex g_registrationMutex;

    void RequestShutdown()
    {
        g_shutdownRequested.store(true, std::memory_order_release);

        std::mutex* mutex = nullptr;
        std::condition_variable* cv = nullptr;
        {
            std::lock_guard<std::mutex> regLock(g_registrationMutex);
            mutex = g_mutex;
            cv = g_cv;
        }

        if (mutex && cv)
        {
            std::lock_guard<std::mutex> lock(*mutex);
            cv->notify_all();
        }
    }

    bool IsShutdownRequested()
    {
        return g_shutdownRequested.load(std::memory_order_acquire);
    }

    void RegisterNotifier(std::mutex& mutex, std::condition_variable& cv)
    {
        std::lock_guard<std::mutex> regLock(g_registrationMutex);
        g_mutex = &mutex;
        g_cv = &cv;
    }

    void UnregisterNotifier()
    {
        std::lock_guard<std::mutex> regLock(g_registrationMutex);
        g_mutex = nullptr;
        g_cv = nullptr;
    }
}

