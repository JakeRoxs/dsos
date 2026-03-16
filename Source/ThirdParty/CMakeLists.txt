# ================================================================================================
#  DS3OS
#  Copyright (C) 2022 Tim Leonard
# ================================================================================================

add_subdirectory(aes_modes)
add_subdirectory(civetweb)
add_subdirectory(aes)
add_subdirectory(protobuf-2.6.1rc1)
add_subdirectory(sqlite)
add_subdirectory(steam)
add_subdirectory(zlib)

option(DSOS_USE_VENDORED_CURL "Build curl from the vendored Source/ThirdParty/curl sources" OFF)
if(DSOS_USE_VENDORED_CURL)
    add_subdirectory(curl)
else()
    include(FetchContent)

    # When building curl via FetchContent, disable legacy/weak auth mechanisms
    # (NTLM/SSPI) to reduce the surface for security scanners.
    set(CURL_DISABLE_NTLM ON CACHE BOOL "Disable NTLM support in curl" FORCE)

    FetchContent_Declare(
        curl
        URL https://curl.se/download/curl-8.1.0.tar.xz
        URL_HASH SHA256=6bd80ad4f07187015911216ee7185b90d285ac5162aed1bded144f9f93232a3c
    )
    FetchContent_MakeAvailable(curl)

    if(NOT TARGET libcurl)
        # Curl's upstream CMake export provides a namespaced target (CURL::libcurl).
        # Create a convenience alias for projects that expect an un-namespaced target.
        if(TARGET CURL::libcurl)
            add_library(libcurl ALIAS CURL::libcurl)
        endif()
    endif()
endif()

add_subdirectory(openssl)

if (WIN32)
    add_subdirectory(detours)
endif()
