#pragma once

// Common utilities used by DS2/DS3 PlayerDataManager implementations.
// This consolidates operations that are shared between the two
// managers, such as logging, connection renaming, visitor-pool
// computation, and Discord notifications.  Previously similar logic
// was duplicated in both managers or scattered across small headers
// (e.g. BonfireNotify); moving everything here reduces maintenance
// burden.

#include "Server/Server.h"
#include "Server/GameService/GameClient.h"

// DS2 headers aren't present when only building the DS3 target; use
// __has_include so the shared utilities can be compiled by either project
// without forcing the DS2 include path to be added globally.
#if __has_include("Server/GameService/DS2_PlayerState.h")
#  include "Server/GameService/DS2_PlayerState.h"
#  include "Server/Streams/DS2_Frpg2ReliableUdpMessage.h"
#  include "Server/GameService/Utils/DS2_GameIds.h"  // needed for DS2_OnlineAreaId etc.
#else
// forward-declare a few DS2 types used in templates so that this header
// stays valid even when the DS2 target isn't part of the build.
struct DS2_PlayerState;
namespace DS2_Frpg2PlayerData {
    class AllStatus;
}
enum class DS2_OnlineAreaId : int;
#endif

// DS3 headers may also be absent when only building the DS2 target; mirror
// the DS2 pattern above so this header stays self-contained.
#if __has_include("Server/GameService/DS3_PlayerState.h")
#  include "Server/GameService/DS3_PlayerState.h"
#  include "Server/Streams/DS3_Frpg2ReliableUdpMessage.h"
#else
// forward declarations for the few DS3 types referenced in templates
struct DS3_PlayerState;
namespace DS3_Frpg2PlayerData {
    class AllStatus;
}
enum class DS3_OnlineAreaId : int;
#endif

#include "Shared/Core/Utils/Logging.h"
#include "Shared/Core/Utils/Strings.h"

// Send a bonfire-lit discord notice if the configuration allows it
// and the bonfire id resolves to a non-empty name.  Enum type is
// templated so DS2/DS3 each pass their respective bonfire enum.

