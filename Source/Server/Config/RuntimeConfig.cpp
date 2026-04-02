/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Config/RuntimeConfig.h"
#include "Shared/Core/Utils/File.h"
#include "Shared/Core/Utils/Logging.h"

#include <cfloat>

bool RuntimeConfig::Save(const std::filesystem::path& Path)
{
    nlohmann::json json;

    Serialize(json, false);

    if (!WriteTextToFile(Path, json.dump(4)))
    {
        return false;
    }

    return true;
}

bool RuntimeConfig::Load(const std::filesystem::path& Path)
{
    std::string JsonText;

    if (!ReadTextFromFile(Path, JsonText))
    {
        return false;
    }

    try
    {
        nlohmann::json json = nlohmann::json::parse(JsonText);
        Serialize(json, true);
    }
    catch (nlohmann::json::parse_error)
    {
        return false;
    }

    ApplyEnvironmentOverrides();
    return true;
}

namespace
{
    bool ReadEnvString(const char* Name, std::string& OutValue)
    {
        const char* Value = std::getenv(Name);
        if (!Value || *Value == '\0')
        {
            return false;
        }
        OutValue = Value;
        return true;
    }

    static char ToLowerAscii(char c)
    {
        // std::tolower expects either EOF or an unsigned char cast to int.
        // Casting through unsigned char avoids undefined behavior on platforms
        // where `char` is signed.
        return static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    }

    bool ReadEnvBool(const char* Name, bool& OutValue)
    {
        std::string Value;
        if (!ReadEnvString(Name, Value))
        {
            return false;
        }

        std::transform(Value.begin(), Value.end(), Value.begin(), ToLowerAscii);
        if (Value == "1" || Value == "true" || Value == "yes" || Value == "on")
        {
            OutValue = true;
            return true;
        }
        if (Value == "0" || Value == "false" || Value == "no" || Value == "off")
        {
            OutValue = false;
            return true;
        }

        Warning("Invalid value for %s environment variable: '%s'", Name, Value.c_str());
        return false;
    }

    bool ReadEnvInt(const char* Name, int& OutValue)
    {
        std::string Value;
        if (!ReadEnvString(Name, Value))
        {
            return false;
        }

        try
        {
            OutValue = std::stoi(Value);
            return true;
        }
        catch (const std::exception& e)
        {
            Warning("Invalid integer value for %s environment variable '%s': %s", Name, Value.c_str(), e.what());
            return false;
        }
        catch (...)
        {
            Warning("Invalid integer value for %s environment variable '%s'", Name, Value.c_str());
            return false;
        }
    }

    bool ReadEnvFloat(const char* Name, float& OutValue)
    {
        std::string Value;
        if (!ReadEnvString(Name, Value))
        {
            return false;
        }

        try
        {
            OutValue = std::stof(Value);
            return true;
        }
        catch (const std::exception& e)
        {
            Warning("Invalid float value for %s environment variable '%s': %s", Name, Value.c_str(), e.what());
            return false;
        }
        catch (...)
        {
            Warning("Invalid float value for %s environment variable '%s'", Name, Value.c_str());
            return false;
        }
    }
}

