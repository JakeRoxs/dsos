# ================================================================================================
#  DS3OS
#  Copyright (C) 2022 Tim Leonard
# ================================================================================================

add_subdirectory(aes_modes)
add_subdirectory(aes)
add_subdirectory(protobuf-2.6.1rc1)
add_subdirectory(steam)
add_subdirectory(zlib)

option(DSOS_USE_VENDORED_CIVETWEB "(deprecated) Build civetweb from vendored Source/ThirdParty/civetweb sources (removed)" OFF)
set(DSOS_CIVETWEB_SOURCE_DIR "" CACHE PATH "Path to a local civetweb source tree to use instead of downloading it")
set(DSOS_CIVETWEB_GIT_REPOSITORY "https://github.com/civetweb/civetweb.git" CACHE STRING "CivetWeb git repository to fetch when DSOS_CIVETWEB_SOURCE_DIR is unset")
set(DSOS_CIVETWEB_GIT_TAG "master" CACHE STRING "CivetWeb branch/tag to checkout from the repository")

option(DSOS_USE_VENDORED_SQLITE "Build sqlite from the vendored Source/ThirdParty/sqlite sources" OFF)
set(DSOS_SQLITE_SOURCE_DIR "" CACHE PATH "Path to a local sqlite source tree to use instead of downloading it")
set(DSOS_SQLITE_ARCHIVE "https://sqlite.org/2026/sqlite-autoconf-3510300.tar.gz" CACHE STRING "URL to a SQLite source archive for FetchContent resolution when system sqlite is unavailable")
option(DSOS_SQLITE_ALLOW_REMOTE_FETCH "Allow automatically fetching SQLite via network when system sqlite is not available" ON)

option(DSOS_USE_VENDORED_OPENSSL "Build OpenSSL from the vendored Source/ThirdParty/openssl sources" OFF)
set(DSOS_OPENSSL_SOURCE_DIR "" CACHE PATH "Path to a local openssl source tree to use instead of downloading it")

# Keep vendored curl as a legacy fallback for offline or locked-down environments.
# Default behavior is external FetchContent-based curl fetching.
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
    message(WARNING "DSOS_USE_VENDORED_CIVETWEB=ON is not supported; Source/ThirdParty/civetweb is removed. Falling back to DSOS_CIVETWEB_SOURCE_DIR/FetchContent.")
    dsos_append_dep_status("civetweb: vendored-unsupported")
endif()

if(DSOS_CIVETWEB_SOURCE_DIR)
    dsos_append_dep_status("civetweb: local source")
    add_subdirectory(${DSOS_CIVETWEB_SOURCE_DIR})
