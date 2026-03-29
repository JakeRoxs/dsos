# ================================================================================================
#  DS3OS
#  Copyright (C) 2022 Tim Leonard
# ================================================================================================

add_subdirectory(aes_modes)
add_subdirectory(aes)
add_subdirectory(protobuf-2.6.1rc1)
add_subdirectory(steam)
add_subdirectory(zlib)

# Deprecated vendor-style options remain for compatibility, but are no-op.
set(DSOS_CIVETWEB_SOURCE_DIR "" CACHE PATH "Path to a local civetweb source tree to use instead of downloading it")
set(DSOS_CIVETWEB_ARCHIVE "" CACHE STRING "URL to a CivetWeb source archive to fetch when local source is unavailable (empty means use git main branch)")
set(DSOS_CIVETWEB_ARCHIVE_HASH "" CACHE STRING "Expected SHA256 of the CivetWeb archive (optional but strongly recommended when DSOS_CIVETWEB_ARCHIVE is set; leave empty to skip hash verification)")
option(DSOS_CIVETWEB_ALLOW_REMOTE_FETCH "Allow automatically fetching CivetWeb via network when local source is unavailable" ON)
set(DSOS_CIVETWEB_GIT_REPOSITORY "https://github.com/civetweb/civetweb.git" CACHE STRING "CivetWeb git repository to fetch if archive-based fetch is disabled")
set(DSOS_CIVETWEB_GIT_TAG "master" CACHE STRING "CivetWeb branch/tag to checkout from the repository (use master for latest upstream fixes)")

set(DSOS_SQLITE_SOURCE_DIR "" CACHE PATH "Path to a local sqlite source tree to use instead of downloading it")
# Prefers SQLite amalgamation package, which contains sqlite3.c/sqlite3.h/sqlite3ext.h in a stable layout for fallback builds.
set(DSOS_SQLITE_ARCHIVE "https://sqlite.org/2026/sqlite-amalgamation-3510300.zip" CACHE STRING "URL to a SQLite source archive for FetchContent resolution when system sqlite is unavailable")
set(DSOS_SQLITE_ARCHIVE_HASH "" CACHE STRING "Optional SHA256 hash (e.g., SHA256=<hash>) for DSOS_SQLITE_ARCHIVE to enable archive integrity validation and avoid redundant re-downloads")
option(DSOS_SQLITE_ALLOW_REMOTE_FETCH "Allow automatically fetching SQLite via network when system sqlite is not available" ON)

set(DSOS_OPENSSL_SOURCE_DIR "" CACHE PATH "Path to a local openssl source tree to use instead of downloading it")
option(DSOS_OPENSSL_BOOTSTRAP_PERL "Automatically download portable Perl on Windows if perl is not found in PATH (needed for fetched OpenSSL source builds)" ON)
set(DSOS_OPENSSL_PERL_ARCHIVE_URL "https://github.com/StrawberryPerl/Perl-Dist-Strawberry/releases/download/SP_54201_64bit/strawberry-perl-5.42.0.1-64bit-portable.zip" CACHE STRING "Portable Perl archive URL used when bootstrapping Perl for OpenSSL")
set(DSOS_OPENSSL_PERL_ARCHIVE_HASH "SHA256=a1cde185656cf307b51670eed69f648b9eff15b5c518cb136e027c628e650b71" CACHE STRING "Expected SHA256 for DSOS_OPENSSL_PERL_ARCHIVE_URL")

set(DSOS_CURL_SOURCE_DIR "" CACHE PATH "Path to a local curl source tree to use instead of downloading it")

include(FetchContent)
include(ExternalProject)

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

function(dsos_fetch_archive_if_missing)
    # args: NAME URL ARCHIVE [HASH]
    set(_name "${ARGV0}")
    set(_url "${ARGV1}")
    set(_archive "${ARGV2}")
    set(_hash "${ARGV3}")

    file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps")

    set(_use_cached FALSE)
    if(EXISTS "${_archive}")
        if(_hash)
            file(SHA256 "${_archive}" _current_hash)
            string(REPLACE "SHA256=" "" _expected_hash "${_hash}")
            if(_current_hash STREQUAL _expected_hash)
                set(_use_cached TRUE)
                message(STATUS "Using cached ${_name} archive (hash matches): ${_archive}")
            else()
                message(WARNING "Cached ${_name} archive hash mismatch: expected=${_expected_hash} got=${_current_hash}; re-downloading")
                file(REMOVE "${_archive}")
            endif()
        else()
            set(_use_cached TRUE)
            message(STATUS "Using existing cached ${_name} archive: ${_archive}")
        endif()
    endif()

    if(NOT _use_cached)
        if(_hash)
            file(DOWNLOAD "${_url}" "${_archive}" EXPECTED_HASH "${_hash}" STATUS _dl_status SHOW_PROGRESS)
        else()
            file(DOWNLOAD "${_url}" "${_archive}" STATUS _dl_status SHOW_PROGRESS)
        endif()
        list(GET _dl_status 0 _dl_res)
        if(_dl_res EQUAL 0 AND EXISTS "${_archive}")
            message(STATUS "Fetched ${_name} archive to ${_archive}")
        else()
            message(FATAL_ERROR "Failed to download ${_name} archive from ${_url}; status=${_dl_res}")
        endif()
        set(_status "fetched")
    else()
        set(_status "cached")
        set(_dl_res 0)
    endif()

    set(${_name}_FETCH_STATUS "${_status}" PARENT_SCOPE)
