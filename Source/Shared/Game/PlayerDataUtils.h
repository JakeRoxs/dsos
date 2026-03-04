#pragma once

// Utilities shared by both DS2 and DS3 PlayerDataManager implementations.
// Contains cross-game helpers to reduce duplication.

#include "Server/Server.h"
#include "Server/GameService/GameClient.h"

// forward declarations for the two game player-state types;
// concrete headers are included in the files that instantiate templates.
struct DS2_PlayerState;
namespace DS2_Frpg2PlayerData { class AllStatus; }
enum class DS2_OnlineAreaId : int;

struct DS3_PlayerState;
namespace DS3_Frpg2PlayerData { class AllStatus; }
enum class DS3_OnlineAreaId : int;

// STL helpers used by various utilities below.

#include <vector>
#include <algorithm>

#include "Shared/Core/Utils/Logging.h"
#include "Shared/Core/Utils/Strings.h"

// The helpers in this header occasionally need to produce log output.  
struct DefaultLogger
{
    template<typename... Args>
    void operator()(const char* who, const char* fmt, Args&&... args) const
    {
        LogS(who, fmt, std::forward<Args>(args)...);
    }
};

// Discord notification helper (bonfire lit).

template<typename BonfireEnum>
inline void SendBonfireDiscord(Server* server,
                                   const std::shared_ptr<GameClient>& client,
                                   uint32_t bonfireId)
{
    if (!server->GetConfig().SendDiscordNotice_BonfireLit)
        return;

    std::string bonfireName = GetEnumString<BonfireEnum>((BonfireEnum)bonfireId);
    if (bonfireName.empty())
        return;

    server->SendDiscordNotice(client, DiscordNoticeType::BonfireLit,
        StringFormat("Lit the '%s' bonfire.", bonfireName.c_str()));
}

// -----------------------------------------------------------------------------
// Status merging with per-game clearing logic.

// forward declarations for traits
template<typename PlayerState, typename AllStatus> struct StatusClearer;




// generic entry point used by both games

template<typename PlayerState, typename AllStatus>
inline void MergePlayerStatusDelta(PlayerState& state, const AllStatus& status)
{
    StatusClearer<PlayerState, AllStatus>::Clear(state, status);
    state.GetPlayerStatus_Mutable().MergeFrom(status);
}

// Area change helper; specialised by each game's source file.
//
// The shared header avoids game knowledge entirely now – the DS2/DS3
// translation units provide explicit template specialisations with the
// appropriate logic.  This keeps the header clean and free of
// BUILD_DARKSOULS{2,3} clutter.

template<typename PlayerState>
inline void HandleAreaChange(PlayerState& /*state*/, const std::shared_ptr<GameClient>& /*client*/)
{
    // no-op; see game-specific specialization
}

// Rename connection when player character name changes.

template<typename PlayerStateType, typename Logger = DefaultLogger>
inline void RenameConnectionIfCharacterNameChanged(PlayerStateType& state,
                                                   const std::shared_ptr<GameClient>& client,
                                                   Logger log = {})
{
    if (!state.GetPlayerStatus().player_status().has_name())
        return;

    std::string newName = state.GetPlayerStatus().player_status().name();
    if (state.GetCharacterName() == newName)
        return;

    state.SetCharacterName(newName);
    std::string newConnName = StringFormat("%i:%s", state.GetPlayerId(), state.GetCharacterName().c_str());
    log(client->GetName().c_str(), "Renaming connection to '%s'.", newConnName.c_str());
    client->Connection->Rename(newConnName);
}


// trait mapping for bonfire enums
template<typename PlayerState> struct BonfireEnumFor;

// forward-declare the actual enum types used by the two games.
enum class DS3_BonfireId : int;


// extracts the list of currently unlocked/lit bonfire IDs for the state

template<typename PlayerState> struct BonfireAccessor;
// public template used by the managers

template<typename PlayerState, typename Logger = DefaultLogger>
inline void ProcessBonfires(PlayerState& state, Server* server, const std::shared_ptr<GameClient>& client,
                            Logger log = {})
{
    std::vector<uint32_t>& litBonfires = state.GetLitBonfires_Mutable();
    std::vector<uint32_t> current;
    BonfireAccessor<PlayerState>::Collect(state, current);

    for (uint32_t bonfireId : current)
    {
        if (std::find(litBonfires.begin(), litBonfires.end(), bonfireId) != litBonfires.end())
            continue;

        if (state.GetHasInitialState())
        {
            log(client->GetName().c_str(), "Has lit bonfire %i.", bonfireId);
            SendBonfireDiscord<typename BonfireEnumFor<PlayerState>::type>(server, client, bonfireId);
        }

        litBonfires.push_back(bonfireId);
    }
}

