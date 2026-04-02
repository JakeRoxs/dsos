# ================================================================================================
#  Rekindled Server
#  Copyright (C) 2021 Tim Leonard
#  Copyright (C) 2026 Jake Morgeson
# ================================================================================================

project(DarkSouls2 C CXX)

SET(SOURCES
    Protobuf/DS2_Protobufs.h
    Protobuf/Generated/DS2_Frpg2PlayerData.pb.cc
    Protobuf/Generated/DS2_Frpg2RequestMessage.pb.cc
    
    Server/GameService/DS2_PlayerState.h
    
    Server/GameService/GameManagers/Boot/DS2_BootManager.cpp
    Server/GameService/GameManagers/Boot/DS2_BootManager.h
    Server/GameService/GameManagers/PlayerData/DS2_PlayerDataManager.cpp
    Server/GameService/GameManagers/PlayerData/DS2_PlayerDataManager.h
    Server/GameService/GameManagers/Ghosts/DS2_GhostManager.cpp
    Server/GameService/GameManagers/Ghosts/DS2_GhostManager.h
    Server/GameService/GameManagers/BloodMessage/DS2_BloodMessageManager.cpp
    Server/GameService/GameManagers/BloodMessage/DS2_BloodMessageManager.h
    Server/GameService/GameManagers/Bloodstain/DS2_BloodstainManager.cpp
    Server/GameService/GameManagers/Bloodstain/DS2_BloodstainManager.h
    Server/GameService/GameManagers/Signs/DS2_SignManager.cpp
    Server/GameService/GameManagers/Signs/DS2_SignManager.h
    Server/GameService/GameManagers/BreakIn/DS2_BreakInManager.cpp
    Server/GameService/GameManagers/BreakIn/DS2_BreakInManager.h
    Server/GameService/GameManagers/Logging/DS2_LoggingManager.cpp
    Server/GameService/GameManagers/Logging/DS2_LoggingManager.h
    Server/GameService/GameManagers/Misc/DS2_MiscManager.cpp
    Server/GameService/GameManagers/Misc/DS2_MiscManager.h
    Server/GameService/GameManagers/Visitor/DS2_VisitorManager.cpp
    Server/GameService/GameManagers/Visitor/DS2_VisitorManager.h
    Server/GameService/GameManagers/Ranking/DS2_RankingManager.cpp
    Server/GameService/GameManagers/Ranking/DS2_RankingManager.h
    Server/GameService/GameManagers/MirrorKnight/DS2_MirrorKnightManager.cpp
    Server/GameService/GameManagers/MirrorKnight/DS2_MirrorKnightManager.h
    Server/GameService/GameManagers/QuickMatch/DS2_QuickMatchManager.cpp
    Server/GameService/GameManagers/QuickMatch/DS2_QuickMatchManager.h
    
    Server/GameService/Utils/DS2_GameIds.h
    Server/GameService/Utils/DS2_CellAndAreaId.h
    Server/GameService/Utils/DS2_NRSSRSanitizer.h

    Server/GameService/Utils/Ids/BonfireId.inc
    Server/GameService/Utils/Ids/CharacterTypeId.inc
    Server/GameService/Utils/Ids/CovenantId.inc
    Server/GameService/Utils/Ids/GestureId.inc
    Server/GameService/Utils/Ids/InvasionTypeId.inc
    Server/GameService/Utils/Ids/ItemId.inc
    Server/GameService/Utils/Ids/OnlineAreaId.inc
    Server/GameService/Utils/Ids/TeamTypeId.inc
    Server/GameService/Utils/Ids/BossId.inc    
    Server/GameService/Utils/Ids/BloodMessageConjunction.inc
    Server/GameService/Utils/Ids/BloodMessageGesture.inc
    Server/GameService/Utils/Ids/BloodMessageTemplate.inc
    Server/GameService/Utils/Ids/BloodMessageWord.inc    

    Server/Streams/DS2_Frpg2ReliableUdpMessageTypes.inc
    Server/Streams/DS2_Frpg2ReliableUdpMessage.h
    
    Server/DS2_Game.h
    Server/DS2_Game.cpp
)
 
add_library(${PROJECT_NAME} STATIC ${SOURCES} ${PLATFORM_SOURCES})

target_include_directories(${PROJECT_NAME} PUBLIC 
    "${CMAKE_CURRENT_SOURCE_DIR}" 
    "${CMAKE_CURRENT_SOURCE_DIR}/../"
    "${CMAKE_CURRENT_SOURCE_DIR}/../Server"
)

# the following warnings-suppressing definitions are only meaningful on
# MSVC/Windows; other platforms don’t need them (and including unknown
# macros confuses certain toolchains).
if(MSVC OR WIN32)
    target_compile_definitions(${PROJECT_NAME} PRIVATE
        _CRT_SECURE_NO_WARNINGS
        _WINSOCK_DEPRECATED_NO_WARNINGS
        _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS
    )
endif()

# always define the game-specific flag regardless of platform
target_compile_definitions(${PROJECT_NAME} PUBLIC BUILD_DARKSOULS2)

target_link_libraries(
    ${PROJECT_NAME}
    Shared
    civetweb
    libprotobuf-lite
    sqlite
    steam
)

util_setup_folder_structure(${PROJECT_NAME} SOURCES "Games")
