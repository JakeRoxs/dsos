# ================================================================================================
#  Rekindled Server
#  Copyright (C) 2026 Jake Morgeson
# ================================================================================================

# Prevent errors if configuring this directory standalone.
if (CMAKE_CURRENT_SOURCE_DIR STREQUAL CMAKE_SOURCE_DIR)
    cmake_minimum_required(VERSION 3.20)
    project(LoaderAvalonia LANGUAGES C CXX)
endif()

# make sure dotnet is available
find_program(DOTNET_EXECUTABLE dotnet)
if (NOT DOTNET_EXECUTABLE)
    message(FATAL_ERROR "dotnet CLI not found. Please install the .NET SDK and ensure 'dotnet' is available on your PATH to build the Loader.Avalonia project.")
endif()

# expose the Loader.Avalonia project as an external MSBuild target
include_external_msproject(
        LoaderAvalonia
        "${CMAKE_CURRENT_SOURCE_DIR}/Loader.Avalonia.csproj"
)

# keep restore/build artifacts under the CMake binary tree.
set(_loader_avalonia_platform "$<IF:$<BOOL:${CMAKE_GENERATOR_PLATFORM}>,${CMAKE_GENERATOR_PLATFORM},AnyCPU>")

if (TARGET LoaderAvalonia)
    set_property(TARGET LoaderAvalonia PROPERTY
        VS_GLOBAL_BaseIntermediateOutputPath
        "${CMAKE_BINARY_DIR}/Intermediate/${_loader_avalonia_platform}$<CONFIG>"
    )

    set_property(TARGET LoaderAvalonia PROPERTY
        VS_GLOBAL_RestorePackagesOnBuild true
    )
else()
    message(STATUS "LoaderAvalonia target was not created by include_external_msproject for generator '${CMAKE_GENERATOR}'; skipping VS_GLOBAL properties.")
endif()
