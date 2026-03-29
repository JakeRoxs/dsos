#include <cassert>
#include <optional>

#include "Injector/HookManager.h"

// A simple stub hook implementation that records whether Install/Uninstall were called.
struct StubHook : public Hook
{
    StubHook(HookError installErrorCode)
        : InstallErrorCode(installErrorCode)
    {
    }

    HookError Install(const InjectorContext& /*context*/) override
    {
        Installed = true;
        return InstallErrorCode;
    }

    void Uninstall() override
    {
        Uninstalled = true;
    }

    const char* GetName() override
    {
        return "StubHook";
    }

    HookError InstallErrorCode = HookError::Success;
    bool Installed = false;
    bool Uninstalled = false;
};

// A hook implementation that exercises the search callbacks in InjectorContext and records the result.
struct SearchCallbackHook : public Hook
{
    SearchCallbackHook(HookError installErrorCode)
        : InstallErrorCode(installErrorCode)
    {
    }

    HookError Install(const InjectorContext& context) override
    {
        Installed = true;
        FoundAOB = context.SearchAOB({0xAA, std::nullopt, 0xBB});
        FoundString = context.SearchString("test");
        FoundWideString = context.SearchWString(L"test");
        return InstallErrorCode;
    }

    void Uninstall() override
    {
        Uninstalled = true;
    }

    const char* GetName() override
    {
        return "SearchCallbackHook";
    }

    HookError InstallErrorCode = HookError::Success;
    bool Installed = false;
    bool Uninstalled = false;

    std::vector<intptr_t> FoundAOB;
    std::vector<intptr_t> FoundString;
    std::vector<intptr_t> FoundWideString;
};

void RunHookManagerTests()
{
    // Case 1: all hooks install; uninstall should not be called until requested.
    {
        RuntimeConfig config;
        InjectorContext context{
            config,
            GameType::Unknown,
            0,
            [](const std::vector<InjectorContext::AOBByte>&) { return std::vector<intptr_t>{}; },
            [](const std::string&) { return std::vector<intptr_t>{}; },
            [](const std::wstring&) { return std::vector<intptr_t>{}; },
        };

        HookManager manager;
        auto h1 = std::make_unique<StubHook>(HookError::Success);
        auto h2 = std::make_unique<StubHook>(HookError::Success);
        StubHook* ptr1 = h1.get();
        StubHook* ptr2 = h2.get();

        manager.AddHook(std::move(h1));
        manager.AddHook(std::move(h2));

        DWORD result = manager.InstallAll(context);
        assert(result == static_cast<DWORD>(HookError::Success));
        assert(ptr1->Installed);
        assert(ptr2->Installed);
        assert(!ptr1->Uninstalled);
        assert(!ptr2->Uninstalled);

        manager.UninstallAll();
        assert(ptr1->Uninstalled);
        assert(ptr2->Uninstalled);
    }

    // Case 2: second hook fails; first should be uninstalled.
    {
        RuntimeConfig config;
        InjectorContext context{
            config,
            GameType::Unknown,
            0,
            [](const std::vector<InjectorContext::AOBByte>&) { return std::vector<intptr_t>{}; },
            [](const std::string&) { return std::vector<intptr_t>{}; },
            [](const std::wstring&) { return std::vector<intptr_t>{}; },
        };

        HookManager manager;
        auto h1 = std::make_unique<StubHook>(HookError::Success);
        auto h2 = std::make_unique<StubHook>(HookError::GeneralFailure);
        StubHook* ptr1 = h1.get();
        StubHook* ptr2 = h2.get();

        manager.AddHook(std::move(h1));
        manager.AddHook(std::move(h2));

        DWORD result = manager.InstallAll(context);
        assert(result != static_cast<DWORD>(HookError::Success));
        assert(result == static_cast<DWORD>(HookError::GeneralFailure));
        assert(ptr1->Installed);
        assert(ptr2->Installed);
        assert(ptr1->Uninstalled);
        // The failing hook should not be uninstalled (it never installed successfully).
        assert(!ptr2->Uninstalled);
    }

    // Case 3: verify that hooks can use the InjectorContext search callbacks.
    {
        RuntimeConfig config;
        InjectorContext context{
            config,
            GameType::Unknown,
            0,
            [](const std::vector<InjectorContext::AOBByte>&) { return std::vector<intptr_t>{0x1234}; },
            [](const std::string&) { return std::vector<intptr_t>{0x5678}; },
            [](const std::wstring&) { return std::vector<intptr_t>{0x9ABC}; },
        };

        HookManager manager;
        auto h = std::make_unique<SearchCallbackHook>(HookError::Success);
        SearchCallbackHook* ptr = h.get();

        manager.AddHook(std::move(h));

        DWORD result = manager.InstallAll(context);
        assert(result == static_cast<DWORD>(HookError::Success));
        assert(ptr->Installed);

        assert(ptr->FoundAOB.size() == 1 && ptr->FoundAOB[0] == 0x1234);
        assert(ptr->FoundString.size() == 1 && ptr->FoundString[0] == 0x5678);
        assert(ptr->FoundWideString.size() == 1 && ptr->FoundWideString[0] == 0x9ABC);

        manager.UninstallAll();
        assert(ptr->Uninstalled);
    }
}

