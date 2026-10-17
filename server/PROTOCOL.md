# ORE SEAS — client ⇄ server protocol (v1)

For the Unity client. The server referees matches, follows live ORE rounds read-only, and keeps leaderboards.
It never signs or sends transactions. The client signs everything: the wallet signs via MWA/Seed Vault, and the
session key signs in-app.

- **WebSocket:** `ws(s)://<host>/ws`. One JSON object per text frame. Each message has a string field `t` (its type).
- **REST:** `http(s)://<host>/...`. GET only, JSON, CORS `*`.
- **Local dev on the box:** `ws://localhost:8787/ws` and `http://localhost:8787/health`.
- **Numbers:** lamports, ORE "grams" (1 ORE = 10^11 grams) and other u64 amounts are **decimal strings**. Slots and
  round ids are JSON numbers, which are safe below 2^53. Squares are integers `0..24`.

## 0. Coordinates, fleet and commit

| Concept | Definition |
|---|---|
| Square index | `square = row*5 + col`. Rows are `A..E` and cols are `1..5`. A1=0, A5=4, B1=5, C3=12, E5=24. The index is the ORE square index directly (bit `i` of the Deploy mask). |
| Fleet | galleon (size 3, code 1), sloop (size 2, code 2), raft (size 1, code 3). Each ship is a straight, contiguous line. Ships may touch. |
| Layout | `int[25]`. `layout[i]` is the ship code on square i (0 = water). |
| Salt | 32 random bytes, sent as 64 hex chars. Generate a fresh salt every match (C#: `FleetCommit.NewSalt()`). |
| Commit | `hex(sha256(layoutBytes[25] ‖ salt[32]))`, lowercase (C#: `FleetCommit.Compute`). |

Test vector, checked in both the TS and C# tests:
layout `[1,1,1,0,0, 0,0,0,0,0, 2,0,0,0,0, 2,0,0,0,0, 0,0,0,0,3]` with salt `000102…1f` (bytes 0..31) gives
`f16a694e03790b6af47baee163085adab6b145ebc39165db6bfc7cf16ef6cf2a`.

The client sends `layout`, `salt` and `commit` in `place_fleet`. The server needs the layout to referee. Both clients
get both commits in `fleets_committed`, and `match_end` reveals both layouts and salts. **The client must verify**
`sha256(opp.layout ‖ opp.salt) == opponentCommit` and flag the match if it doesn't match.

## 1. Session flow

```
client                                   server
  | ---- (connect /ws) ------------------> |
  | <--- challenge {nonce} ---------------- |
  | ---- hello {name, wallet?, signIn?} --> |   signIn = SIWS (MWA authorize sign-in) over a message that contains nonce
  | <--- welcome {playerId, token, ...} --- |
  | ---- queue_ranked | create_private | join_private | start_solo
  | <--- match_found {matchId, seat, rules, opponent, stakeLamports, source}
  | ---- place_fleet {layout, salt, commit}
  | <--- fleet_ack / opponent_ready / fleets_committed {yourCommit, opponentCommit}
  |                    ┌──────────── each turn = one ORE round ────────────┐
  | <--- turn_start {roundId, endSlot, lockSlot, slot, slotMs, maxShots}   |
  | ---- aim {roundId, squares[]}   (repeat freely until lock)             |
  | <--- aim_ack / opponent_aiming {count} / clock (every ~3 s)            |
  | <--- lock {squares, mask, amountLamports, fire:true}  ← SAME slot for both players
  |      client builds + signs Checkpoint(prev)+Deploy(mask) and sends it before endSlot
  | ---- fired {roundId, signature}  (informational)                       |
  | <--- round_closed                                                      |
  |      (ORE intermission ~48 slots ≈ 13 s; the server reads both players' Miner accounts on chain)
  | <--- reveal {incoming, outgoing, ore{winningSquare, motherlode}, next}  |
  |                    └───────────────────────────────────────────────────┘
  | <--- match_end {result, stats, fleets{you, opponent}} ; rating_update
```

## 2. Timing (slots, not wall clock)

- ORE round: deploy window of `endSlot - startSlot` = 240 slots (~64 s at ~0.267 s/slot), then a 48-slot intermission
  (~13 s). The next round opens when its first deploy lands, usually right after the reset.
- `lockSlot = endSlot - lockOffset`. lockOffset is 20 slots (~5 s) for auto-fire with a session key, or 60 slots (~16 s)
  if either human uses `fireMode:"manual"` (a Seed Vault prompt per shot). Practice uses 1 slot. Both players lock
  at the same slot, and the server ignores aims after lock.
- Client countdown: `secondsLeft = (lockSlot - estSlot) * slotMs / 1000`, where
  `estSlot = msg.slot + (now - receivedAt) / slotMs`. Re-anchor on every `clock` and `turn_start` message.
- If fewer than 40 slots (~10 s) remain before lock when a round opens, the server waits for the next round.
- If ORE never settles a round (an RPC outage), the server sends `turn_void` about 260 slots after the intermission
  and moves to the next round.

## 3. Client → server messages

| t | fields | notes |
|---|---|---|
| `hello` | `name` (≤20 chars), `wallet?` (base58), `signIn?` `{message, signature}`, `guestId?`, `resume?` `{playerId, token}` | `signature` may be base58, base64 or hex over the UTF-8 `message`, and `message` must contain the `nonce`. **Ranked needs a verified wallet.** Without a wallet you're a guest (`g_<16 hex>`). Keep `guestId` so stats persist. |
| `queue_ranked` | `stakeLamports?` (one of `stakeOptionsLamports`, default `"5000000"`), `fireMode?` `"auto"`\|`"manual"` | Elo matchmaking (±100, widening 25/s). Only pairs players with the same stake. |
| `cancel_queue` | – | |
| `queue_ranked` (dev) | | With the server env `ALLOW_GUEST_RANKED=true`, guests (no wallet) may queue; they always play `fireMode: "virtual"`. |
| `create_private` | `stakeLamports?`, `fireMode?` `"auto"`\|`"manual"`\|`"virtual"` | Replies `private_created {code}` (6 chars, valid 10 min). `"virtual"` makes the match off-chain with no wallet needed. |
| `join_private` | `code`, `fireMode?` | |
| `start_solo` | `captain` (id from `/api/captains`), `mode` `"practice"`\|`"live"`, `fireMode?`, `stakeLamports?` | `practice` runs fast off-chain turns (30 s aim, `fireNow` skips ahead). `live` uses real ORE rounds; with a wallet plus auto/manual your shots are real deploys, and without one they're virtual (no ORE). |
| `place_fleet` | `matchId`, `layout` int[25], `salt` hex64, `commit?` hex64, `stakeLamports?` | If you skip placement, the server auto-places after 60 s and returns the layout and salt in `fleet_ack`. `stakeLamports` (v1.1.0, solo vs AI only, one of the stake options) sets SOL per cannon for the match, which is chosen on the placement screen. It is optional: when absent, the `start_solo` stake stays. A bad value or a PvP match gets `bad_stake` and the fleet is not placed. |
| `aim` | `matchId`, `roundId`, `squares` int[] (≤ `maxShots`, unique, not already fired at), `fireNow?` bool | The latest aim before lock wins. `fireNow` only works in practice vs AI. |
| `fired` | `matchId`, `roundId`, `signature` | Informational only. The referee always reads chain state. |
| `resign` | `matchId` | |
| `leave_match` | `matchId` | v1.1.0. This is HARBOR on the placement screen. It works only for a solo vs AI before the first round: the match is dropped with no result (no loss, no rating, no stats) and you get `match_left {recorded:false}`. After the fleets are committed you get `too_late`; in PvP you get `not_allowed`, so resign instead. `set_profile` on a wallet session also needs a SIWS-verified session (`wallet_unverified` otherwise). |
| `get_state` | – | The server replies `match_state` (use after app resume). |
| `ping` | `echo?` | The server replies `pong {serverTime, slot, echo}`. |

## 4. Server → client messages

| t | key fields |
|---|---|
| `challenge` | `protocol`, `nonce`, `serverTime` |
| `welcome` | `playerId`, `token` (keep for `resume`), `name`, `wallet`, `verified`, `rating`, `activeMatch` (non-null: a `match_state` follows), `ore` (same as `/api/ore/status`), `stakeOptionsLamports` |
| `queued` / `queue_left` | `position`, `stakeLamports` |
| `private_created` | `code`, `stakeLamports`, `expiresInSeconds` |
| `match_found` | `matchId`, `mode` (`ranked`\|`private`\|`solo_live`\|`practice`), `seat` (0/1), `fireMode`, `stakeLamports` (per shot, per square), `source` (`live`\|`replay`\|`practice`), `rules`, `placementSeconds`, `opponent {name, id, isAI, captain, rating, wallet}` |
| `fleet_ack` | `commit`, `autoPlaced`, `stakeLamports` (the match stake after placement), plus `layout` and `salt` if auto-placed |
| `opponent_ready` | – |
| `fleets_committed` | `yourCommit`, `opponentCommit` (**store opponentCommit**) |
| `turn_start` | `turn`, `roundId`, `startSlot`, `endSlot`, `lockSlot`, `slot`, `slotMs`, `serverTime`, `maxShots`, `bonusShots`, `bonusReason` (`"sank"`\|`"crit"`\|null), `afloat` string[], `opponentAfloat`, `alreadyFired` int[] |
| `aim_ack` | `roundId`, `squares`, `maxShots` |
| `opponent_aiming` | `count` (never the squares) |
| `clock` | `phase` (`aiming`\|`locked`\|`settling`), `roundId`, `slot`, `lockSlot`, `endSlot`, `slotMs`, `serverTime` |
| `lock` | `roundId`, `squares`, `coords`, `mask` (u32 Deploy mask), `amountLamports`, `fire` (true means send the Deploy now), `fireMode`, `endSlot`, `slot` |
| `round_closed` | `roundId`, `slot` (deploys are now final, waiting on ORE's entropy and reset) |
| `reveal` | see §5 |
| `turn_void` | `roundId`, `reason` |
| `opponent_disconnected` / `opponent_reconnected` | `graceSeconds` (90). If the opponent doesn't come back within it, they forfeit. |
| `match_state` | full snapshot: `phase`, `turn`, `slot`, `you{layout, salt, commit, afloat, hitsTaken, aim, locked, maxShots}`, `opponent{commit, afloatCount, sunk, sunkShips, hitsOnThem, missesOnThem}` |
| `match_end` | `result` (`win`\|`loss`\|`draw`), `reason` (`fleet_sunk`\|`mutual_destruction`\|`turn_limit`\|`resign`\|`disconnect`\|`inactive`), `turns`, `stats` (`volleys`, `shots`, `hits`, `sunk`, `crits`, `oreGrams`, `ore`, `misfires`, `kraken`, `lamportsDeployed`, `solReturnedLamports`), `fleets{you{layout, salt, commit}, opponent{layout, salt, commit, commitVerified}}`, `claim` |
| `rating_update` | `delta`, `rating` (ranked only, Elo K=32) |
| `error` | `code`, `message`, `ref` (the offending `t`). Codes: `bad_json`, `not_authenticated`, `wallet_required`, `wallet_unverified`, `bad_signature`, `bad_wallet`, `bad_stake`, `bad_code`, `self_join`, `in_match`, `bad_captain`, `no_match`, `bad_fleet`, `bad_aim`, `unknown_type`, `internal` |

## 5. `reveal` payload (per player perspective)

Play `order` in sequence: first `incoming` (their volley on your sea), then `outgoing` (yours on theirs).

```jsonc
{
  "t": "reveal", "matchId": "6ba342ac4ffd", "turn": 1, "roundId": 431893,
  "ore": { "winningSquare": 22, "winningCoord": "E3", "motherlode": false, "motherlodeOre": 0, "split": true,
           "totalDeployedSol": 10.788, "slotHash": "ea2c…70ac", "synthetic": false, "roundId": 431893 },
  "kraken": false,                       // true => play the Kraken overlay on top of everything
  "order": ["incoming", "outgoing"],
  "incoming": VolleyView,                // opponent's shots on YOUR fleet (`ship` names your ship that was hit)
  "outgoing": VolleyView,                // your shots; `ship` stays null unless that shot sank it (no info leak)
  "you":      { "afloat": ["galleon","sloop","raft"], "sunk": [], "hitsTaken": [12], "missesTaken": [7, 11] },
  "opponent": { "afloatCount": 3, "sunk": [], "sunkShips": [{ "type": "sloop", "cells": [3, 4] }],
                "hitsOnThem": [], "missesOnThem": [6, 11, 20] },
  "next":     { "maxShots": 3, "bonusShots": 0, "bonusReason": null },
  "totals":   { "ore": 0, "crits": 0, "hits": 0, "shots": 3 },
  "log": ["Calculon fired B3, C2, C3. C3 hit your galleon!", "You fired B2, C2, E1."]
}
VolleyView = {
  "shooter": "Hunter", "seat": 0,
  "locked": [6, 11, 20],                 // what was locked at lockSlot
  "shots": [ { "square": 6, "coord": "B2", "hit": false, "ship": null, "sunk": null, "crit": false,
               "fx": "splash", "oreGrams": "123…"? } ],
  "misfire": false,                      // locked targets but NO matching on-chain deploy => no shots counted
  "verification": "chain" | "event" | "sim" | "virtual" | "ai",
  "ignoredSquares": [],                  // deployed on chain but not locked (still mines ORE, not a shot)
  "underfundedSquares": [],              // deployed less than stakeLamports => not a shot
  "oreGrams": "0", "ore": 0, "oreEstimated": false,
  "lamportsDeployed": "15000000", "solReturnedLamports": "0"
}
```

`fx` maps directly to the animation:

| hit | shot on ORE winning square | `fx` | animation |
|---|---|---|---|
| no | no | `splash` | splash |
| yes | no | `explosion` | explosion + fire |
| no | yes | `gold_bubbles` | splash, then gold bubbles (ORE mined) |
| yes | yes | `critical` | CRITICAL HIT + ORE, nugget burst, Nuggy dance |

**Shot rules**
- Max shots per volley is ships afloat (3→1). Add +1 next volley after sinking a ship or a crit. The Kraken Cult boss gets +1 every volley.
- Turn limit is 15. After that the winner is decided by cells hit + ships sunk, otherwise it's a draw.
- If a human goes 3 turns in a row without a valid volley, they forfeit (`inactive`).

**ORE amounts**
- `oreGrams` is what ORE's checkpoint math gives the player for that round, before the 10% claim fee on unrefined ORE. Split rounds pay pro-rata on the winning square. In non-split rounds only ORE's `top_miner` gets the +1 ORE, so a crit in a non-split round usually earns 0 ORE unless you're the top miner.
- The Motherlode always pays pro-rata.
- In the simulator `oreEstimated` is true, because sim deploys aren't really in the pot.

## 6. Firing (client side, C# `csharp/OreSeas.Ore`)

Auto-fire, the default (one Seed Vault approval per match):

1. **Match start (wallet signs once via MWA):** build `OreSeasTx.SessionSetup(wallet, sessionKey, stakeLamports, budget)`. This is a SystemTransfer of ~0.002 SOL to the session key for tx fees, then `Automate`: strategy Discretionary(2), `executor` = session key, `amount` = stakeLamports, `deposit` = budget, `fee` = 5000 (reimburses the session key per deploy), `reload` = true (returned SOL refills the budget). Get the suggested `budget` from `GET /api/session/plan`.
2. **Each `lock` with `fire:true` (session key signs in-app):** read the player's Miner account (`OrePda.Miner(wallet)`). If `miner.NeedsCheckpoint`, include `Checkpoint(miner.RoundId)`. Then send `OreSeasTx.FireTurn(sessionKey, wallet, roundId, mask, amountLamports, checkpointRoundId)` before `endSlot`. Send `fired {signature}`.
3. **Match end (optional second approval):** `OreInstructions.AutomateClose(wallet)` refunds the remaining budget. `ClaimOre`/`ClaimSol` are separate wallet actions.

For manual mode, the wallet is the signer of `FireTurn` (`signer = authority = wallet`) and gets a Seed Vault prompt per turn. Lock comes 60 slots early to leave time for it.

The referee checks each turn by reading the wallet's ORE Miner PDA right after `endSlot`. A shot only counts if that
square has ≥ `stakeLamports` deployed in `roundId` **and** was locked. If the miner has already moved to a later
round, the referee falls back to the `DeployEvent` logs in the miner PDA's recent transactions.

## 6b. Captain profiles (name + portrait)

Guest ids (`g_…`) are public (they appear on leaderboards), so a guest also sends a private **`guestKey`**
(32–64 hex chars, generated once on the phone) in `hello`. The first hello that presents a key binds it (the server
stores only `sha256(guestKey)`); a later hello for that id with a missing/wrong key gets a fresh guest id.

| client → server | server → client |
|---|---|
| `hello {…, guestId?, guestKey?}` | `welcome {…, name, avatar, nameClaimed, pairedWallet, walletPairing}` |
| `check_name {name}` | `name_check {name, ok, reason}` — reason `available` / `yours` / `name_taken` / a validation code |
| `upload_avatar {png}` (base64 of a 20x20 PNG, ≤ 6 KB) | `avatar_uploaded {avatar:"u:<hash16>", url}` or `error bad_size / not_png / bad_dimensions` |
| `set_profile {name, avatar}` | `profile {playerId, name, avatar, nameClaimed:true, wallet, pairedWallet}` or `error name_taken / too_short / too_long / bad_chars / needs_letter / reserved / not_allowed / bad_avatar` |
| `pairing_message {wallet}` | `pairing_message {enabled, message}` — the SIWS text the wallet *would* sign (preview only) |
| `pair_wallet {wallet, message, signature}` | `wallet_paired {playerId, wallet}`; **`error pairing_disabled` unless the server runs with `ALLOW_WALLET_PAIRING=true`** (the client also gates it with `ServerConfig.SIWS_PAIRING = false`) |

Name rules: shown upper case, 3–16 chars of `A-Z 0-9 space _ . -`, at least one letter, no leading/trailing or doubled
punctuation, not a reserved/captain name, small blocklist. Uniqueness is on the squashed key (`Salty_Nugget` ==
`salty nugget`). An unclaimed `hello` name can't impersonate a claimed one (it becomes `Captain`).

Avatar refs: `a:<id>` built-in (`nuggy nuggy_ginger nuggy_snow nuggy_ghost nuggy_royal nuggy_tricorn captain barnacle brine
molly ghost cult salt corsair skull kraken parrot nugget`) or `u:<hash16>` = first 16 hex of sha256(png) of an uploaded
20x20 portrait. `match_found.opponent.avatar` carries the rival's ref (null for AI captains); leaderboard entries carry `avatar`.

### 6c. Store policy: report, block, delete

| client → server | server → client |
|---|---|
| `report_player {playerId, kind:"name"\|"avatar", reason?}` | `report_ack {playerId, kind, duplicate}`; errors `bad_kind`, `bad_target` (self / AI), `unknown_player`, `report_limit` (30 per reporter per day) |
| `block_player {playerId}` / `unblock_player {playerId}` | `blocked {playerId, blocked:[ids]}`. `welcome.blocked` carries the list. Blocked pairs are never matched in the ranked queue; joining a blocked captain's invite code answers `bad_code`. |
| `delete_account {confirm:"DELETE"}` | `account_deleted {playerId}`, then the socket closes (1000). Without the confirm: `error confirm_required`. |

Reports are stored with a snapshot of the reported name/portrait. When `REPORT_THRESHOLD` (default 3) distinct captains
report the same name, it reverts to an unclaimed `Captain` (the name is free again); for a portrait, the uploaded PNG is
hidden (404, can't be re-used) and the player falls back to the default portrait. Reports stay in the `reports` table
(`resolved=1`) for review.

Deletion removes the player row (name, portrait ref, guest-key hash, wallet links), their per-match stat rows (gone from
every leaderboard), portraits only they used, reports and blocks in both directions, and replaces their id/name/wallet
in stored match records with `deleted` / `DELETED CAPTAIN`. A live match is resigned first. The same phone can start
over (its guest id comes back unclaimed).

Scripted clients send `hello {…, bot:true}`; leaderboard entries and `/api/players/:id` carry `ai: true` for them (and
for the demo captains seeded into screenshot DBs) so the client can tag them AI.

## 7. REST

| GET | returns |
|---|---|
| `/health` | `{ok, protocol, ore, matches, queue, connections, db}` |
| `/api/ore/status` | `{source, slot, slotMs, roundSlots, intermissionSlots, round{roundId, startSlot, endSlot, phase, secondsToEnd}, lastResult}` |
| `/api/rules` | rules + stake options |
| `/api/captains` | solo campaign captains: `id`, `name`, `island`, `difficulty`, `tactic`, `bonusShots`, `boss`, `blurb`, `unlocks` |
| `/api/leaderboard?period=all\|week&mode=ranked\|solo\|all&sort=wins\|wl\|games\|ore\|accuracy\|crits\|sunk\|volleys\|kraken\|rating&limit=50&minGames=1` | `{entries:[{rank, id, name, avatar, ai, wallet, rating, games, wins, losses, draws, wl, battles, volleys, shots, hits, accuracy, sunk, crits, ore, oreGrams, kraken, misfires}]}`. The week starts Monday 00:00 UTC. |
| `/api/avatars/<hash16>.png` | the uploaded 20x20 portrait (`image/png`, immutable cache) |
| `/api/avatars` | built-in avatar ids |
| `/api/names/check?name=&player=` | `{name, ok, reason}` (same as `check_name`) |
| `/api/players/:id` | `{id, name, wallet, rating, global{all, ranked, solo}, weekly{all, ranked, solo}}` (same stat fields) |
| `/api/players/:id/matches?limit=20` | recent matches |
| `/api/matches/:id` | live status, or the finished record with both layouts, salts, commits, `commitVerified`, and every reveal |
| `/api/session/plan?stakeLamports=&turns=&wallet=` | suggested automation budget, notes, and the wallet's Miner/Automation PDAs |

## 8. Server restarts (deploys)

On SIGTERM (e.g. `fly deploy`) the server stops matchmaking and sends every socket
`{ "t": "server_restart", "reason": "maintenance", "reconnectAfterMs": 5000, "matchAbandoned": bool }`, then closes it
with WebSocket close code **1012** ("Service Restart"). Unfinished matches are abandoned: not recorded, no rating
change, no loss. During the drain, `queue_ranked` / `create_private` / `start_solo` return error `server_restarting`,
and `/health` returns 503. Client: show "SERVER RESTARTING", then reconnect with backoff (5 s, 10 s, 20 s…). Sessions
and resume tokens live in memory, so they don't survive a restart: send a fresh `hello` with the same `guestId` and
`guestKey`.
