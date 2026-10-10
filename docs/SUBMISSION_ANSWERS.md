# CLOCK IN submission: draft answers (NUGSHIP)

Portal: Radiants **Align** (solanamobile.radiant.nexus). The live hackathon record lists **no custom application questions**
(`applicationQuestions: []` in the public API), so these drafts cover the usual fields. Paste them in, then trim to whatever limits the form shows.
**Save as a draft only.** Completing the "final submission agreement" locks the entry, and the team roster becomes fixed at that point.

---

**Project name:** NUGSHIP

**Tagline / one-liner (≤60 chars):** Sink ships. Mine ORE. A PvP naval duel on live ORE rounds.
- Alternative (≤100): Battleship for the Seeker where every turn is a live ORE mining round and every cannon shot is an ORE deploy.

**Short description (≤280):**
NUGSHIP is a native Android PvP naval duel for the Solana Seeker. Hide your fleet on ORE's 5×5 grid, fire salvos that are real ORE deploys,
and watch each live ORE round settle the volley. Hits sink ships. A hit on ORE's winning square is a critical hit, and a Motherlode summons the Kraken.

**Full description:**
> ORE runs a mining round on Solana about every minute: 25 squares, deploy SOL, one square wins. It's a great primitive, but you play it alone and then wait.
>
> NUGSHIP turns those rounds into a head-to-head naval battle. Each player secretly places a galleon, a sloop and a raft on a 5×5 board whose
> squares *are* ORE's squares. The layout is sealed with sha256(layout‖salt). Each turn both captains aim and lock on the same Solana slot. Every
> shot is an ORE Deploy on that square, and the round's outcome decides the volley. A hit means fire and smoke. A splash can still land on ORE's
> winning square and mine ORE. A hit *on* the winning square is a CRITICAL HIT, and a Motherlode round (1 in 500) means THE KRAKEN RISES. Sinking ships shrinks
> your salvo, so every turn is a decision.
>
> It's built for the Seeker. One Seed Vault approval per match funds an ORE Automation session, so there are no wallet popups mid-battle. It's portrait-first
> pixel art at exactly 5× the Seeker's 1200×2670 screen, with haptics and a reactive ship's cat, Nuggy.
>
> Modes: a solo campaign against 5 AI captains with distinct tactics, the Daily Tide, ranked 1v1 with Elo, and invite codes. Rank unlocks cosmetic fleets from
> Driftwood to the gold-veined Motherlode fleet. Plus global and weekly leaderboards, a captain's log, 6 biomes with weather, and an original chiptune score.
> There's no player-vs-player wagering and no in-game token. ORE is the only on-chain product.

**Category / track:** Mobile (single main category) · Games / Consumer. Also applying for the **ORE matched prize**. *Not* entering the SKR prize (no SKR integration).

**How does it use the Solana Mobile Stack / MWA?**
Mobile Wallet Adapter via Solana.Unity-SDK (Seed Vault): wallet connect, Sign-In With Solana for profile pairing, and a single per-match approval that sets up an
ORE Automation account with an in-app session key as executor. Per-shot signing is the fallback. It's a native Unity Android app (IL2CPP, ARM64), not a web wrapper.

**How does it interact with Solana?**
Each turn is one mainnet ORE v3 round. The client sends Checkpoint(previous round) + Deploy(square mask) per volley. The server follows rounds read-only
(Board/Round accounts) and referees each shot by reading the player's ORE Miner PDA after the round's end slot.
*[If submitted with LIVE_ORE off, say this plainly:]* "In the submitted build, live deploys are behind a compile-time safety gate. Matches follow live mainnet ORE rounds
read-only, and shots are virtual. The deploy path was tested on mainnet on [DATE] (tx [SIG])."

**ORE integration (for the ORE prize):**
> ORE is NUGSHIP's game clock, reward and spectacle. One ORE v3 round = one turn, and ORE's 25 squares = the battle grid. Each shot is a real Deploy on that
> square, bundled with Checkpoint, sent by a per-match ORE Automation session that the player approves once in Seed Vault. Splash = miss, explosion = hit, gold bubbles =
> ORE mined, CRITICAL HIT = hit on ORE's winning square, Kraken = Motherlode. ORE mined, crits and Kraken sightings are leaderboard stats. There's no other
> token in the game. Our TypeScript and C# ORE instruction layers are tested byte-for-byte against mainnet transactions, and the server referee verifies every
> shot on-chain from Miner accounts. We commit to keeping the integration live and maintained, and propose milestone-based deliverables (live launch in the dApp Store build,
> retention seasons, tournaments on ORE rounds) with on-chain-verifiable usage reporting. See ORE_INTEGRATION_BRIEF.md in the repo.

**What's new / built during the hackathon:**
Everything. The project started during CLOCK IN: Unity client, Node/TS server, ORE instruction/decoder layers in TS and C#, commit-reveal referee, AI captains,
art/audio pipeline and chiptune score. *(Confirm this matches your first-commit dates. The rules allow projects started ≤3 months before launch.)*

**Tech stack:** Unity 6000.3.20f1 (URP 2D, IL2CPP, ARM64, minSdk 26) · C# · Solana.Unity-SDK (MWA / Seed Vault) · Node.js 20 + TypeScript · ws (WebSockets)
· better-sqlite3 · @solana/web3.js (read-only RPC) · tweetnacl/bs58 (SIWS verification) · Docker · Fly.io · ORE v3 program.

**Links:**
- GitHub repo: [GITHUB_URL] *(public, or private shared through the Radiants Align GitHub app)*
- Demo video (≤3 min): [VIDEO_URL]
- Pitch deck: [DECK_URL] (pitch_deck.pdf)
- APK download: [APK_URL] · sha256 [APK_SHA256] · version [1.0.0 (versionCode N)]
- X: [X_HANDLE] · Website: [optional]

**Team:** Vittorio Difusco, solo developer (design, client, server, ORE integration, art pipeline, music). Team Representative: Vittorio Difusco.
Country of residence: [COUNTRY: must be on the Eligible Countries list].

**Funding status:** Independent / bootstrapped. **No venture capital or angel investment** (no equity, SAFE, SAFT, convertible or token sale).

**Prize wallet (only if asked at the finalist stage):** [SOLANA ADDRESS], a USDC-compatible Solana wallet controlled by Vittorio. Don't paste it until the organizer asks.

**Anything judges should know / how to test:**
1. Install the APK, then tap SOLO CAMPAIGN › Barnacle. That's the fastest match and needs no wallet.
2. PVP 1V1 › QUICK MATCH, or create an invite code and join from a second device.
3. Server: [wss://APP.fly.dev/ws] (preconfigured). Health check: [https://APP.fly.dev/health].
4. The "DEMO LUCK" setting (Kraken 1/25) exists only for simulated rounds. The real odds are 1/500. If the demo video uses it, the video says so.