void RuntimeConfig::ApplyEnvironmentOverrides()
{
    // Master server settings (legacy env vars kept for backwards compatibility).
    if (ReadEnvString("MASTER_SERVER_IP", MasterServerIp))
    {
        Log("Overriding MasterServerIp from environment: %s", MasterServerIp.c_str());
    }
    if (ReadEnvInt("MASTER_SERVER_PORT", MasterServerPort))
    {
        Log("Overriding MasterServerPort from environment: %d", MasterServerPort);
    }

    // General runtime configuration overrides.
    if (ReadEnvString("SERVER_NAME", ServerName))
    {
        Log("Overriding ServerName from environment: %s", ServerName.c_str());
    }
    if (ReadEnvString("SERVER_DESCRIPTION", ServerDescription))
    {
        Log("Overriding ServerDescription from environment: %s", ServerDescription.c_str());
    }
    if (ReadEnvString("SERVER_HOSTNAME", ServerHostname))
    {
        Log("Overriding ServerHostname from environment: %s", ServerHostname.c_str());
    }
    if (ReadEnvString("SERVER_PRIVATE_HOSTNAME", ServerPrivateHostname))
    {
        Log("Overriding ServerPrivateHostname from environment: %s", ServerPrivateHostname.c_str());
    }

    if (ReadEnvBool("QUIET_LOGGING", QuietLogging))
    {
        Log("Overriding QuietLogging from environment: %s", QuietLogging ? "true" : "false");
    }
    if (ReadEnvBool("ADVERTISE", Advertise))
    {
        Log("Overriding Advertise from environment: %s", Advertise ? "true" : "false");
    }
    if (ReadEnvFloat("ADVERTISE_HEARTBEAT_TIME", AdvertiseHearbeatTime))
    {
        Log("Overriding AdvertiseHeartbeatTime from environment: %.2f", AdvertiseHearbeatTime);
    }
    if (ReadEnvBool("SUPPORT_SHARDING", SupportSharding))
    {
        Log("Overriding SupportSharding from environment: %s", SupportSharding ? "true" : "false");
    }

    if (ReadEnvString("PASSWORD", Password))
    {
        Log("Overriding Password from environment.");
    }

    if (ReadEnvString("MODS_WHITELIST", ModsWhitelist))
    {
        Log("Overriding ModsWhitelist from environment: %s", ModsWhitelist.c_str());
    }
    if (ReadEnvString("MODS_BLACKLIST", ModsBlacklist))
    {
        Log("Overriding ModsBlacklist from environment: %s", ModsBlacklist.c_str());
    }
    if (ReadEnvString("MODS_REQUIRED_LIST", ModsRequiredList))
    {
        Log("Overriding ModsRequiredList from environment: %s", ModsRequiredList.c_str());
    }

    if (ReadEnvInt("LOGIN_SERVER_PORT", LoginServerPort))
    {
        Log("Overriding LoginServerPort from environment: %d", LoginServerPort);
    }
    if (ReadEnvInt("AUTH_SERVER_PORT", AuthServerPort))
    {
        Log("Overriding AuthServerPort from environment: %d", AuthServerPort);
    }
    if (ReadEnvInt("GAME_SERVER_PORT", GameServerPort))
    {
        Log("Overriding GameServerPort from environment: %d", GameServerPort);
    }
    if (ReadEnvInt("WEBUI_SERVER_PORT", WebUIServerPort))
    {
        Log("Overriding WebUIServerPort from environment: %d", WebUIServerPort);
    }
}

template <typename DataType>
void SerializeVar(nlohmann::json& Json, const char* Name, DataType& Value, bool Loading)
{
    if (Loading)
    {
        if (Json.contains(Name))
        {
            Value = Json[Name];
        }
    }
    else
    {
        Json[Name] = Value;
    }
}

template <typename DataType>
void SerializeStructVar(nlohmann::json& Json, const char* Name, DataType& Value, bool Loading)
{
    if (Loading)
    {
        if (Json.contains(Name))
        {
            Value.Serialize(Json[Name], Loading);
        }
    }
    else
    {
        Value.Serialize(Json[Name], Loading);
    }
}

template <typename DataType>
void SerializeVar(nlohmann::json& Json, const char* Name, std::vector<DataType>& Value, bool Loading)
{
    if (Loading)
    {
        if (Json.contains(Name))
        {
            Value.resize(Json[Name].size());

            int Index = 0;
            for (auto& ChildValue : Json[Name].items())
            {
                Value[Index++].Serialize(ChildValue.value(), Loading);
            }
        }
    }
    else
    {
        nlohmann::json Array = nlohmann::json::array();

        for (auto& ChildValue : Value)
        {
            nlohmann::json ChildNode;
            ChildValue.Serialize(ChildNode, Loading);
            Array.push_back(ChildNode);
        }

        Json[Name] = Array;
    }
}

template <>
void SerializeVar(nlohmann::json& Json, const char* Name, std::vector<int>& Value, bool Loading)
{
    if (Loading)
    {
        if (Json.contains(Name))
        {
            Value.resize(Json[Name].size());

            int Index = 0;
            for (auto& ChildValue : Json[Name].items())
            {
                Value[Index++] = ChildValue.value();
            }
        }
    }
    else
    {
        nlohmann::json Array = nlohmann::json::array();

        for (auto& ChildValue : Value)
        {
            Array.push_back(ChildValue);
        }

        Json[Name] = Array;
    }
}

bool RuntimeConfigMatchingParameters::CheckMatch(int HostSoulLevel, int HostWeaponLevel, int ClientSoulLevel, int ClientWeaponLevel, bool HasPassword) const
{
    // Can ignore all of this if we have a password.
    if (PasswordDisablesLimits && HasPassword)
    {
        return true;
    }

    if (!DisableLevelMatching)
    {
        float lower_limit = (HostSoulLevel * LowerLimitMultiplier) + LowerLimitModifier;
        float upper_limit = (HostSoulLevel * UpperLimitMultiplier) + UpperLimitModifier;

        // When host is above 351 upper limit gets removed. 
        if (HostSoulLevel >= RangeRemovalLevel)
        {
            lower_limit = (float)RangeRemovalLevel;
            upper_limit = FLT_MAX;
        }

        // If match falls outside bounds host can't match with them.
        if (ClientSoulLevel < lower_limit || ClientSoulLevel > upper_limit)
        {
            return false;
        }
    }

    if (!DisableWeaponLevelMatching)
    {
        if (ClientWeaponLevel > WeaponLevelUpperLimit[HostWeaponLevel])
        {
            return false;
        }
        if (HostWeaponLevel > WeaponLevelUpperLimit[ClientWeaponLevel])
        {
            return false;
        }
    }

    return true;
}