else()
    # Prefer a user-defined archive when provided (e.g., offline builds or pinned version).
    if(DEFINED DSOS_CIVETWEB_ARCHIVE AND DSOS_CIVETWEB_ARCHIVE)
        set(_civetweb_use_archive TRUE)
        set(_civetweb_url "${DSOS_CIVETWEB_ARCHIVE}")
        set(_civetweb_hash "SHA256=90a533422944ab327a4fbb9969f0845d0dba05354f9cacce3a5005fa59f593b9")
    else()
        set(_civetweb_use_archive FALSE)
    endif()

    message(STATUS "CivetWeb: using module=${_civetweb_use_archive} archive=${DSOS_CIVETWEB_ARCHIVE}")

    if(_civetweb_use_archive)
        file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps")
        set(_civetweb_archive "${CMAKE_BINARY_DIR}/_deps/civetweb-archive.tar.gz")
        file(DOWNLOAD "${_civetweb_url}" "${_civetweb_archive}" STATUS _dl_status SHOW_PROGRESS)
        list(GET _dl_status 0 _dl_res)

        if(_dl_res EQUAL 0 AND EXISTS "${_civetweb_archive}")
            message(STATUS "Fetched CivetWeb archive to ${_civetweb_archive}")
            dsos_append_dep_status("civetweb: fetched archive")

            FetchContent_Declare(
                civetweb
                URL "${_civetweb_archive}"
                URL_HASH ${_civetweb_hash}
            )
        else()
            message(FATAL_ERROR "Failed to download CivetWeb archive (${_civetweb_url}); cannot continue without FetchContent. To build from vendored sources instead, set -DDSOS_USE_VENDORED_CIVETWEB=ON.")
        endif()
    else()
        message(STATUS "Fetching CivetWeb from ${DSOS_CIVETWEB_GIT_REPOSITORY}@${DSOS_CIVETWEB_GIT_TAG}")
        dsos_append_dep_status("civetweb: fetched git")

        FetchContent_Declare(
            civetweb
            GIT_REPOSITORY "${DSOS_CIVETWEB_GIT_REPOSITORY}"
            GIT_TAG "${DSOS_CIVETWEB_GIT_TAG}"
            GIT_SHALLOW TRUE
            GIT_PROGRESS TRUE
        )
    endif()

    FetchContent_GetProperties(civetweb)
    if(NOT civetweb_POPULATED)
        # Older releases of CivetWeb use low cmake_minimum_required values.
        # Patch their CMakeLists before it is processed.
        if(POLICY CMP0169)
            cmake_policy(SET CMP0169 OLD)
        endif()
        FetchContent_Populate(civetweb)
        if(POLICY CMP0169)
            cmake_policy(SET CMP0169 NEW)
        endif()
    endif()

    # Patch CivetWeb's CMakeLists if needed (covers both initial fetch and cached content).
    if(EXISTS "${civetweb_SOURCE_DIR}/CMakeLists.txt")
        file(READ "${civetweb_SOURCE_DIR}/CMakeLists.txt" _civ RESULT_VARIABLE _civ_read_result)
        if(_civ_read_result EQUAL 0)
            set(_civ_orig "${_civ}")
            string(REPLACE "cmake_minimum_required (VERSION 3.3.0)" "cmake_minimum_required (VERSION 3.10)" _civ "${_civ}")
            string(REPLACE "cmake_policy(VERSION 3.2.2)" "cmake_policy(VERSION 3.10)" _civ "${_civ}")
            if(NOT _civ STREQUAL "${_civ_orig}")
                file(WRITE "${civetweb_SOURCE_DIR}/CMakeLists.txt" "${_civ}")
                message(STATUS "Patched CivetWeb CMakeLists.txt to use a newer CMake policy/requirement")
            endif()
        else()
            message(WARNING "Unable to patch CivetWeb CMakeLists.txt (${civetweb_SOURCE_DIR}): ${_civ_read_result}")
        endif()
    endif()

    # Disable civetweb unit tests / external project downloads (e.g., check-unit-test-framework).
    set(CIVETWEB_BUILD_TESTING OFF CACHE BOOL "Disable CivetWeb unit tests" FORCE)

    # Enable the C++ wrapper library so downstream code can link against the
    # CivetHandler/CivetServer C++ API.
    set(CIVETWEB_ENABLE_CXX ON CACHE BOOL "Enable the CivetWeb C++ wrapper" FORCE)

    add_subdirectory(${civetweb_SOURCE_DIR} ${civetweb_BINARY_DIR})
    # Upstream civetweb provides a C++ wrapper target (civetweb-cpp) that
    # includes CivetHandler/CivetServer. If present, prefer it as the
    # canonical "civetweb" target. Otherwise, fall back to the C API library.
    if(TARGET civetweb-cpp AND NOT TARGET civetweb)
        add_library(civetweb ALIAS civetweb-cpp)
    elseif(TARGET civetweb-c-library AND NOT TARGET civetweb)
        add_library(civetweb ALIAS civetweb-c-library)
    endif()
endif()

# SQLite
if(DSOS_USE_VENDORED_SQLITE)
    dsos_append_dep_status("sqlite: vendored (forced)")
    add_subdirectory(sqlite)
