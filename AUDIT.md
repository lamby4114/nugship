# Nugship – play-through audit + polish (night of Oct 7–8, 2026)

Repo: `C:\Users\vdifu\Desktop\OreSeas` on VENGEANCE (Unity 6000.3.20f1). There's no git remote; every commit is local.
Server: `/workspace/orehack/server` on the box, synced byte-for-byte to `C:\Users\vdifu\Desktop\OreSeasServer`.
The money gate is untouched: `LIVE_ORE=false`, `SIWS_PAIRING=false`. No adb install was run, and the phone stayed unplugged.

## Commits (on top of 05a8fe6)
| hash | what |
|---|---|
| bf00cf2 | Play-through audit polish + desktop play-test harness (19 files) |
| 35387c6 | Hosted-server readiness: server_restart / close 1012 reconnect, build-time server URL, BuildAndroidRelease path, secrets guard (.gitignore + pre-commit hook + build-time scan) |
| 5c96a82 | Store policy: delete profile, report / block captains, AI tags on the leaderboard |
| ea1a6a4 | Play-through fixes: reconnect NRE, stale-aim toast, over-wide strings |
| f1d911d | ServerClient: no NullReferenceException when a message (account_deleted / server_restart) drops the socket mid-inbox (found by play4) |

Checks: CompileCheck OK with 0 errors and every SELFTEST passing (17 checks, now including "release server is TLS" and "no secrets in the project"). Server: `tsc --noEmit` is clean and `npm test` passes **41/41** (4 new policy tests).
APK: `C:\Users\vdifu\Desktop\OreSeas\Builds\Nugship-dev.apk`, **121.4 MB**, built 3:10 AM ET Oct 8 from f1d911d (BuildAndroidDev, 0 errors, 1 min 51 s; the secrets scan passed first). aapt2: `com.nugship.game` versionCode 1 / 0.1.0, min SDK 26, target 36, permissions INTERNET + VIBRATE only, `debuggable=true` (dev build; the release path is non-debuggable). Not installed: the phone stayed unplugged.

## How the play-through was done
`PlayTest.cs` (`-nugplay <dir> -nugrole A|B -nugsync <dir> -nugserver ws://127.0.0.1:8788/ws`) drives two Windows dev players (`Builds\win`, `Builds\winB`) with synthetic taps at Seeker portrait resolution (1200x2670, logical 240x534). It plays every screen and screenshots each state:
title → 9 help cards → profile (each name-rule error, taken name, portrait, photo upload, save) → ranks (log/global/weekly) → settings → campaign map, locked isle, isle 1 + isle 2 (fleet rotate/shuffle/drag, battle, pause/help, kraken, results, retry) → daily tide → PvP quick match A-vs-B → results REPORT/BLOCK → invite code / private match → B drops and reconnects → resign → bad invite code → queue cancel → simulated server restart → ranks after PvP (rival actions, block, unblock) → B deletes its profile → offline SIM rival → title.
`CenterAudit` runs on every shot and checks centring, text overlap, screen-edge clipping and the camera hole.
Runs: play1 (~217 shots), play2 (A 217 / B 30), play3 (A 260 / B 33, **19,176 audit checks, 6 issues**, all false positives from text under a modal; fixed in 5c96a82), play4 (final, after all fixes: A 276 shots / B 32 shots, campaign through isle 5, **22,760 CenterAudit checks, 0 issues**, no trimmed strings, no stale-aim toast; one NRE in `ServerClient.Update` right after DELETE PROFILE -> fixed in f1d911d (CompileCheck + self-tests pass after it)).
Shots: `/workspace/orehack/play{1..4}/{A,B}`. Before/after composites: `/workspace/orehack/audit_shots/`.

