# ================================================================================================
#  DS3OS
#  Copyright (C) 2021 Tim Leonard
# ================================================================================================

project(Shared C CXX)

SET(SOURCES   
    Core/Crypto/Cipher.h
    Core/Crypto/CWCCipher.cpp
    Core/Crypto/CWCCipher.h
    Core/Crypto/CWCClientUDPCipher.cpp
    Core/Crypto/CWCClientUDPCipher.h
    Core/Crypto/CWCServerUDPCipher.cpp
    Core/Crypto/CWCServerUDPCipher.h
    Core/Crypto/RSACipher.cpp
    Core/Crypto/RSACipher.h
    Core/Crypto/RSAKeyPair.cpp
    Core/Crypto/RSAKeyPair.h
    Core/Network/NetConnection.h
    Core/Network/NetConnectionTCP.cpp
    Core/Network/NetConnectionTCP.h
    Core/Network/NetConnectionUDP.cpp
    Core/Network/NetConnectionUDP.h
    Core/Network/NetHttpRequest.cpp
    Core/Network/NetHttpRequest.h
    Core/Network/NetIPAddress.cpp
    Core/Network/NetIPAddress.h
    Core/Network/NetUtils.cpp
    Core/Network/NetUtils.h
    Core/Utils/Compression.cpp
    Core/Utils/Compression.h
    Core/Utils/DebugCounter.cpp
    Core/Utils/DebugCounter.h
    Core/Utils/DebugObjects.cpp
    Core/Utils/DebugObjects.h
    Core/Utils/DebugObjects.inc
    Core/Utils/DebugTimer.cpp
    Core/Utils/DebugTimer.h
    Core/Utils/DiffTracker.cpp
    Core/Utils/DiffTracker.h
    Core/Utils/Endian.h
    Core/Utils/Enum.h
    Core/Utils/EnumDef.inc
    Core/Utils/Event.h
    Core/Utils/File.cpp
    Core/Utils/File.h
    Core/Utils/Logging.cpp
    Core/Utils/Logging.h
    Core/Utils/Random.cpp
    Core/Utils/Random.h
    Core/Utils/Strings.cpp
    Core/Utils/Strings.h
    Core/Utils/WinApi.cpp
    Core/Utils/WinApi.h
    Core/Utils/Rtti.cpp
    Core/Utils/Rtti.h
    Core/Utils/Protobuf.cpp
    Core/Utils/Protobuf.h

    
    Game/GameType.cpp
    Game/GameType.h
    Game/PlayerDataUtils.h

    Platform/Platform.h
)
 
if (WIN32)
    SET(PLATFORM_SOURCES
        Platform/Win32/Win32Platform.cpp
    )

    # Prefer a fully-resolved Windows SDK dbghelp path so Ninja/MSVC can link even when
    # LIB environment entries are not set in this shell.
    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
        set(_dbghelp_arch "x64")
    else()
        set(_dbghelp_arch "x86")
    endif()

    # Look for dbghelp in known Windows Kits install paths.
    find_library(DBGHELP_LIBRARY NAMES dbghelp DbgHelp)

    if(NOT DBGHELP_LIBRARY)
        set(_win_kits_paths
            "C:/Program Files (x86)/Windows Kits/10/Lib"
            "C:/Program Files/Windows Kits/10/Lib"
        )

        foreach(_kits_path IN LISTS _win_kits_paths)
            if(EXISTS "${_kits_path}")
                file(GLOB DBGHELP_CANDIDATES "${_kits_path}/*/um/${_dbghelp_arch}/DbgHelp.Lib")
                if(DBGHELP_CANDIDATES)
                    list(SORT DBGHELP_CANDIDATES)
                    list(REVERSE DBGHELP_CANDIDATES)
                    list(GET DBGHELP_CANDIDATES 0 DBGHELP_LIBRARY)
                    break()
                endif()
            endif()
        endforeach()
    endif()

    if(NOT DBGHELP_LIBRARY)
        message(WARNING "dbghelp library not found; stack trace/symbolization features may fail. Install the Windows SDK Debugging Tools component.")
    endif()

    # Look for platform/system libraries in known SDK and MSVC paths.
    file(GLOB _windows_kit_lib_dirs
        "C:/Program Files (x86)/Windows Kits/10/Lib/*/um/${_dbghelp_arch}"
        "C:/Program Files/Windows Kits/10/Lib/*/um/${_dbghelp_arch}"
        "C:/Program Files (x86)/Windows Kits/10/Lib/*/ucrt/${_dbghelp_arch}"
        "C:/Program Files/Windows Kits/10/Lib/*/ucrt/${_dbghelp_arch}"
    )
    file(GLOB _msvc_lib_dirs
        "C:/Program Files/Microsoft Visual Studio/*/*/VC/Tools/MSVC/*/lib/${_dbghelp_arch}"
        "C:/Program Files (x86)/Microsoft Visual Studio/*/*/VC/Tools/MSVC/*/lib/${_dbghelp_arch}"
    )

    set(_system_link_paths ${_windows_kit_lib_dirs} ${_msvc_lib_dirs})

    set(_system_libs kernel32 user32 gdi32 winspool shell32 ole32 oleaut32 uuid comdlg32 advapi32 rpcrt4 dbghelp crypt32)

    foreach(_lib IN LISTS _system_libs)
        find_library(_found_${_lib} NAMES ${_lib} PATHS ${_system_link_paths} NO_DEFAULT_PATH)
        if(NOT _found_${_lib})
            find_library(_found_${_lib} NAMES ${_lib})
        endif()

        set(_found_var "_found_${_lib}")
        if(DEFINED ${_found_var} AND NOT "${${_found_var}}" STREQUAL "")
            list(APPEND PLATFORM_LIBRARIES "${${_found_var}}")
        else()
            message(WARNING "${_lib}.lib not found in SDK paths; using unqualified name (may fail if LIB path is missing).")
            list(APPEND PLATFORM_LIBRARIES "${_lib}.lib")
        endif()
    endforeach()

    # Avoid global link_directories; use target_link_directories for the shared library target.
    # The _system_link_paths are already set above and reused by the target_link_directories call later.