endfunction()

function(dsos_set_fetchcontent_source_dir_if_cached)
    # args: NAME SOURCE_DIR
    set(_name "${ARGV0}")
    set(_source_dir "${ARGV1}")
    if(EXISTS "${_source_dir}/CMakeLists.txt")
        message(STATUS "Using cached ${_name} source tree: ${_source_dir}")
        set(${_name}_SOURCE_DIR "${_source_dir}" CACHE PATH "" FORCE)
    endif()
endfunction()

# CivetWeb
if(DSOS_CIVETWEB_SOURCE_DIR)
    dsos_append_dep_status("civetweb: local source")
    add_subdirectory(${DSOS_CIVETWEB_SOURCE_DIR})
else()
    # CivetWeb upstream sets -pedantic-errors by default in many paths and can
    # fail on compilers that do not support OS-specific language extension tokens.
    set(CIVETWEB_ALLOW_WARNINGS ON CACHE BOOL "Do not treat warnings as errors in CivetWeb" FORCE)
    # Disable ASAN for civetweb on Windows to avoid clang sanitizer Win32 dependency issues during link.
    set(CIVETWEB_ENABLE_ASAN OFF CACHE BOOL "Disable ASAN for civetweb in dsos" FORCE)
    if (CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
        set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -fms-extensions")
        set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -fms-extensions")
    endif()
    # Prefer a CivetWeb archive if configured, for consistent non-vendored dependency flow.
    if(DEFINED DSOS_CIVETWEB_ARCHIVE AND DSOS_CIVETWEB_ARCHIVE)
        set(_civetweb_use_archive TRUE)
        set(_civetweb_url "${DSOS_CIVETWEB_ARCHIVE}")
        set(_civetweb_hash "${DSOS_CIVETWEB_ARCHIVE_HASH}")
    else()
        set(_civetweb_use_archive FALSE)
    endif()

    message(STATUS "CivetWeb: archive-enabled=${_civetweb_use_archive} archive-url=${DSOS_CIVETWEB_ARCHIVE} allow-remote=${DSOS_CIVETWEB_ALLOW_REMOTE_FETCH}")

    set(_civetweb_src_dir "${CMAKE_BINARY_DIR}/_deps/civetweb-src")
    dsos_set_fetchcontent_source_dir_if_cached("civetweb" "${_civetweb_src_dir}")

    if(_civetweb_use_archive)
        set(_civetweb_archive "${CMAKE_BINARY_DIR}/_deps/civetweb-archive.tar.gz")
        dsos_fetch_archive_if_missing("civetweb" "${_civetweb_url}" "${_civetweb_archive}" "${_civetweb_hash}")
        dsos_append_dep_status("civetweb: ${civetweb_FETCH_STATUS}_archive")

        if(_civetweb_hash)
            FetchContent_Declare(
                civetweb
                URL "${_civetweb_archive}"
                URL_HASH ${_civetweb_hash}
                SOURCE_DIR "${_civetweb_src_dir}"
            )
        else()
            FetchContent_Declare(
                civetweb
                URL "${_civetweb_archive}"
                SOURCE_DIR "${_civetweb_src_dir}"
            )
        endif()
    else()
        if(NOT DSOS_CIVETWEB_ALLOW_REMOTE_FETCH)
            message(FATAL_ERROR "No CivetWeb archive provided and remote fetch is disabled (DSOS_CIVETWEB_ALLOW_REMOTE_FETCH=OFF). Provide DSOS_CIVETWEB_ARCHIVE or DSOS_CIVETWEB_SOURCE_DIR.")
        endif()

        message(STATUS "Fetching CivetWeb from ${DSOS_CIVETWEB_GIT_REPOSITORY}@${DSOS_CIVETWEB_GIT_TAG}")
        dsos_append_dep_status("civetweb: fetched git")

        FetchContent_Declare(
            civetweb
            GIT_REPOSITORY "${DSOS_CIVETWEB_GIT_REPOSITORY}"
            GIT_TAG "${DSOS_CIVETWEB_GIT_TAG}"
            GIT_SHALLOW TRUE
            GIT_PROGRESS TRUE
            SOURCE_DIR "${_civetweb_src_dir}"
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
    set(_civet_cmake "${civetweb_SOURCE_DIR}/CMakeLists.txt")
    if(EXISTS "${_civet_cmake}")
        if(NOT IS_DIRECTORY "${_civet_cmake}")
            file(READ "${_civet_cmake}" _civ RESULT_VARIABLE _civ_read_result)
            if(_civ_read_result EQUAL 0)
                set(_civ_orig "${_civ}")
                string(REPLACE "cmake_minimum_required (VERSION 3.3.0)" "cmake_minimum_required (VERSION 3.10)" _civ "${_civ}")
                string(REPLACE "cmake_policy(VERSION 3.2.2)" "cmake_policy(VERSION 3.10)" _civ "${_civ}")
                if(NOT _civ STREQUAL "${_civ_orig}")
                    file(WRITE "${_civet_cmake}" "${_civ}")
                    message(STATUS "Patched CivetWeb CMakeLists.txt to use a newer CMake policy/requirement")
                endif()
            else()
                message(STATUS "Unable to read CivetWeb CMakeLists.txt (${_civet_cmake}); continuing without patch: ${_civ_read_result}")
            endif()
        else()
            message(WARNING "CivetWeb CMakeLists.txt exists but is a directory: ${_civet_cmake}")
        endif()
    else()
        message(WARNING "CivetWeb CMakeLists.txt missing: ${_civet_cmake}")
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
    if(TARGET civetweb-cpp)
        target_compile_options(civetweb-cpp PRIVATE
            $<$<OR:$<C_COMPILER_ID:Clang>,$<CXX_COMPILER_ID:Clang>>:-Wno-error=language-extension-token -Wno-language-extension-token>
        )
    endif()
    if(TARGET civetweb-c-library)
        target_compile_options(civetweb-c-library PRIVATE
            $<$<OR:$<C_COMPILER_ID:Clang>,$<CXX_COMPILER_ID:Clang>>:-Wno-error=language-extension-token -Wno-language-extension-token>
        )
    endif()
endif()

# SQLite
# Fallback path: local source -> system package -> archive fetch.
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
            message(FATAL_ERROR "Could not locate system SQLite3 and automatic remote fetch is disabled.\n" 
                                "Install SQLite3 development package (e.g. vcpkg sqlite3 on Windows or libsqlite3-dev on Linux).\n")
        endif()

        # Attempt to fetch SQLite from GitHub (latest backports) as a non-vendored fallback.
        # This path supports todo 037: externalized dependency usage from upstream repo.
        set(_sqlite_fetch_url "${DSOS_SQLITE_ARCHIVE}")
        if(NOT _sqlite_fetch_url)
            set(_sqlite_fetch_url "https://sqlite.org/2026/sqlite-autoconf-3510300.tar.gz")
        endif()

        set(_sqlite_archive "${CMAKE_BINARY_DIR}/_deps/sqlite-fetch.tar.gz")
        file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/_deps")

        set(_sqlite_use_cached_archive FALSE)
        if(EXISTS "${_sqlite_archive}")
            if(DSOS_SQLITE_ARCHIVE_HASH)
                file(SHA256 "${_sqlite_archive}" _sqlite_current_hash)
                set(_sqlite_expected_hash "${DSOS_SQLITE_ARCHIVE_HASH}")
                string(REPLACE "SHA256=" "" _sqlite_expected_hash_norm "${_sqlite_expected_hash}")
                if(_sqlite_current_hash STREQUAL _sqlite_expected_hash_norm)
                    set(_sqlite_use_cached_archive TRUE)
                    message(STATUS "Using cached SQLite archive (hash matches): ${_sqlite_archive}")
                else()
                    message(WARNING "Cached SQLite archive hash mismatch: expected=${_sqlite_expected_hash_norm} got=${_sqlite_current_hash}; re-downloading")
                    file(REMOVE "${_sqlite_archive}")
                endif()
            else()
                set(_sqlite_use_cached_archive TRUE)
                message(STATUS "Using existing cached SQLite archive: ${_sqlite_archive}")
            endif()
        endif()

        if(NOT _sqlite_use_cached_archive)
            if(DSOS_SQLITE_ARCHIVE_HASH)
                file(DOWNLOAD "${_sqlite_fetch_url}" "${_sqlite_archive}" EXPECTED_HASH "${DSOS_SQLITE_ARCHIVE_HASH}" STATUS _dl_status SHOW_PROGRESS)
            else()
                file(DOWNLOAD "${_sqlite_fetch_url}" "${_sqlite_archive}" STATUS _dl_status SHOW_PROGRESS)
            endif()
            list(GET _dl_status 0 _dl_res)

            if(_dl_res EQUAL 0 AND EXISTS "${_sqlite_archive}")
                message(STATUS "Fetched SQLite archive to ${_sqlite_archive}")
                dsos_append_dep_status("sqlite: fetched")
            else()
                message(FATAL_ERROR "Could not locate a usable SQLite target. Install a system sqlite3 development package (e.g. libsqlite3-dev on Linux, vcpkg sqlite3 on Windows) so FindSQLite3 succeeds. Fetch attempt from ${_sqlite_fetch_url} returned ${_dl_res}.")
            endif()
        else()
            dsos_append_dep_status("sqlite: cached")
        endif()

            # Use the cached archive directly, without forcing FetchContent to re-download
            # from a URL protocol each configure. This improves reliability in offline/
            # cached dependency scenarios.
            set(_sqlite_extracted_dir "${CMAKE_BINARY_DIR}/_deps/sqlite_fetch_unpack")
            set(_sqlite_source_from_cache "")

            if(EXISTS "${_sqlite_extracted_dir}")
                file(GLOB_RECURSE _sqlite3_from_cache
                    "${_sqlite_extracted_dir}/sqlite3.c"
                    "${_sqlite_extracted_dir}/*/sqlite3.c"
                )
                if(_sqlite3_from_cache)
                    list(GET _sqlite3_from_cache 0 _sqlite_source_from_cache)
                    message(STATUS "Reusing cached extracted sqlite3.c from ${_sqlite_source_from_cache}")
                endif()
            endif()

            if(NOT _sqlite_source_from_cache)
                # Remove stale/unexpected extraction that does not contain sqlite3.c.
                if(EXISTS "${_sqlite_extracted_dir}")
                    file(REMOVE_RECURSE "${_sqlite_extracted_dir}")
                endif()

                file(MAKE_DIRECTORY "${_sqlite_extracted_dir}")
                file(ARCHIVE_EXTRACT INPUT "${_sqlite_archive}" DESTINATION "${_sqlite_extracted_dir}")

                # Some archive layouts may put sqlite3.c in a nested folder; fallback detection below will handle this.
                if(NOT EXISTS "${_sqlite_extracted_dir}/sqlite3.c")
                    set(_sqlite_manual_extract_dir "${CMAKE_BINARY_DIR}/_deps/sqlite_fetch_unpack")
                    file(REMOVE_RECURSE "${_sqlite_manual_extract_dir}")
                    file(MAKE_DIRECTORY "${_sqlite_manual_extract_dir}")
                    file(ARCHIVE_EXTRACT INPUT "${_sqlite_archive}" DESTINATION "${_sqlite_manual_extract_dir}")
                    set(_sqlite_extracted_dir "${_sqlite_manual_extract_dir}")
                endif()
            else()
                # Already have extracted sqlite3.c; avoid re-extraction and preserve cache.
                set(_sqlite_extracted_dir "${CMAKE_BINARY_DIR}/_deps/sqlite_fetch_unpack")
            endif()

            if(_sqlite_source_from_cache)
                set(_sqlite3_source "${_sqlite_source_from_cache}")
            endif()

            # SQLite upstream source does not provide a CMake project we can use directly.
            # Build from the amalgamation sqlite3.c source file.
            if(NOT DEFINED _sqlite3_source)
                set(_sqlite3_source "${_sqlite_extracted_dir}/sqlite3.c")
            endif()
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

# OpenSSL
# Prefer manual local source over system; see docs/BUILD if this is not desirable.
if(DEFINED DSOS_OPENSSL_SOURCE_DIR AND DSOS_OPENSSL_SOURCE_DIR AND EXISTS "${DSOS_OPENSSL_SOURCE_DIR}/include/openssl/opensslv.h")
    dsos_append_dep_status("openssl: local source")
    message(STATUS "Building OpenSSL from DSOS_OPENSSL_SOURCE_DIR=${DSOS_OPENSSL_SOURCE_DIR}")
    set(_openssl_source_build_dir "${CMAKE_BINARY_DIR}/_deps/openssl-local-build")
    file(MAKE_DIRECTORY "${_openssl_source_build_dir}")
    add_subdirectory("${DSOS_OPENSSL_SOURCE_DIR}" "${_openssl_source_build_dir}")
    add_library(openssl INTERFACE)
    if(TARGET OpenSSL::Crypto AND TARGET OpenSSL::SSL)
        target_link_libraries(openssl INTERFACE OpenSSL::Crypto OpenSSL::SSL)
    else()
        target_link_libraries(openssl INTERFACE ssl crypto)
    endif()
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

        dsos_fetch_archive_if_missing("openssl" "${_openssl_url}" "${_openssl_archive}" "${_openssl_hash}")
        dsos_append_dep_status("openssl: ${openssl_FETCH_STATUS}")

        set(_openssl_src_dir "${CMAKE_BINARY_DIR}/_deps/openssl-src")
        dsos_set_fetchcontent_source_dir_if_cached("openssl_cmake" "${_openssl_src_dir}")

        FetchContent_Declare(
            openssl_cmake
            URL "${_openssl_archive}"
            URL_HASH ${_openssl_hash}
            SOURCE_DIR "${_openssl_src_dir}"
        )
        FetchContent_MakeAvailable(openssl_cmake)
        set(_openssl_source_dir "${openssl_cmake_SOURCE_DIR}")

            # opensslconf.h is generated from opensslconf.h.in by OpenSSL's own
            # Configure script. The fetched archive is not CMake-native, so we
            # must build/install it before consuming headers/libraries.
            set(_openssl_install_dir "${CMAKE_BINARY_DIR}/_deps/openssl-install")
            file(MAKE_DIRECTORY "${_openssl_install_dir}")

            if(WIN32)
                set(_openssl_crypto_lib_path "${_openssl_install_dir}/lib/libcrypto.lib")
                set(_openssl_ssl_lib_path "${_openssl_install_dir}/lib/libssl.lib")
            else()
                set(_openssl_crypto_lib_path "${_openssl_install_dir}/lib/libcrypto.a")
                set(_openssl_ssl_lib_path "${_openssl_install_dir}/lib/libssl.a")
            endif()

            if(EXISTS "${_openssl_crypto_lib_path}" AND EXISTS "${_openssl_ssl_lib_path}")
                message(STATUS "OpenSSL already installed in ${_openssl_install_dir}; skipping source build.")
                add_library(openssl_ssl STATIC IMPORTED GLOBAL)
                set_target_properties(openssl_ssl PROPERTIES IMPORTED_LOCATION "${_openssl_ssl_lib_path}")

                add_library(openssl_crypto STATIC IMPORTED GLOBAL)
                set_target_properties(openssl_crypto PROPERTIES IMPORTED_LOCATION "${_openssl_crypto_lib_path}")

                add_library(openssl INTERFACE)
                target_include_directories(openssl INTERFACE "${_openssl_install_dir}/include")
                target_link_libraries(openssl INTERFACE openssl_crypto openssl_ssl)
                dsos_append_dep_status("openssl: cached-source-build")
            else()
                message(STATUS "OpenSSL fetching: verifying required tooling")
                find_program(_openssl_perl_executable perl)
                if(_openssl_perl_executable)
                    message(STATUS "Found perl executable: ${_openssl_perl_executable}")
                else()
                    if(WIN32 AND DSOS_OPENSSL_BOOTSTRAP_PERL)
                        message(STATUS "Perl missing, but DSOS_OPENSSL_BOOTSTRAP_PERL is enabled; falling back to bootstrap path check.")
                    else()
                        message(FATAL_ERROR "Perl is required for OpenSSL fetched source build. Install Strawberry Perl, set DSOS_OPENSSL_SOURCE_DIR to a local OpenSSL tree, or set DSOS_OPENSSL_BOOTSTRAP_PERL=ON on Windows.")
                    endif()
                endif()

                if(WIN32)
                    set(_openssl_need_nmake TRUE)
                    if(CMAKE_GENERATOR MATCHES "Ninja")
                        set(_openssl_need_nmake FALSE)
                        message(STATUS "Ninja generator detected; nmake.exe check is optional for non-nmake generators.")
                    endif()
                else()
                    set(_openssl_need_nmake FALSE)
                endif()

            if(_openssl_need_nmake)
                find_program(_openssl_nmake_executable NAMES nmake.exe mingw32-make make)
                if(_openssl_nmake_executable)
                    message(STATUS "Found OpenSSL build executable: ${_openssl_nmake_executable}")
                else()
                    message(WARNING "nmake/mingw32-make/make not found in PATH; OpenSSL source build may fail. Install Visual Studio Build Tools or MinGW build tools.")
                endif()
            elseif(WIN32)
                message(STATUS "Skipping nmake.exe requirement for Ninja generator; will try to use ${_openssl_nmake_executable} if available or fallback tools.")
            endif()

            if(NOT _openssl_perl_executable)
                if(WIN32 AND DSOS_OPENSSL_BOOTSTRAP_PERL)
                    set(_perl_archive "${CMAKE_BINARY_DIR}/_deps/strawberry-perl-portable.zip")
                    set(_perl_extract_dir "${CMAKE_BINARY_DIR}/_deps/perl-portable")
                    file(MAKE_DIRECTORY "${_perl_extract_dir}")

                    # Prefer existing extracted bootstrap if available.
                    if(EXISTS "${_perl_extract_dir}/perl/bin/perl.exe")
                        set(_bootstrap_perl "${_perl_extract_dir}/perl/bin/perl.exe")
                        message(STATUS "Using cached extracted perl at ${_bootstrap_perl}")
                    elseif(EXISTS "${_perl_extract_dir}/strawberry-perl-5.42.0.1-64bit-portable/perl/bin/perl.exe")
                        set(_bootstrap_perl "${_perl_extract_dir}/strawberry-perl-5.42.0.1-64bit-portable/perl/bin/perl.exe")
                        message(STATUS "Using cached extracted perl at ${_bootstrap_perl}")
                    else()
                        if(EXISTS "${_perl_archive}")
                            message(STATUS "Using cached Perl archive ${_perl_archive}")
                        else()
                            message(STATUS "Perl not found; attempting portable bootstrap from ${DSOS_OPENSSL_PERL_ARCHIVE_URL}")
                            file(DOWNLOAD "${DSOS_OPENSSL_PERL_ARCHIVE_URL}" "${_perl_archive}" EXPECTED_HASH "${DSOS_OPENSSL_PERL_ARCHIVE_HASH}" STATUS _perl_dl_status SHOW_PROGRESS)
                            list(GET _perl_dl_status 0 _perl_dl_res)
                            if(NOT _perl_dl_res EQUAL 0)
                                message(FATAL_ERROR "Failed to download portable Perl archive from ${DSOS_OPENSSL_PERL_ARCHIVE_URL}. Set DSOS_OPENSSL_BOOTSTRAP_PERL=OFF and install Perl manually, or provide a valid DSOS_OPENSSL_PERL_ARCHIVE_URL/DSOS_OPENSSL_PERL_ARCHIVE_HASH.")
                            endif()
                        endif()

                        file(ARCHIVE_EXTRACT INPUT "${_perl_archive}" DESTINATION "${_perl_extract_dir}")

                        set(_perl_candidates
                            "${_perl_extract_dir}/perl/bin/perl.exe"
                            "${_perl_extract_dir}/strawberry-perl-5.42.0.1-64bit-portable/perl/bin/perl.exe"
                        )

                        foreach(_candidate IN LISTS _perl_candidates)
                            if(EXISTS "${_candidate}")
                                set(_bootstrap_perl "${_candidate}")
                                break()
                            endif()
                        endforeach()

                        if(NOT _bootstrap_perl)
                            file(GLOB_RECURSE _perl_glob LIST_DIRECTORIES FALSE "${_perl_extract_dir}/*/perl/bin/perl.exe")
                            list(LENGTH _perl_glob _perl_glob_len)
                            if(_perl_glob_len GREATER 0)
                                list(GET _perl_glob 0 _bootstrap_perl)
                            endif()
                        endif()

                        if(_bootstrap_perl)
                            message(STATUS "Using bootstrapped Perl: ${_bootstrap_perl}")
                        endif()
                    endif()
                    # If we already found a cached bootstrap path in previous steps, use it.
                    if(_bootstrap_perl)
                        set(_openssl_perl_executable "${_bootstrap_perl}")
                        message(STATUS "Using bootstrapped Perl: ${_openssl_perl_executable}")
                    else()
                        set(_perl_candidates
                            "${_perl_extract_dir}/perl/bin/perl.exe"
                            "${_perl_extract_dir}/strawberry-perl-5.42.0.1-64bit-portable/perl/bin/perl.exe"
                        )
                        foreach(_candidate IN LISTS _perl_candidates)
                            if(EXISTS "${_candidate}")
                                set(_bootstrap_perl "${_candidate}")
                                break()
                            endif()
                        endforeach()

                        if(NOT _bootstrap_perl)
                            file(GLOB_RECURSE _perl_glob LIST_DIRECTORIES FALSE "${_perl_extract_dir}/*/perl/bin/perl.exe")
                            list(LENGTH _perl_glob _perl_glob_len)
                            if(_perl_glob_len GREATER 0)
                                list(GET _perl_glob 0 _bootstrap_perl)
                            endif()
                        endif()

                        if(_bootstrap_perl)
                            set(_openssl_perl_executable "${_bootstrap_perl}")
                            message(STATUS "Using bootstrapped Perl: ${_openssl_perl_executable}")
                        else()
                            message(FATAL_ERROR "Portable Perl was downloaded/extracted but perl.exe was not found under ${_perl_extract_dir}.")
                        endif()
                    endif()
                else()
                    message(FATAL_ERROR "Perl is required to build fetched OpenSSL from source. Install Perl (e.g. Strawberry Perl), enable DSOS_OPENSSL_BOOTSTRAP_PERL on Windows, or provide DSOS_OPENSSL_SOURCE_DIR/OpenSSL system package.")
                endif()
            endif()

            if(WIN32)
                # OpenSSL for Windows may be built using MSVC (nmake) or MinGW (mingw32-make).
                if(CMAKE_C_COMPILER_ID STREQUAL "GNU" OR CMAKE_C_COMPILER_ID STREQUAL "Clang")
                    set(_openssl_mingw_build TRUE)
                else()
                    set(_openssl_mingw_build FALSE)
                endif()

                if(_openssl_mingw_build)
                    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
                        set(_openssl_configure_target "mingw64")
                    else()
                        set(_openssl_configure_target "mingw")
                    endif()
                else()
                    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
                        set(_openssl_configure_target "VC-WIN64A")
                    else()
                        set(_openssl_configure_target "VC-WIN32")
                    endif()
                endif()

                # Detect vcvars for accurate MSVC toolchain environment inside external project commands.
                set(_openssl_vcvarsall "")
                if(NOT _openssl_mingw_build)
                    if(DEFINED ENV{VSINSTALLDIR})
                        set(_openssl_vcvarsall "$ENV{VSINSTALLDIR}/VC/Auxiliary/Build/vcvars64.bat")
                    else()
                        get_filename_component(_cl_bin_dir "${CMAKE_CXX_COMPILER}" DIRECTORY)
                        # Expected: .../VC/Tools/MSVC/<ver>/bin/Hostx64/x64
                        get_filename_component(_msvc_tools_dir "${_cl_bin_dir}" DIRECTORY 4)
                        set(_openssl_vcvarsall "${_msvc_tools_dir}/VC/Auxiliary/Build/vcvars64.bat")
                    endif()
                endif()

                if(NOT EXISTS "${_openssl_vcvarsall}")
                    message(WARNING "vcvars64.bat not found at ${_openssl_vcvarsall}, using existing environment for OpenSSL build. Ensure cl.exe and nmake.exe are on PATH.")
                    set(_openssl_vcvarsall "")
                endif()

                if(_openssl_vcvarsall)
                    file(TO_CMAKE_PATH "${_openssl_vcvarsall}" _openssl_vcvarsall_cmake)
                    message(STATUS "OpenSSL vcvars path (raw): ${_openssl_vcvarsall}")
                    message(STATUS "OpenSSL vcvars path (cmake): ${_openssl_vcvarsall_cmake}")
                endif()
            else()
                if(CMAKE_SIZEOF_VOID_P EQUAL 8)
                    set(_openssl_configure_target "linux-x86_64")
                else()
                    set(_openssl_configure_target "linux-elf")
                endif()

                # On non-Windows, we do not need vcvars/nmake; standard build tool flow is used.
                set(_openssl_vcvarsall "")
            endif()

            # Detect an available nmake.exe, either from PATH or the same MSVC bin tree as cl.exe.
            find_program(_openssl_nmake_executable NAMES nmake.exe)
            if(NOT _openssl_nmake_executable)
                get_filename_component(_cl_bin_dir "${CMAKE_CXX_COMPILER}" DIRECTORY)
                set(_nmake_candidate "${_cl_bin_dir}/nmake.exe")
                if(EXISTS "${_nmake_candidate}")
                    set(_openssl_nmake_executable "${_nmake_candidate}")
                endif()
            endif()

            if(NOT _openssl_nmake_executable AND DEFINED ENV{VSINSTALLDIR})
                set(_vs_install_dir $ENV{VSINSTALLDIR})
                file(GLOB _nmake_fallback "${_vs_install_dir}/VC/Tools/MSVC/*/bin/Hostx64/x64/nmake.exe")
                list(GET _nmake_fallback 0 _openssl_nmake_executable)
            endif()

            if(_openssl_need_nmake AND NOT _openssl_nmake_executable)
                message(FATAL_ERROR "nmake.exe not found. Install Visual Studio build tools or run in Developer Command Prompt. Alternatively, set DSOS_OPENSSL_SOURCE_DIR to a prebuilt local OpenSSL tree or use system OpenSSL via find_package(OpenSSL).")
            elseif(NOT _openssl_need_nmake AND NOT _openssl_nmake_executable)
                message(WARNING "nmake.exe was not found, but Ninja generator was selected. Ensure the OpenSSL source build path is supported on this environment.")
            endif()

            set(_openssl_script_dir "${CMAKE_BINARY_DIR}/_deps")
            if(WIN32)
                set(_openssl_configure_script "${_openssl_script_dir}/openssl_configure.bat")
                set(_openssl_build_script "${_openssl_script_dir}/openssl_build.bat")
                set(_openssl_install_script "${_openssl_script_dir}/openssl_install.bat")

                file(WRITE "${_openssl_configure_script}" "@echo off\r\n")
                if(_openssl_vcvarsall)
                    file(APPEND "${_openssl_configure_script}" "call \"${_openssl_vcvarsall_cmake}\"\r\n")
                endif()
                file(APPEND "${_openssl_configure_script}" "\"${_openssl_perl_executable}\" Configure ${_openssl_configure_target} no-shared no-tests no-asm --prefix=\"${_openssl_install_dir}\" --openssldir=\"${_openssl_install_dir}/ssl\"\r\n")

                file(WRITE "${_openssl_build_script}" "@echo off\r\n")
                if(_openssl_vcvarsall)
                    file(APPEND "${_openssl_build_script}" "call \"${_openssl_vcvarsall_cmake}\"\r\n")
                endif()
                if(_openssl_nmake_executable)
                    file(APPEND "${_openssl_build_script}" "\"${_openssl_nmake_executable}\"\r\n")
                elseif(_openssl_mingw_build)
                    file(APPEND "${_openssl_build_script}" "mingw32-make\r\n")
                else()
                    file(APPEND "${_openssl_build_script}" "nmake.exe\r\n")
                endif()

                file(WRITE "${_openssl_install_script}" "@echo off\r\n")
                if(_openssl_vcvarsall)
                    file(APPEND "${_openssl_install_script}" "call \"${_openssl_vcvarsall_cmake}\"\r\n")
                endif()
                file(APPEND "${_openssl_install_script}" "\"${_openssl_nmake_executable}\" install_dev\r\n")

                set(_openssl_configure_cmd "${_openssl_configure_script}")
                set(_openssl_build_cmd "${_openssl_build_script}")
                set(_openssl_install_cmd "${_openssl_install_script}")
            else()
                set(_openssl_configure_script "${_openssl_script_dir}/openssl_configure.sh")
                set(_openssl_build_script "${_openssl_script_dir}/openssl_build.sh")
                set(_openssl_install_script "${_openssl_script_dir}/openssl_install.sh")

                set(_openssl_make_jobs "$ENV{CMAKE_BUILD_PARALLEL_LEVEL}")
                if(NOT _openssl_make_jobs)
                    include(ProcessorCount)
                    ProcessorCount(_openssl_make_jobs)
                endif()

                if(NOT _openssl_make_jobs OR _openssl_make_jobs LESS 1)
                    set(_openssl_make_jobs 2)
                endif()

                file(WRITE "${_openssl_configure_script}" "#!/usr/bin/env bash\nset -euo pipefail\ncd \"${_openssl_source_dir}\"\n\"${_openssl_perl_executable}\" ./Configure ${_openssl_configure_target} no-shared no-tests no-asm --prefix=\"${_openssl_install_dir}\" --openssldir=\"${_openssl_install_dir}/ssl\"\n")

                file(WRITE "${_openssl_build_script}" "#!/usr/bin/env bash\nset -euo pipefail\ncd \"${_openssl_source_dir}\"\nmake -j${_openssl_make_jobs}\n")

                file(WRITE "${_openssl_install_script}" "#!/usr/bin/env bash\nset -euo pipefail\ncd \"${_openssl_source_dir}\"\nmake install_sw\n")

                set(_openssl_configure_cmd "${CMAKE_COMMAND} -E sh \"${_openssl_configure_script}\"")
                set(_openssl_build_cmd "${CMAKE_COMMAND} -E sh \"${_openssl_build_script}\"")
                set(_openssl_install_cmd "${CMAKE_COMMAND} -E sh \"${_openssl_install_script}\"")
            endif()

            ExternalProject_Add(openssl_fetched_build
                SOURCE_DIR "${_openssl_source_dir}"
                BUILD_IN_SOURCE TRUE
                CONFIGURE_COMMAND ${_openssl_configure_cmd}
                BUILD_COMMAND ${_openssl_build_cmd}
                INSTALL_COMMAND ${_openssl_install_cmd}
                BUILD_BYPRODUCTS
                    "${_openssl_crypto_lib_path}"
                    "${_openssl_ssl_lib_path}"
                LOG_CONFIGURE TRUE
                LOG_BUILD TRUE
                LOG_INSTALL TRUE
            )

            add_library(openssl_ssl STATIC IMPORTED GLOBAL)
            set_target_properties(openssl_ssl PROPERTIES
                IMPORTED_LOCATION "${_openssl_ssl_lib_path}"
            )
            add_dependencies(openssl_ssl openssl_fetched_build)

            add_library(openssl_crypto STATIC IMPORTED GLOBAL)
            set_target_properties(openssl_crypto PROPERTIES
                IMPORTED_LOCATION "${_openssl_crypto_lib_path}"
            )
            add_dependencies(openssl_crypto openssl_fetched_build)

            add_library(openssl INTERFACE)
            target_include_directories(openssl INTERFACE "${_openssl_install_dir}/include")
            target_link_libraries(openssl INTERFACE openssl_crypto openssl_ssl)
            add_dependencies(openssl openssl_fetched_build)
            dsos_append_dep_status("openssl: fetched-source-build")
        endif()  # if(EXISTS _openssl_ssl_lib_path && EXISTS _openssl_crypto_lib_path)
    endif()      # if(OpenSSL_FOUND)
endif()          # if(DEFINED DSOS_OPENSSL_SOURCE_DIR && ...)

# Curl
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

        dsos_fetch_archive_if_missing("curl" "${_curl_url}" "${_curl_archive}" "${_curl_hash}")
        dsos_append_dep_status("curl: ${curl_FETCH_STATUS}")

        set(_curl_src_dir "${CMAKE_BINARY_DIR}/_deps/curl-src")
        dsos_set_fetchcontent_source_dir_if_cached("curl" "${_curl_src_dir}")

        FetchContent_Declare(
            curl
            URL "${_curl_archive}"
            URL_HASH ${_curl_hash}
            SOURCE_DIR "${_curl_src_dir}"
        )
        FetchContent_MakeAvailable(curl)
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
