# build stage based on ubuntu LTS
FROM ubuntu@sha256:186072bba1b2f436cbb91ef2567abca677337cfc786c86e107d25b7072feef0c AS build

# install build dependencies without recommendations and clean apt cache in same layer
RUN apt-get update && \
    DEBIAN_FRONTEND=noninteractive apt-get install -q -y --no-install-recommends \
        g++ make curl zip unzip tar binutils cmake git yasm ninja-build pkg-config \
        libssl-dev zlib1g-dev libpcre3-dev libuuid1 uuid-dev uuid-runtime ca-certificates && \
    rm -rf /var/lib/apt/lists/*

COPY ./ /build
WORKDIR /build/Tools
# strip any CRLF bytes from script; host may check out with Windows endings
RUN sed -i 's/\r$//' ./generate_make_release.sh && \
    ./generate_make_release.sh
WORKDIR /build
RUN cd intermediate/make && (if [ -f build.ninja ]; then ninja -j$(nproc || echo 4); else make -j$(nproc || echo 4); fi)

# Ensure canonical output exists before transitioning to runtime stage
RUN if [ ! -d /build/bin/x64_release ]; then \
      echo "Error: canonical build output directory /build/bin/x64_release not found"; exit 1; \
    fi

FROM steamcmd/steamcmd:latest@sha256:b2f9129d051bc9e776fa3603de413b714df8bae27e7e7711b206995b8021557d AS steam

# Make steamcmd download steam client libraries so we can copy them later.
RUN steamcmd +login anonymous +quit

# runtime stage – also based on ubuntu LTS; allow STEAM_APP_ID to be overridden
FROM ubuntu@sha256:186072bba1b2f436cbb91ef2567abca677337cfc786c86e107d25b7072feef0c AS runtime

# default Steam AppID can be overridden with --build-arg STEAM_APP_ID=xxxx
ARG STEAM_APP_ID=335300

RUN mkdir -p /opt/ds2os/Saved \
    && if ! id ds2os >/dev/null 2>&1; then \
           useradd -r -s /bin/bash ds2os; \
       fi \
    && chown ds2os:ds2os /opt/ds2os/Saved \
    && chown ds2os:ds2os /opt/ds2os \
    && chmod 755 /opt/ds2os/Saved \
    && chmod 755 /opt/ds2os \
    && apt update \
    # Healthcheck needs curl to check the server
    && apt install -y --no-install-recommends --reinstall ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

# expose the various ports the game server uses so operators can easily publish them
#   50000/udp – DS3 game traffic (also used for quickmatch/arena)
#   50010/udp – DS2 game traffic
#   50050/udp – arena transport (DS3)
# and the HTTP/admin API port
EXPOSE 50000/udp 50010/udp 50050/udp
EXPOSE 50005/tcp

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -fsS http://localhost:50005/ || exit 1

# write the AppID from build arg
ENV STEAM_APP_ID=${STEAM_APP_ID}
RUN echo "$STEAM_APP_ID" >> /opt/ds2os/steam_appid.txt

# Copy only the built runtime outputs from the build stage into the runtime image.
# Avoid copying the full /build tree to keep image size small.
COPY --from=build /build/bin/x64_release/. /opt/ds2os/

# Optional debug output during build (comment out in production):
# RUN ls -al /opt/ds2os && find /opt/ds2os -maxdepth 4 -type f -print

# Uncomment this to preserve full /build for inspection.
# COPY --from=build /build /build
COPY --from=steam /root/.local/share/Steam/steamcmd/linux64/steamclient.so /opt/ds2os/steamclient.so

ENV LD_LIBRARY_PATH="/opt/ds2os"

USER ds2os
WORKDIR /opt/ds2os
ENTRYPOINT ["/opt/ds2os/Server"]
CMD [] 