## Issues found → fixed
### Layout / text (single-player bottom banner first)
1. **Single-player bottom banner overlap (the known bug).** The button sub-label (e.g. `VS BARNACLE - SERVER AI`) ran under the coin icon and anchor. `Ui.Big` now measures the sub against the icon and frame (`subMax`/`FitSub`), and hints are clamped to `HintMax` (224 px) so the plate stays on screen. → `02_campaign_set_sail_sub.png`, `01_battle_bottom_banner.png`.
2. Results: the FIRE/RELOAD deck and the last hint peeked out under the results parchment. `ClearDeck()` now runs before results. → `17_results_before_after.png`.
3. HUD captain names were unreadable over clouds, so they now have an outline. → `10_battle_before_after.png`.
4. The fleet "sealed" toast sat on top of the captain dialog; it moved above it. → `13_fleet_before_after.png`.
5. Matchmaking: HARBOR was off-centre while STAKE was hidden (LIVE_ORE off). It's centred now. → `16_matchmaking_before_after.png`.
6. Title chip said `OFFLINE - NO WALLET` while LIVE_ORE is off. It now reports the game server (`ONLINE - FREE PLAY` / `OFFLINE - LOCAL AI`). → `19_title_before_after.png`, `20_title_centering_before_after.png`.
7. Settings showed the player id in mixed case and now uses upper case (pixel font). → `18_settings_before_after.png`.
8. Pause in local practice now says `PRACTICE CLOCK PAUSED` instead of implying the ORE clock stopped.
9. Leaderboard: the first AI tag after the name truncated names (`BARNAC..`). It's now an **AI chip in the portrait slot**, names keep their full 54 px, and a legend reads `AI = COMPUTER CAPTAIN`. → `14_/15_leaderboard_*`, `30_leaderboard_ai_chip.png`.
10. Strings trimmed with `..` at 240 px: `LOCKED - WAITING FOR THE ORE ROUND TO END` (226 px) → `LOCKED IN - WAITING FOR THE ORE ROUND`; `TURN ONLINE PLAY ON TO DELETE YOUR SERVER PROFILE` → `TURN ON ONLINE PLAY TO DELETE`; `WARNING: SERVER HAS A DIFFERENT COMMIT FOR YOU` → `WARNING: SERVER COMMIT MISMATCH`; `WARNING: RIVAL FLEET DID NOT MATCH ITS COMMIT` → `RIVAL FLEET BROKE ITS COMMIT!`. I measured every upper-case literal of 30+ characters with `tools/measure.py`, and none is over 224 px now.
11. Profile still said `NAME AVAILABLE` after a successful save. It now says `THIS NAME IS YOURS`.
12. DELETE PROFILE appeared on a fresh install with nothing to delete. It now shows only once a profile or online games exist.
13. The Report/Block and Confirm overlays sat within 40 sort orders of the results parchment, so CenterAudit reported 6 false overlaps. They moved up to orders 350–355.
14. CenterAudit flagged text that had slid wholly off screen (hidden boards). That text is ignored now.

### Game feel / pacing
15. Online Kraken now uses the full `KRAKEN RISES` overlay, matching solo. It auto-closes and the ORE clock keeps running.
16. Barnacle (isle 1, the tutorial captain) fires one shot fewer per volley (never below 1). "A gentle start" is now actually gentle for a first-time player.
17. The friendly reason headline on results (resigned / timed out / disconnected / struck colours) replaces generic text.

### Networking / robustness
18. Your aim was lost if the link dropped mid-aim. It's now re-sent after a reconnect.
19. **`server_restart` message and WS close 1012** (Fly deploys): the client shows `SERVER RESTARTING`, drops the resume token, and reconnects with backoff (server `reconnectAfterMs` clamped to 2–30 s, then doubling up to 60 s) using a fresh hello with the same guest key. Matchmaking returns to its menu once the server is back. An online match that dies with the process ends `SERVER RESTARTED - MATCH NOT COUNTED`. The harness exercised this (`pvp_server_restarting` / `pvp_server_back`).
20. NullReferenceExceptions: `ServerClient.StartConnect` when a connect was superseded by the restart backoff (play3, after DUEL SIM RIVAL -> title; ea1a6a4) and `ServerClient.Update` when account_deleted dropped the socket mid-inbox (play4; f1d911d). Both fixed.
21. After a reconnect the server rejected the stale queued volley, and the player saw a `BAD AIM: STALE ROUND` toast. That toast is now suppressed; other errors still toast.
22. Server: an oversize WS frame could crash the process. The ws server now has a 16 KB maxPayload and an `error` handler. A flood guard drops anything over 40 msgs/s, and leaderboard `LIMIT` is clamped.

### Build / deploy
23. **Build-time server URL**: `ServerConfig.DevServer = ws://127.0.0.1:8787/ws` (dev builds, via adb reverse), `ReleaseServer = wss://nugship-server.fly.dev/ws` under the `NUGSHIP_RELEASE` define. A release build ignores any non-wss override.
24. **`BuildScript.BuildAndroidRelease()`** is structured but **not run**, because no release keystore exists. It builds non-development with `NUGSHIP_RELEASE` and `insecureHttpOption = NotAllowed`. The keystore comes from env vars outside the repo (`NUGSHIP_KEYSTORE`, `NUGSHIP_KEYSTORE_PASS`, `NUGSHIP_KEY_ALIAS`, `NUGSHIP_KEY_PASS`, optional `NUGSHIP_VERSION_CODE/NAME`). It refuses to build without them (exit 3/4), never persists the passwords, and restores dev settings afterwards. Dev builds keep `insecureHttpOption = DevelopmentOnly`. Documented in the server's DEPLOY.md §4.
25. **Secrets never land in the repo**: `.gitignore` covers `x_kit/`, `*credentials*`, `.env*`, `*.p12/*.pem/*.key/*.keystore/*.jks`, `id_rsa*`, `secrets.json`, `*service-account*.json`. `Tools/hooks/pre-commit` (active via `core.hooksPath`, LF-pinned in .gitattributes) refuses secret-looking paths and private-key/API-secret content even with `git add -f`; I verified that with probe files. `BuildScript.SecretsScan` runs in CompileCheck and before every Android build. Git history scan: no secret file was ever committed. The box copy of `x_kit/` sits outside the Unity project.

