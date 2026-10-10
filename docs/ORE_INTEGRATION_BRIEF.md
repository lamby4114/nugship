# NUGSHIP × ORE: integration brief (for ORE / RadiantsDAO judges)

**One line:** NUGSHIP turns every ORE v3 round into a turn of a 1v1 naval duel on the Seeker. The 5×5 board is ORE's grid, every cannon shot is
an ORE Deploy, a hit on ORE's winning square is a **critical hit**, and a Motherlode is **the Kraken**. ORE is the game clock, the
reward and the spectacle. It is the only on-chain product in the app.

---

## The four ORE prize criteria

The criteria below are quoted from the official CLOCK IN site, "Optional Prizes: ORE" (solanamobile.radiant.nexus):

| ORE criterion | How NUGSHIP meets it | Status at submission |
|---|---|---|
| **Build with ORE**: "A working, user-facing ORE integration must be live in the submitted product." | Every match follows **live mainnet ORE rounds**: real round IDs, slot-driven countdown, real winning square and Motherlode flag, read from ORE's Board/Round accounts. With live mode on, each shot is a real `Deploy` from the player's wallet via a per-match ORE Automation session. | Live round following: **working** (read-only RPC). Player deploys: **[GATED: LIVE_ORE=false / ENABLED in build X.Y after owner-approved mainnet test on DATE, tx SIGNATURE]** |
| **Make ORE a core part of the product**: ORE must be the primary product featured and promoted. | ORE *is* the turn structure. There's no other token: Nuggy is a mascot with no token or ticker, and there are no NFTs. ORE mined, crits and Kraken sightings sit on the leaderboards and in the captain's log. The tagline is "Sink ships. Mine ORE." | Done by design |
| **Keep it live**: the integration must stay live and actively supported. | We maintain the server, client and ORE layer, with pinned program constants and regression tests against mainnet fixtures. See the maintenance commitment below. | Commitment below |
| **Milestones / progress updates / promote** | We propose the milestone plan below, with usage metrics reported from our server and verifiable on-chain. ORE gets named in every launch post, demo and store listing. | Proposal below |

## Why it's real (verifiable)

- **ORE program:** `oreV3EG1i9BEgiAJ8b177Z2S2rMarzak4NMv1kULvWv` (code reviewed at regolith-labs/ore @48c203b, 2026-10-02).
- **Instruction layer:** Deploy, Checkpoint, ClaimSOL, ClaimORE, Automate/AutomateV2 (incl. session setup + close) in TypeScript (server) and
  C# (client). The encodings are checked **byte-for-byte against real mainnet transactions** (`server/test/ore-encoding.test.ts`; 36/36 server tests pass).
- **Referee reads the chain:** after `end_slot` it reads each player's Miner PDA (falling back to DeployEvent logs). A shot only counts if the square has at least the
  per-shot amount deployed in that round and was locked.
- **Timing measured on mainnet** (read-only, Oct 7 2026): 240-slot deploy window, 48-slot intermission, 289 slots start-to-start (~77 s).
- **Each turn is one tx:** Checkpoint(prev) + Deploy(mask). That's 32 of ~38 sampled checkpoint txs on mainnet, so it matches how existing miners already behave.

## What NUGSHIP brings to ORE (no invented numbers)

- **New miners with a reason to come back:** every PvP duel puts two wallets into ORE rounds for the whole match, and daily/ranked loops bring them back.
- **Volume per match, as an arithmetic ceiling:** ≤4 shots/turn × 15 turns = **≤60 square-deploys per player per match**.
  At the default 0.005 SOL per shot that's ≤0.3 SOL per player per match. *This is a ceiling from the rules, not a forecast.*
- **ORE as a spectacle on mobile:** the winning-square reveal and the 1-in-500 Motherlode become moments players watch for.
- **A gentle on-ramp:** free practice and solo modes teach ORE's grid and timing before anyone deploys SOL.

## Maintenance commitment ("keep it live")

| Area | Commitment |
|---|---|
| Uptime | Server hosted on Fly.io (always-on machine, persistent volume, health checks, auto-restart). Target: game playable through the full judging + prize period. |
| ORE program changes | We follow regolith-labs/ore releases. A weekly `npm run fixtures` + `npm test` run re-checks our encodings against fresh mainnet transactions, and `scripts/check-chain-reader.ts` validates the referee against live miners. Encoding breaks get patched within **72 h** of detection. Until then, live mode falls back to practice with an in-app notice. |
| RPC | Dedicated RPC key (Helius/Triton/QuickNode free or paid tier) with the public endpoint as fallback. Rate-limited, cached reader. |
| Client releases | Updates ship through the Solana dApp Store, with the same release key and a bumped versionCode. |
| Safety | LIVE_ORE stays a compile-time gate. Per-shot amount and Automation budget are shown before the single approval. The session key is limited to the approved budget and closed at match end. |
| Support | In-app feedback link + X account [X_HANDLE]. Incident notes are posted publicly if live mode is ever paused. |
| Reporting | Short written progress update at each milestone (below), plus monthly while prize payments are outstanding. |

## Proposed milestones (for the staged ORE prize, to be agreed with ORE)

Targets are left as **[TBD with ORE]** on purpose. We'd rather agree real numbers than invent them.

| # | Milestone | Deliverable | Evidence we'll share |
|---|---|---|---|
| M1 | **Live ORE launch** (≤30 days after winners are announced) | Live mode on mainnet in the published dApp Store build, plus Seed Vault one-approval sessions | Store listing URL, release tx/NFT, sample deploy signatures, server stats endpoint |
| M2 | **Retention release** (+30 days) | Seasons / weekly ladders, Daily Tide rewards (cosmetic), Kraken spectator replay | Matches/day, unique deploying wallets, D1/D7 retention, deploys + SOL deployed via NUGSHIP: [TBD with ORE] |
| M3 | **Social & competitive** (+60 days) | 2v2 fleets or crews/guilds, plus a first community tournament played on ORE rounds | Tournament results, participants, ORE mined in-game: [TBD with ORE] |
| M4 | **Sustain** (ongoing during the payment period) | Monthly maintenance updates, ORE program compatibility | Monthly update post + metrics |

**Metric sources:** server match logs (wallet, roundId, mask, `fired` signature), cross-checked on-chain via Miner PDAs and tx signatures.
Players are pseudonymous, with no personal data beyond the profile name.

## Open items before submission (owner decisions)

1. Run one **owner-approved mainnet test round** (about 0.005 SOL deploy plus one-time rents; see TIMELINE.md) to prove the deploy path end-to-end.
2. Decide whether the **submitted APK ships live mode on** (opt-in toggle, practice by default) or gated. The ORE prize text says the integration "must be
   live in the submitted product". With deploys gated, only the read-only live-round integration counts, and that's weaker.
3. Fill in the placeholders: [X_HANDLE], links, and the tx signature of the test round.
