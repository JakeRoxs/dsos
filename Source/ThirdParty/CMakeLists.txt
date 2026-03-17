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

set(DSOS_DEP_FETCH_STATUS_FILE "${CMAKE_BINARY_DIR}/dependency-fetch-status.txt" CACHE FILEPATH "Path to a file recording which deps were fetched vs vendored")
# Clear/initialize status file on each configure pass
file(WRITE "${DSOS_DEP_FETCH_STATUS_FILE}" "")
function(dsos_append_dep_status)
    # (name, status)
    if(NOT ARGN)
        return()
    endif()
    file(APPEND "${DSOS_DEP_FETCH_STATUS_FILE}" "${ARGN}\n")
endfunction()

# CivetWeb
if(DSOS_USE_VENDORED_CIVETWEB)
    dsos_append_dep_status("civetweb: vendored (forced)")
    add_subdirectory(civetweb)
else()
    if(DSOS_CIVETWEB_SOURCE_DIR)
        add_subdirectory(${DSOS_CIVETWEB_SOURCE_DIR})
    else()
        # First try to download the civetweb archive, but fall back to the vendored
        # source tree on failure (e.g., transient network issues or blocked downloads).
        set(_civetweb_url "https://github.com/civetweb/civetweb/archive/refs/tags/v1.15.tar.gz")
        set(_civetweb_hash "SHA256=90a533422944ab327a4fbb9969f0845d0dba05354f9cacce3a5005fa59f593b9")
        set(_civetweb_archive "${CMAKE_BINARY_DIR}/_deps/civetweb-1.15.tar.gz")
        file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps")

        file(DOWNLOAD "${_civetweb_url}" "${_civetweb_archive}" STATUS _dl_status SHOW_PROGRESS)
        list(GET _dl_status 0 _dl_res)

        if(_dl_res EQUAL 0 AND EXISTS "${_civetweb_archive}")
            message(STATUS "Fetched CivetWeb archive to ${_civetweb_archive}")
            dsos_append_dep_status("civetweb: fetched")

            message(STATUS "Using FetchContent to populate CivetWeb")
            FetchContent_Declare(
                civetweb
                URL "${_civetweb_archive}"
                URL_HASH ${_civetweb_hash}
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
        else()
            message(WARNING "Failed to download CivetWeb archive (${_civetweb_url}); falling back to vendored source")
            dsos_append_dep_status("civetweb: vendored")
            file(REMOVE "${_civetweb_archive}")
            add_subdirectory(civetweb)
        endif()
    endif()
endif()

# SQLite
if(DSOS_USE_VENDORED_SQLITE)
    dsos_append_dep_status("sqlite: vendored (forced)")
    add_subdirectory(sqlite)
else()
    find_package(SQLite3 QUIET)
    if(SQLite3_FOUND)
        add_library(sqlite ALIAS SQLite::SQLite3)
        dsos_append_dep_status("sqlite: system")
    else()
        message(STATUS "SQLite3 not found; building vendored sqlite")
        dsos_append_dep_status("sqlite: vendored")
        add_subdirectory(sqlite)
    endif()
endif()

# OpenSSL
if(DSOS_USE_VENDORED_OPENSSL)
    dsos_append_dep_status("openssl: vendored (forced)")
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
        dsos_append_dep_status("openssl: system")
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
        # Try to download the wrapped OpenSSL CMake build, but fall back to vendored OpenSSL
        # if the download fails (e.g., transient network issues).
        set(_openssl_url "https://github.com/kzalewski/openssl-1.1.1/archive/refs/tags/1.1.1ze.tar.gz")
        set(_openssl_hash "SHA256=c6537bb633e73928eea0312cb9e11de9378f2a53cf72478914840925080c2501")
        set(_openssl_archive "${CMAKE_BINARY_DIR}/_deps/openssl-1.1.1ze.tar.gz")
        file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps")

        file(DOWNLOAD "${_openssl_url}" "${_openssl_archive}" STATUS _dl_status SHOW_PROGRESS)
        list(GET _dl_status 0 _dl_res)

        if(_dl_res EQUAL 0 AND EXISTS "${_openssl_archive}")
            message(STATUS "Fetched OpenSSL archive to ${_openssl_archive}")
            dsos_append_dep_status("openssl: fetched")
            FetchContent_Declare(
                openssl_cmake
                URL "${_openssl_archive}"
                URL_HASH ${_openssl_hash}
            )
            FetchContent_MakeAvailable(openssl_cmake)
            set(openssl_cmake_source_dir "${openssl_cmake_SOURCE_DIR}")

            # Build OpenSSL using our bundled CMake wrapper, but point it at the fetched source tree
            set(DSOS_OPENSSL_SOURCE_DIR "${openssl_cmake_source_dir}" CACHE PATH "Path to a local openssl source tree to use instead of downloading it" FORCE)
            add_subdirectory(openssl)
            add_library(openssl INTERFACE)
            target_link_libraries(openssl INTERFACE ssl crypto)
            target_include_directories(openssl INTERFACE "${openssl_cmake_source_dir}/include")
        else()
            message(WARNING "Failed to download OpenSSL archive (${_openssl_url}); falling back to vendored OpenSSL")
            dsos_append_dep_status("openssl: vendored")
            file(REMOVE "${_openssl_archive}")
            add_subdirectory(openssl)
            add_library(openssl INTERFACE)
            target_link_libraries(openssl INTERFACE ssl crypto)
            target_include_directories(openssl INTERFACE "${CMAKE_CURRENT_SOURCE_DIR}/openssl/include")
        endif()
    endif()
endif()

# Curl
if(DSOS_USE_VENDORED_CURL)
    dsos_append_dep_status("curl: vendored (forced)")
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
        FetchContent_MakeAvailable(curl)
    else()
        set(_curl_url "https://curl.se/download/curl-8.1.0.tar.xz")
        set(_curl_hash "SHA256=6bd80ad4f07187015911216ee7185b90d285ac5162aed1bded144f9f93232a3c")
        set(_curl_archive "${CMAKE_BINARY_DIR}/_deps/curl-8.1.0.tar.xz")
        file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps")

        file(DOWNLOAD "${_curl_url}" "${_curl_archive}" STATUS _dl_status SHOW_PROGRESS)
        list(GET _dl_status 0 _dl_res)

        if(_dl_res EQUAL 0 AND EXISTS "${_curl_archive}")
            message(STATUS "Fetched curl archive to ${_curl_archive}")
            dsos_append_dep_status("curl: fetched")
            FetchContent_Declare(
                curl
                URL "${_curl_archive}"
                URL_HASH ${_curl_hash}
            )
            FetchContent_MakeAvailable(curl)
        else()
            message(WARNING "Failed to download curl archive (${_curl_url}); falling back to vendored curl")
            dsos_append_dep_status("curl: vendored")
            file(REMOVE "${_curl_archive}")
            add_subdirectory(curl)
        endif()
    endif()

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
