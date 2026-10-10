# NUGSHIP: Solana dApp Store publishing plan

**Why it matters:** CLOCK IN winners must have the app "published, listed, and publicly available on the Solana dApp Store no later than thirty (30)
calendar days after the date on which the winners are first publicly announced". "Merely submitting an application for store review does not constitute publication."
Extensions are discretionary, for example for documented review delays. (Terms §9.3. The site says results are announced **November 10th**, so the deadline is about **Dec 10, 2026** if that date holds.)
The ORE prize also requires the integration to "remain live and actively supported", and a store listing is how we keep it live.

> **Nothing in this plan has been done.** Every step that creates an account, does KYC, signs or spends SOL needs Vittorio's explicit OK.

---

## 1. Official process (Solana Mobile docs, checked Oct 8 2026)

Source: docs.solanamobile.com/dapp-store/submit-new-app, /build-and-sign-an-apk, /submit-an-update, /publishing-cli, /support.

1. **Publisher account:** sign up at **publish.solanamobile.com**, fill in the publisher profile and **submit KYC/KYB verification**.
2. **Publisher wallet:** connect a browser-extension wallet (Phantom/Solflare/Backpack). *"Your publisher wallet is required for all future submissions of this app. Do not lose access to it."*
   Fund it with **~0.2 SOL** "to cover transaction fees and ArDrive upload costs".
3. **Storage provider:** ArDrive is recommended. Uploads are paid from an ArDrive storage balance (use the portal's cost estimator, and **Top Up Balance** if needed).
   AWS S3 (your own bucket) is the alternative.
4. **Add a dApp:** "Add a dApp" › "New dApp". Enter the details (name, descriptions, icon, preview media) and save. You can edit them until submission.
5. **First release:** app Home › "New Version" › upload the APK › Submit. You'll be asked to **sign multiple messages and transactions** (Arweave uploads + minting
   the **release NFT**; listings are NFTs on Solana with metadata incl. APK hash). *Approve every request.* Skipped ones can leave assets missing.
6. **Review:** results by email from `publishersupport@dappstore.solanamobile.com`, "within 3-5 business days", reviewed in order. If you hear nothing after 5 business days, open an
   "App Review Inquiry" in Solana Mobile Discord `#dev-answers` (get the Developer role first). Once approved, the app goes live automatically.
7. **Updates:** same signing key, higher `versionCode`/`versionName`, and "What's new" filled in. Listing-detail changes only show after a new version is approved.
   CI option: `npm i -g @solana-mobile/dapp-store-cli`, then `dapp-store --apk-file … --keypair … --whats-new …` with `DAPP_STORE_API_KEY`. This needs the App NFT already minted
   in the portal and a Solana CLI keypair file. **Treat that keypair like a wallet key.**

Policies to accept: Publisher Policy (solanamobile.com/publisher-policy-web, last updated Jul 21 2026), Developer Agreement
(solanamobile.com/developer-agreement-web) and the dApp Store privacy policy.

## 2. Costs and approvals

| Item | Cost | Approval |
|---|---|---|
| Publisher account + KYC/KYB | no fee stated in docs (submits identity data) | **Vittorio: account creation + KYC** |
| Publisher wallet funding | ~0.2 SOL (fees + ArDrive storage; exact figure from the portal estimator, which scales with APK size) | **Vittorio: spend SOL** |
| Each update | tx fees + ArDrive upload for the new APK (estimate in portal) | **Vittorio: per release** |
| Release keystore | free (keytool) | Vittorio holds and backs it up |

## 3. Release signing keystore (separate from debug)

The current dev APK is a **Development** build signed with Unity's debug key. The dApp Store needs a release-signed APK, every update must use the
**same key**, and the docs say "Every app needs its own signing key" (and a separate key from Google Play if we ever publish there).