template<typename BonfireEnum>
inline void MaybeSendBonfireDiscord(Server* server,
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
// Player status merging helpers
//
// Each game needs to clear a small list of arrays before calling
// MergeFrom.  Rather than maintain two almost-identical overloads, we use
// a little traits machinery below.  The public MergePlayerStatusDelta template
// is the only symbol used by consumers.

// forward declarations for traits
template<typename PlayerState, typename AllStatus> struct StatusClearer;

// DS3 specialization (only if DS3 headers are present)
#if __has_include("Server/GameService/DS3_PlayerState.h")
template<>
struct StatusClearer<DS3_PlayerState, DS3_Frpg2PlayerData::AllStatus>
{
    static void Clear(DS3_PlayerState& state, const DS3_Frpg2PlayerData::AllStatus& status)
    {
        if (status.has_player_status() && status.player_status().played_areas_size() > 0)
        {
            state.GetPlayerStatus_Mutable().mutable_player_status()->clear_played_areas();
        }
        if (status.has_player_status() && status.player_status().unknown_18_size() > 0)
        {
            state.GetPlayerStatus_Mutable().mutable_player_status()->clear_unknown_18();
        }
        if (status.has_player_status() && status.player_status().anticheat_data_size() > 0)
        {
            state.GetPlayerStatus_Mutable().mutable_player_status()->clear_anticheat_data();
        }
    }
};
#endif

// DS2 specialization (only available if DS2 headers were included above)
#if __has_include("Server/GameService/DS2_PlayerState.h")
template<>
struct StatusClearer<DS2_PlayerState, DS2_Frpg2PlayerData::AllStatus>
{
    static void Clear(DS2_PlayerState& state, const DS2_Frpg2PlayerData::AllStatus& status)
    {
        if (status.has_player_status() && status.player_status().played_areas_size() > 0)
        {
            state.GetPlayerStatus_Mutable().mutable_player_status()->clear_played_areas();
        }
        if (status.has_stats_info())
        {
            auto* stats = state.GetPlayerStatus_Mutable().mutable_stats_info();
            stats->clear_bonfire_levels();
            stats->clear_phantom_type_count_6();
            stats->clear_phantom_type_count_7();
            stats->clear_phantom_type_count_8();
            stats->clear_phantom_type_count_9();
            stats->clear_unknown_11();
            stats->clear_unlocked_bonfires();
            stats->clear_unknown_21();
        }
    }
};
#endif


// generic entry point used by both games

template<typename PlayerState, typename AllStatus>
inline void MergePlayerStatusDelta(PlayerState& state, const AllStatus& status)
{
    StatusClearer<PlayerState, AllStatus>::Clear(state, status);
    state.GetPlayerStatus_Mutable().MergeFrom(status);
}

// Handle online location logging and area state updates.  The DS2 code
// includes an extra check for "online activity area", so we use a single
// template with constexpr branches rather than duplicate the bulk of the
// logic.

template<typename PlayerState>
inline void HandleAreaChange(PlayerState& state, const std::shared_ptr<GameClient>& client)
{
    if (!state.GetPlayerStatus().has_player_location())
        return;

    // choose the correct online-area enum depending on which game state we
    // are operating on.  We avoid naming DS2_OnlineAreaId in builds that don't
    // have the DS2 headers available by using an if constexpr instead of
    // std::conditional_t.
    
    if constexpr (std::is_same_v<PlayerState, DS3_PlayerState>)
    {
        DS3_OnlineAreaId AreaId = static_cast<DS3_OnlineAreaId>(
            state.GetPlayerStatus().player_location().online_area_id());
        if (AreaId != state.GetCurrentArea() && AreaId != DS3_OnlineAreaId::None)
        {
            VerboseS(client->GetName().c_str(), "User has entered '%s' (0x%08x)",
                     GetEnumString(AreaId).c_str(), AreaId);
            state.SetCurrentArea(AreaId);
        }
    }
    else
    {
        // DS2 path: DS2_OnlineAreaId is guaranteed to be defined when the DS2
        // headers are available (we included DS2_GameIds.h above inside
        // __has_include guard).  This branch will be discarded when building
        // for DS3 since the condition is false, so a missing DS2 type won't be
        // an error.
        using AreaIdType = DS2_OnlineAreaId;
        AreaIdType AreaId = static_cast<AreaIdType>(
            state.GetPlayerStatus().player_location().online_area_id());
        if (AreaId != state.GetCurrentArea() && AreaId != AreaIdType::None)
        {
            VerboseS(client->GetName().c_str(), "User has entered '%s' (0x%08x)",
                     GetEnumString(AreaId).c_str(), AreaId);
            state.SetCurrentArea(AreaId);
        }
    }

    if constexpr (std::is_same_v<PlayerState, DS2_PlayerState>)
    {
        if (state.GetPlayerStatus().player_location().has_online_activity_area_id())
        {
            int OnlineActivityAreaId = state.GetPlayerStatus().player_location().online_activity_area_id();
            if (OnlineActivityAreaId != state.GetCurrentOnlineActivityArea())
            {
                VerboseS(client->GetName().c_str(),
                         "User has entered online activity area (0x%08x)",
                         OnlineActivityAreaId);
                state.SetCurrentOnlineActivityArea(OnlineActivityAreaId);
            }
        }
    }
}

// Update character id field from player status if present.
// DS2 doesn't expose this field, so the template either compiles to nothing
// or works if the accessors exist.

template<typename PlayerState>
inline void UpdateCharacterId(PlayerState& state)
{
    if constexpr (std::is_same_v<PlayerState, DS3_PlayerState>)
    {
        if (state.GetPlayerStatus().player_status().has_character_id())
            state.SetCharacterId(state.GetPlayerStatus().player_status().character_id());
    }
}

// Update matchmaking state for either game.  DS3 reads fields directly
// from the status packet; DS2 derives invadability from several flags.

template<typename PlayerState>
inline void UpdateMatchmakingState(PlayerState& state)
{
    if (!state.GetPlayerStatus().has_player_status())
        return;

    if constexpr (std::is_same_v<PlayerState, DS3_PlayerState>)
    {
        if (state.GetPlayerStatus().player_status().has_is_invadable())
        {
            bool NewState = state.GetPlayerStatus().player_status().is_invadable();
            if (NewState != state.GetIsInvadable())
            {
                VerboseS("", "User is now %s", NewState ? "invadable" : "no longer invadable");
                state.SetIsInvadable(NewState);
            }
        }

        if (state.GetPlayerStatus().player_status().has_soul_level())
            state.SetSoulLevel(state.GetPlayerStatus().player_status().soul_level());

        if (state.GetPlayerStatus().player_status().has_max_weapon_level())
            state.SetMaxWeaponLevel(state.GetPlayerStatus().player_status().max_weapon_level());
    }
    else // DS2
    {
        bool NewState = true;
        if (state.GetPlayerStatus().player_status().sitting_at_bonfire() ||
            state.GetPlayerStatus().player_status().human_effigy_burnt() ||
            state.GetCurrentOnlineActivityArea() == 0)
        {
            NewState = false;
        }

        if (NewState != state.GetIsInvadable())
        {
            VerboseS("", "User is now %s", NewState ? "invadable" : "no longer invadable");
            state.SetIsInvadable(NewState);
        }

        if (state.GetPlayerStatus().player_status().has_soul_level())
            state.SetSoulLevel(state.GetPlayerStatus().player_status().soul_level());
    }
}

// If the character name has changed in the provided state, update the
// state, log the event, and rename the client connection accordingly.
// Works for both DS2_PlayerState and DS3_PlayerState since they share
// the necessary accessors.

template<typename PlayerStateType>
inline void RenameConnectionIfCharacterNameChanged(PlayerStateType& state,
                                                   const std::shared_ptr<GameClient>& client)
{
    if (!state.GetPlayerStatus().player_status().has_name())
        return;

    std::string newName = state.GetPlayerStatus().player_status().name();
    if (state.GetCharacterName() == newName)
        return;

    state.SetCharacterName(newName);
    std::string newConnName = StringFormat("%i:%s", state.GetPlayerId(), state.GetCharacterName().c_str());
    LogS(client->GetName().c_str(), "Renaming connection to '%s'.", newConnName.c_str());
    client->Connection->Rename(newConnName);
}

// Determine visitor pool for DS2.
inline DS2_Frpg2RequestMessage::VisitorType DetermineVisitorPool(const DS2_PlayerState& state)
{
    DS2_Frpg2RequestMessage::VisitorType pool = DS2_Frpg2RequestMessage::VisitorType::VisitorType_None;
    if (state.GetPlayerStatus().item_using_info().has_guardians_seal() &&
        state.GetPlayerStatus().item_using_info().guardians_seal() &&
        state.GetPlayerStatus().player_status().covenant() == (int)DS2_CovenantId::Blue_Sentinels &&
        state.GetCurrentOnlineActivityArea() != 0)
    {
        pool = DS2_Frpg2RequestMessage::VisitorType::VisitorType_BlueSentinels;
    }
    if (state.GetPlayerStatus().item_using_info().has_bell_keepers_seal() &&
        state.GetPlayerStatus().item_using_info().bell_keepers_seal() &&
        state.GetPlayerStatus().player_status().covenant() == (int)DS2_CovenantId::Bell_Keepers &&
        (state.GetCurrentOnlineActivityArea() == 0x18e3e || state.GetCurrentOnlineActivityArea() == 0x18d08))
    {
        pool = DS2_Frpg2RequestMessage::VisitorType::VisitorType_BellKeepers;
    }
    if (state.GetPlayerStatus().player_status().covenant() != (int)DS2_CovenantId::Rat_King_Covenant &&
        state.GetCurrentOnlineActivityArea() == 0x193f2)
    {
        pool = DS2_Frpg2RequestMessage::VisitorType::VisitorType_Rat;
    }
    return pool;
}

// Determine visitor pool for DS3.
inline DS3_Frpg2RequestMessage::VisitorPool DetermineVisitorPool(const DS3_PlayerState& state)
{
    DS3_Frpg2RequestMessage::VisitorPool pool = DS3_Frpg2RequestMessage::VisitorPool::VisitorPool_None;
    if (state.GetPlayerStatus().player_status().has_can_summon_for_way_of_blue() &&
        state.GetPlayerStatus().player_status().can_summon_for_way_of_blue())
    {
        pool = DS3_Frpg2RequestMessage::VisitorPool::VisitorPool_Way_of_Blue;
    }
    if (state.GetPlayerStatus().player_status().has_can_summon_for_watchdog_of_farron() &&
        state.GetPlayerStatus().player_status().can_summon_for_watchdog_of_farron())
    {
        pool = DS3_Frpg2RequestMessage::VisitorPool::VisitorPool_Watchdog_of_Farron;
    }
    if (state.GetPlayerStatus().player_status().has_can_summon_for_aldritch_faithful() &&
        state.GetPlayerStatus().player_status().can_summon_for_aldritch_faithful())
    {
        pool = DS3_Frpg2RequestMessage::VisitorPool::VisitorPool_Aldrich_Faithful;
    }
    if (state.GetPlayerStatus().player_status().has_can_summon_for_spear_of_church() &&
        state.GetPlayerStatus().player_status().can_summon_for_spear_of_church())
    {
        pool = DS3_Frpg2RequestMessage::VisitorPool::VisitorPool_Spear_of_the_Church;
    }
    return pool;
}

// -----------------------------------------------------------------------------
// Bonfire processing helpers
//
// The two overloads above were almost identical; the only differences were
// how the current list of bonfires was fetched and which enum to pass to
// MaybeSendBonfireDiscord.  We define a small traits API so the public
// template can remain terse.

// maps a player state type to the corresponding bonfire enum used by
// MaybeSendBonfireDiscord

template<typename PlayerState> struct BonfireEnumFor;
template<> struct BonfireEnumFor<DS3_PlayerState> { using type = DS3_BonfireId; };
template<> struct BonfireEnumFor<DS2_PlayerState> { using type = DS2_BonfireId; };

// extracts the list of currently unlocked/lit bonfire IDs for the state

template<typename PlayerState> struct BonfireAccessor;

template<>
struct BonfireAccessor<DS3_PlayerState>
{
    static void Collect(const DS3_PlayerState& state, std::vector<uint32_t>& out)
    {
        if (!state.GetPlayerStatus().has_play_data())
            return;
        for (int i = 0; i < state.GetPlayerStatus().play_data().bonfire_info_size(); ++i)
        {
            const auto& bon = state.GetPlayerStatus().play_data().bonfire_info(i);
            if (bon.has_been_lit())
                out.push_back(bon.bonfire_id());
        }
    }
};

template<>
struct BonfireAccessor<DS2_PlayerState>
{
    static void Collect(const DS2_PlayerState& state, std::vector<uint32_t>& out)
    {
        if (!state.GetPlayerStatus().has_stats_info())
            return;
        for (int i = 0; i < state.GetPlayerStatus().stats_info().unlocked_bonfires_size(); ++i)
            out.push_back(state.GetPlayerStatus().stats_info().unlocked_bonfires(i));
    }
};

// public template used by the managers

template<typename PlayerState>
inline void ProcessBonfires(PlayerState& state, Server* server, const std::shared_ptr<GameClient>& client)
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
            LogS(client->GetName().c_str(), "Has lit bonfire %i.", bonfireId);
            MaybeSendBonfireDiscord<typename BonfireEnumFor<PlayerState>::type>(server, client, bonfireId);
        }

        litBonfires.push_back(bonfireId);
    }
}

