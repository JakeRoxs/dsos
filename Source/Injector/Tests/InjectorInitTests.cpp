#include <cassert>
#include <filesystem>
#include <chrono>

#include "Injector/Injector.h"
#include "Injector/Config/RuntimeConfig.h"

void RunInjectorInitTests()
{
    // Create a temporary directory to store a config file.
    auto tmpRoot = std::filesystem::temp_directory_path();
    auto testDir = tmpRoot / ("rekindled_injector_test_" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    std::filesystem::create_directories(testDir);

    RuntimeConfig config;
    config.ServerGameType = "DarkSouls3";
    config.ServerHostname = "example.com";
    config.ServerPort = 50050;

    // Save a config file to the directory.
    bool saved = config.SaveToDirectory(testDir);
    assert(saved);

    Injector injector;

    // In CI/test environments we likely won't have the game module loaded, so Init should fail
    // gracefully rather than crash. The primary assertion is that it returns cleanly.
    bool initialized = injector.Init(RuntimeConfig::GetConfigPath(testDir));
    assert(initialized == false);

    // Ensure the failure reason is recorded so CI failures are diagnosable.
    assert(!injector.GetLastInitError().empty());

    // Term should also be safe to call even if Init fell over.
    injector.Term();
}
