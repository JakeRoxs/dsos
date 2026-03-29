#include "Client/Client.h"
#include <cassert>

int main()
{
    Client::ClientConfig config;
    config.ServerIP = "10.0.0.42";
    config.ServerPort = 55555;
    config.ServerPublicKey = "-----BEGIN RSA PUBLIC KEY-----\nFAKEKEY\n-----END RSA PUBLIC KEY-----\n";
    config.DisablePersistentData = true;
    config.InstanceId = 1;

    assert(config.ServerIP == "10.0.0.42");
    assert(config.ServerPort == 55555);
    assert(config.ServerPublicKey.find("FAKEKEY") != std::string::npos);
    assert(config.DisablePersistentData == true);
    assert(config.InstanceId == 1);

    return 0;
}
