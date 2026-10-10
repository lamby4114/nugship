# NUGSHIP demo video: shot list, voiceover and capture guide

**Target length 2:45** (the official site describes "a demo of three minutes", so stay **under 3:00**). Judges score completion "based on the demo video".
Show the real app on a real Seeker. Every on-screen number must come from the actual build, so no invented stats.

Two variants depending on Vittorio's live-ORE decision:
- **A: LIVE**: the owner-approved mainnet test round is done and live mode is in the build. Show Seed Vault approval and a real deploy tx.
- **B: GATED**: LIVE_ORE=false. Show live ORE round following (real round IDs) with virtual shots, and say so in one honest line.

## Shot list + voiceover

| # | Time | Shot (screen) | Voiceover (VO) | On-screen text |
|---|---|---|---|---|
| 1 | 0:00–0:08 | Cold open: crit moment. Gold rays, nugget fountain, Nuggy dancing (battle screen). | "Every minute, ORE runs a mining round on Solana. What if every round was a cannon shot?" | **NUGSHIP** |
| 2 | 0:08–0:18 | Title screen: Nuggy bobbing, galleon sailing. Tap SOLO CAMPAIGN / PVP. | "This is NUGSHIP, a PvP naval duel built natively for the Seeker. Sink ships. Mine ORE." | Native Android · Seeker · ORE v3 |
| 3 | 0:18–0:30 | Profile: pick a name, pixel portrait or upload a PFP. | "Pick a captain name and a portrait. Names are unique, checked by the server." | |
| 4 | 0:30–0:45 | Overlay: 5×5 board with ORE square indices (deck slide 4 graphic, or an animated grid). | "The 5-by-5 board *is* ORE's 25 squares. Each turn is one live ORE round, about a minute." | A1–E5 = ORE squares 0–24 |
| 5 | 0:45–1:00 | Fleet placement: drag, rotate, SET SAIL. Briefly show the commit hash. | "Hide your galleon, sloop and raft. Your layout is sealed with a SHA-256 commitment, so nobody can move ships mid-match." | sha256(layout‖salt) |
| 6A | 1:00–1:12 | **A:** Seed Vault approval sheet (one tap). | "One Seed Vault approval funds the whole match through an ORE Automation session. No popups mid-battle." | 1 approval / match |
| 6B | 1:00–1:12 | **B:** PVP 1v1 › QUICK MATCH / invite code; matchmaking. | "Quick match pairs you by Elo, or send a friend an invite code." | |
| 7 | 1:12–1:40 | Battle: ORE ROUND #id visible, wheel timer, tap 3 targets, LOCK. Then the reveal: incoming (your waters), then outgoing. Splash, explosion, sink. | "Salvo size is your ships afloat. Both captains lock on the same Solana slot. Every shot is an ORE deploy on that square. When the round settles: splash, hit... sunk!" | ORE ROUND #[id] (live) |
| 8 | 1:40–1:52 | Crit: hit on the winning square. Gold bubbles / CRITICAL HIT. Nuggy reacts. | "Land a hit on ORE's winning square and it's a critical hit, plus the ORE you mined." | CRITICAL = hit on ORE winner |
| 9A | 1:52–2:05 | **A:** Solscan/Explorer page of the deploy tx (screen capture from PC), signature matching the in-game log. | "That's a real deploy on mainnet. Our server referees by reading your ORE miner account, and it never signs anything." | tx [SIG] |
| 9B | 1:52–2:05 | **B:** Settings › Server/ORE clock, or the ORE round id beside ore.supply (PC capture). | "Rounds are live mainnet ORE, read straight from the chain. Real-SOL deploys sit behind a safety gate in this build." | Live ORE rounds · deploys gated |
| 10 | 2:05–2:15 | Kraken overlay (Motherlode). | "And once every 500 rounds, the Motherlode hits... the Kraken rises." | If DEMO LUCK was used: "Demo luck on, real odds 1/500" |
| 11 | 2:15–2:30 | Solo campaign chart, captains, biome montage (storm, fog, snow, volcano), Daily Tide. | "Five AI captains, each with its own tactics. Six seas with weather and day-night, and a fresh Daily Tide every day." | |
| 12 | 2:30–2:40 | Leaderboard (global/weekly), captain's log, results with FLEET VERIFIED. Shipyard mockup only if labelled. | "Climb the ranks from Driftwood to the Motherlode fleet. Leaderboards track wins, battles and Elo, and every fleet is verified at the end." | Fleet verified ✓ |
| 13 | 2:40–2:48 | End card: logo, Nuggy, links. | "NUGSHIP. Sink ships. Mine ORE. Built solo for the Seeker by Vittorio Difusco." | repo · X · "Built on ORE + Solana Mobile" |