elseif (UNIX)
    SET(PLATFORM_SOURCES
        Platform/Linux/LinuxPlatform.cpp
    )
    SET(PLATFORM_LIBRARIES         
        uuid
    )
endif()
 
add_library(${PROJECT_NAME} STATIC ${SOURCES} ${PLATFORM_SOURCES})

target_include_directories(${PROJECT_NAME} PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}" "${CMAKE_CURRENT_SOURCE_DIR}/../")

if(WIN32)
    target_compile_definitions(${PROJECT_NAME} PRIVATE
        _CRT_SECURE_NO_WARNINGS
        _WINSOCK_DEPRECATED_NO_WARNINGS
        _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS
        WIN32_LEAN_AND_MEAN
        NOMINMAX
        _WINSOCKAPI_
        _WIN32_WINNT=0x0A00
    )
else()
    target_compile_definitions(${PROJECT_NAME} PRIVATE
        _CRT_SECURE_NO_WARNINGS
        _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS
    )
endif()

message(STATUS "PLATFORM_LIBRARIES=${PLATFORM_LIBRARIES}")
if(WIN32)
    list(REMOVE_DUPLICATES PLATFORM_LIBRARIES)

    # Avoid globally clobbering CMake's standard library defaults, which can break
    # linking by removing runtime/system libraries for all targets in this scope.
    # Keep the normal CMake standard runtime link handling, and ensure platform
    # libs are resolved via explicit link directories/target_link_libraries.

    if(_system_link_paths)
        target_link_directories(${PROJECT_NAME} PRIVATE ${_system_link_paths})
    endif()
endif()

target_link_libraries(
    ${PROJECT_NAME}
    PUBLIC
    aes_modes
    lib_generic_c
    zlib
    libcurl
    # OpenSSL target from ThirdParty/openssl (resolved by ThirdParty/CMakeLists)
    openssl
    # Shared exposes protobuf headers in its public interface, so consumers need transitive protobuf include support.
    libprotobuf-lite
    ${PLATFORM_LIBRARIES}
)

# Ensure OpenSSL external project is built before Shared, because openssl.conf is generated
# and libcrypto/libssl are produced in the openssl install output.
if(TARGET openssl_fetched_build)
    add_dependencies(${PROJECT_NAME} openssl_fetched_build)
endif()

# Avoid direct legacy link names (crypto/ssl) in case target import names differ on MSVC.
# ThirdParty creates the correct openssl interface target with proper OpenSSL::Crypto/OpenSSL::SSL mapping.

util_setup_folder_structure(${PROJECT_NAME} SOURCES "DS3OS")

# build unit tests for shared utilities (if any)
add_subdirectory(Tests)