int RuntimeConfigSoulMemoryMatchingParameters::CalculateTier(int SoulMemory) const
{
    int Tier = 0;
    for (size_t i = 0; i < Tiers.size(); i++)
    {
        if (SoulMemory > Tiers[i])
        {
            Tier++;
        }
        else
        {
            break;
        }
    }

    return Tier;
}

bool RuntimeConfigSoulMemoryMatchingParameters::CheckMatch(int HostSoulMemory, int ClientSoulMemory, bool UsingPassword) const
{
    if (DisableSoulMemoryMatching)
    {
        return true;
    }

    int HostSoulTier = CalculateTier(HostSoulMemory);
    int ClientSoulTier = CalculateTier(ClientSoulMemory);

    int LowerLimit = HostSoulTier;
    int UpperLimit = HostSoulTier;

    if (UsingPassword)
    {
        LowerLimit -= TiersBelowWithPassword;
        UpperLimit += TiersAboveWithPassword;
    }
    else
    {
        LowerLimit -= TiersBelow;
        UpperLimit += TiersAbove;
    }

    return (ClientSoulTier >= LowerLimit && ClientSoulTier <= UpperLimit);
}

#define SERIALIZE_VAR(x) SerializeVar(Json, #x, x, Loading);
#define SERIALIZE_STRUCT_VAR(x) SerializeStructVar(Json, #x, x, Loading);

bool RuntimeConfigAnnouncement::Serialize(nlohmann::json& Json, bool Loading)
{
    SERIALIZE_VAR(Header);
    SERIALIZE_VAR(Body);

    return true;
}

bool RuntimeConfigSoulMemoryMatchingParameters::Serialize(nlohmann::json& Json, bool Loading)
{
    SERIALIZE_VAR(Tiers);
    SERIALIZE_VAR(DisableSoulMemoryMatching);
    SERIALIZE_VAR(TiersBelow);
    SERIALIZE_VAR(TiersAbove);
    SERIALIZE_VAR(TiersBelowWithPassword);
    SERIALIZE_VAR(TiersAboveWithPassword);

    return true;
}

bool RuntimeConfigMatchingParameters::Serialize(nlohmann::json& Json, bool Loading)
{
    SERIALIZE_VAR(LowerLimitMultiplier);
    SERIALIZE_VAR(LowerLimitModifier);
    SERIALIZE_VAR(UpperLimitMultiplier);
    SERIALIZE_VAR(UpperLimitModifier);
    SERIALIZE_VAR(WeaponLevelUpperLimit);
    SERIALIZE_VAR(RangeRemovalLevel);
    SERIALIZE_VAR(PasswordDisablesLimits);
    SERIALIZE_VAR(DisableLevelMatching);
    SERIALIZE_VAR(DisableWeaponLevelMatching);
    
    return true;
}

