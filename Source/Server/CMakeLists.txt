# ================================================================================================
#  DS3OS
#  Copyright (C) 2021 Tim Leonard
# ================================================================================================

project(Server C CXX)

SET(SOURCES
    Entry.cpp

    Client/Client.cpp
    Client/Client.h

    Config/BuildConfig.h
    Config/RuntimeConfig.h
    Config/RuntimeConfig.cpp

    Protobuf/Generated/Shared_FpdLogMessage.pb.cc
    Protobuf/Generated/Shared_Frpg2PlayerData.pb.cc
    Protobuf/Generated/Shared_Frpg2RequestMessage.pb.cc
    
    Protobuf/SharedProtobufs.h
        
    Server/Game.cpp
    Server/Game.h
    Server/ServerManager.cpp
    Server/ServerManager.h
    Server/Server.cpp
    Server/Server.h
    Server/Service.h
    Server/AuthService/AuthClient.cpp
    Server/AuthService/AuthClient.h
    Server/AuthService/AuthService.cpp
    Server/AuthService/AuthService.h
    Server/Database/DatabaseTypes.h
    Server/Database/ServerDatabase.cpp
    Server/Database/ServerDatabase.h
    Server/GameService/GameClient.cpp
    Server/GameService/GameClient.h
    Server/GameService/GameManager.h
    Server/GameService/GameService.cpp
    Server/GameService/GameService.h
    Server/GameService/PlayerState.h
    Server/GameService/Utils/OnlineAreaPool.h

    Server/LoginService/LoginClient.cpp
    Server/LoginService/LoginClient.h
    Server/LoginService/LoginService.cpp
    Server/LoginService/LoginService.h
    Server/Streams/Frpg2Message.h
    Server/Streams/Frpg2MessageStream.cpp
    Server/Streams/Frpg2MessageStream.h
    Server/Streams/Frpg2Packet.h
    Server/Streams/Frpg2PacketStream.cpp
    Server/Streams/Frpg2PacketStream.h
    Server/Streams/Frpg2ReliableUdpFragment.h
    Server/Streams/Frpg2ReliableUdpFragmentStream.cpp
    Server/Streams/Frpg2ReliableUdpFragmentStream.h
    Server/Streams/Frpg2ReliableUdpMessage.cpp
    Server/Streams/Frpg2ReliableUdpMessage.h
    Server/Streams/Frpg2ReliableUdpMessageStream.cpp
    Server/Streams/Frpg2ReliableUdpMessageStream.h
    Server/Streams/Frpg2ReliableUdpPacket.cpp
    Server/Streams/Frpg2ReliableUdpPacket.h
    Server/Streams/Frpg2ReliableUdpPacketStream.cpp
    Server/Streams/Frpg2ReliableUdpPacketStream.h
    
    Server/Streams/Frpg2UdpPacket.h
    Server/Streams/Frpg2UdpPacketStream.cpp
    Server/Streams/Frpg2UdpPacketStream.h
    Server/Streams/README
    Server/WebUIService/WebUIService.cpp
    Server/WebUIService/WebUIService.h
    Server/WebUIService/Handlers/AuthHandler.cpp
    Server/WebUIService/Handlers/AuthHandler.h
    Server/WebUIService/Handlers/DebugStatisticsHandler.cpp
    Server/WebUIService/Handlers/DebugStatisticsHandler.h
    Server/WebUIService/Handlers/MessageHandler.cpp
    Server/WebUIService/Handlers/MessageHandler.h
    Server/WebUIService/Handlers/BansHandler.cpp
    Server/WebUIService/Handlers/BansHandler.h
    Server/WebUIService/Handlers/PlayersHandler.cpp
    Server/WebUIService/Handlers/PlayersHandler.h
    Server/WebUIService/Handlers/SettingsHandler.cpp
    Server/WebUIService/Handlers/SettingsHandler.h
    Server/WebUIService/Handlers/StatisticsHandler.cpp
    Server/WebUIService/Handlers/StatisticsHandler.h
    Server/WebUIService/Handlers/ShardingHandler.cpp
    Server/WebUIService/Handlers/ShardingHandler.h
    Server/WebUIService/Handlers/WebUIHandler.cpp
    Server/WebUIService/Handlers/WebUIHandler.h
)
 
add_executable(${PROJECT_NAME} ${SOURCES} ${PLATFORM_SOURCES})

target_include_directories(${PROJECT_NAME} PUBLIC 
    "${CMAKE_CURRENT_SOURCE_DIR}" 
    "${CMAKE_CURRENT_SOURCE_DIR}/../"
)

if(WIN32)
    target_compile_definitions(${PROJECT_NAME} PRIVATE
        _CRT_SECURE_NO_WARNINGS
        _WINSOCK_DEPRECATED_NO_WARNINGS
        _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS
        WIN32_LEAN_AND_MEAN
        NOMINMAX
        _WINSOCKAPI_
        _WIN32_WINNT=0x0A00
    )
else()
    target_compile_definitions(${PROJECT_NAME} PRIVATE
        _CRT_SECURE_NO_WARNINGS
        _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS
    )
endif()

