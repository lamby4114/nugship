# Hosting options for the ORE SEAS server (prices checked Oct 2026)

> **Chosen: Fly.io.** The step-by-step runbook, current pricing and config are in [DEPLOY.md](DEPLOY.md). The table below is the original comparison.

What the server needs:
- One always-on Node 20 process with WebSockets.
- Somewhere for the SQLite leaderboard file to persist (`DB_PATH`, default `/data/oreseas.db`).
- Outbound access to a Solana RPC (read-only).

The image is the same everywhere. Use the `Dockerfile` in this folder, mount a volume at `/data`, and expose `PORT`
(8787). Nothing has been deployed anywhere yet. Every option below needs Victor to create an account, and the paid
ones need his OK.

| | **Render (Free web service)** | **Fly.io (pay-as-you-go)** | **Oracle Cloud Always Free VM** |
|---|---|---|---|
| Cost | $0 | ~$2.19/mo (shared-cpu-1x, 256 MB, iad/ewr) + $0.15/GB/mo volume. About $2.35/mo with a 1 GB volume. New orgs only get a trial (2 machine-hours or 7 days). | $0 (2 OCPU / 12 GB ARM total on the Always Free tier, 200 GB block storage) |
| Account | email/GitHub, no card | card required after trial | card for identity check, home-region pick |
| WebSockets | yes (an active WS message stream keeps it awake) | yes | yes (you run it) |
| SQLite persistence | **no**: the filesystem is ephemeral and wiped on restart, redeploy or spin-down. You'd need to switch to Postgres (Render's free Postgres expires after 30 days) or accept resets. | yes (Fly volume at `/data`) | yes (boot volume) |
| Cold starts | spins down after 15 min with no HTTP or WS traffic, and takes ~1 min to wake | none (min 1 machine running) | none |
| Limits | 512 MB / 0.1 CPU, 750 instance-h/mo, 5 GB outbound/mo on the new Hobby plan | small | idle VMs (<20% CPU/net/mem for 7 days) can be reclaimed, and capacity can be scarce in some regions |
| Setup effort | lowest: connect the GitHub repo, Docker deploy | low: `fly launch` + `fly volumes create data` | highest: create a VM, install Docker, open ports, set up TLS (Caddy) |

**Recommendation for the hackathon (judging window):** Fly.io (~$2–3 for the month) is the least fragile: always
on, persistent leaderboards, one CLI. If it has to be $0, use Oracle Always Free. Render Free is fine for a quick
demo, but leaderboards reset whenever it sleeps, and the first connection after idle takes about a minute.

Other notes:
- The public Solana RPC (`api.mainnet-beta.solana.com`) is rate-limited. For the demo it's worth setting `RPC_URL` and
  `RPC_WS_URL` to a free-tier key from Helius, Triton or QuickNode (also needs an account). The server batches
  reads, caching for a few seconds per round. Total load is about 1 board poll every 6 s, plus 1 round fetch and 1
  batched miner read per ORE round.
- Environment variables: `PORT`, `DB_PATH`, `RPC_URL`, `RPC_WS_URL`, `ROUND_SOURCE=live|replay`, `REPLAY_SPEED`,
  `REQUIRE_WALLET_PROOF` (keep `true` in prod), `STAKE_OPTIONS`, `DEFAULT_STAKE`.
- The Seeker app should use `wss://` in production. All three options terminate TLS (Oracle needs Caddy or nginx).

Sketches, not run:
```
# Fly.io
fly launch --no-deploy --dockerfile Dockerfile --internal-port 8787
fly volumes create data --size 1
#   fly.toml: [mounts] source="data" destination="/data"
fly deploy

# Oracle VM (Ubuntu ARM)
docker build -t ore-seas-server . && docker run -d --restart=always -p 8787:8787 -v /srv/oreseas:/data ore-seas-server
```