```bash
# Run on VENGEANCE (JDK keytool from Unity's bundled OpenJDK works). Vittorio types the passwords; never commit or paste them.
keytool -genkey -v -keystore nugship-dappstore.keystore -alias nugship -keyalg RSA -keysize 2048 -validity 10000
# Verify a built APK:
apksigner verify --print-certs Nugship-1.0.0.apk
```
- Store it **outside the repo** (e.g. `C:\Users\vdifu\keys\`). Keep two offline backups (USB + encrypted cloud) plus the passwords in a password manager.
  Losing it means we can never update the listing.
- Unity: Player Settings › Publishing Settings › Custom Keystore. Better, set it in a new `BuildScript.BuildAndroidRelease()` that reads the passwords from environment
  variables (`NUGSHIP_KS_PASS`, `NUGSHIP_KEY_PASS`), so nothing ends up in `ProjectSettings.asset`.
- Add `*.keystore`, `*.jks`, `keystore.properties`, `.env` and `x_kit/.nuggyore_credentials` to `.gitignore` **before** the first push.

## 4. Listing assets

The docs reference "Listing Page Guidelines", but the current docs pages don't spell out sizes. The sizes below come from the **dapp-publishing
spec (github.com/solana-mobile/dapp-publishing, publishing-spec/SPEC.md)** and secondary guides (Blueshift course, Solana Mobile PWA docs). **Check them
against the portal's upload form.**

| Asset | Spec | NUGSHIP source |
|---|---|---|
| App icon | 512×512 PNG | Re-export Nuggy-in-bandana from the sprite pipeline at 512 (x_kit `nug_pfp_400.png` is only 400 px) |
| Banner graphic (required) | 1200×600 | Re-crop from x_kit `nug_header_1500x500.png` scene, or the title scene: NUGSHIP sign + galleon + Nuggy |
| Feature graphic (optional, Editor's Choice) | 1200×1200 | Nuggy + Kraken + gold ORE key art |
| Screenshots | ≥4, each ≥1080 px on both sides, same orientation + aspect ratio | Seeker captures at 1200×2670 portrait from the **release** build (no "Development Build" watermark): title, fleet placement, battle (live ORE round), crit/reveal, campaign, leaderboard, results |
| Video (optional) | e.g. 1080×1920 mp4 | 30–45 s cut of the demo video |
| App name | NUGSHIP | |
| Short description | ≤30 chars (secondary sources) | `Sink ships. Mine ORE.` (21) |
| Long description | features overview | Adapt SUBMISSION_ANSWERS.md "Full description" and add the SOL-risk disclosure |
| Category | Games | |
| Privacy policy URL | required by the Publisher Policy (user data) | Host at e.g. `https://[domain]/privacy` (GitHub Pages works) |
| Contact / website | publisher email, site | [EMAIL], [SITE] |

## 5. Policy checklist (Publisher Policy, Jul 21 2026)

- [ ] **Privacy policy** disclosing what we collect: captain name, uploaded 20×20 avatar, guest id, wallet address (if paired), match stats, IP in server logs.
- [ ] **Account deletion**: "Provide a means … to delete their account, and delete the User Data". *Not implemented yet.* Add a server `delete_account` message
      plus a SETTINGS › DELETE PROFILE button that removes the player, avatar and stats rows and anonymises match history.
- [ ] **User-generated content** (names, uploaded portraits): user terms that include the policy restrictions, **in-app report/flag**, moderation, and a **block** function.
      *Not implemented yet* (there's only a small name blocklist). Minimum: report button on the opponent card/leaderboard row, an admin hide endpoint, a block list per player.
- [ ] **Minors**: the hackathon is 18+, and live ORE uses real SOL. Add an 18+ confirmation before enabling live mode. No data from minors.
- [ ] **Not misleading**: no implied ORE/Solana Mobile endorsement ("Built on ORE", not "Official ORE game"). Keep wording honest about SOL risk and odds (Motherlode 1/500; DEMO LUCK removed or clearly sim-only).
- [ ] **No webview loading untrusted URLs** (n/a; native). **No vulnerability-introducing code**: dev server-URL editor gated in release, cleartext off.
- [ ] **IP**: confirm we own or have a license for every asset, especially the **REELM font + wood texture** (`reelm_ref/`) and anything derived from them.
- [ ] Financial-services clause: NUGSHIP doesn't custody funds and has no PvP wagering. Players deploy into the ORE protocol from their own wallet. Get a quick legal sanity check
      on how the deploy mechanic (a lottery-like winning square) reads to reviewers, and describe it as ORE mining.

## 6. What must change in our build

| # | Change | Where (current state) |
|---|---|---|
| 1 | **Release build**, not Development | `BuildScript.cs` uses `options = BuildOptions.Development`. Add `BuildAndroidRelease()` with `BuildOptions.None` |
| 2 | **Custom release keystore** | Unity Publishing Settings / BuildScript (debug key today) |
| 3 | **insecureHttp off** | `BuildScript.Configure()` sets `PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed`. Set it to `NotAllowed` for release (the box copy of ProjectSettings shows 0, but Configure() overrides it at build time) |
| 4 | **Production server URL** | `ServerConfig.DefaultServer = "ws://127.0.0.1:8787/ws"`. Change to `wss://[app].fly.dev/ws` (or a custom domain). Hide or advanced-gate the SERVER URL editor in release |
| 5 | **Server config for prod** | `fly.toml`: `REQUIRE_WALLET_PROOF=true` ✓. Decide on `ALLOW_GUEST_RANKED` (true today for the demo). `SIWS_DOMAIN` must match the final host. `RPC_URL` secret |
| 6 | **Versioning** | `bundleVersion 0.1.0` / `versionCode 1`. Set 1.0.0 / 2+ and bump on every upload. Package id `com.nugship.game` is permanent after the first publish |
| 7 | **LIVE_ORE / SIWS gates** | Flip only with owner OK after the mainnet test. Release notes and listing must match what's actually enabled |
| 8 | **Privacy policy + delete account + UGC report/block** | new (see §5) |
| 9 | **Remove/label dev features** | DEMO LUCK (sim-only), FAST ORE clock, PRACTICE tags, desktop `-nugshot` ShotMode path |
| 10 | **Icons/splash** | Adaptive icon set from the 512 master, splash colour #1a1210 already set |
| 11 | **Secrets hygiene** | `.gitignore` for keystores/credentials. Never embed RPC API keys in the APK (use the server or a public endpoint) |

## 7. Timeline relative to the hackathon

| When (ET) | Step | Owner OK |
|---|---|---|
| Before Mon Oct 12, 7:59 PM EDT | Hackathon submission. Publishing isn't required yet ("not required by the submission deadline"). Do items 1–4 and 6 of §6 now anyway, since the submitted APK is better as a release build | release key |
| Oct 13–20 | Create the publisher account + KYC/KYB *early* (KYC can be slow). Draft the listing assets and privacy policy. Implement delete-account + report/block | **account + KYC** |
| Oct 20 – Nov 9 | Polish from judge feedback. Live ORE in ranked (opt-in). Second release candidate | |
| ~Nov 10 (results; API: judging closes Nov 9 2:59 AM EST) | **Day 0** if we place | |
| Day 0–3 (≈Nov 10–13) | Final release APK. Fund the publisher wallet (~0.2 SOL). Submit the first version (sign Arweave + release NFT txs) | **~0.2 SOL + signing** |
| Day 3–10 | Review (3–5 business days). Fix and resubmit if rejected | per resubmission |
| Day 14 (≈Nov 24) | **Target: live in store** (leaves ~2 weeks of buffer) | |
| Day 30 (≈Dec 10) | **Hard deadline** (publicly available). Ask the organizer in writing for an extension the moment review stalls | |

Even if we don't place, publishing keeps NUGSHIP eligible for ORE's "keep it live" framing and gets it in front of Seeker users. Recommend publishing either way.
