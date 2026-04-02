# native deps
{
    lib
    , cmake
    , pkg-config
    , removeReferencesTo
    , writeShellApplication
    , jq
    , xz
}:

# build deps
{
    stdenv
    , libuuid
    , sqlite
    , openssl
}:

with builtins;
with lib;

let
    civetwebArchive = builtins.fetchurl {
      url = "https://github.com/civetweb/civetweb/archive/refs/tags/v1.15.tar.gz";
      sha256 = "scstgqrjisvte6spxomwt4eelug3ubjvj6okztr2kac7uwpvso4q";
    };

    opensslArchive = builtins.fetchurl {
      url = "https://github.com/kzalewski/openssl-1.1.1/archive/refs/tags/1.1.1ze.tar.gz";
      sha256 = "yzjxxnrt444sr3vagewltyi55e3y6kstz5zepciuqqeskcameuaq";
    };

    curlArchive = builtins.fetchurl {
      url = "https://curl.se/download/curl-8.1.0.tar.xz";
      sha256 = "npmavvhqogdqcwirefxoogc3sdjillcrmkxndppncrhz7ezdfi6a";
    };

    pkg = stdenv.mkDerivation rec {
        name = "rekindled-server";

        src = with fileset; toSource {
            root = ./.;
            fileset = unions [ ./CMakeLists.txt ./Source ./Tools/Build ];
        };

        nativeBuildInputs = [ cmake pkg-config removeReferencesTo xz ];
        buildInputs = [ libuuid sqlite openssl ];

        enableParallelBuilding = true;

        installPhase = ''
            install -Dm755 ../bin/x64_release/libsteam_api.so $out/lib/libsteam_api.so
            install -Dm755 ../bin/x64_release/Server $out/bin/Server
            mkdir -p $out/share/rekindled-server
            cp -R ../bin/x64_release/WebUI $out/share/rekindled-server/WebUI
            '';

        # google's generated protobuf headers puts absolute file path garbage into the binaries
        # which will break reproducible builds.
        fixupPhase = ''
            find "$out" -type f -exec remove-references-to -t "${src}" '{}' +
            '';

        cmakeFlags = [
            # Fix third party builds
            "-DCMAKE_C_STANDARD=99"
            "-DCMAKE_C_FLAGS=-Wno-implicit-function-declaration"

            # Prefer using FetchContent to download third-party sources at configure time
            # (falls back to system libraries if available).
            # When using Nix, supply the pre-fetched archive paths so CMake doesn't need network access.
            "-DREKINDLED_CIVETWEB_ARCHIVE=file://${civetwebArchive}"
            "-DREKINDLED_OPENSSL_ARCHIVE=file://${opensslArchive}"
            "-DREKINDLED_CURL_ARCHIVE=file://${curlArchive}"
        ];

        # Can't pass multiple flags through cmakeFlags *sigh*
        # TODO: Nixpkgs now supports spaces in cmakeFlags when __structuredAttrs = true, so multiple flags can be passed.
        # https://github.com/NixOS/nixpkgs/issues/114044
        # Though these really should be fixed in rekindled itself
        NIX_CFLAGS_COMPILE = [ "-Wno-format-security" "-Wno-non-pod-varargs" ];

        meta = {
            homepage = "https://github.com/jakeroxs/rekindled-server";
            license = licenses.mit;
            platforms = [ "x86_64-linux" ];
        };
    };
in writeShellApplication {
    runtimeInputs = [ jq ];
    name = "rekindled-server";
    text = ''
        tmp="$(mktemp -d)"
        trap 'rm -rf "$tmp"' EXIT
        cd "$tmp"

        export LD_LIBRARY_PATH="${pkg}/lib:''${LD_LIBRARY_PATH:-}"
        config="''${XDG_CONFIG_HOME:-$HOME/.config/rekindled-server}"
        mkdir -p "$config"

        if [[ -f "$config/default/config.json" ]]; then
            game_type="$(jq -r '.GameType' "$config/default/config.json")"
        fi

        case "''${game_type:-DarkSouls3}" in
            DarkSouls2)
                echo "GameType: DarkSouls2"
                echo 335300 > steam_appid.txt
                ;;
            DarkSouls3)
                echo "GameType: DarkSouls3"
                echo 374320 > steam_appid.txt
                ;;
        esac

        ln -sf "${pkg}/share/rekindled-server/WebUI" .
        ln -sf "${pkg}/bin/Server" .
        ln -sf "$config" Saved
        exec ./Server "$@"
        '';
}
