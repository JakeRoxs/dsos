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
        if(DEFINED DSOS_CIVETWEB_ARCHIVE AND DSOS_CIVETWEB_ARCHIVE)
            # Prefer a pre-fetched local archive (e.g., provided by Nix) to avoid
            # requiring network access inside the build sandbox.
            set(_civetweb_url "${DSOS_CIVETWEB_ARCHIVE}")
            set(_civetweb_hash "SHA256=90a533422944ab327a4fbb9969f0845d0dba05354f9cacce3a5005fa59f593b9")
        else()
            set(_civetweb_url "https://github.com/civetweb/civetweb/archive/refs/tags/v1.15.tar.gz")
            set(_civetweb_hash "SHA256=90a533422944ab327a4fbb9969f0845d0dba05354f9cacce3a5005fa59f593b9")
        endif()
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
        else()
            message(FATAL_ERROR "Failed to download CivetWeb archive (${_civetweb_url}); cannot continue without FetchContent. To build from vendored sources instead, set -DDSOS_USE_VENDORED_CIVETWEB=ON.")
        endif()
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

    # If we still don't have a usable SQLite target, fall back to the vendored build.
    if(NOT TARGET sqlite)
        message(WARNING "Could not locate a usable SQLite target; falling back to vendored sqlite")
        dsos_append_dep_status("sqlite: vendored")
        add_subdirectory(sqlite)

        if(TARGET sqlite)
            message(STATUS "Vendored sqlite target created successfully: sqlite")
        else()
            message(WARNING "Vendored sqlite did not create a 'sqlite' target; downstream linking will fail")
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
            set(openssl_cmake_source_dir "${openssl_cmake_SOURCE_DIR}")

            # Build OpenSSL using our bundled CMake wrapper, but point it at the fetched source tree
            set(DSOS_OPENSSL_SOURCE_DIR "${openssl_cmake_source_dir}" CACHE PATH "Path to a local openssl source tree to use instead of downloading it" FORCE)
            add_subdirectory(openssl)
            add_library(openssl INTERFACE)
            target_link_libraries(openssl INTERFACE ssl crypto)
            target_include_directories(openssl INTERFACE "${openssl_cmake_source_dir}/include")
        else()
            message(FATAL_ERROR "Failed to download OpenSSL archive (${_openssl_url}); cannot continue without FetchContent. To build from vendored sources instead, set -DDSOS_USE_VENDORED_OPENSSL=ON.")
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
endif()

if (WIN32)
    add_subdirectory(detours)
endif()
