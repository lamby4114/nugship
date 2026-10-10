# Deploying the NUGSHIP server to Fly.io

**Status: prep only. Nothing has been created or deployed.** Every command below is for Victor (or an agent, once
Victor OKs the exact spend) to run. Fly needs a credit card once the trial ends, and with an always-on machine that
happens almost immediately.

Files: `Dockerfile` (multi-stage, Node 22 slim, app runs as the non-root `node` user), `docker-entrypoint.sh` (chowns
the Fly volume, then drops root), `.dockerignore`, `fly.toml`, `tsconfig.build.json`, `scripts/ws-smoke.mjs`.

## 1. What it costs (Fly.io public pricing, checked Oct 8 2026)

Source: https://fly.io/docs/about/pricing/ (iad/ewr prices; machines are billed per second while running).

| Item | Price | This app |
|---|---|---|
| `shared-cpu-1x` 256 MB machine, always on | $2.19 / 30 days | 1 machine → **$2.19** |
| Volume (provisioned size) | $0.15 / GB / month | 1 GB → **$0.15** |
| Volume snapshots | $0.08 / GB / month, **first 10 GB free** | DB is a few MB → $0.00 |
| Outbound data, North America/Europe | $0.02 / GB | ~1–5 GB of WebSocket/REST → $0.02–$0.10 |
| Shared IPv4 + IPv6, `*.fly.dev` TLS cert | free (first 10 single-host certs free) | $0.00 |
| Dedicated IPv4 | $2 / month, optional | not needed |

**Estimate: ~$2.35–$2.45 per month** (about 8¢ a day). Through judging (to mid-November) that's about $2.50–$3.
There's no free tier. New orgs get a trial of 2 machine-hours or 7 days, whichever comes first, with no card needed
to start. An always-on machine burns the 2 hours on day one, so plan on adding a card.

Measured on the box: idle RSS ~31–40 MB (fits 256 MB easily; `NODE_OPTIONS=--max-old-space-size=160`, 256 MB swap as
a safety net). Docker image ~290 MB uncompressed.

## 2. Decisions baked into `fly.toml`

- App `nugship-server`, region `iad`, 1 × `shared-cpu-1x`/256 MB, `auto_stop_machines = "off"`,
  `min_machines_running = 1`. The machine never sleeps, so live WebSocket matches and the ORE round follower keep
  running.
- `[http_service]` on internal port 8787, `force_https = true` (clients use `wss://` and `https://`), connection-based
  concurrency (soft 400 / hard 500), idle timeout 600 s. Health check `GET /health` every 15 s, with a 20 s grace
  period.
- Volume `nugship_data` → `/data` (SQLite `DB_PATH=/data/oreseas.db`, WAL mode), with daily snapshots kept 5 days.
- `kill_signal = SIGTERM`, `kill_timeout = 15`. On shutdown the server stops matchmaking, sends `server_restart` to
  every client, closes sockets with code 1012, and checkpoints and closes SQLite. Unfinished matches are abandoned:
  not recorded and no rating change. Finished matches are already safe on disk. See PROTOCOL.md §8.
  - In-memory matches can't be carried across a restart. They hold live round subscriptions, timers and referee
    state, and a volume can only attach to one machine, so every deploy means about 10–30 s of downtime anyway.
    **Don't deploy during a live demo.**
- Structured JSON logs (`LOG_FORMAT=json`), one line per event: `ts`, `level`, `msg`, `app`, `region`, `machine`.
  The boot line prints the config with RPC URLs reduced to their host, so API keys never reach the logs.
  Crashes (`uncaughtException`) exit 1, and Fly restarts the machine (`[[restart]] policy = "on-failure"`).

### Environment variables

| Var | Prod value | Notes |
|---|---|---|
| `NODE_ENV` | `production` | turns on JSON logs by default |
| `ROUND_SOURCE` | `live` | follows mainnet ORE rounds, read-only. The server never signs or sends transactions. |
| `REQUIRE_WALLET_PROOF` | `true` | keep |
| `ALLOW_GUEST_RANKED` | **`true` for the hackathon** (code default `false`) | see below |
| `ALLOW_WALLET_PAIRING` | `false` | SIWS pairing stays off until Victor OKs it |
| `SIWS_DOMAIN` | `nugship-server.fly.dev` | domain shown in the (future) sign-in message |
| `ALLOWED_ORIGINS` | `*` | REST is public read-only data, and the native app sends no `Origin` header (native clients are always allowed). Set e.g. `https://nugship.xyz` if a website ever calls the API: other browser origins then get 403 on REST and WS. |
| `RPC_URL`, `RPC_WS_URL` | **secrets** | unset = public `https://api.mainnet-beta.solana.com` |
| `SHUTDOWN_GRACE_MS` | `1500` | time between the `server_restart` notice and closing sockets |
| `REPORT_THRESHOLD` | `3` | distinct captains reporting the same name / portrait before it is taken down automatically (PROTOCOL §6c) |

