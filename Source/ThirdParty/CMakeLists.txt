# ================================================================================================
#  DS3OS
#  Copyright (C) 2022 Tim Leonard
# ================================================================================================

add_subdirectory(aes_modes)
add_subdirectory(aes)
add_subdirectory(protobuf-2.6.1rc1)
add_subdirectory(steam)
add_subdirectory(zlib)

option(DSOS_USE_VENDORED_CIVETWEB "Build civetweb from the vendored Source/ThirdParty/civetweb sources" OFF)
set(DSOS_CIVETWEB_SOURCE_DIR "" CACHE PATH "Path to a local civetweb source tree to use instead of downloading it")

option(DSOS_USE_VENDORED_SQLITE "Build sqlite from the vendored Source/ThirdParty/sqlite sources" OFF)
set(DSOS_SQLITE_SOURCE_DIR "" CACHE PATH "Path to a local sqlite source tree to use instead of downloading it")

option(DSOS_USE_VENDORED_OPENSSL "Build OpenSSL from the vendored Source/ThirdParty/openssl sources" OFF)
set(DSOS_OPENSSL_SOURCE_DIR "" CACHE PATH "Path to a local openssl source tree to use instead of downloading it")

option(DSOS_USE_VENDORED_CURL "Build curl from the vendored Source/ThirdParty/curl sources" OFF)

set(DSOS_CURL_SOURCE_DIR "" CACHE PATH "Path to a local curl source tree to use instead of downloading it")

include(FetchContent)

# CivetWeb
if(DSOS_USE_VENDORED_CIVETWEB)
    add_subdirectory(civetweb)
else()
    if(DSOS_CIVETWEB_SOURCE_DIR)
        add_subdirectory(${DSOS_CIVETWEB_SOURCE_DIR})
    else()
        message(STATUS "Fetching CivetWeb via FetchContent")
        FetchContent_Declare(
            civetweb
            URL https://github.com/civetweb/civetweb/archive/refs/tags/v1.15.tar.gz
            URL_HASH SHA256=90a533422944ab327a4fbb9969f0845d0dba05354f9cacce3a5005fa59f593b9
        )
        FetchContent_GetProperties(civetweb)
        if(NOT civetweb_POPULATED)
            # Older releases of CivetWeb use low cmake_minimum_required values.
            # Patch their CMakeLists before it is processed.
            cmake_policy(SET CMP0169 OLD)
            FetchContent_Populate(civetweb)
            cmake_policy(SET CMP0169 NEW)

            file(READ "${civetweb_SOURCE_DIR}/CMakeLists.txt" _civ)
            string(REPLACE "cmake_minimum_required (VERSION 3.3.0)" "cmake_minimum_required (VERSION 3.10)" _civ "${_civ}")
            string(REPLACE "cmake_policy(VERSION 3.2.2)" "cmake_policy(VERSION 3.10)" _civ "${_civ}")
            file(WRITE "${civetweb_SOURCE_DIR}/CMakeLists.txt" "${_civ}")
        endif()

        # Disable civetweb unit tests / external project downloads (e.g., check-unit-test-framework).
        set(CIVETWEB_BUILD_TESTING OFF CACHE BOOL "Disable CivetWeb unit tests" FORCE)

        add_subdirectory(${civetweb_SOURCE_DIR} ${civetweb_BINARY_DIR})
    endif()
endif()

# SQLite
if(DSOS_USE_VENDORED_SQLITE)
    add_subdirectory(sqlite)
else()
    find_package(SQLite3 QUIET)
    if(SQLite3_FOUND)
        add_library(sqlite ALIAS SQLite::SQLite3)
    else()
        message(STATUS "SQLite3 not found; building vendored sqlite")
        add_subdirectory(sqlite)
    endif()
endif()

# OpenSSL
if(DSOS_USE_VENDORED_OPENSSL)
    add_subdirectory(openssl)
    add_library(openssl INTERFACE)
    target_link_libraries(openssl INTERFACE ssl crypto)
    target_include_directories(openssl INTERFACE "${CMAKE_CURRENT_SOURCE_DIR}/openssl/include")
