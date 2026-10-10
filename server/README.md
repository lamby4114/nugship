# ORE SEAS server

This is the Node/TypeScript backend for ORE SEAS. It handles matchmaking (ranked Elo queue, private invite codes,
solo vs AI), a match state machine synced to live ORE rounds, the fleet commit-reveal, an on-chain hit referee,
leaderboards (SQLite) and REST.

**It's read-only on chain:** it never signs or sends transactions.

- Protocol for the Unity client: [PROTOCOL.md](PROTOCOL.md)
- Hosting comparison: [HOSTING.md](HOSTING.md). **Fly.io deploy runbook: [DEPLOY.md](DEPLOY.md)** (Dockerfile, fly.toml)
- C# (Unity) ORE layer: `../csharp/OreSeas.Ore`, with tests in `../csharp/OreSeas.Ore.Tests`

```
npm ci
npm test                 # encoding tests vs real mainnet fixtures + game/referee tests (offline)
npm run dev              # live ORE rounds, ws://localhost:8787/ws
ROUND_SOURCE=replay npm run dev   # replays recent real rounds at 10x (fast local testing)
npm run sim -- --source replay --speed 25   # 4 matches end-to-end with bots, no txs sent
npm run sim -- --source live                 # same on live mainnet rounds (~10-15 min)
npm run fixtures         # (read-only) refresh test/fixtures/mainnet.json from mainnet
npx tsx scripts/check-chain-reader.ts        # (read-only) referee deploy reader vs live miners
npm run build && npm start
ALLOW_GUEST_RANKED=true npm start            # DEV: guests may quick-match (always fireMode "virtual", off-chain)
```

Windows / phone testing: `run_dev.cmd [replay]` then `adb reverse tcp:8787 tcp:8787` (Unity client default `ws://127.0.0.1:8787/ws`).

## Profiles

Captain names (unique, validated) and portraits (built-in `a:<id>` or uploaded 20x20 PNG `u:<hash16>`) — see
PROTOCOL.md §6b. Uploaded portraits live in the `avatars` table. Wallet pairing via Sign-In-With-Solana is implemented
but **off** unless `ALLOW_WALLET_PAIRING=true` (needs Victor's OK: it asks the wallet to sign a message).

## Layout
```
src/ore/            ORE program layer: constants, PDAs, account decoders (Board/Round/Miner/Automation/Treasury/Config),
                    instruction builders (Deploy, Checkpoint, ClaimSOL, ClaimORE, Automate/AutomateV2 incl. session setup + close),
                    instruction/event decoders, ORE reward math (oreEarned/solReturned)
src/chain/rpc.ts    read-only, rate-limited RPC wrapper (serialized queue, 429 backoff, TTL cache)
src/chain/roundSource.ts   LiveRoundSource (slot + board subscriptions w/ polling fallback), ReplayRoundSource (real
                    settled rounds on a compressed clock), PracticeRoundSource (per-match synthetic, fire-now)
src/chain/deployReader.ts  ChainDeployReader (Miner account snapshot at end_slot, DeployEvent-log fallback), SimLedger
src/game/fleet.ts   layout validation, sha256 commit, FleetState
src/game/ai.ts      AI captains (random / hunt / probability-density) + campaign metadata
src/game/match.ts   match state machine + referee + reveal payloads
src/db/store.ts     SQLite: players, matches, match_players; global + weekly leaderboards; Elo
src/net/server.ts   HTTP REST + WebSocket protocol, matchmaking
src/sim/            bot client + end-to-end harness
test/               ore-encoding.test.ts (byte-for-byte vs mainnet), game.test.ts, fixtures/
```