### Store policy (Google Play / Solana dApp Store must-fixes)
26. **In-app profile deletion**: Profile → DELETE PROFILE → confirm (`THIS CANNOT BE UNDONE...`) → server `delete_account {confirm:"DELETE"}`. The server resigns any live match, erases the player row, rating, name claim, owned portraits, reports and blocks, and scrubs match history to `DELETED CAPTAIN`, then replies `account_deleted` and closes. The phone forgets its guest key, name, portrait and block list, and campaign progress stays. It works offline-only too (local wipe). The harness covers it end-to-end (B: `b_delete_confirm` → `b_delete_kept` → `b_deleting` → `b_profile_after_delete`, and B's row disappears from A's leaderboard).
27. **Report / block**: tap a rival on the leaderboard, or REPORT/BLOCK on PvP results, to get REPORT NAME, REPORT PORTRAIT (uploaded pictures only), BLOCK/UNBLOCK and CLOSE. The server stores reports (`reports` table, one open report per reporter/target/kind, 30/day per reporter). At `REPORT_THRESHOLD` distinct reporters (default 3) the name resets to `Captain` or the picture is hidden. Blocks are stored server-side, both directions are never paired, and a blocked player can't join your invite code. Blocked captains show as `BLOCKED` with the default portrait.
28. **AI labelling**: bots identify with `hello.bot:true`, and the server stores `is_bot` and returns `ai:true` on leaderboard rows. The client draws an `AI` chip and legend line.

## Money gate review (unchanged)
- `ServerConfig.LIVE_ORE = false`. Every match is off-chain ("virtual" shots on live ORE round timing): no deploy, no signing, no SOL. A self-test asserts the gate.
- `SIWS_PAIRING = false`: pairing returns immediately and nothing is signed (self-test). No wallet data is sent to the server while gated.
- No new code path touches Wallet/MWA signing. Report/block/delete send only player ids and text.

## Server edge cases checked
90 s disconnect grace + resume token; 60 s fleet placement; 15 max turns; inactivity forfeit; avatar 20x20 ≤ 6 KB; name rules (length, letters, reserved, unique key); invite code expiry (`bad_code`); report self/unknown/limit; block during queue; delete mid-match (opponent gets the win, match not counted); repeated delete; rating not sent to a deleted connection.

## Remaining issues / not done
- **No release keystore.** `BuildAndroidRelease` is untested end to end; it needs Victor to create a key (separate from the debug key) and set the env vars. The dApp Store also needs a versionCode bump, app NFT and dapp-store CLI publish.
- `LIVE_ORE` and `SIWS_PAIRING` stay off until Victor OKs flipping them.
- Report review is automatic only (threshold take-down). There's no moderator dashboard or appeal flow; reports sit in SQLite (`reports` table) for manual review.
- After a profile delete, DELETE PROFILE stays visible because local stats are kept (gamesPlayed > 0). Deleting again just wipes the fresh guest, which is harmless.
- The `SIM` practice clock shows large round numbers (e.g. `ORE ROUND 433894`). They're cosmetic, derived from wall-clock time.
- Admiral Brine (isle 2) won 3 of 4 harness games in play3, but in play4 the harness beat Brine and pushed on to isle 5. The harness aims randomly, so this is noise and wasn't changed. Worth a human feel-check.
- Gallery photo picker (`NugshipPicker.androidlib`) is untested on a device because the phone was unplugged.
- The box server copy (`/workspace/orehack/server`) isn't under git.
- CenterAudit still notes single glyphs (`A`..`E` column labels) sitting 0.5 px right. That's the pixel-grid limit, not a bug.

## Showcase shots (1200x2670, Seeker portrait)
`audit_shots/showcase_title.png` (play4), `showcase_battle_mid_volley.png` (play4, isle 1 critical hit), `showcase_how_to_play.png` (play4, card 1), `showcase_profile_creator.png` (play3, `NAME AVAILABLE`). The profile shot comes from play3 because play4 reused the play-server DB without reseeding, so play3's `SALTY NUGGET` claim made play4 show `TAKEN - TRY ANOTHER`. The name rule is working as intended; it's a harness artifact.
