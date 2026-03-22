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
    if (CMAKE_C_COMPILER_ID STREQUAL "GNU" OR CMAKE_C_COMPILER_ID STREQUAL "Clang")
        target_compile_options(${PROJECT_NAME} PRIVATE -maes -mssse3 -msse4.1)
        set_source_files_properties(aes_ni.c PROPERTIES COMPILE_OPTIONS "-maes;-mssse3;-msse4.1")
    endif()
endif()

util_setup_folder_structure(${PROJECT_NAME} SOURCES "ThirdParty")
