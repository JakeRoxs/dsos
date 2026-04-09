/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#pragma once

#include "Server/Database/ServerDatabase.h"

#include "Shared/Platform/Platform.h"

#include "Shared/Core/Crypto/RSAKeyPair.h"
#include "Shared/Core/Network/NetIPAddress.h"

#include "Shared/Core/Utils/Logging.h"

#include <memory>
#include <vector>
#include <filesystem>
#include <atomic>

#include <steam/steam_api.h>
#include <steam/isteamuser.h>

// This is a very-very-very simple client emulator. Its used to
// as a super simple way to server behaviour.
//
// TODO: Split this out into a separate application.

class NetConnection;
class Frpg2MessageStream;
class Frpg2ReliableUdpMessageStream;
struct Frpg2ReliableUdpMessage;
struct Frpg2Message;

class Client {
public:
  struct ClientConfig {
    std::string ServerIP = "127.0.0.1";
    int ServerPort = 50050;
    std::string ServerPublicKey =
        "-----BEGIN RSA PUBLIC KEY-----\n"
        "MIIBCgKCAQEAtSGwOqmYyMldifSB99oqPc4jnWbOvtU9441/anExQtajz8AGA+V2\n"
        "uq9s6PNGZCkCCFYlxq7iXr+PTrL20irkqyNAX8Fjub+hckwBFtOGWOf2/ENJk9A8\n"
        "uyhfpmOVZ9+qB76ZcdwdSVWrCmzlgKjPU2RVz0moE1CHFtBr6gfdG+LlUBUEHr1X\n"
        "lnMlhNdRni+9Ju8X3Mt/EEdS++F+1s8/9VVMdf7RCPru09rR2fc9sD72DB7d8WeH\n"
        "MJssXGmcb6sZsU0u/3zNS8lGatDLivSwRrxeOUeUCIgu8ZrSTq0fCnHjUZ2WU6im\n"
        "Df1boE+E786Rf9cyK6I61zSUDMaqke7f8QIDAQAB\n"
        "-----END RSA PUBLIC KEY-----\n";
    bool DisablePersistentData = false;
    size_t InstanceId = 0;
  };

  Client();
  explicit Client(const ClientConfig& config);
  ~Client();

  // Initialize the instance using the current ClientConfig values.
  bool Init();

  // Allows callers to override the disable-persistent/instance-id values before Init.
  void OverrideConfig(bool disablePersistentData, size_t instanceId);

  bool Term();
  void RunUntilQuit();

private:
  void Pump();

  enum class ClientState {
    LoginServer_Connect,
    LoginServer_RequestServerInfo,

    AuthServer_Connect,
    AuthServer_RequestHandshake,
    AuthServer_RequestServiceStatus,
    AuthServer_ExchangeKeyData,
    AuthServer_GetServerInfo,

    GameServer_Connect,
    GameServer_RequestWaitForUserLogin,
    GameServer_RequestGetAnnounceMessageList,
    GameServer_RequestUpdateLoginPlayerCharacter,
    GameServer_RequestUpdatePlayerStatus,
    GameServer_RequestUpdatePlayerCharacter,
    GameServer_RequestGetRightMatchingArea,
    GameServer_Idle,
#if 0
        GameServer_GatherStatistics,
#endif

    Complete
  };

  void Handle_LoginServer_Connect();
  void Handle_LoginServer_RequestServerInfo();

  void Handle_AuthServer_Connect();
  void Handle_AuthServer_RequestHandshake();
  void Handle_AuthServer_RequestServiceStatus();
  void Handle_AuthServer_ExchangeKeyData();
  void Handle_AuthServer_GetServerInfo();

  void Handle_GameServer_Connect();
  void Handle_GameServer_RequestWaitForUserLogin();
  void Handle_GameServer_RequestGetAnnounceMessageList();
  void Handle_GameServer_RequestUpdateLoginPlayerCharacter();
  void Handle_GameServer_RequestUpdatePlayerStatus();
  void Handle_GameServer_RequestUpdatePlayerCharacter();
  void Handle_GameServer_RequestGetRightMatchingArea();
  void Handle_GameServer_Idle();
#if 0
    void Handle_GameServer_GatherStatistics();
#endif

  void ChangeState(ClientState State);

  void WaitForNextMessage(std::shared_ptr<NetConnection> Connection, std::shared_ptr<Frpg2MessageStream> Stream, Frpg2Message& Output);
  void SendAndAwaitWaitForReply(google::protobuf::MessageLite* Request, Frpg2ReliableUdpMessage& Response);
  void SendAndAwaitWaitForReply(google::protobuf::MessageLite* Request, google::protobuf::MessageLite* Response);

  std::string GetName();

  template <typename... Args>
  void Abort(const char* Format, Args... args) {
    ErrorS(GetName().c_str(), Format, args...);
    throw std::exception();
  }

private:
  static inline std::atomic<size_t> gClientCount{0};
  bool WasConnected = false;

  ClientState State = ClientState::LoginServer_Connect;

  bool QuitReceived = false;

  PlatformEvents::CtrlSignalEvent::DelegatePtr CtrlSignalHandle = nullptr;

  RSAKeyPair PrimaryKeyPair;

  std::shared_ptr<NetConnection> LoginServerConnection;
  std::shared_ptr<Frpg2MessageStream> LoginServerMessageStream;

  std::shared_ptr<NetConnection> AuthServerConnection;
  std::shared_ptr<Frpg2MessageStream> AuthServerMessageStream;

  std::shared_ptr<NetConnection> GameServerConnection;
  std::shared_ptr<Frpg2ReliableUdpMessageStream> GameServerMessageStream;

  ServerDatabase Database;

  std::filesystem::path SavedPath;
  std::filesystem::path DatabasePath;

  ClientConfig Config;

  std::string ClientStreamId = "";
  int ClientAppVersion = 115;
  int LocalCharacterId = 10;
  int ServerCharacterId;

  int ClientSoulLevel = 0;
  int ClientSoulMemory = 0;
  int ClientWeaponLevel = 0;

  std::string AuthServerIP = "";
  int AuthServerPort = 0;

  bool GotAppTicketResponse = false;
  bool HasAppTicket = false;
  std::vector<uint8_t> AppTicket;
  HAuthTicket AppTicketHandle = k_HAuthTicketInvalid;

  std::vector<uint8_t> GameServerCwcKey;
  uint64_t GameServerAuthToken;
  std::string GameServerIP = "";
  int GameServerPort = 0;
  uint32_t GamePlayerId = 0;

  // Server connection configuration values are now stored in Config:
  // Config.ServerIP, Config.ServerPort, Config.ServerPublicKey, Config.DisablePersistentData, Config.InstanceId
};