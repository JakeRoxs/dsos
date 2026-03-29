# ================================================================================================
#  DS3OS
#  Copyright (C) 2021 Tim Leonard
# ================================================================================================

project(lib_generic_c C CXX)

SET(SOURCES
    aes.h
    aes_ni.h
    aes_via_ace.h
    aesopt.h
    aestab.h
    brg_endian.h
    brg_types.h
    aes_modes.c
    aes_ni.c
    aescrypt.c
    aeskey.c
    aestab.c
)

add_library(${PROJECT_NAME} STATIC ${SOURCES})

# AES intrinsics require target CPU features at compile time to avoid
# __builtin_ia32_aeskeygenassist128 target feature errors on GCC/Clang.
if (CMAKE_SYSTEM_PROCESSOR MATCHES "(x86_64|AMD64|i386|i686)")
    if (CMAKE_C_COMPILER_ID STREQUAL "GNU" OR (CMAKE_C_COMPILER_ID STREQUAL "Clang" AND NOT MSVC))
        # Only apply AES/SSE extensions to AES-specific source files to avoid forcing
        # these ISA extensions on all object files in the library.
        set_source_files_properties(aes_ni.c aeskey.c aescrypt.c PROPERTIES COMPILE_OPTIONS "-maes;-mssse3;-msse4.1")
    elseif(CMAKE_C_COMPILER_ID STREQUAL "Clang" AND MSVC)
        # clang-cl uses MSVC option style; do not add GCC-style -m flags here.
        message(STATUS "clang-cl detected; AES/SSSE3/SSE4.1 support should be set through MSVC-compatible flags if required.")
    endif()
endif()

util_setup_folder_structure(${PROJECT_NAME} SOURCES "ThirdParty")
