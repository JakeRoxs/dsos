# ================================================================================================
#  Rekindled Server
#  Copyright (C) 2021 Tim Leonard
#  Copyright (C) 2026 Jake Morgeson
# ================================================================================================

project(sqlite C CXX)

SET(SOURCES
    shell.c
    sqlite3.c
    sqlite3.h
    sqlite3ext.h
)

add_library(${PROJECT_NAME} STATIC ${SOURCES})

util_setup_folder_structure(${PROJECT_NAME} SOURCES "ThirdParty")