**`ALLOW_GUEST_RANKED`, the tradeoff.** The Seeker build ships with `LIVE_ORE = false` and `SIWS_PAIRING = false`,
so no player can have a verified wallet. With the flag off, **quick-match ranked returns `wallet_required` for
everyone**, and judges can't try PvP matchmaking (invite-code matches still work in `virtual` mode).
With it on, walletless guests can queue ranked. Their shots are always `fireMode: "virtual"`: no deploys, nothing on
chain, no money. The cost is integrity. Guest identities are free, so one person with two devices could farm wins
and Elo, and the ranked leaderboard isn't tied to a wallet. **Recommendation: `true` through judging** (the
leaderboard is a showcase, there are no prizes or payouts, and one command undoes it). Flip it to `false` when wallet
sign-in ships: `fly secrets set ALLOW_GUEST_RANKED=false` (secrets override `[env]`), or edit `fly.toml` and redeploy.

**RPC.** The server makes ~0.18 read calls/s at idle (measured: 42 calls in 240 s ≈ 450k/month), plus slot/board
WebSocket subscriptions and a few reads per match turn. The public endpoint rate-limits per IP, and Fly's shared
egress IPs are shared with other apps, so 429s are likelier there. Helius Free costs $0/month with 1M credits/month,
10 RPC req/s, 1 credit per standard call and standard WebSocket methods included
(https://www.helius.dev/pricing, https://www.helius.dev/docs/billing/plans). That covers this load. Signing up is
Victor's call: it's an account, but no card. The server already rate-limits itself (serialized queue, 150 ms spacing,
429 backoff, TTL cache). Also note the Unity client reads `https://api.mainnet-beta.solana.com` directly for its
round display (`ServerConfig.DefaultRpc`).

## 3. Step by step (run once Victor approves)

Run from the server folder: on VENGEANCE `C:\Users\vdifu\Desktop\OreSeasServer` once it's synced from the box, or on
the box at `/workspace/orehack/server`. `fly deploy` builds the image remotely on Fly's builder, so Docker isn't
needed locally.

```bash
# 0. Install flyctl
#    Windows (PowerShell):  pwsh -Command "iwr https://fly.io/install.ps1 -useb | iex"
#    macOS/Linux:           curl -L https://fly.io/install.sh | sh
fly version

# 1. Account (opens a browser). New account: signup; existing: login. Add a card under Billing when prompted.
fly auth signup        # or: fly auth login
fly auth whoami

# 2. Create the app (name is global; if taken, pick another and change `app =` in fly.toml + the Unity URL)
fly apps create nugship-server

# 3. Create the 1 GB volume in iad (fly warns that a single volume has no redundancy; answer y, daily snapshots are on)
fly volumes create nugship_data --app nugship-server --region iad --size 1

# 4. Secrets (optional but recommended). Staged now, applied on first deploy. Skip to use the public RPC.
fly secrets set --app nugship-server --stage \
  RPC_URL="https://mainnet.helius-rpc.com/?api-key=YOUR_KEY" \
  RPC_WS_URL="wss://mainnet.helius-rpc.com/?api-key=YOUR_KEY"

# 5. Deploy exactly ONE machine (--ha=false: otherwise Fly may try to create 2 machines, and a volume can't be shared)
fly deploy --app nugship-server --ha=false

# 6. Verify
fly status --app nugship-server                     # 1 machine, started, check passing
curl https://nugship-server.fly.dev/health          # {"ok":true,...,"ore":{"source":"live",...}}
node scripts/ws-smoke.mjs wss://nugship-server.fly.dev/ws   # 2 guests: welcome -> queued -> match_found -> match_end
fly logs --app nugship-server                       # JSON lines; look for "ORE SEAS server on :8787" and round lines
```

Day-2 commands:

```bash
fly deploy --app nugship-server                     # redeploy (expect ~10-30 s downtime: clients get server_restart, then reconnect)
fly secrets set --app nugship-server ALLOW_GUEST_RANKED=false   # flip a flag (restarts the machine)
fly releases --app nugship-server                   # history; roll back with: fly deploy --image <previous image ref>
fly volumes snapshots list <volume-id>              # daily backups (volume id from: fly volumes list)
fly ssh console --app nugship-server               # then inside: cd /app && node -e 'require("better-sqlite3")("/data/oreseas.db").backup("/data/backup.db").then(()=>console.log("ok"))'
fly ssh sftp get /data/backup.db --app nugship-server   # download a consistent copy of the leaderboard DB
fly scale count 0 --app nugship-server --yes        # pause compute (volume still billed $0.15/mo)
fly apps destroy nugship-server                     # delete everything (irreversible: deletes the volume and leaderboards)
```

## 4. Unity client changes for the hosted server (done in the Unity repo)

1. `ServerConfig.cs`: `DefaultServer` is chosen at build time. Dev builds keep `ws://127.0.0.1:8787/ws` (adb reverse);
   builds with the `NUGSHIP_RELEASE` scripting define use `ReleaseServer = "wss://nugship-server.fly.dev/ws"`.
   `HttpBase` derives `https://nugship-server.fly.dev` for REST and avatars. The SETTINGS override stays, but a
   release build ignores any non-`wss://` override (a stale dev URL from an old install).
2. `BuildScript.cs`: `BuildAndroidDev` uses `InsecureHttpOption.DevelopmentOnly` (cleartext to the PC only in
   development builds). `BuildAndroidRelease` (menu **NUGSHIP > Build Android RELEASE APK**) builds a non-development
   APK with `NUGSHIP_RELEASE` + `InsecureHttpOption.NotAllowed`, signed with a keystore given by env vars
   `NUGSHIP_KEYSTORE` (outside the repo), `NUGSHIP_KEYSTORE_PASS`, `NUGSHIP_KEY_ALIAS`, `NUGSHIP_KEY_PASS`
   (+ optional `NUGSHIP_VERSION_CODE`, `NUGSHIP_VERSION_NAME`). It refuses to build without them. No release keystore
   exists yet: create a dedicated one for the Solana dApp Store (not the Google Play key) and keep it off the repo.
3. Phones that already saved a server URL in PlayerPrefs (`os.server`) keep using it in dev builds. Tap **RESET URL** in
   SETTINGS, or reinstall.
4. `ServerClient.cs`: handles `server_restart` and close code 1012: shows "SERVER RESTARTING", drops the resume token,
   reconnects with backoff (`reconnectAfterMs`, then x2 up to 60 s) and sends a fresh `hello` with the same
   guestId/guestKey. A match that was abandoned ends with "SERVER RESTARTED - MATCH NOT COUNTED".
5. Ranked quick-match against the hosted server needs `ALLOW_GUEST_RANKED=true` (§2) while `LIVE_ORE`/`SIWS_PAIRING`
   are off.
6. Store policy (PROTOCOL §6c): DELETE PROFILE on the profile screen (`delete_account`), REPORT/BLOCK from a leaderboard
   row and from PvP results (`report_player`, `block_player`), AI tag on scripted/demo leaderboard rows (`ai: true`).

## 5. Optional: custom domain

```bash
fly certs add play.<yourdomain> --app nugship-server    # first 10 single-host certs are free
fly ips list --app nugship-server                       # then DNS: CNAME play -> nugship-server.fly.dev (or A/AAAA to these IPs)
fly certs check play.<yourdomain> --app nugship-server
```

Then point the client at `wss://play.<yourdomain>/ws` and set `SIWS_DOMAIN=play.<yourdomain>`. The domain itself costs
whatever the registrar charges, which is a separate purchase that needs Victor's OK.

## 6. Local verification done on the box (Oct 8 2026, ~1:20–1:37 AM ET)

- `npm test`: **36/36 pass** (the 33 existing tests plus 3 new ones in `test/deploy.test.ts`: env config and
  redaction, origin allowlist on REST/WS, graceful shutdown with `server_restart` + 1012 + abandoned match not
  recorded + `/health` 503).
- `docker build`: OK on node:22-bookworm-slim. `docker run` with a named volume: `/health` 200, live ORE rounds
  followed. The app runs as uid 1000 (`node`), including on a root-owned bind mount (simulating a Fly volume).
- `scripts/ws-smoke.mjs` against the container and against `npm run build && node dist/src/index.js` (prod env): two
  guests → `queued` → `match_found` → resign → `match_end` → `rating_update`.
- `docker stop` (SIGTERM) with a client mid-match: client got `server_restart` (`matchAbandoned: true`) and close
  1012. Logs show `shutting down` → `unfinished matches abandoned` → `shutdown complete`. After a restart on the same
  volume, players and matches persisted.
- `fly.toml` validates against the SchemaStore fly.toml JSON schema (0 errors). `flyctl config validate` needs a
  login, so it wasn't run.
