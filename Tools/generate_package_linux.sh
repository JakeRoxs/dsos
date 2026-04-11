#!/usr/bin/env bash

OUTPUT_ROOT="Bin/x64_Release"
if [ ! -d "$OUTPUT_ROOT" ]; then
  if [ -d "bin/x64_release" ]; then
    OUTPUT_ROOT="bin/x64_release"
  elif [ -d "Bin/AnyCPU_Release" ]; then
    OUTPUT_ROOT="Bin/AnyCPU_Release"
  elif [ -d "bin/AnyCPU_Release" ]; then
    OUTPUT_ROOT="bin/AnyCPU_Release"
  elif [ -d "intermediate/build/bin/x64_release" ]; then
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

rm -rf rekindled-server
mkdir -p rekindled-server
mkdir -p rekindled-server/Server
mkdir -p rekindled-server/Loader
mkdir -p rekindled-server/Loader.Avalonia
cp Resources/ReadMe.txt rekindled-server/ReadMe.txt

ERR=0
LOADER_AVALONIA_LINUX_PUBLISH_DIR="${LOADER_AVALONIA_LINUX_PUBLISH_DIR:-}"

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

mkdir -p rekindled-server/Loader
mkdir -p rekindled-server/Loader.Avalonia
loader_output_dir=""

if [ -n "$LOADER_AVALONIA_LINUX_PUBLISH_DIR" ] && [ -x "$LOADER_AVALONIA_LINUX_PUBLISH_DIR/Loader.Avalonia" ]; then
  loader_output_dir="$LOADER_AVALONIA_LINUX_PUBLISH_DIR"
fi

if [ -z "$loader_output_dir" ]; then
  for candidate in \
    "$OUTPUT_ROOT/Loader.Avalonia" \
    "$OUTPUT_ROOT/x64/Release/net10.0" \
    "$OUTPUT_ROOT/Loader.Avalonia.dll" \
    "Source/Loader.Avalonia/bin/Release/net10.0" \
    "Source/Loader.Avalonia/bin/Release/net10.0-windows" \
    "Source/Loader.Avalonia/bin/Release/net10.0/linux-x64" \
    "Source/Loader.Avalonia/bin/Release/net10.0-windows/linux-x64" \
    "Source/Loader.Avalonia/bin/Release/net10.0/" \
    "Source/Loader.Avalonia/bin/Release/net10.0-windows/"; do
    if [ -e "$candidate" ]; then
      loader_output_dir="$candidate"
      break
    fi
  done
fi

if [ -n "$loader_output_dir" ]; then
  if [ -d "$loader_output_dir" ]; then
    cp -R "$loader_output_dir/" rekindled-server/Loader.Avalonia/
  else
    cp "$loader_output_dir" rekindled-server/Loader.Avalonia/
    cp "${loader_output_dir%.dll}.pdb" rekindled-server/Loader.Avalonia/ 2>/dev/null || true
  fi
else
  echo "WARNING: Loader.Avalonia output not found in $OUTPUT_ROOT or Source/Loader.Avalonia/bin/Release"
  echo "Candidate paths tried:"
  echo "  $OUTPUT_ROOT/Loader.Avalonia"
  echo "  $OUTPUT_ROOT/x64/Release/net10.0"
  echo "  $OUTPUT_ROOT/Loader.Avalonia.dll"
  echo "  Source/Loader.Avalonia/bin/Release/net10.0"
  echo "  Source/Loader.Avalonia/bin/Release/net10.0-windows"
  echo "  Source/Loader.Avalonia/bin/Release/net10.0/linux-x64"
  echo "  Source/Loader.Avalonia/bin/Release/net10.0-windows/linux-x64"
  ls -la "Source/Loader.Avalonia/bin/Release" 2>/dev/null || true
  ERR=1
fi

if [ "$ERR" -eq 1 ]; then
  echo "ERROR: One or more required files were missing. Check build output path and CMake configuration."
  ls -la "$OUTPUT_ROOT"
  exit 1
fi