else()
    # Allow overriding sqlite with a local source tree, matching other vendored deps.
    # This is useful when building offline or using a custom sqlite snapshot.
    set(DSOS_SQLITE_SOURCE_DIR_USED FALSE)
    if(DSOS_SQLITE_SOURCE_DIR)
        if(EXISTS "${DSOS_SQLITE_SOURCE_DIR}/CMakeLists.txt")
            message(STATUS "Building sqlite from local source: ${DSOS_SQLITE_SOURCE_DIR}")
            dsos_append_dep_status("sqlite: local source")
            add_subdirectory(${DSOS_SQLITE_SOURCE_DIR})
            set(DSOS_SQLITE_SOURCE_DIR_USED TRUE)
        else()
            message(WARNING "DSOS_SQLITE_SOURCE_DIR=${DSOS_SQLITE_SOURCE_DIR} does not appear to be a CMake-enabled sqlite tree; continuing system detection")
        endif()
    endif()

    if(NOT DSOS_SQLITE_SOURCE_DIR_USED)
        # Prefer the system sqlite package when available. On some platforms the
        # FindSQLite3 module only sets variables (no imported targets), and on others
        # the imported target may exist even if SQLite3_FOUND is unset.
        find_package(SQLite3 QUIET)
        message(STATUS "After find_package(SQLite3): SQLite3_FOUND=${SQLite3_FOUND} SQLite3_LIBRARY='${SQLite3_LIBRARY}' SQLite3_LIBRARIES='${SQLite3_LIBRARIES}' SQLite3_INCLUDE_DIR='${SQLite3_INCLUDE_DIR}' SQLite3_INCLUDE_DIRS='${SQLite3_INCLUDE_DIRS}'")

    # Normalize older FindSQLite3 outputs (singular vars) into the newer plural vars.
    if(NOT DEFINED SQLite3_LIBRARIES OR NOT SQLite3_LIBRARIES)
        if(DEFINED SQLite3_LIBRARY AND SQLite3_LIBRARY)
            set(SQLite3_LIBRARIES "${SQLite3_LIBRARY}")
        endif()
    endif()
    if(NOT DEFINED SQLite3_INCLUDE_DIRS OR NOT SQLite3_INCLUDE_DIRS)
        if(DEFINED SQLite3_INCLUDE_DIR AND SQLite3_INCLUDE_DIR)
            set(SQLite3_INCLUDE_DIRS "${SQLite3_INCLUDE_DIR}")
        endif()
    endif()

    message(STATUS "Normalized SQLite variables: SQLite3_LIBRARIES='${SQLite3_LIBRARIES}' SQLite3_INCLUDE_DIRS='${SQLite3_INCLUDE_DIRS}'")

    # Some FindSQLite3 implementations claim success but do not define a target.
    # Try to recover by emitting a usable target via pkg-config.
    set(DSOS_SQLITE_HAS_TARGET FALSE)
    if(TARGET SQLite::SQLite3 OR TARGET SQLite3::SQLite3)
        set(DSOS_SQLITE_HAS_TARGET TRUE)
    endif()

    set(DSOS_SQLITE_HAS_LIBS FALSE)
    if(DEFINED SQLite3_LIBRARIES AND SQLite3_LIBRARIES)
        set(DSOS_SQLITE_HAS_LIBS TRUE)
    endif()

    set(DSOS_SQLITE_AVAILABLE FALSE)
    if(SQLite3_FOUND OR DSOS_SQLITE_HAS_TARGET OR DSOS_SQLITE_HAS_LIBS)
        set(DSOS_SQLITE_AVAILABLE TRUE)
    endif()

    if(NOT DSOS_SQLITE_AVAILABLE)
        find_package(PkgConfig QUIET)
        if(PKG_CONFIG_FOUND)
            pkg_check_modules(_sqlite3_pc sqlite3)
            if(_sqlite3_pc_FOUND)
                message(STATUS "pkg-config sqlite3 found: libs='${_sqlite3_pc_LIBRARIES}' includes='${_sqlite3_pc_INCLUDE_DIRS}'")
                set(SQLite3_FOUND TRUE)
                set(SQLite3_LIBRARIES "${_sqlite3_pc_LIBRARIES}")
                set(SQLite3_INCLUDE_DIRS "${_sqlite3_pc_INCLUDE_DIRS}")
                set(DSOS_SQLITE_HAS_LIBS TRUE)
                set(DSOS_SQLITE_AVAILABLE TRUE)
            endif()
        endif()
    endif()

    if(DSOS_SQLITE_AVAILABLE)
        message(STATUS "Using system sqlite: SQLite3_FOUND=${SQLite3_FOUND} SQLite3_LIBRARIES='${SQLite3_LIBRARIES}' SQLite3_INCLUDE_DIRS='${SQLite3_INCLUDE_DIRS}'")

        # Ensure we have a usable library variable when FindSQLite3 reports success.
        if(SQLite3_FOUND AND (NOT DEFINED SQLite3_LIBRARIES OR NOT SQLite3_LIBRARIES))
            message(WARNING "SQLite3_FOUND=TRUE but no SQLite3_LIBRARIES set; attempting to use SQLite3_LIBRARY")
            if(DEFINED SQLite3_LIBRARY AND SQLite3_LIBRARY)
                set(SQLite3_LIBRARIES "${SQLite3_LIBRARY}")
                set(DSOS_SQLITE_HAS_LIBS TRUE)
            endif()
        endif()

        # Provide a consistent, portable target name for consumers.
        # Some systems already provide a `SQLite::SQLite3` / `SQLite3::SQLite3` target.
        # Other environments may only supply variables (e.g., pkg-config).
        if(NOT TARGET sqlite)
            if(TARGET SQLite::SQLite3)
                add_library(sqlite ALIAS SQLite::SQLite3)
            elseif(TARGET SQLite3::SQLite3)
                add_library(sqlite ALIAS SQLite3::SQLite3)
            elseif(DSOS_SQLITE_HAS_LIBS)
                add_library(sqlite INTERFACE)
                target_link_libraries(sqlite INTERFACE ${SQLite3_LIBRARIES})
                if(DEFINED SQLite3_INCLUDE_DIRS AND SQLite3_INCLUDE_DIRS)
                    target_include_directories(sqlite INTERFACE ${SQLite3_INCLUDE_DIRS})
                endif()
            endif()
        endif()

        # Provide the canonical CMake target name if it is missing.
        if(TARGET sqlite AND NOT TARGET SQLite::SQLite3)
            add_library(SQLite::SQLite3 ALIAS sqlite)
        endif()
    endif()

    if(NOT TARGET sqlite)
        if(NOT DSOS_SQLITE_ALLOW_REMOTE_FETCH)
            message(FATAL_ERROR "Could not locate system SQLite3, vendored SQLite usage is disabled, and automatic remote fetch is disabled.\n" 
                                "Install SQLite3 development package (e.g. vcpkg sqlite3 on Windows or libsqlite3-dev on Linux),\n" 
                                "or enable DSOS_USE_VENDORED_SQLITE=ON.\n")
        endif()

        # Attempt to fetch SQLite from GitHub (latest backports) as a non-vendored fallback.
        # This path supports todo 037: externalized dependency usage from upstream repo.
        set(_sqlite_fetch_url "${DSOS_SQLITE_ARCHIVE}")
        if(NOT _sqlite_fetch_url)
            set(_sqlite_fetch_url "https://sqlite.org/2026/sqlite-autoconf-3510300.tar.gz")
        endif()

        set(_sqlite_archive "${CMAKE_BINARY_DIR}/_deps/sqlite-fetch.tar.gz")
        file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps")

        file(DOWNLOAD "${_sqlite_fetch_url}" "${_sqlite_archive}" STATUS _dl_status SHOW_PROGRESS)
        list(GET _dl_status 0 _dl_res)

        if(_dl_res EQUAL 0 AND EXISTS "${_sqlite_archive}")
            message(STATUS "Fetched SQLite archive to ${_sqlite_archive}")
            dsos_append_dep_status("sqlite: fetched")

            FetchContent_Declare(
                sqlite_fetch
                URL "${_sqlite_archive}"
            )
            FetchContent_GetProperties(sqlite_fetch)
            if(NOT sqlite_fetch_POPULATED)
                # CMP0169: `FetchContent_Populate` is deprecated for direct use with declared content.
                # Use `FetchContent_MakeAvailable` to be forward-compatible.
                FetchContent_MakeAvailable(sqlite_fetch)
            endif()

            set(_sqlite_extracted_dir "${sqlite_fetch_SOURCE_DIR}")
            # Some CMake distributions may not auto-populate compression archives when
            # there is no CMakeLists.txt in the archive; in that case, unpack manually.
            if(NOT EXISTS "${_sqlite_extracted_dir}/sqlite3.c")
                set(_sqlite_manual_extract_dir "${CMAKE_BINARY_DIR}/_deps/sqlite_fetch_unpack")
                file(REMOVE_RECURSE "${_sqlite_manual_extract_dir}")
                file(MAKE_DIRECTORY "${_sqlite_manual_extract_dir}")
                file(ARCHIVE_EXTRACT INPUT "${_sqlite_archive}" DESTINATION "${_sqlite_manual_extract_dir}")
                set(_sqlite_extracted_dir "${_sqlite_manual_extract_dir}")
            endif()

            # SQLite upstream source does not provide a CMake project we can use directly.
            # Build from the amalgamation sqlite3.c source file.
            set(_sqlite3_source "${_sqlite_extracted_dir}/sqlite3.c")
            if(NOT EXISTS "${_sqlite3_source}")
                message(STATUS "sqlite3.c not found at ${_sqlite3_source}; trying nested directory detect in ${_sqlite_extracted_dir}")

                file(GLOB _sqlite_entries "${_sqlite_extracted_dir}/*")
                foreach(_entry IN LISTS _sqlite_entries)
                    if(IS_DIRECTORY "${_entry}" AND EXISTS "${_entry}/sqlite3.c")
                        set(_sqlite3_source "${_entry}/sqlite3.c")
                        message(STATUS "Found sqlite3.c at ${_sqlite3_source} in nested folder")
                        break()
                    endif()
                endforeach()
            endif()

            if(NOT EXISTS "${_sqlite3_source}")
                message(STATUS "Still no sqlite3.c; searching recursively under ${_sqlite_extracted_dir}")
                file(GLOB_RECURSE _sqlite3_files "${_sqlite_extracted_dir}/*.c")
                foreach(_entry IN LISTS _sqlite3_files)
                    get_filename_component(_name "${_entry}" NAME)
                    if(_name STREQUAL "sqlite3.c")
                        set(_sqlite3_source "${_entry}")
                        message(STATUS "Found sqlite3.c at ${_sqlite3_source} via recursive scan")
                        break()
                    endif()
                endforeach()
            endif()

            if(EXISTS "${_sqlite3_source}")
                get_filename_component(_sqlite_include_dir "${_sqlite3_source}" DIRECTORY)
                add_library(sqlite STATIC "${_sqlite3_source}")
                target_include_directories(sqlite PUBLIC "${_sqlite_include_dir}")
                add_library(SQLite::SQLite3 ALIAS sqlite)
                set(DSOS_SQLITE_AVAILABLE TRUE)
            else()
                message(WARNING "Fetched SQLite archive does not include sqlite3.c (unexpected).")
            endif()
        else()
            message(FATAL_ERROR "Could not locate a usable SQLite target. Install a system sqlite3 development package (e.g. libsqlite3-dev on Linux, vcpkg sqlite3 on Windows) so FindSQLite3 succeeds.  If you must, you can optionally use vendored sqlite via -DDSOS_USE_VENDORED_SQLITE=ON. Fetch attempt from ${_sqlite_fetch_url} returned ${_dl_res}.")
        endif()
    endif()
    endif()  # end if(NOT DSOS_SQLITE_SOURCE_DIR_USED)

    # Ensure a usable sqlite target exists so downstream projects can link.
    # This is a safety net for environments where the vendored sqlite subdirectory
    # does not produce the expected `sqlite` target.
    if(NOT TARGET sqlite)
        message(WARNING "No sqlite target present after dependency resolution; creating an INTERFACE target to allow configuration to proceed.")
        add_library(sqlite INTERFACE)
        if(DEFINED SQLite3_LIBRARIES AND SQLite3_LIBRARIES)
            target_link_libraries(sqlite INTERFACE ${SQLite3_LIBRARIES})
        endif()
        if(DEFINED SQLite3_INCLUDE_DIRS AND SQLite3_INCLUDE_DIRS)
            target_include_directories(sqlite INTERFACE ${SQLite3_INCLUDE_DIRS})
        endif()
    endif()
