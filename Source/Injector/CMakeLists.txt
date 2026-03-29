# ================================================================================================
#  DS3OS
#  Copyright (C) 2021 Tim Leonard
# ================================================================================================

project(Injector C CXX)

set(INJECTOR_CORE_SOURCES
    Config/BuildConfig.h
    Config/RuntimeConfig.h
    Config/RuntimeConfig.cpp

    Injector/Injector.h
    Injector/Injector.cpp
    ShutdownSignal.h
    ShutdownSignal.cpp
    HookManager.h
    HookManager.cpp

    Hooks/Hook.h
    Hooks/Hook.cpp
    Hooks/DarkSouls3/DS3_ReplaceServerAddressHook.h
    Hooks/DarkSouls3/DS3_ReplaceServerAddressHook.cpp
    Hooks/DarkSouls2/DS2_ReplaceServerAddressHook.h
    Hooks/DarkSouls2/DS2_ReplaceServerAddressHook.cpp
    Hooks/DarkSouls2/DS2_LogProtobufsHook.h
    Hooks/DarkSouls2/DS2_LogProtobufsHook.cpp
    Hooks/Shared/ReplaceServerPortHook.h
    Hooks/Shared/ReplaceServerPortHook.cpp
    Hooks/Shared/ChangeSaveGameFilenameHook.h
    Hooks/Shared/ChangeSaveGameFilenameHook.cpp
)

# Core injector implementation shared between the DLL and unit tests.
add_library(InjectorCore STATIC ${INJECTOR_CORE_SOURCES})

# Injector DLL entrypoint is built separately and links against the core implementation.
add_library(${PROJECT_NAME} SHARED Entry.cpp)
# Link the core implementation into the DLL so its symbols are resolved.
target_link_libraries(${PROJECT_NAME} PUBLIC InjectorCore)
add_dependencies(${PROJECT_NAME} InjectorCore)

# Unit tests for the injector component
add_subdirectory(Tests)

# Enable testing here so add_test works even if subdirectories enable testing later.
enable_testing()

foreach(target IN ITEMS InjectorCore ${PROJECT_NAME})
    target_include_directories(${target} PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}" "${CMAKE_CURRENT_SOURCE_DIR}/../")

    if(WIN32)
        target_compile_definitions(${target} PRIVATE
            _CRT_SECURE_NO_WARNINGS
            _WINSOCK_DEPRECATED_NO_WARNINGS
            _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS
            WIN32_LEAN_AND_MEAN
            NOMINMAX
            _WINSOCKAPI_
            _WIN32_WINNT=0x0A00
        )
    else()
        target_compile_definitions(${target} PRIVATE
            _CRT_SECURE_NO_WARNINGS
            _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS
        )
    endif()
    target_link_libraries(${target} PUBLIC Shared detours)
    util_setup_folder_structure(${target} SOURCES "DS3OS")
endforeach()


