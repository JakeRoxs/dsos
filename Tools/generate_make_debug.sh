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

$CMakeExePath -S $RootPath -B $BuildPath -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Debug
