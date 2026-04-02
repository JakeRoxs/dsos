![Dark Souls 3 - Open Server](./Resources/banner.png?raw=true)

![GitHub license](https://img.shields.io/github/license/jakeroxs/rekindled-server)
![GitHub release](https://img.shields.io/github/release/jakeroxs/rekindled-server)
![GitHub downloads](https://img.shields.io/github/downloads/jakeroxs/rekindled-server/total)

<div align="center">

English | [简体中文](./README_zhCN.md)

</div>

# Note:

This is a fork of the original DS3OS project, now rebranded as Rekindled Server.

# What is this project?

An open source implementation of the game servers for dark souls 2 (SOTFS) and 3.

This project exists to provide an alternative to playing online with mods without the risk of being banned, or just for people who want to play privately and not deal with cheaters/invasions/etc.

# Can I use it with a pirated game?

No, the server authenticates steam tickets. Please do not ask about piracy, steam emulators or the like, we have no interest in supporting them.

FROM SOFTWARE deserves your support too for the excellent work they do, please buy their games if you can.

# Where can I download it?

Downloads are available on the github releases page - https://github.com/jakeroxs/rekindled-server/releases

# How do I use it?

Once built you should have a folder called Bin, there are 2 subfolders of relevance. Loader and Server.

When running the loader you will get the option to create a server or join an existing server.

If you want to create a dedicated server yourself you should run Server.exe in the Server folder, this will start the actual custom server running on your computer.

The first time the server runs it will emit the file Saved\default\config.json which contains various matchmaking parameters that you can adjust (and apply by restarting the server) to customise the server.

Servers can also be password protected if required by setting as password in Saved\default\config.json, a password will need to be entered when attempting to launch the game with a protected server.

**NOTE**: The **steam** client (no login needed) must be installed when you run the **Server.exe**. Otherwise, **Server.exe** will fail to initialize.

# What currently works?

Most of the games core functionality works now, with some degree of variance to the retail game. We're currently looking to closer match retail server behaviour and make some general improvements to the running of unoffical servers.

:bangbang: Dark Souls 2 support is still experimental and under development, there is a high probability of things not behaving correctly.

| Feature                     | Dark Souls 3       | Dark Souls 2 SOTFS |
| --------------------------- | ------------------ | ------------------ |
| Stable enough for use       | :heavy_check_mark: | Experimental       |
| Network transport           | :heavy_check_mark: | :heavy_check_mark: |
| Announcement messages       | :heavy_check_mark: | :heavy_check_mark: |
| Profile management          | :heavy_check_mark: | :heavy_check_mark: |
| Blood messages              | :heavy_check_mark: | :heavy_check_mark: |
| Bloodstains                 | :heavy_check_mark: | :heavy_check_mark: |
| Ghosts                      | :heavy_check_mark: | :heavy_check_mark: |
| Summoning                   | :heavy_check_mark: | :heavy_check_mark: |
| Invasions                   | :heavy_check_mark: | :heavy_check_mark: |
| Auto-Summoning (Convenants) | :heavy_check_mark: | :heavy_check_mark: |
| Mirror Knight               | n/a                | :heavy_check_mark: |
| Matchmaking                 | :heavy_check_mark: | :heavy_check_mark: |
| Leaderboards                | :heavy_check_mark: | :heavy_check_mark: |
| Bell Ringing                | :heavy_check_mark: | n/a                |
| Quick Matches (Arenas)      | :heavy_check_mark: | :heavy_check_mark: |
| Telemetry/Misc              | :heavy_check_mark: | :heavy_check_mark: |
| Ticket Authentication       | :heavy_check_mark: | :heavy_check_mark: |
| Hub Server Support          | :heavy_check_mark: | :heavy_check_mark: |
| Loader Support              | :heavy_check_mark: | :heavy_check_mark: |
| WebUI For Admin             | :heavy_check_mark: | :heavy_check_mark: |
| Sharding Support            | :heavy_check_mark: | :heavy_check_mark: |
| Discord Activity Feed       | :heavy_check_mark: |                    |

Future roadmap:

- Support for various mod-settings per server (eg. allow servers to remove summon limit)
- Better Anticheat (potentially we could do some more harsh checks than FROM does).

# Will this ban my account on the retail server?

Rekindled Server uses its own save files, as long as you don't copy Rekindled Server saves back to your retail saves you should be fine.

# FAQ

# How do I switch between hosting Dark Souls 3 and Dark Souls 2?

After running the server once a file will be created at Saved/default/config.json. You can change the GameType parameter from DarkSouls2 and DarkSouls3 to change what game the server hosts.

## Why aren't my save files appearing?

Rekindled Server uses its own saves to avoid any issues with retail game saves. If you want to transfer your retail saves to Rekindled Server, click the settings (cog) icon at the bottom of the loader and press the copy retail saves button.

We don't provide an automation option to copy Rekindled Server saves back to retail saves for safety. If you ~really~ want to do this you can find the folder the saves are stored in and rename the .rekindled files to .sl2.

## Can I run the server via docker?

Yes, there are 2 docker containers currently published for Rekindled Server, these are automatically updated each time a new release is made:

jakeroxs/rekindled-ds2s-server - This is the main Dark Souls 2: SOTFS server.
jakeroxs/rekindled-ds3-server - This is the main Dark Souls 3 server.
jakeroxs/rekindled-hub - This is for the hub server, eventually hub servers will communicate between each other if federation is enabled.

If you want a quick one-liner to run the server, you can use this. Note that it mounts the Saved folder to the host filesystem at /opt/rekindled-server/Saved, making it easier to modify the configuration files. Access /opt/rekindled-server/Saved to view and modify the configuration files.

`sudo mkdir -p /opt/rekindled-server/Saved && sudo chown 1000:1000 /opt/rekindled-server/Saved && sudo docker run -d -m 2G --restart always --net host --mount type=bind,source=/opt/rekindled-server/Saved,target=/opt/rekindled-server/Saved jakeroxs/rekindled-server:latest`

### Docker Compose example

A `docker-compose.yml` file is included in the repository root with a simple configuration for running both the game server and (optionally) the master server. To launch the services, run:

```bash
docker compose up -d
```

The compose file uses the published images and binds the `Saved` directory to `./Saved` on the host. Adjust ports or remove the master service if you only need the game server.

## I launch the game but its unable to connect?

There are a few different causes of this, the simplest one is to make sure you're running as admin, the launcher needs to patch the games memory to get it to connect to the new server, this requires admin privileges.

If the server is being hosted by yourself and the above doesn't solve your issue, try these steps:

1. Ensure these ports are forwarded on your router, both for tcp and udp: 50000, 50010, 50050, 50020

2. Ensure you have allowed the server access through the windows defender firewall, you can set rules here: Start Bar -> Windows Administrative Tools -> Windows Defender Firewall with Advanced Security -> Inbound/Output Rules

3. Its possible you don't have the configuration for the server setup correctly. After running the server once make sure to open the configuration file (Saved/config.json) and make sure its setup correctly (it will attempt to autoconfigure itself, but may get incorrect values if you have multiple network adapters). The most critical settings to get correct are ServerHostname and ServerPrivateHostname, these should be set to your WAN IP (the one you get from sites like https://whatismyip.com), and your LAN IP (the one you get from running ipconfig) respectively. If you are using LAN emulation software (eg. hamachi) you will need to set these to the appropriate hamachi IP.

## What do all the properties in the config file mean?

The settings are all documented in the source code in this file, in future I'll write some more detailed documentation.

https://github.com/jakeroxs/rekindled-server/blob/main/Source/Server/Config/RuntimeConfig.h

# How do I build it?

The project is written in C++17 and uses CMake for cross-platform builds.

**Required:** CMake should be configured with Ninja by default.
The repo now enforces this in `CMakeLists.txt` (fatal configure error when non-Ninja is used unless `-DREKINDLED_ALLOW_NON_NINJA=ON`).

Use:

- `Ninja` (preferred, cross-platform)

Optional fallback (only with explicit override):

- `Visual Studio 18 2026` (MSVC / Clang-CL) with `-DREKINDLED_ALLOW_NON_NINJA=ON`
- `Visual Studio 17 2022` (if available) with `-DREKINDLED_ALLOW_NON_NINJA=ON`

## Prerequisites

- [CMake](https://cmake.org/download/) (3.20+ recommended)
- Visual Studio 2026 or 2022 + optional Clang-cl, or another CMake-compatible toolchain
- .NET SDK 5.0 or later (`dotnet` command) – used by the WinForms loader project (project currently targets net5.0-windows)
- Node.js & npm (only if you intend to build the master server, which is
  managed separately with npm)

The loader target is a SDK‑style .NET project with NuGet dependencies. CMake now
includes a pre‑build step that runs `dotnet restore` using the same
`BaseIntermediateOutputPath` and configuration that MSBuild will use, so you
should **not** have to run `dotnet restore` manually. A clean checkout can be
configured and built end‑to‑end with a single invocation of CMake:

```powershell
# from repo root
cmake -S . -B build -G "Visual Studio 18 2026" -A x64  # or your preferred generator
# Optional: append -T ClangCL if you have Visual Studio ClangCL toolset installed
cmake --build build --config Debug --target ALL_BUILD
```

If you prefer to use the generated project files directly, run one of the
`Tools/generate_*` scripts (Windows, WSL, etc.) and open the resulting
solution in Visual Studio. The loader target will restore its NuGet packages
automatically when you build.

Once generated the project files are stored in the intermediate folder, at this
point you can just open them and build the project.

## Recommended local build helper

If you want a consistent path across environments, use the provided helper script:

```powershell
pwsh .\Tools\build-cmake.ps1 -BuildType Release
```

The script automatically picks the first available supported generator:

- Ninja if installed
- Visual Studio (via vswhere) if installed

You can override explicitly:

```powershell
pwsh .\Tools\build-cmake.ps1 -Generator "Ninja" -BuildType Debug
pwsh .\Tools\build-cmake.ps1 -Generator "Visual Studio 18 2026" -BuildType Release
```

## Using nix

```sh
# to build a package
nix build github:jakeroxs/rekindled-server
# to run it directly
nix run github:jakeroxs/rekindled-server
# to run hub
nix run github:jakeroxs/rekindled-server#hub
```

The nix version stores the configs in `${XDG_CONFIG_HOME:-$HOME/.config}/rekindled-server`

# Whats in the repository?

```
/
├── Protobuf/              Contains the protobuf definitions used by the server's network traffic. Compiling them is done via the bat file in Tools/
├── Resources/             General resources used for building and packaging - icons/readmes/etc.
├── Source/                All source code for the project.
│   ├── Injector/          This is the DLL that gets injected into the game to provide Rekindled Server's functionality.
│   ├── Loader/            WinForms launcher that patches the game and starts it with all network traffic redirected to a custom Rekindled Server.
│   ├── hub/               NodeJS source code for a simple API server for advertising and listing active servers.
│   ├── Server/            Source code for the main server.
│   ├── Server.DarkSouls3/ Source code thats special to dark souls 3 support.
│   ├── Server.DarkSouls2/ Source code thats special to dark souls 2 support.
│   ├── Shared/            Source code that is shared between the server and injector projects.
│   └── ThirdParty/        Source code for any third-party libraries used.
│   └── WebUI/             Contains the static resources used to assemble the management web page for the server.
├── Tools/                 Various cheat engine tables, bat files and alike used for analysis.
```

# How can I help?

Check out the issues page, or send me a message for suggestions on what can be done.

Right now there are a few server calls we either have stubbed out or returning dummy information, implementing
them properly, or finding out the format of the data they need to return would be worth while.

There are also a lot of protobuf fields that are still unknown and use constant values when sent from the
server, determining what they represent would be a good improvement.

# Credit

A lot of the information needed to produce this implementation has been figured out by the community.
Especially the members of the ?ServerName? souls modding discord.

The following 3 repositories have provided a lot of information used in this implementation:

https://github.com/garyttierney/ds3-open-re

https://github.com/Jellybaby34/DkS3-Server-Emulator-Rust-Edition

https://github.com/AmirBohd/ModEngine2

Graphics and icons provided by:

Campfire icon made by ultimatearm from www.flaticon.com

Various UI icons made by Mark James from http://www.famfamfam.com/lab/icons/silk/
