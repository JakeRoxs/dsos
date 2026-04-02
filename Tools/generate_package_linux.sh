#!/usr/bin/env bash

OUTPUT_ROOT="bin/x64_release"
if [ ! -d "$OUTPUT_ROOT" ]; then
  if [ -d "intermediate/build/bin/x64_release" ]; then
    OUTPUT_ROOT="intermediate/build/bin/x64_release"
  elif [ -d "intermediate/build/Source/Server" ]; then
    OUTPUT_ROOT="intermediate/build/Source/Server"
  elif [ -d "intermediate/build/Source/Server.DarkSouls3" ]; then
    OUTPUT_ROOT="intermediate/build/Source/Server.DarkSouls3"
  elif [ -d "intermediate/build/Source/Server.DarkSouls2" ]; then
    OUTPUT_ROOT="intermediate/build/Source/Server.DarkSouls2"
  elif [ -d "intermediate/make/bin/x64_release" ]; then
    OUTPUT_ROOT="intermediate/make/bin/x64_release"
  elif [ -d "intermediate/make/Source/Server" ]; then
    OUTPUT_ROOT="intermediate/make/Source/Server"
  elif [ -d "intermediate/make/Source/Server.DarkSouls3" ]; then
    OUTPUT_ROOT="intermediate/make/Source/Server.DarkSouls3"
  elif [ -d "intermediate/make/Source/Server.DarkSouls2" ]; then
    OUTPUT_ROOT="intermediate/make/Source/Server.DarkSouls2"
  fi
fi

echo "Using package source output path: $OUTPUT_ROOT"

mkdir -p rekindled-server
mkdir -p rekindled-server/Server
cp Resources/ReadMe.txt rekindled-server/ReadMe.txt

ERR=0

if [ -f "$OUTPUT_ROOT/steam_appid.txt" ]; then
  cp "$OUTPUT_ROOT/steam_appid.txt" rekindled-server/Server/
elif [ -f "Resources/steam_appid.txt" ]; then
  cp "Resources/steam_appid.txt" rekindled-server/Server/
else
  echo "WARNING: steam_appid.txt not found in $OUTPUT_ROOT or Resources"
  ERR=1
fi

if [ -f "$OUTPUT_ROOT/libsteam_api.so" ]; then
  cp "$OUTPUT_ROOT/libsteam_api.so" rekindled-server/Server/
elif [ -f "Source/ThirdParty/steam/redistributable_bin/linux64/libsteam_api.so" ]; then
  cp "Source/ThirdParty/steam/redistributable_bin/linux64/libsteam_api.so" rekindled-server/Server/
elif [ -f "Source/ThirdParty/steam/redistributable_bin/linux32/libsteam_api.so" ]; then
  cp "Source/ThirdParty/steam/redistributable_bin/linux32/libsteam_api.so" rekindled-server/Server/
else
  echo "WARNING: libsteam_api.so not found in $OUTPUT_ROOT or Source/ThirdParty"
  ERR=1
fi

if [ -f "$OUTPUT_ROOT/Server" ]; then
  cp "$OUTPUT_ROOT/Server" rekindled-server/Server/
elif [ -f "intermediate/make/Source/Server/Server" ]; then
  cp "intermediate/make/Source/Server/Server" rekindled-server/Server/
else
  echo "WARNING: Server executable not found in $OUTPUT_ROOT or intermediate/make/Source/Server"
  ERR=1
fi

if [ -d "$OUTPUT_ROOT/WebUI" ]; then
  cp -R "$OUTPUT_ROOT/WebUI/" rekindled-server/Server/WebUI/
elif [ -d "Source/WebUI" ]; then
  cp -R "Source/WebUI/" rekindled-server/Server/WebUI/
else
  echo "WARNING: WebUI folder not found in $OUTPUT_ROOT or Source/WebUI"
  ERR=1
fi

if [ "$ERR" -eq 1 ]; then
  echo "ERROR: One or more required files were missing. Check build output path and CMake configuration."
  ls -la "$OUTPUT_ROOT"
  exit 1
fi