else()
    # If DSOS_OPENSSL_SOURCE_DIR is set to a build/_deps path (from a prior FetchContent run),
    # clear it so we can re-evaluate the correct source selection path.
    if(DEFINED DSOS_OPENSSL_SOURCE_DIR AND DSOS_OPENSSL_SOURCE_DIR AND
       DSOS_OPENSSL_SOURCE_DIR MATCHES "${CMAKE_BINARY_DIR}.*")
        unset(DSOS_OPENSSL_SOURCE_DIR CACHE)
    endif()

    find_package(OpenSSL QUIET)
    if(OpenSSL_FOUND)
        add_library(openssl INTERFACE)
        target_link_libraries(openssl INTERFACE OpenSSL::Crypto OpenSSL::SSL)
        target_include_directories(openssl INTERFACE ${OPENSSL_INCLUDE_DIR})
    elseif(DEFINED DSOS_OPENSSL_SOURCE_DIR AND DSOS_OPENSSL_SOURCE_DIR)
        if(EXISTS "${DSOS_OPENSSL_SOURCE_DIR}/CMakeLists.txt")
            message(STATUS "Building OpenSSL from local source: ${DSOS_OPENSSL_SOURCE_DIR}")
            add_subdirectory(openssl)
            add_library(openssl INTERFACE)
            target_link_libraries(openssl INTERFACE ssl crypto)
            target_include_directories(openssl INTERFACE "${DSOS_OPENSSL_SOURCE_DIR}/include")
        else()
            message(WARNING "DSOS_OPENSSL_SOURCE_DIR=${DSOS_OPENSSL_SOURCE_DIR} does not appear to be a CMake-enabled OpenSSL tree; falling back to vendored OpenSSL")
            add_subdirectory(openssl)
            add_library(openssl INTERFACE)
            target_link_libraries(openssl INTERFACE ssl crypto)
            target_include_directories(openssl INTERFACE "${CMAKE_CURRENT_SOURCE_DIR}/openssl/include")
        endif()
    else()
        message(STATUS "Fetching OpenSSL CMake build (kzalewski/openssl-1.1.1) via FetchContent")
        FetchContent_Declare(
            openssl_cmake
            URL https://github.com/kzalewski/openssl-1.1.1/archive/refs/tags/1.1.1ze.tar.gz
            URL_HASH SHA256=c6537bb633e73928eea0312cb9e11de9378f2a53cf72478914840925080c2501
        )
        FetchContent_MakeAvailable(openssl_cmake)
        set(openssl_cmake_source_dir "${openssl_cmake_SOURCE_DIR}")

        # Build OpenSSL using our bundled CMake wrapper, but point it at the fetched source tree
        set(DSOS_OPENSSL_SOURCE_DIR "${openssl_cmake_source_dir}" CACHE PATH "Path to a local openssl source tree to use instead of downloading it" FORCE)
        add_subdirectory(openssl)
        add_library(openssl INTERFACE)
        target_link_libraries(openssl INTERFACE ssl crypto)
        target_include_directories(openssl INTERFACE "${openssl_cmake_source_dir}/include")
    endif()
endif()

# Curl
if(DSOS_USE_VENDORED_CURL)
    add_subdirectory(curl)
else()

    # When building curl via FetchContent, disable legacy/weak auth mechanisms
    # (NTLM/SSPI) to reduce the surface for security scanners.
    set(CURL_DISABLE_NTLM ON CACHE BOOL "Disable NTLM support in curl" FORCE)

    # Prevent curl's own test suite from being built in CI (requires Perl and
    # produces extra executables that we don't need for dsos).
    set(CURL_DISABLE_TESTS ON CACHE BOOL "Disable curl test suite" FORCE)

    if(DSOS_CURL_SOURCE_DIR)
        FetchContent_Declare(
            curl
            SOURCE_DIR "${DSOS_CURL_SOURCE_DIR}"
        )
    else()
        FetchContent_Declare(
            curl
            URL https://curl.se/download/curl-8.1.0.tar.xz
            URL_HASH SHA256=6bd80ad4f07187015911216ee7185b90d285ac5162aed1bded144f9f93232a3c
        )
    endif()
    FetchContent_MakeAvailable(curl)

    if(NOT TARGET libcurl)
        # Curl's upstream CMake export provides a namespaced target (CURL::libcurl).
        # Create a convenience alias for projects that expect an un-namespaced target.
        if(TARGET CURL::libcurl)
            add_library(libcurl ALIAS CURL::libcurl)
        endif()
    endif()
endif()

if (WIN32)
    add_subdirectory(detours)
endif()