endif()

# OpenSSL
if(DSOS_USE_VENDORED_OPENSSL)
    message(WARNING "DSOS_USE_VENDORED_OPENSSL=ON is not supported; Source/ThirdParty/openssl has been removed. Falling back to system or FetchContent build path.")
endif()

# Prefer manual local source over system; see docs/BUILD if this is not desirable.
if(DEFINED DSOS_OPENSSL_SOURCE_DIR AND DSOS_OPENSSL_SOURCE_DIR AND EXISTS "${DSOS_OPENSSL_SOURCE_DIR}/include/openssl/opensslv.h")
    dsos_append_dep_status("openssl: local source")
    message(STATUS "Building OpenSSL from DSOS_OPENSSL_SOURCE_DIR=${DSOS_OPENSSL_SOURCE_DIR}")
    set(_openssl_source_build_dir "${CMAKE_BINARY_DIR}/_deps/openssl-local-build")
    file(MAKE_DIRECTORY "${_openssl_source_build_dir}")
    add_subdirectory("${DSOS_OPENSSL_SOURCE_DIR}" "${_openssl_source_build_dir}")
    add_library(openssl INTERFACE)
    target_link_libraries(openssl INTERFACE ssl crypto)
    target_include_directories(openssl INTERFACE "${DSOS_OPENSSL_SOURCE_DIR}/include")
