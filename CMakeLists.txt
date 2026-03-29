# ================================================================================================
#  DS3OS
#  Copyright (C) 2021 Tim Leonard
# ================================================================================================
cmake_minimum_required(VERSION 3.20)

# Environment settings that all projects inherit.
set(CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/Tools/Build;${CMAKE_MODULE_PATH}")
set(ENV_ROOT_PATH "${CMAKE_CURRENT_SOURCE_DIR}")

# Common configuration for all projects.
include(common)

# Windows SDK/VS tools sanity checks.
if(MSVC)
    include(CheckIncludeFileCXX)
    include(CheckLibraryExists)

    check_include_file_cxx("Windows.h" DSOS_HAS_WINDOWS_H)
    if(NOT DSOS_HAS_WINDOWS_H)
        message(FATAL_ERROR "MSVC could not find Windows SDK headers. Install Windows 10/11 SDK and run from the Developer Command Prompt, or set CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION to a valid SDK version (e.g., 10.0.22621.0).")
    endif()

    check_include_file_cxx("stdlib.h" DSOS_HAS_STDLIB_H)
    if(NOT DSOS_HAS_STDLIB_H)
        message(FATAL_ERROR "MSVC could not find C runtime headers (stdlib.h). Ensure you run this from a Developer Command Prompt or that VCToolsInstallDir and WindowsSDKDir are correctly set in the environment. Example: \"C:/Program Files/Microsoft Visual Studio/2022/Community/VC/Auxiliary/Build/vcvars64.bat\".")
    endif()

    # Add explicit linker search directories for the MSVC/Windows SDK libraries in case the environment lacks LIB paths.
    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
        set(_arch x64)
    else()
        set(_arch x86)
    endif()

    file(GLOB _global_sdk_um "C:/Program Files (x86)/Windows Kits/10/Lib/*/um/${_arch}")
    file(GLOB _global_sdk_ucrt "C:/Program Files (x86)/Windows Kits/10/Lib/*/ucrt/${_arch}")
    file(GLOB _global_msvc_lib "C:/Program Files/Microsoft Visual Studio/*/*/VC/Tools/MSVC/*/lib/${_arch}")
    file(GLOB _global_msvc_lib_x86 "C:/Program Files (x86)/Microsoft Visual Studio/*/*/VC/Tools/MSVC/*/lib/${_arch}")

    set(_global_link_dirs ${_global_sdk_um} ${_global_sdk_ucrt} ${_global_msvc_lib} ${_global_msvc_lib_x86})
    if(_global_link_dirs)
        # Prefer to keep linking behavior local to MSVC generator; avoid global link_directories()
        # because it can affect unrelated targets and is brittle across custom installs.
        # Use CMAKE_LIBRARY_PATH as a fallback for find_library checks in this project.
        list(APPEND CMAKE_LIBRARY_PATH ${_global_link_dirs})
        message(STATUS "Added fallback MSVC/Windows SDK library paths to CMAKE_LIBRARY_PATH")
    endif()

    check_library_exists(kernel32 "GetModuleHandleA" "Windows.h" DSOS_HAS_KERNEL32)
    if(NOT DSOS_HAS_KERNEL32)
        message(FATAL_ERROR "MSVC could not find kernel32.lib. Ensure Windows SDK installation is complete and the Windows SDK lib path is available to MSVC.")
    endif()

    # Automatically use installed Windows SDK version if not explicitly requested.
    if(NOT DEFINED CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION)
        if(DEFINED ENV{WindowsSDKVersion} AND NOT "" STREQUAL "$ENV{WindowsSDKVersion}")
            set(CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION "$ENV{WindowsSDKVersion}" CACHE STRING "Windows SDK version" FORCE)
            message(STATUS "Auto-set CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION=$ENV{WindowsSDKVersion}")
        endif()
    endif()
endif()

# OpenSSL's bundled CMakeLists has its own `cmake_minimum_required(3.5)`,
# which triggers a policy error on newer CMake versions when the effective
# policy version exceeds that.  Set a loose policy range to avoid this.
cmake_policy(VERSION 3.10...${CMAKE_VERSION})

# Some vendored dependencies (CivetWeb 1.15) still declare CMake < 3.5 and
# CMake 3.30+ rejects that unless CMAKE_POLICY_VERSION_MINIMUM is set.
# This default allows upstream third-party projects to configure successfully.
if(NOT DEFINED CMAKE_POLICY_VERSION_MINIMUM)
    set(CMAKE_POLICY_VERSION_MINIMUM 3.5 CACHE STRING "Minimum CMake policy version for legacy third-party deps")
endif()

project(ds3os C CXX)

option(DSOS_ALLOW_NON_NINJA "Allow non-Ninja generator to configure/build" OFF)

if(NOT CMAKE_GENERATOR STREQUAL "Ninja" AND NOT DSOS_ALLOW_NON_NINJA)
    message(FATAL_ERROR "dsos requires Ninja generator by default for consistent builds. Re-run CMake with -G Ninja, or set -DDSOS_ALLOW_NON_NINJA=ON to override.")
endif()

if(NOT CMAKE_C_COMPILER OR NOT CMAKE_CXX_COMPILER)
    message(FATAL_ERROR "No C/C++ compiler found. Install a compatible toolchain (e.g., Ninja with gcc/clang, or Visual Studio 2022/2026) and retry. Ensure CMake is invoked with -G Ninja or -G 'Visual Studio 18 2026' when appropriate.")
endif()

# Always generate a compile_commands.json database for tools such as
# the VS Code C/C++ extension.
set(CMAKE_EXPORT_COMPILE_COMMANDS ON CACHE BOOL "Export compile commands for IDEs" FORCE)

# Keep a stable IDE lookup location outside of the build folder
# (root path is safer when `build/` gets removed by clean/reconfigure).
set(DSOS_COMPILE_COMMANDS_SOURCE "${CMAKE_BINARY_DIR}/compile_commands.json")
set(DSOS_COMPILE_COMMANDS_DEST "${CMAKE_SOURCE_DIR}/compile_commands.json")

set(DSOS_COMPILE_COMMANDS_SYNC_SCRIPT "${CMAKE_BINARY_DIR}/dsos_sync_compile_commands.cmake")
file(WRITE "${DSOS_COMPILE_COMMANDS_SYNC_SCRIPT}"
"if(EXISTS \"${DSOS_COMPILE_COMMANDS_SOURCE}\")\n"
"  file(COPY \"${DSOS_COMPILE_COMMANDS_SOURCE}\" DESTINATION \"${CMAKE_SOURCE_DIR}\")\n"
"endif()\n"
)

option(DSOS_SYNC_COMPILE_COMMANDS "Enable explicit target to sync compile_commands.json into the source tree" OFF)

if(DSOS_SYNC_COMPILE_COMMANDS)
    add_custom_target(dsos_sync_compile_commands
        COMMAND ${CMAKE_COMMAND} -P "${DSOS_COMPILE_COMMANDS_SYNC_SCRIPT}"
        COMMENT "Sync compile_commands.json into workspace root for VS Code"
    )
else()
    add_custom_target(dsos_sync_compile_commands
        COMMAND ${CMAKE_COMMAND} -P "${DSOS_COMPILE_COMMANDS_SYNC_SCRIPT}"
        COMMENT "Sync compile_commands.json into workspace root for VS Code (run explicitly with -DDSOS_SYNC_COMPILE_COMMANDS=ON or 'cmake --build . --target dsos_sync_compile_commands')"
        VERBATIM
    )
    set_target_properties(dsos_sync_compile_commands PROPERTIES EXCLUDE_FROM_ALL TRUE)
endif()

add_subdirectory(Source)



