# Common CMake helper definitions for dsos.
# This file is normally included by the root CMakeLists.txt.

# util_setup_folder_structure is used by project CMakeLists for IDE folder grouping.
# For build correctness, this is a no-op in this environment.
function(util_setup_folder_structure target sources scope)
    # No-op for now. Maintains compatibility with existing CMakeLists
    # which call this utility for source grouping.
    # Expected call form: util_setup_folder_structure(<target> SOURCES "<scope>")
    set(_target "${target}")
    set(_scope "${scope}")
    if(NOT DEFINED CMAKE_VERSION)
        return()
    endif()
    if(NOT "${sources}" STREQUAL "SOURCES")
        # Handle calls where sources are provided as named arguments
        # or as explicit sources; currently we don't require implementation.
    endif()
endfunction()