# Prefer the modern FindSQLite3 target if available.
# ThirdParty/CMakeLists.txt is responsible for providing a usable `sqlite` target.
if(NOT TARGET sqlite)
    # Try to recover from missing sqlite target by aliasing known FindSQLite3 targets.
    if(TARGET SQLite::SQLite3)
        message(STATUS "SQLite::SQLite3 target found; aliasing to sqlite")
        add_library(sqlite ALIAS SQLite::SQLite3)
    elseif(TARGET SQLite3::SQLite3)
        message(STATUS "SQLite3::SQLite3 target found; aliasing to sqlite")
        add_library(sqlite ALIAS SQLite3::SQLite3)
    else()
        # Last-resort: attempt to find a system sqlite (may give variables but no target).
        find_package(SQLite3 QUIET)
        if(SQLite3_FOUND AND (DEFINED SQLite3_LIBRARIES OR DEFINED SQLite3_LIBRARY))
            message(STATUS "Found system SQLite via find_package; creating sqlite INTERFACE target")
            add_library(sqlite INTERFACE)
            if(DEFINED SQLite3_LIBRARIES AND SQLite3_LIBRARIES)
                target_link_libraries(sqlite INTERFACE ${SQLite3_LIBRARIES})
            elseif(DEFINED SQLite3_LIBRARY AND SQLite3_LIBRARY)
                target_link_libraries(sqlite INTERFACE ${SQLite3_LIBRARY})
            endif()
            if(DEFINED SQLite3_INCLUDE_DIRS AND SQLite3_INCLUDE_DIRS)
                target_include_directories(sqlite INTERFACE ${SQLite3_INCLUDE_DIRS})
            elseif(DEFINED SQLite3_INCLUDE_DIR AND SQLite3_INCLUDE_DIR)
                target_include_directories(sqlite INTERFACE ${SQLite3_INCLUDE_DIR})
            endif()
        endif()
    endif()
endif()

# If the target is still missing, attempt to directly bring in the vendored sqlite
# directory (for CI/host scenarios where ThirdParty did not create it).
if(NOT TARGET sqlite)
    if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/../ThirdParty/sqlite/CMakeLists.txt")
        message(STATUS "sqlite target still missing; adding ThirdParty/sqlite subdirectory directly")
        add_subdirectory("${CMAKE_CURRENT_SOURCE_DIR}/../ThirdParty/sqlite")
    endif()
endif()

if(NOT TARGET sqlite)
    # Diagnostic output to help troubleshoot why sqlite is missing.
    get_cmake_property(_all_targets TARGETS)
    list(FILTER _all_targets INCLUDE REGEX "[sS]qlite")
    message(STATUS "SQLite-related CMake targets: ${_all_targets}")
    message(STATUS "CMake cache entries: SQLite3_FOUND=${SQLite3_FOUND} SQLite3_LIBRARIES='${SQLite3_LIBRARIES}' SQLite3_LIBRARY='${SQLite3_LIBRARY}' SQLite3_INCLUDE_DIRS='${SQLite3_INCLUDE_DIRS}' SQLite3_INCLUDE_DIR='${SQLite3_INCLUDE_DIR}'")
    message(FATAL_ERROR "Missing required SQLite target `sqlite`. Ensure ThirdParty is configured correctly or that a system SQLite is available.")
endif()
set(DSOS_SQLITE_TARGET sqlite)

# Diagnostic output for CI: show which SQLite target we will link.
message(STATUS "Using SQLite link target: ${DSOS_SQLITE_TARGET}")
if(TARGET ${DSOS_SQLITE_TARGET})
    get_target_property(_DSOS_SQLITE_LOCATION ${DSOS_SQLITE_TARGET} IMPORTED_LOCATION)
    if(_DSOS_SQLITE_LOCATION)
        message(STATUS "${DSOS_SQLITE_TARGET} IMPORTED_LOCATION = ${_DSOS_SQLITE_LOCATION}")
    endif()
endif()

target_link_libraries(
    ${PROJECT_NAME}
    DarkSouls3
    DarkSouls2
    Shared
    civetweb
    libprotobuf-lite
    ${DSOS_SQLITE_TARGET}
    steam
)

target_include_directories(${PROJECT_NAME} PRIVATE $<TARGET_PROPERTY:civetweb,INTERFACE_INCLUDE_DIRECTORIES>)

# Copy some important files over to our output directory.
foreach(config ${CMAKE_CONFIGURATION_TYPES})
    string(TOUPPER ${config} config_upper)
    
    # Write steam appid file, required for dedicated servers.
    # Server writes this out automatically now.
    #file(WRITE "${CMAKE_RUNTIME_OUTPUT_DIRECTORY_${config_upper}}/steam_appid.txt" "374320")

    # Copy WebUI resources to output folder (if output path is configured).
    set("dst_dir" "${CMAKE_RUNTIME_OUTPUT_DIRECTORY_${config_upper}}")
    if(NOT dst_dir)
        # If runtime directory is not set yet, fall back to binary dir.
        set(dst_dir "${CMAKE_BINARY_DIR}")
    endif()

    if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/../WebUI")
        file(COPY "${CMAKE_CURRENT_SOURCE_DIR}/../WebUI" DESTINATION "${dst_dir}")
    else()
        message(WARNING "WebUI folder not found, skipping copy: ${CMAKE_CURRENT_SOURCE_DIR}/../WebUI")
    endif()
endforeach()

util_setup_folder_structure(${PROJECT_NAME} SOURCES "DS3OS")

#-----------------------------------------------------------------------------#
# Client emulator config unit tests
#-----------------------------------------------------------------------------#
add_executable(ServerClientConfigTests
    Client/Tests/ClientConfigTests.cpp
)

target_include_directories(ServerClientConfigTests PUBLIC
    "${CMAKE_CURRENT_SOURCE_DIR}"
)

if(TARGET ServerClientConfigTests)
    target_include_directories(ServerClientConfigTests PRIVATE $<TARGET_PROPERTY:civetweb,INTERFACE_INCLUDE_DIRECTORIES>)
endif()

target_link_libraries(ServerClientConfigTests PRIVATE
    Shared
    steam
    libprotobuf-lite
)

add_test(NAME ServerClientConfigTests COMMAND ServerClientConfigTests)

