/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Shared/Core/Network/NetUtils.h"
#include "Shared/Core/Network/NetHttpRequest.h"

#ifdef _WIN32
#include <winsock2.h>
#endif

#ifdef __linux__
#include <netdb.h>
#include <unistd.h>
#endif

bool GetMachineIPv4(NetIPAddress& Output, bool GetPublicAddress)
{
    // For public ip address we query an external webapi.
    if (GetPublicAddress)
    {
        NetHttpRequest Request;
        Request.SetMethod(NetHttpMethod::GET);
        // Use HTTPS to ensure the public IP lookup is not tampered with in transit.
        Request.SetUrl("https://api.ipify.org");
        if (Request.Send())
        {
            if (std::shared_ptr<NetHttpResponse> Response = Request.GetResponse(); Response && Response->GetWasSuccess())
            {
                std::string IPAddress;
                IPAddress.assign((char*)Response->GetBody().data(), Response->GetBody().size());

                if (!NetIPAddress::ParseString(IPAddress, Output))
                {
                    return false;
                }

                return true;
            }
        }
    }
    // Otherwise we can just grab one with some winsock shenanigans. This won't
    // work correctly for machines with multiple interfaces. But for our purposes it should
    // be ok. This ip is mainly used for debugging.
    else
    {
        char Buffer[1024];
        if (gethostname(Buffer, sizeof(Buffer)) == -1)
        {
            return false;
        }

        struct hostent* HostEntry = gethostbyname(Buffer);
        if (!HostEntry)
        {
            return false;
        }

        struct in_addr* Addr = (struct in_addr* )HostEntry->h_addr;

#ifdef _WIN32
        Output = NetIPAddress(
            Addr->S_un.S_un_b.s_b1,
            Addr->S_un.S_un_b.s_b2,
            Addr->S_un.S_un_b.s_b3,
            Addr->S_un.S_un_b.s_b4
        );
#else
        uint32_t addr = ntohl(Addr->s_addr);
        Output = NetIPAddress(
            (addr >> 24) & 0xFF,
            (addr >> 16) & 0xFF,
            (addr >> 8) & 0xFF,
            addr & 0xFF
        );
#endif

        return true;
    }

    return false;
}