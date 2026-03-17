#!/usr/bin/env bash

ScriptPath=$PWD
RootPath="$ScriptPath"/../
BuildPath="$ScriptPath"/../intermediate/make/
CMakeExePath="$ScriptPath"/Build/cmake/linux/bin/cmake

echo "Generating $RootPath"
echo "$CMakeExePath -S $RootPath -B $BuildPath"

$CMakeExePath -S $RootPath -B $BuildPath -G "Unix Makefiles" \
  -DCMAKE_BUILD_TYPE=Release \
  -DDSOS_USE_VENDORED_CIVETWEB=ON \
  -DDSOS_USE_VENDORED_OPENSSL=ON \
  -DDSOS_USE_VENDORED_SQLITE=ON
