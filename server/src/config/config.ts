// Runtime configuration from environment variables (12-factor). Parsed once at boot; printed redacted.
// Production values live in fly.toml [env] (non-secret) and `fly secrets set` (RPC URLs).

export type LogFormat = "json" | "text";

export interface Config {
  nodeEnv: string;
  port: number;
  dbPath: string;
  rpcUrl: string;
  rpcWsUrl: string | undefined;
  roundSource: "live" | "replay" | "practice";
  /** TEST ONLY: Motherlode odds (1 in N) for off-chain practice rounds. Real ORE is ~1/500; default 500. */
  practiceMotherlodeOdds: number;
  replaySpeed: number;
  requireWalletProof: boolean;
  allowGuestRanked: boolean;
  allowWalletPairing: boolean;
  siwsDomain: string;
  /** "*" or a list of exact origins (e.g. https://nugship.gg). Native apps send no Origin header and are always allowed. */
  allowedOrigins: "*" | string[];
  stakeOptions: bigint[] | undefined;
  defaultStake: bigint | undefined;
  logFormat: LogFormat;
  /** ms to wait after telling clients the server is restarting, before closing sockets (fits inside fly.toml kill_timeout). */
  shutdownGraceMs: number;
  /** distinct reporters before a reported name/portrait is taken down automatically */
  reportThreshold: number;
}

const bool = (v: string | undefined, dflt: boolean) => (v === undefined || v === "" ? dflt : /^(1|true|yes|on)$/i.test(v.trim()));

export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
  const nodeEnv = env.NODE_ENV ?? "development";
  const prod = nodeEnv === "production";
  const origins = (env.ALLOWED_ORIGINS ?? "*").trim();
  const roundSource = env.ROUND_SOURCE === "replay" ? "replay" : env.ROUND_SOURCE === "practice" && !prod ? "practice" : "live";
  const port = Number(env.PORT ?? 8787);
  if (!Number.isInteger(port) || port <= 0 || port > 65535) throw new Error(`bad PORT ${env.PORT}`);
  return {
    nodeEnv, port,
    dbPath: env.DB_PATH ?? "./data/oreseas.db",
    rpcUrl: env.RPC_URL || "https://api.mainnet-beta.solana.com",
    rpcWsUrl: env.RPC_WS_URL || undefined,
    roundSource,
    replaySpeed: Number(env.REPLAY_SPEED ?? 10),
    practiceMotherlodeOdds: prod ? 500 : Math.max(2, Number(env.PRACTICE_MOTHERLODE_ODDS ?? 500) || 500),
    requireWalletProof: bool(env.REQUIRE_WALLET_PROOF, true),
    // DEV/DEMO flag: walletless guests may join the ranked queue (always fireMode "virtual": nothing on chain).
    allowGuestRanked: bool(env.ALLOW_GUEST_RANKED, false),
    allowWalletPairing: bool(env.ALLOW_WALLET_PAIRING, false),
    siwsDomain: env.SIWS_DOMAIN || "nugship.game",
    allowedOrigins: origins === "*" || origins === "" ? "*" : origins.split(",").map((s) => s.trim().replace(/\/+$/, "")).filter(Boolean),
    stakeOptions: env.STAKE_OPTIONS?.split(",").map((x) => BigInt(x.trim())),
    defaultStake: env.DEFAULT_STAKE ? BigInt(env.DEFAULT_STAKE) : undefined,
    logFormat: env.LOG_FORMAT === "json" || env.LOG_FORMAT === "text" ? env.LOG_FORMAT : prod ? "json" : "text",
    shutdownGraceMs: Math.max(0, Number(env.SHUTDOWN_GRACE_MS ?? 1500)),
    reportThreshold: Math.max(1, Math.floor(Number(env.REPORT_THRESHOLD ?? 3)) || 3),
  };
}

/** Config safe to log: RPC URLs often embed an API key (?api-key=...), so only the host is shown. */
export function redacted(c: Config) {
  const host = (u?: string) => { if (!u) return null; try { return new URL(u).host; } catch { return "(invalid url)"; } };
  return { ...c, rpcUrl: host(c.rpcUrl), rpcWsUrl: host(c.rpcWsUrl), stakeOptions: c.stakeOptions?.map(String), defaultStake: c.defaultStake?.toString() };
}
