# Use a stable LTS Node version
FROM docker.io/node:lts-alpine AS base

# create app directory and switch to it early so we can take advantage of cache
WORKDIR /app

# copy package definitions first to leverage layer caching
COPY package*.json ./

# install only production dependencies, clean npm cache, install curl, create unprivileged user and take ownership
RUN if [ -f package-lock.json ]; then npm ci --only=production; else npm install --only=production; fi \
    && npm cache clean --force \
    && apk add --no-cache curl \
    && addgroup -S master && adduser -S master -G master \
    && chown -R master:master /app

# copy the rest of the source tree
COPY . .

USER master

# expose the api port for documentation
EXPOSE 50020/tcp

# simple healthcheck against the servers endpoint
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:50020/api/v1/servers || exit 1

# run the server via npm script
ENTRYPOINT ["npm", "run", "start"]
