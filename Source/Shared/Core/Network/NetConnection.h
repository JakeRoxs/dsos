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

#include <string>
#include <memory>
#include <vector>

#if __linux__
#include <unistd.h>
#include <sys/socket.h>
#include <netdb.h>
#include <netinet/tcp.h>
#include <netinet/in.h>
#include <fcntl.h>

#define SOCKET_ERROR (-1)
#define WSAGetLastError() (errno)
#endif

// Base class for network connections. This class handles
// both listening and connecting. There are different
// specializations of this class for each network protocol
// that is used (TCP / UDP).

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#define _WINSOCKAPI_
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#endif

#include "Shared/Core/Network/NetIPAddress.h"

class Cipher;

class NetConnection
{
public:
    virtual ~NetConnection() {};

    virtual bool Listen(int Port) = 0;

    virtual std::shared_ptr<NetConnection> Accept() = 0;

    virtual bool Connect(std::string Hostname, int Port, bool ForceLastIpEntry = false) = 0;

    virtual bool Pump() = 0;

    // Replace with std::span's when they are available.
    virtual bool Peek(std::vector<uint8_t>& Buffer, int Offset, int Count, int& BytesReceived) = 0;
    virtual bool Receive(std::vector<uint8_t>& Buffer, int Offset, int Count, int& BytesReceived) = 0; 
    virtual bool Send(const std::vector<uint8_t>& Buffer, int Offset, int Count) = 0;

    virtual bool Disconnect() = 0;

    virtual bool IsConnected() = 0;
    virtual NetIPAddress GetAddress() = 0;

    virtual std::string GetName() = 0;
    virtual void Rename(const std::string& Name) = 0;

};