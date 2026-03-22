# ================================================================================================
#  DS3OS
#  Copyright (C) 2022 Tim Leonard
# ================================================================================================

# Enforce C++17 across the project for modern std features used by Shared and others.
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_subdirectory(ThirdParty)

if (WIN32)
    add_subdirectory(Loader)
    add_subdirectory(Injector)
endif()

add_subdirectory(Server)
add_subdirectory(Server.DarkSouls3)
add_subdirectory(Server.DarkSouls2)
add_subdirectory(Shared)