bool RuntimeConfig::Serialize(nlohmann::json& Json, bool Loading)
{
    SERIALIZE_VAR(ServerId);
    SERIALIZE_VAR(ServerName);
    SERIALIZE_VAR(ServerDescription);
    SERIALIZE_VAR(ServerHostname);
    SERIALIZE_VAR(ServerPrivateHostname);
    SERIALIZE_VAR(GameType);

    SERIALIZE_VAR(QuietLogging);

    SERIALIZE_VAR(MasterServerIp);
    SERIALIZE_VAR(MasterServerPort);
    SERIALIZE_VAR(Advertise);
    SERIALIZE_VAR(AdvertiseHearbeatTime);
    SERIALIZE_VAR(Password);
    SERIALIZE_VAR(ModsWhitelist);
    SERIALIZE_VAR(ModsBlacklist);
    SERIALIZE_VAR(ModsRequiredList);

    SERIALIZE_VAR(SupportSharding);

    SERIALIZE_VAR(LoginServerPort);
    SERIALIZE_VAR(AuthServerPort);
    SERIALIZE_VAR(GameServerPort);
    SERIALIZE_VAR(StartGameServerPortRange);
    SERIALIZE_VAR(WebUIServerPort);
    SERIALIZE_VAR(WebUIServerUsername);
    SERIALIZE_VAR(WebUIServerPassword);
    SERIALIZE_VAR(Announcements);
    SERIALIZE_VAR(DatabaseTrimInterval);
    SERIALIZE_VAR(BloodMessageMaxLivePoolEntriesPerArea);
    SERIALIZE_VAR(BloodMessageMaxDatabaseEntries);
    SERIALIZE_VAR(BloodMessagePrimeCountPerArea);
    SERIALIZE_VAR(BloodstainMaxLivePoolEntriesPerArea);
    SERIALIZE_VAR(BloodstainMaxDatabaseEntries);
    SERIALIZE_VAR(BloodstainPrimeCountPerArea);
    SERIALIZE_VAR(BloodstainMemoryCacheOnly);
    SERIALIZE_VAR(GhostMaxLivePoolEntriesPerArea);
    SERIALIZE_VAR(GhostPrimeCountPerArea);
    SERIALIZE_VAR(GhostMemoryCacheOnly);
    SERIALIZE_VAR(GhostMaxDatabaseEntries);
    SERIALIZE_VAR(QuickMatchWinXp);
    SERIALIZE_VAR(QuickMatchLoseXp);
    SERIALIZE_VAR(QuickMatchDrawXp);
    SERIALIZE_VAR(QuickMatchRankXp);
    SERIALIZE_VAR(DisableInvasions);
    SERIALIZE_VAR(DisableCoop);
    SERIALIZE_VAR(DisableBloodMessages);
    SERIALIZE_VAR(DisableBloodStains);
    SERIALIZE_VAR(DisableGhosts);
    SERIALIZE_VAR(DisableInvasionAutoSummon);
    SERIALIZE_VAR(DisableCoopAutoSummon);
    SERIALIZE_VAR(IgnoreInvasionAreaFilter);
    SERIALIZE_VAR(PlayerStatusUploadInterval);
    SERIALIZE_VAR(PlayerCharacterUpdateSendDelay);
    SERIALIZE_VAR(PlayerStatusUploadSendDelay);
    SERIALIZE_STRUCT_VAR(SummonSignMatchingParameters);
    SERIALIZE_STRUCT_VAR(WayOfBlueMatchingParameters);
    SERIALIZE_STRUCT_VAR(DarkSpiritInvasionMatchingParameters);
    SERIALIZE_STRUCT_VAR(MoundMakerInvasionMatchingParameters);
    SERIALIZE_STRUCT_VAR(CovenantInvasionMatchingParameters);
    SERIALIZE_STRUCT_VAR(UndeadMatchMatchingParameters);

    SERIALIZE_STRUCT_VAR(DS2_WhiteSoapstoneMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_SmallWhiteSoapstoneMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_RedSoapstoneMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_MirrorKnightMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_DragonEyeMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_RedEyeOrbMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_BlueEyeOrbMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_BellKeeperMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_RatMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_BlueSentinelMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_ArenaMatchingParameters);
    SERIALIZE_STRUCT_VAR(DS2_MatchingAreaMatchingParameters);

    SERIALIZE_VAR(AntiCheatEnabled);
    SERIALIZE_VAR(AntiCheatApplyPenalties);
    SERIALIZE_VAR(AntiCheatWarningMessage);
    SERIALIZE_VAR(AntiCheatDisconnectMessage);
    SERIALIZE_VAR(AntiCheatBanMessage);
    SERIALIZE_VAR(BanAnnouncementMessage);
    SERIALIZE_VAR(WarningAnnouncementMessage);

    SERIALIZE_VAR(AntiCheatSendWarningMessageInGame);
    SERIALIZE_VAR(AntiCheatSendWarningMessageInGameInterval);
    SERIALIZE_VAR(AntiCheatWarningThreshold);
    SERIALIZE_VAR(AntiCheatDisconnectThreshold);
    SERIALIZE_VAR(AntiCheatBanThreshold);
    SERIALIZE_VAR(AntiCheatScore_ClientFlagged);
    SERIALIZE_VAR(AntiCheatScore_ImpossibleStats);
    SERIALIZE_VAR(AntiCheatScore_Exploit);
    SERIALIZE_VAR(AntiCheatScore_ImpossibleName);
    SERIALIZE_VAR(AntiCheatScore_ImpossibleStatDelta);
    SERIALIZE_VAR(AntiCheatScore_ImpossibleGetItemQuantity);
    SERIALIZE_VAR(AntiCheatScore_ImpossiblePlayTime);
    SERIALIZE_VAR(AntiCheatScore_ImpossibleLocation);
    SERIALIZE_VAR(AntiCheatScore_UnfairDisconnect);

    SERIALIZE_VAR(DiscordWebHookUrl);
    SERIALIZE_VAR(SendDiscordNotice_AntiCheat);
    SERIALIZE_VAR(SendDiscordNotice_SummonSign);
    SERIALIZE_VAR(SendDiscordNotice_QuickMatch);
    SERIALIZE_VAR(SendDiscordNotice_Bell);
    SERIALIZE_VAR(SendDiscordNotice_Boss);
    SERIALIZE_VAR(SendDiscordNotice_PvP);

    return true;
}

#undef SERIALIZE_STRUCT_VAR
#undef SERIALIZE_VAR