else()
    find_package(OpenSSL QUIET)
    if(OpenSSL_FOUND)
        dsos_append_dep_status("openssl: system")
        add_library(openssl INTERFACE)
        target_link_libraries(openssl INTERFACE OpenSSL::Crypto OpenSSL::SSL)
        target_include_directories(openssl INTERFACE ${OPENSSL_INCLUDE_DIR})
    else()
        if(DEFINED DSOS_OPENSSL_ARCHIVE AND DSOS_OPENSSL_ARCHIVE)
            set(_openssl_url "${DSOS_OPENSSL_ARCHIVE}")
            set(_openssl_hash "SHA256=c6537bb633e73928eea0312cb9e11de9378f2a53cf72478914840925080c2501")
        else()
            set(_openssl_url "https://github.com/kzalewski/openssl-1.1.1/archive/refs/tags/1.1.1ze.tar.gz")
            set(_openssl_hash "SHA256=c6537bb633e73928eea0312cb9e11de9378f2a53cf72478914840925080c2501")
        endif()
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
            set(_openssl_source_dir "${openssl_cmake_SOURCE_DIR}")

            set(_openssl_source_build_dir "${CMAKE_BINARY_DIR}/_deps/openssl-fetched-build")
            file(MAKE_DIRECTORY "${_openssl_source_build_dir}")
            add_subdirectory("${_openssl_source_dir}" "${_openssl_source_build_dir}")
            add_library(openssl INTERFACE)
            target_link_libraries(openssl INTERFACE ssl crypto)
            target_include_directories(openssl INTERFACE "${_openssl_source_dir}/include")
        else()
            message(FATAL_ERROR "Failed to download OpenSSL archive (${_openssl_url}); cannot continue without FetchContent. To build from vendored sources instead, set -DDSOS_USE_VENDORED_OPENSSL=ON.")
        endif()
    endif()
