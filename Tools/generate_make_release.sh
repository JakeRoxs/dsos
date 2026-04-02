#!/usr/bin/env bash

ScriptPath=$PWD
RootPath="$ScriptPath"/../
BuildPath="$ScriptPath"/../intermediate/make/
CMakeExePath="$ScriptPath"/Build/cmake/linux/bin/cmake

if [ ! -x "$CMakeExePath" ]; then
  CMakeExePath="$(command -v cmake || true)"
fi

if [ -z "$CMakeExePath" ]; then
  echo "ERROR: cmake not found. Install cmake or add it to PATH."
  exit 1
fi

echo "Generating $RootPath"
echo "$CMakeExePath -S $RootPath -B $BuildPath"

# Allow overriding generator via environment variable, default to Ninja for faster builds.
GENERATOR="${GENERATOR:-Ninja}"
echo "$CMakeExePath -S $RootPath -B $BuildPath -G \"$GENERATOR\""

$CMakeExePath -S $RootPath -B $BuildPath -G "$GENERATOR" \
  -DCMAKE_BUILD_TYPE=Release

# Ensure the solution file uses the renamed branding.
if [ -f "$BuildPath/rekindled-server.sln" ]; then
  echo "Using existing rekindled-server.sln"
else
  echo "ERROR: rekindled-server.sln not found"
fi

# Explicit canonical output location for packaging and Docker.
OUTPUT_PATH="$RootPath/bin/x64_release"
mkdir -p "$OUTPUT_PATH"

# Promote the newly built output into the canonical location.
# At this point the build may have placed artifacts under intermediate/make.
if [ -d "$BuildPath/bin/x64_release" ]; then
  cp -a "$BuildPath/bin/x64_release/." "$OUTPUT_PATH/"
elif [ -d "$BuildPath/Source/Server" ]; then
  cp -a "$BuildPath/Source/Server/." "$OUTPUT_PATH/"
elif [ -d "$BuildPath/Source/Server.DarkSouls3" ]; then
  cp -a "$BuildPath/Source/Server.DarkSouls3/." "$OUTPUT_PATH/"
elif [ -d "$BuildPath/Source/Server.DarkSouls2" ]; then
  cp -a "$BuildPath/Source/Server.DarkSouls2/." "$OUTPUT_PATH/"
else
  echo "Warning: canonical output path not populated; build artifacts may not exist yet."
fi

echo "OUTPUT_PATH=$OUTPUT_PATH"
