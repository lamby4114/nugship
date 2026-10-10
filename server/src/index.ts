// NUGSHIP / ORE SEAS server entry. All settings come from env (see src/config/config.ts and DEPLOY.md):
//   PORT=8787  DB_PATH=./data/oreseas.db  RPC_URL / RPC_WS_URL (mainnet read-only; secrets in prod)  ROUND_SOURCE=live|replay
//   REPLAY_SPEED=10  REQUIRE_WALLET_PROOF=true|false  ALLOW_GUEST_RANKED  ALLOW_WALLET_PAIRING  SIWS_DOMAIN
//   ALLOWED_ORIGINS=*|https://a,https://b  LOG_FORMAT=json|text  SHUTDOWN_GRACE_MS=1500
//   STAKE_OPTIONS=1000000,5000000,10000000  DEFAULT_STAKE=5000000
// The server never signs or sends transactions.
import { ReadOnlyRpc } from "./chain/rpc.js";
import { LiveRoundSource, PracticeRoundSource, ReplayRoundSource, RoundSource } from "./chain/roundSource.js";
import { ChainDeployReader } from "./chain/deployReader.js";
import { Store } from "./db/store.js";
import { GameServer } from "./net/server.js";
import { loadConfig, redacted } from "./config/config.js";
import { makeLogger } from "./config/log.js";

const cfg = loadConfig();
const log = makeLogger(cfg.logFormat, cfg.logFormat === "json" ? { app: "nugship-server", region: process.env.FLY_REGION, machine: process.env.FLY_MACHINE_ID } : {});
log.at("info", "config", redacted(cfg) as any);
if (cfg.allowGuestRanked) log.at("warn", "ALLOW_GUEST_RANKED is on: walletless guests can join the ranked queue (virtual shots only, nothing on chain)");

process.on("unhandledRejection", (e: any) => log.at("error", "unhandledRejection", { err: String(e?.stack ?? e) }));
process.on("uncaughtException", (e) => { log.at("error", "uncaughtException, exiting", { err: String(e?.stack ?? e) }); process.exit(1); });

const rpc = new ReadOnlyRpc({ url: cfg.rpcUrl, wsUrl: cfg.rpcWsUrl });
// ROUND_SOURCE=practice (dev/test only, refused in production): one shared off-chain practice clock for PvP, 8 s rounds
const source: RoundSource = cfg.roundSource === "replay"
  ? new ReplayRoundSource(rpc, { count: 60, speed: cfg.replaySpeed, loop: true })
  : cfg.roundSource === "practice" ? new PracticeRoundSource({ aimSeconds: 8, tickMs: 250, motherlodeOdds: cfg.practiceMotherlodeOdds })
  : new LiveRoundSource(rpc);
if (cfg.roundSource === "practice") log.at("warn", "ROUND_SOURCE=practice: PvP runs on an off-chain practice clock (test servers only)");
(source as any).log = log;
const reader = new ChainDeployReader(rpc);
reader.log = log;
const store = new Store(cfg.dbPath);
await source.start();
const server = new GameServer({
  port: cfg.port, store, source, reader, log,
  requireWalletProof: cfg.requireWalletProof,
  allowGuestRanked: cfg.allowGuestRanked, // dev/demo: guest ranked queue, virtual shots only
  allowWalletPairing: cfg.allowWalletPairing, // SIWS pairing: OFF by default (asks the wallet to sign a message)
  domain: cfg.siwsDomain,
  allowedOrigins: cfg.allowedOrigins,
  stakeOptions: cfg.stakeOptions,
  defaultStake: cfg.defaultStake,
  practiceMotherlodeOdds: cfg.practiceMotherlodeOdds, // TEST ONLY knob (default 500 = real odds)
  reportThreshold: cfg.reportThreshold,
});
const port = await server.listen();
log(`ORE SEAS server on :${port} (source=${source.kind}, ws path /ws, env=${cfg.nodeEnv})`);

let stopping = false;
const shutdown = async (sig: string) => {
  if (stopping) return; stopping = true;
  log.at("info", "shutting down", { signal: sig, connections: (server as any).conns?.size, matches: server.matches.size });
  const hardExit = setTimeout(() => { log.at("error", "shutdown timed out, forcing exit"); process.exit(1); }, cfg.shutdownGraceMs + 6000);
  hardExit.unref();
  try {
    const { abandoned } = await server.shutdown(cfg.shutdownGraceMs);
    if (abandoned.length) log.at("warn", "unfinished matches abandoned (not recorded, no rating change)", { abandoned });
    source.stop();
    store.close();
    log.at("info", "shutdown complete");
    process.exit(0);
  } catch (e: any) { log.at("error", "shutdown failed", { err: String(e?.stack ?? e) }); process.exit(1); }
};
process.on("SIGINT", () => void shutdown("SIGINT")); process.on("SIGTERM", () => void shutdown("SIGTERM"));