endif()

# Curl
if(DSOS_USE_VENDORED_CURL)
    message(WARNING "DSOS_USE_VENDORED_CURL=ON is deprecated; Source/ThirdParty/curl has been removed from repo. Using DSOS_CURL_SOURCE_DIR or FetchContent instead.")
    dsos_append_dep_status("curl: vendored requested but unsupported; fallback to fetch")
endif()

# When building curl via FetchContent, disable legacy/weak auth mechanisms
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
        if(DEFINED DSOS_CURL_ARCHIVE AND DSOS_CURL_ARCHIVE)
            set(_curl_url "${DSOS_CURL_ARCHIVE}")
            set(_curl_hash "SHA256=6bd80ad4f07187015911216ee7185b90d285ac5162aed1bded144f9f93232a3c")
        else()
            set(_curl_url "https://curl.se/download/curl-8.1.0.tar.xz")
            set(_curl_hash "SHA256=6bd80ad4f07187015911216ee7185b90d285ac5162aed1bded144f9f93232a3c")
        endif()
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
            message(FATAL_ERROR "Failed to download curl archive (${_curl_url}); cannot continue without FetchContent. To build from vendored sources instead, set -DDSOS_USE_VENDORED_CURL=ON.")
        endif()
    endif()

    if(NOT TARGET libcurl)
        # Curl's upstream CMake export provides a namespaced target (CURL::libcurl).
        # Create a convenience alias for projects that expect an un-namespaced target.
        if(TARGET CURL::libcurl)
            add_library(libcurl ALIAS CURL::libcurl)
        endif()
    endif()

if (WIN32)
    add_subdirectory(detours)
endif()
