# build stage based on ubuntu LTS
FROM ubuntu@sha256:d1e2e92c075e5ca139d51a140fff46f84315c0fdce203eab2807c7e495eff4f9 AS build

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

FROM steamcmd/steamcmd:latest@sha256:e5d7e8acdef9d99dbeff206df05b3762d3108a5ce58315be488ae199abca7b09 AS steam

# Make steamcmd download steam client libraries so we can copy them later.
RUN steamcmd +login anonymous +quit

# runtime stage – also based on ubuntu LTS; allow STEAM_APP_ID to be overridden
FROM ubuntu@sha256:d1e2e92c075e5ca139d51a140fff46f84315c0fdce203eab2807c7e495eff4f9 AS runtime

# default Steam AppID can be overridden with --build-arg STEAM_APP_ID=xxxx
ARG STEAM_APP_ID=374320

RUN mkdir -p /opt/ds3os/Saved \
    && if ! id ds3os >/dev/null 2>&1; then \
           useradd -r -s /bin/bash ds3os; \
       fi \
    && chown ds3os:ds3os /opt/ds3os/Saved \
    && chown ds3os:ds3os /opt/ds3os \
    && chmod 755 /opt/ds3os/Saved \
    && chmod 755 /opt/ds3os \
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
RUN echo "$STEAM_APP_ID" >> /opt/ds3os/steam_appid.txt

# Copy the built outputs from the build stage into the runtime image.
# The runtime stage originally had no /build contents, so this COPY is required.
COPY --from=build /build /build

RUN ls -al /build && find /build -maxdepth 5 -type f -print

RUN if [ -d /build/bin/x64_release ]; then \
      cp -a /build/bin/x64_release/. /opt/ds3os/; \
    else \
      echo "Error: canonical build output directory /build/bin/x64_release not found"; exit 1; \
    fi
COPY --from=steam /root/.local/share/Steam/steamcmd/linux64/steamclient.so /opt/ds3os/steamclient.so

ENV LD_LIBRARY_PATH="/opt/ds3os"

USER ds3os
WORKDIR /opt/ds3os
ENTRYPOINT ["/opt/ds3os/Server"]
CMD [] 
