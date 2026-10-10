# NUGSHIP - Unity client (Seeker / Android)

**Sink ships. Mine ORE.**

Pirate Battleship duels where every volley is an ORE round. Portrait 240x534 pixel art (Seeker 1200x2670 = exactly 5x).
Unity **6000.3.20f1**, URP 2D, Android (IL2CPP, ARM64, minSdk 26), package id `com.nugship.game`, product name `Nugship`.
(The folder, C# namespaces `OreSeas.*` and PlayerPrefs keys `os.*` keep the old working name on purpose.)

> Dev build rules: simulated ORE only. Nothing is signed, sent or spent. The wallet seam can CONNECT via MWA (devnet)
> and read a public key; `SignTransactionStub` always refuses until Victor explicitly OKs real transactions.
> Nuggy is a mascot character only - no token/price/ticker anywhere in the app.

## Play it
1. Open the folder in Unity Hub (6000.3.20f1, Android module). First open resolves packages (Solana.Unity-SDK from git).
2. Open `Assets/Scenes/Main.unity`, press Play. Game view: 1200x2670 (or any portrait size; integer-scaled).
3. Mouse = touch. Esc = Android back.

Build an APK: menu **NUGSHIP > Build Android Dev APK** (-> `Builds/Nugship-dev.apk`), or batchmode:

    "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" -batchmode -quit -projectPath C:\Users\vdifu\Desktop\OreSeas ^
      -buildTarget Android -executeMethod OreSeas.EditorTools.BuildScript.BuildAndroidDev -logFile C:\Users\vdifu\Desktop\OreSeasLogs\build.log

Compile-only check: `-executeMethod OreSeas.EditorTools.BuildScript.CompileCheck` (logs `[OreSeas] COMPILE OK`, then
`[OreSeas] SELFTEST ...` lines: fleet-commit test vector, JSON, LIVE_ORE gate; exit 0 = ok, 2 = self-test failed). Also menu
**NUGSHIP > Compile Check + Self Test**, or `Tools\compile_check.cmd`.

## Online play (NUGSHIP server)
Server: `/server` in the orehack repo (copied to `C:\Users\vdifu\Desktop\OreSeasServer` on VENGEANCE). Protocol: its PROTOCOL.md.

    cd C:\Users\vdifu\Desktop\OreSeasServer
    npm install            (once; builds better-sqlite3)
    run_dev.cmd            (ALLOW_GUEST_RANKED=true, npm start on :8787; add "replay" for fast replayed rounds)
    adb reverse tcp:8787 tcp:8787     (Seeker -> PC; the game's default server is ws://127.0.0.1:8787/ws)

- `ROUND_SOURCE=live` (default) follows mainnet ORE rounds (~60 s) via read-only public RPC; `replay` replays real rounds at 10x.
- `ALLOW_GUEST_RANKED=true` is a dev flag so guests (no wallet) can use QUICK MATCH; walletless players are always `fireMode: "virtual"`.
- In game: SETTINGS > SERVER (tap to edit the URL, TEST, RESET URL), ONLINE PLAY on/off.

What's wired (client):
- `Net/ServerClient` - WebSocket session: challenge -> hello (guest id persisted, resume token on reconnect) -> welcome; ping;
  auto-reconnect while a match is live; REST helpers. `Net/WsClient` (System.Net.WebSockets, main-thread dispatch), `Net/Json`.
- **PvP 1v1** (`MatchmakingScreen`): QUICK MATCH (`queue_ranked`, Elo) and private **INVITE CODE** (`create_private` virtual) / **JOIN CODE**
  (OS keyboard; editor: paste from clipboard). Offline -> honest PVP SERVER OFFLINE + clearly labelled SIM rival.
- **Solo vs AI via server** (`CampaignScreen`): `start_solo` practice with the island's captain (barnacle/brine/molly/ghost/kraken_cult);
  falls back to the local AI if the server is unreachable. Campaign progress unlocks on a server win too.
- **Fleet commit-reveal**: `place_fleet` with layout int[25] (1 galleon / 2 sloop / 3 raft), fresh 32-byte salt,
  commit = sha256(layout25 || salt32) (`Net/OnlineSession`); placement countdown; server auto-place adopted.
  At `match_end` both revealed fleets are verified locally against our commit and the `opponentCommit` from `fleets_committed`.
- **Round sync** (`OnlineBattleScreen`): `turn_start` (slot clock re-anchored on every `clock`), aims streamed (latest wins),
  FIRE = final aim (`fireNow` in practice), `lock` (same slot for both), `round_closed`, two-beat `reveal`
  (incoming then outgoing, crit / sink / Kraken), `turn_void`, opponent disconnect/reconnect, `match_state` resync, resign (back x2).
- **Results**: win/loss/draw + reason, stats, commit verification, Elo change (`rating_update`), NEW MATCH / REMATCH.
- **Leaderboards** (`LeaderboardScreen` + `Services/ServerLeaderboard`): GLOBAL / WEEKLY from `/api/leaderboard` (rank, captain,
  games, W-L, W/L, battles = volleys, Elo); MY LOG = local stats + naval rank / XP (`PlayerRank`) + server Elo (`/api/players/:id`).
- **Live ORE (read-only)**: `Ore/Chain/*` = the C# ORE layer (csharp/OreSeas.Ore) under `OreSeas.Ore.Chain`;
  `Ore/LiveOreRoundSource` polls getSlot + the Board/Round accounts (JSON-RPC, public endpoint, `os.rpc` override) for the
  round clock and winning square. SETTINGS > SIM RIVAL ROUNDS = LIVE ORE puts the offline SIM rival on real rounds.

**Money gate.** `ServerConfig.LIVE_ORE = false` (const): the wallet is never sent to the server (so every match is off-chain /
virtual), no SIWS message is signed, `lock.fire` is ignored, and `Ore/Chain/OreInstructions.cs` (Deploy/Automate builders) only
compiles with the `ORESEAS_LIVE_ORE` define, which nothing sets. MWA connect (read the pubkey) still works; signing still refuses.

## What's in the game
- **Title**: NUGSHIP sign with Nuggy peeking over it (bob + blink), Nuggy's 3-mast galleon sailing in the midground (replaced the rock sea arch; 24-frame loop `title_ship_*`: bob, sail ripple, Nuggy flag, streamers, wake, stern lantern), lighthouse, Nuggy on his rock, wallet chip (MWA on device; offline in editor).
- **Battle HUD (v2)**: top scoreboard = YOU (your captain portrait + ships-left icons: ok / damaged / sunk) | wheel timer + ORE round | opponent (portrait + name + ships-left, only sunk shown).
  Bottom-left: Nuggy as a reactive mascot (idle blink/tail, hop+cheer on a hit, dance + gold sparkle on a crit, wince with ears down on a miss or when your ship is hit, sleepy Zz while locked) - no label.
  Beside him: **SHOTS left/total** with cannonball pips that empty as you place shots (+1 gold-ringed bonus pip after a sink/crit). Board frames carry no captions;
  practice mode shows one small PRACTICE tag. No 'REAL SOL AT RISK' / 'EXAMPLE VALUES' stamps.
- **Solo campaign**: chart of 5 islands/captains, each a different sea + AI personality, plus a date-seeded **Daily Tide**.
  - Barnacle (Sunset Cove) - random hunter
  - Admiral Brine (Moonlit Reef, night) - hunt/target with checkerboard parity
  - Mad Molly (Storm Strait, rain + lightning) - hunt/target, centre-biased
  - Ghost Captain (Fog Banks) - probability-density hunter
  - Kraken Cult (Volcanic Isles, ash) - probability density + 1 extra shot per volley (boss)
  - Daily Tide (Frozen Fjord, snow)
- **PvP 1v1**: quick match + invite codes through the NUGSHIP server (see Online play). Server unreachable -> **PVP SERVER OFFLINE** and a clearly
  labelled SIMULATED rival on a simulated or live-read ORE clock (fast 0.1 s/slot or real 0.268 s/slot; lock at end_slot-20 auto-fire / -60 manual).
- **Fleet placement**: drag ships, tap to rotate, shuffle; SET SAIL seals `sha256(layout || salt)`; verified at match end.
- **Battle**: salvo = ships afloat (+1 bonus after sinking a ship or a crit); both fire simultaneously; two-beat reveal
  (YOUR WATERS first, then ENEMY WATERS); splash / explosion / gold bubbles / CRITICAL HIT (rays, ribbon, nugget fountain,
  Nuggy dance) / Motherlode = THE KRAKEN RISES overlay; volley report; results with fleet-commit verification.
- **Captain's log / leaderboards**: games, wins, losses, draws, W/L, volleys, shots, accuracy, ships sunk, crits, ORE mined (sim),
  Kraken sightings, campaign progress. GLOBAL / WEEKLY tabs show your row + "SERVER OFFLINE" (no fake players).
- **Settings**: music, sfx, vibration, **REDUCE FLASHES** (on by default), ORE clock FAST/REAL, DEMO LUCK (Kraken 1/25 instead of 1/500 for demo videos).
- **Gentle FX (accessibility)**: no full-screen white flashes anywhere. Hits = warm vignette pulse (<=15% alpha,
  ~150 ms ease-out), crits = soft gold glow, Kraken = one slow dim lightning flicker; screen shake is 1-2 px for <=0.2 s.
  With REDUCE FLASHES on: half-strength pulses, no shake on normal hits/firing, dimmer slower crit rays, storm lightning
  without any screen flicker. (`View/ScreenPulse.cs`, `Settings.ReduceFlashes`)
- **Audio**: chiptune nautical score (title waltz, battle calm + tension layer that rises as the lock nears, Kraken theme,
  victory/defeat stingers) + 23 sfx. Haptics via Android vibrator.

## Layout
    Assets/OreSeas/Scripts/Runtime/
      Core/      Px (logical coords, exact JS hash/rng ports), Pal, PixelCanvas, PixelFont/PixelText (REELM font),
                 Atlas (runtime sprite slicing), Gfx, SpriteAnim, AudioManager+Haptics, InputRouter, UiButton/Ui, GameApp, Bootstrap, Settings
      Net/       ServerConfig (URL + LIVE_ORE gate), ServerClient, WsClient, Json, OnlineSession (commit-reveal, layout codes)
      Ore/       IOreRoundSource, SimulatedOreRoundSource, LiveOreRoundSource (read-only RPC), OreConstants (measured), OreSim
      Ore/Chain/ C# ORE layer: account decoders, PDAs, constants, FleetCommit (OreInstructions gated off)
      Game/      Fleet + FleetCommit, BoardState + Salvo, MatchState (rules), AI captains, Biomes/Captains, PlayerStats
      Services/  IWalletService (+ MwaWalletService, OfflineWalletService), IMatchService (+ LocalSimMatchService),
                 ILeaderboardService (+ LocalLeaderboardService, ServerLeaderboardService, PlayerRank XP)
      View/      OceanRenderer (ported ocean()), WeatherRenderer, BoardView/ShipView, NuggyView, Fx, Scenery
      Screens/   Title, Campaign, Matchmaking, Fleet, Battle (local), OnlineBattle (server), Results/Kraken/Settings overlays, Leaderboard
    Assets/OreSeas/Scripts/Editor/   BuildScript (configure + APK), OreSeasAssetPostprocessor (pixel import rules)
    Assets/OreSeas/Resources/        Atlases (9 PNG atlases + frame JSON), Audio (ogg/wav), Data/reelm_font.txt
    Assets/OreSeas/Art/Source/       approved mockup screens + Nuggy sheet (reference)
    Tools/unity_export/              mockup -> atlas/audio pipeline (see its README)

## Seams for the real thing
- `IOreRoundSource`: `LiveOreRoundSource` reads ORE Board/Round accounts (slot clock). It only *reads*; deploys would go
  through the wallet seam after Victor's OK (and LIVE_ORE + ORESEAS_LIVE_ORE).
- `IWalletService.SignTransactionStub` is where checkpoint+deploy bundles would be signed (MWA -> Seed Vault).
- PvP runs through `ServerClient` + `OnlineSession` (PROTOCOL.md); `IMatchService` remains for the offline SIM rival.
- `ILeaderboardService.Record/Fetch` for global + weekly boards.