**Honesty rules for the edit**
- If DEMO LUCK or the FAST ORE clock is on for any shot, put a small caption on it ("simulated round, sped up"). Live rounds take ~77 s, so cut the wait; don't fake it.
- Don't show leaderboard rows from sim bots as if they were real players. Use MY LOG, or caption them "test data".
- Mockups (shipyard, Kraken art) get a "concept art" caption if they aren't in the build yet.
- A release build removes the "Development Build" watermark. Record from the release-config APK if it's ready.

## Capture on the Seeker (adb)

Run these from VENGEANCE (Windows: use PowerShell or cmd; the commands are the same). The Seeker screen is 1200×2670.

```bash
adb devices                                   # Seeker listed as "device"
adb shell wm size                             # expect Physical size: 1200x2670
adb shell mkdir -p /sdcard/nug

# Clean status bar + visible touches
adb shell settings put global sysui_demo_allowed 1
adb shell am broadcast -a com.android.systemui.demo -e command enter
adb shell am broadcast -a com.android.systemui.demo -e command clock -e hhmm 0941
adb shell am broadcast -a com.android.systemui.demo -e command battery -e level 100 -e plugged false
adb shell am broadcast -a com.android.systemui.demo -e command network -e wifi show -e level 4 -e mobile hide
adb shell am broadcast -a com.android.systemui.demo -e command notifications -e visible false
adb shell settings put global zen_mode 1      # do-not-disturb (some builds: adb shell cmd notification set_dnd priority)
adb shell settings put system show_touches 1  # turn off (0) for "clean" takes and keep one take with touches for the UX shots

# If testing against a local server instead of Fly.io:
adb reverse tcp:8787 tcp:8787

# Record one scene per file (screenrecord caps each file at 180 s)
adb shell screenrecord --bit-rate 16000000 --time-limit 180 /sdcard/nug/s02_title.mp4
#   Ctrl+C stops early. If the encoder rejects native size, retry with:
adb shell screenrecord --size 1080x2400 --bit-rate 16000000 --time-limit 180 /sdcard/nug/s02_title.mp4
#   (last resort: --size 720x1600)
# Suggested files: s01_crit, s02_title, s03_profile, s05_fleet, s06_approve|s06_queue, s07_battle_live, s08_crit, s10_kraken, s11_campaign, s12_leaderboard

# Long live match: chain two segments back-to-back (each ≤180 s)
adb shell "screenrecord --bit-rate 12000000 --time-limit 180 /sdcard/nug/s07a.mp4; screenrecord --bit-rate 12000000 --time-limit 180 /sdcard/nug/s07b.mp4"

# Stills for the deck / dApp Store (Windows-safe: don't use `exec-out ... >` in PowerShell, it corrupts PNGs)
adb shell screencap -p /sdcard/nug/still_battle.png

adb pull /sdcard/nug ./capture

# Restore the phone
adb shell am broadcast -a com.android.systemui.demo -e command exit
adb shell settings put system show_touches 0
adb shell settings put global zen_mode 0
```

Alternative: `scrcpy --record=take.mp4 --no-audio` gives you unlimited length, and the screen stays visible on the PC while you play.

## Edit (ffmpeg sketch)

Game audio isn't captured by screenrecord. Lay the original score under the VO: `assets/audio/music/battle_calm.ogg` + `battle_tension.ogg`,
`kraken_theme.ogg`, `title_theme.ogg`, plus SFX from `assets/audio/sfx/`. All of it is our own work.

```bash
# 1) Vertical clips -> 1920x1080 landscape master: phone centred, keep pixels crisp
ffmpeg -i capture/s07_battle_live.mp4 -vf "scale=-2:1080:flags=neighbor,pad=1920:1080:(ow-iw)/2:0:color=0x141a33,fps=30" -c:v libx264 -crf 18 -an s07.mp4
# 2) concat list -> master; 3) mix VO (vo.wav) + music at -18 dB
ffmpeg -f concat -safe 0 -i list.txt -c copy video.mp4
ffmpeg -i video.mp4 -i vo.wav -i music_bed.wav -filter_complex "[2:a]volume=0.15[m];[1:a][m]amix=inputs=2:duration=first[a]" -map 0:v -map "[a]" -c:v copy -c:a aac -b:a 192k NUGSHIP_demo.mp4
```
Use the side panels of the 16:9 frame for captions (deck style: Press Start 2P headings on navy). Export 1080p30 and keep it under 3:00.
Upload unlisted (YouTube/Drive), but **only after Vittorio OKs it** (it needs his account).

## Pre-flight checklist
- [ ] Server reachable (Fly.io `wss://` or `adb reverse` to the local server); `/health` OK
- [ ] Second player ready for PvP footage (second phone/emulator, or a friend with an invite code)
- [ ] Battery > 60%, DND on, brightness max, wallpaper/notifications hidden
- [ ] DEMO LUCK off for live footage; captions planned for any sim shots
- [ ] Variant A only: Seed Vault funded with the approved test amount, and the test tx signature noted
