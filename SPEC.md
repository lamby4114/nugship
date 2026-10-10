# NUGSHIP (formerly ORE SEAS) — "Sink ships. Mine ORE." — build spec (Solana Mobile "CLOCK IN" hackathon, Seeker)
Owner: Vittorio (Victor). Deadline: submit before Mon Oct 12 2026 7:59 PM EDT (portal submissionCloseDate 2026-10-12T23:59:00.000Z). Deliverables: Android APK (native, NOT a web wrapper), public GitHub repo, demo video, pitch deck.
Rules that matter: must use Mobile Wallet Adapter (MWA) + meaningful Solana interaction; ORE bonus needs a live, user-facing ORE integration with ORE as the featured product. Mascot Nuggy (black kitten, gold ORE-symbol headband, nugget) is a character only: NO NUG token/price/ticker in app.
Hard rule: nothing is signed, sent, deployed with funds, or spent without Victor's explicit in-chat OK. Use devnet/simulated ORE for dev; mainnet only after his OK.

## Stack
- Client: Unity 6000.3.20f1, Android, 2D URP, portrait 1200x2670 (Seeker), pixel-perfect (240x534 logical, 5x). Project: C:\Users\vdifu\Desktop\OreSeas on VENGEANCE (machineId 86f4294d-8ed4-4b92-8359-f5612c770359). Solana.Unity-SDK (https://github.com/magicblock-labs/Solana.Unity-SDK.git) for MWA/Seed Vault + RPC. No Play Solana SDK.
- Server: Node/TypeScript (Colyseus or plain ws), in /workspace/orehack/server: matchmaking, fleet commit-reveal referee, ORE round watcher, leaderboards (SQLite/Postgres). Hosting TBD (needs Victor's OK if paid).
- Art/audio source of truth: /workspace/orehack/mockups_v2 (src/*.js, build.py, nuggy_sprites.png, REELM font/wood in reelm_ref).

## Game
- Modes: SOLO campaign (vs AI captains, no wallet needed, optional real-ORE mode) and RANKED 1v1 PvP matchmaking (real ORE).
- Board: 5x5 per player; coordinates A1–E5 map 1:1 to ORE's 25 squares. Fleet: galleon(3), sloop(2), raft(1). Fleet layout hashed (sha256(layout||salt)) at match start; revealed + verified at match end.
- Turn = one ORE round (~77 s: 240-slot deploy window ~64 s + 48-slot break ~13 s). Countdown driven by slots, not wall clock. Both players fire simultaneously; targets lock for both at the same moment (end_slot-20 in auto-fire, end_slot-60 per-shot signing).
- Salvo: shots per volley = ships afloat (3→1); +1 bonus shot after sinking a ship or a crit. Each shot = ORE deploy on that square, same amount per square (one Deploy ix with square mask), bundled with Checkpoint of prior round.
- Auto-fire (default): one Seed Vault approval at match start funds an ORE Automation budget with a session key as executor; app signs each turn. Per-shot signing as fallback.
- Outcomes per shot: miss/no ORE = splash; hit/no ORE = explosion; miss+ORE = splash then gold bubbles; hit+ORE = CRITICAL HIT (gold flash, nugget burst, Nuggy dance). Motherlode (1/500) = Kraken event overlay.
- Reveal: pan to your waters (their volley), then theirs (your volley); volley log scroll; mini-map damage.
- Leaderboards: games played, wins, losses, W/L ratio, total battles/volleys, ships sunk, accuracy, crits, ORE mined, Kraken sightings; global + weekly.
- Practice/solo off-chain turns run fast (no ORE wait).

## Content
- Maps (biomes): Sunset Cove (default), Moonlit Reef (night), Storm Strait (rain/lightning), Fog Banks (limited visibility visual), Frozen Fjord (snow/ice floes), Volcanic Isles (ash, lava glow). Day/night tint + weather particles as layers.
- Solo campaign: chart of islands; each island = AI captain with personality/tactic (Barnacle, Admiral Brine, Mad Molly, Ghost Captain, The Kraken Cult), boss fights, unlock ship skins/flags/Nuggy hats; daily challenge.
- Music: original retro chiptune nautical score (warm, adventurous, melancholic-epic seafaring feel; NOT copied from any existing game): title theme, harbor/menu loop, battle loop (calm aiming layer + tension layer near lock), victory/defeat stingers, Kraken theme. Loops 60–120 s, soft, non-fatiguing. SFX synthesized retro.

## Progression (rank-unlocked fleets: cosmetic only, gameplay stays fair)
Principle: every fleet has the SAME 3 hulls (galleon 3 tiles, sloop 2, raft 1), same hitboxes, same sprite cell sizes and pivots. Rank only changes looks, flags, Nuggy hats and bragging rights. XP is earned, never sold. No token, no NFT, no tradeable items; profile data lives on our server, not on-chain.
- XP sources (ranked PvP): win +100, loss +30, each enemy ship sunk +20, each CRITICAL (hit+ORE) +25, Kraken sighting (motherlode round during your match) +100, first win of the day +50. Solo campaign: win +40, island boss first clear +150. Rank never goes down (ranked MMR is tracked separately).
- Rank ladder (XP needed → fleet → Nuggy hat → flag unlocked):
  1. DECKHAND 0 → Driftwood (patched rafts, fishing skiff with nets, lug sail) → straw hat → paw
  2. BOSUN 500 → Merchant Cog (striped square sails, single mast, cargo crates) → knit beanie → anchor
  3. FIRST MATE 1500 → Pirate Brig (black sails with skull, red hull) → sailor cap → skull, swords
  4. CAPTAIN 3500 → Navy Frigate (cream sails with red stripe, 3 masts, gun ports) → tricorn → pickaxe
  5. COMMODORE 7000 → Ironclad Steam (riveted grey hull, smokestack + smoke, gear sail, turret) → bicorne → moon
  6. ADMIRAL 12000 → Ghost Fleet (teal spectral hull, tattered glowing sails) → plumed admiral bicorne → kraken
  7. ORE LORD 20000 + at least 1 Kraken sighting → Motherlode (gold-plated hulls with ore veins, dark purple sails with gold veins) → crown → ore
- Equip any unlocked fleet in the Shipyard (mockup: assets/mockups/05_shipyard.png). Locked tiers show a dithered silhouette, padlock and XP needed. Both players see each other's fleets and flags at the reveal.
- Custom flags: pick any unlocked flag (8 designs, assets/sprites/flags.png, 4-frame wave anim). Phase 2: a cloth colour picker (6 palette swaps via colour-replace shader).
- Named flagship: the player names their galleon (3–16 chars, A–Z 0–9 space ' -, REELM font, profanity filter, default "THE SALTY WHISKER"). The name shows on the Shipyard plaque, in the battle intro ("THE SALTY WHISKER SETS SAIL"), in the volley log, on the victory screen and in the captain's log.
- Nuggy hats/outfits per rank: straw, beanie, sailor, tricorn, bicorne, admiral, crown. Sprites: nuggy.png frames `nuggy_<pose>_<hat>` plus anims `nuggy_idle_<hat>` and `nuggy_dance_<hat>`; no suffix means no hat. Players may wear any hat they have unlocked.
- Veteran marks: when a ship takes a hit and its side WINS the match, it keeps a scar for that hull. Store up to 5 per hull as {kind: patch|scorch|hole, x offset}; past 5, add a tally mark. Render with the fx.png `scar_*` decals over the hull sprite (the `*_veteran_*` frames are a baked example). "Repaint" in the Shipyard clears them, and they are worn proudly by default.
- Captain's log: the server auto-writes an in-character entry for notable battles: first win, first sink, CRITICAL, Kraken sighting, flawless win (no hits taken), comeback (won with 1 ship left), rank-up, boss clear. Template: "DAY 12 - THE SALTY WHISKER MET MAD MOLLY IN THE FOG BANKS. SANK HER SLOOP WITH A CRITICAL AT C3. +0.0118 ORE MINED." Shown as parchment pages in the Shipyard tab (newest first, keep the last 50). Any sharing feature is phase 2 and needs Victor's OK.
- Leaderboard rows show rank badge, flag and flagship name.

## Asset drop for the Unity client (generated; source = mockups_v2, re-run `cd /workspace/orehack/mockups_v2 && python3 build.py && node tools/export.mjs`)
- /workspace/orehack/assets/sprites/<sheet>.png + <sheet>.json. Sheets: ships_{driftwood,cog,brig,frigate,ironclad,ghost,motherlode}, fx, flags, nuggy, hud, tiles, reelm_font_1x, biome_<key>, weather; biomes.json = palette/tint/weather particle specs per biome.
- Atlas JSON: meta{image,size,scale:1,pixelsPerUnit:1,filter:'point',compression:'none'}; frames[name] = {frame:{x,y,w,h} (top-left origin), pivotPx:{x,y} (top-left origin), pivot:{x,y} (Unity normalised, bottom-left origin), border?:{l,r,t,b} (9-slice: Unity border x=l,y=b,z=r,w=t), meta?}; animations[name] = {frames[], fps, loop}. Import: Sprite Mode Multiple, PPU 1 (or 5 with a 5x camera), Filter Point, Compression None, slice from JSON.
- Ship frame names: <kind>_{intact,damaged,veteran}_0..3 (8 fps), <kind>_burning_0..5 (12 fps), <kind>_sunk, <kind>_top_{intact,damaged,sunk} (top-down horizontal, for the board), <kind>_rot_{intact,damaged,sunk} (top-down rotated 90 degrees cw for vertical placement). Waterline pivot on side views.
- Mockups 1200x2670: assets/mockups/00_title … 05_shipyard, fleets_showcase.png. Biome battle previews: assets/previews/.
- Audio: assets/audio/music (OGG + WAV 44.1k, loop points in music.json): title_theme 72.7 s loop (D maj 6/8), harbor_loop 85.3 s (F maj swing, tide bed), battle_calm + battle_tension 85.7 s synced stems (A dorian 112 BPM; start both with PlayScheduled on the same dspTime, fade tension in from T-20 s to lock), kraken_theme 80.0 s, victory_stinger 6.1 s, defeat_stinger 8.0 s. assets/audio/sfx/sfx_*.wav (13 SFX). Source: /workspace/orehack/audio/*.py.
