# syntax=docker/dockerfile:1
# NUGSHIP game server (Node/TS, WebSocket /ws, REST, SQLite). Read-only on chain: never signs or sends transactions.
# Build:  docker build -t nugship-server .
# Run:    docker run --rm -p 8787:8787 -v nugship-data:/data nugship-server
# Fly:    see DEPLOY.md (fly.toml mounts a volume at /data).

ARG NODE_IMAGE=node:22-bookworm-slim

# ---- deps + build (dev deps, toolchain in case better-sqlite3 has no prebuilt binary for this arch) ----
FROM ${NODE_IMAGE} AS build
WORKDIR /app
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 make g++ ca-certificates \
 && rm -rf /var/lib/apt/lists/*
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY tsconfig.json tsconfig.build.json ./
COPY src ./src
RUN npx tsc -p tsconfig.build.json \
 && npm prune --omit=dev --no-audit --no-fund \
 && npm cache clean --force

# ---- runtime: no compilers, no dev deps, app runs as the unprivileged "node" user ----
FROM ${NODE_IMAGE} AS runtime
ENV NODE_ENV=production \
    PORT=8787 \
    DB_PATH=/data/oreseas.db \
    ROUND_SOURCE=live \
    REQUIRE_WALLET_PROOF=true \
    ALLOW_GUEST_RANKED=false \
    ALLOW_WALLET_PAIRING=false \
    LOG_FORMAT=json \
    NODE_OPTIONS=--max-old-space-size=160
WORKDIR /app
COPY --from=build /app/package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod 0755 /usr/local/bin/docker-entrypoint.sh \
 && mkdir -p /data && chown node:node /data
# Starts as root only long enough to chown the mounted volume (Fly volumes mount root-owned), then drops to "node".
EXPOSE 8787
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD node -e "fetch('http://127.0.0.1:'+(process.env.PORT||8787)+'/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"
STOPSIGNAL SIGTERM
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["node", "dist/src/index.js"]
