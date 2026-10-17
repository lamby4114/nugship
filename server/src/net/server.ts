// HTTP (REST) + WebSocket game server. Read-only on chain: never signs or sends transactions.
import http from "node:http";
import crypto from "node:crypto";
import { WebSocketServer, WebSocket } from "ws";
import bs58 from "bs58";
import { PublicKey } from "@solana/web3.js";
import { Store, SORTS, Sort, Period, LbMode } from "../db/store.js";
import { RoundSource, PracticeRoundSource } from "../chain/roundSource.js";
import { DeployReader } from "../chain/deployReader.js";
import { Match, MatchMode, FireMode, PlayerInit, RULES } from "../game/match.js";
import { CAPTAINS, captainById } from "../game/ai.js";
import { automationPda, minerPda } from "../ore/index.js";
import { normalizeName, nameKey, validateName, validAvatarRef, checkAvatarPng, pairingMessage, sha256hex, DEFAULT_AVATAR, BUILTIN_AVATARS } from "../profile/profile.js";

export const PROTOCOL_VERSION = 1;

export interface ServerOpts {
  port: number; store: Store; source: RoundSource; reader: DeployReader | null;
  requireWalletProof?: boolean; allowGuestRanked?: boolean; stakeOptions?: bigint[]; defaultStake?: bigint; log?: (m: string) => void;
  practiceAimSeconds?: number; practiceTickMs?: number; aiDelayMs?: number; practiceMotherlodeOdds?: number;
  /** SIWS wallet<->guest pairing. OFF unless ALLOW_WALLET_PAIRING=true (needs Victor's OK: it asks the wallet to sign a message). */
  allowWalletPairing?: boolean; domain?: string;
  /** "*" (default) or exact origins allowed for browser REST/WS. Requests without an Origin header (native apps) are always allowed. */
  allowedOrigins?: "*" | string[];
  /** Distinct reporters needed before a reported name/portrait is taken down automatically (default 3). */
  reportThreshold?: number;
  /** ORE Automation executor (EXECUTOR_KEY). null = players sign every volley themselves. */
  executor?: import("../chain/executor.js").VolleyExecutor | null;
}

/** Per-connection message cap (a real client sends a handful per second; the seat survives a reconnect). */
const MAX_MSGS_PER_SEC = 40;

interface Conn {
  ws: WebSocket; cid: string; nonce: string; playerId?: string; name?: string; wallet?: string | null; verified?: boolean; avatar?: string;
  matchId?: string; seat?: 0 | 1; alive: boolean;
  /** flood guard: messages in the current 1 s window */
  winStart?: number; winCount?: number;
}
interface QueueEntry { conn: Conn; rating: number; stake: bigint; fireMode: FireMode; since: number; }

const CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
const json = (o: any) => JSON.stringify(o, (_k, v) => (typeof v === "bigint" ? v.toString() : v));

export class GameServer {
  readonly http: http.Server; readonly wss: WebSocketServer;
  readonly matches = new Map<string, Match>();
  private queue: QueueEntry[] = [];
  private privates = new Map<string, { conn: Conn; stake: bigint; fireMode: FireMode; created: number }>();
  private sessions = new Map<string, { token: string; matchId?: string; seat?: 0 | 1; verifiedWallet?: string }>();
  /** Challenge nonces issued in the last NONCE_TTL_MS (nonce -> issuedAt). A wallet round-trip (Phantom) often kills the
   *  socket that issued the nonce, so a SIWS proof may arrive on the NEXT socket. Accepted once, then consumed. */
  private recentNonces = new Map<string, number>();
  private usedNonces = new Map<string, number>();
  static readonly NONCE_TTL_MS = 30 * 60_000;   // v1.1.0: 5 min was too short (seeker-test12: nonce issued at app open, signed 4m38s later, verified at 5m01s -> bad_signature). Still single use + wallet-bound.
  private conns = new Set<Conn>();
  /** Accounts deleted while a match was still finishing: their result row is scrubbed again after recordMatch. */
  private deleted = new Set<string>();
  private timers: NodeJS.Timeout[] = [];
  private log: (m: string) => void;
  /** true once shutdown() starts: no new queue entries / matches; clients are told to reconnect. */
  draining = false;
  stakeOptions: bigint[]; defaultStake: bigint;

  constructor(private o: ServerOpts) {
    this.log = o.log ?? ((m) => console.log(new Date().toISOString(), m));
    this.stakeOptions = o.stakeOptions ?? [1_000_000n, 5_000_000n, 10_000_000n];
    this.defaultStake = o.defaultStake ?? 5_000_000n;
    this.http = http.createServer((req, res) => this.rest(req, res).catch((e) => { this.log("rest error " + e); this.send(res, 500, { error: "internal" }); }));
    this.wss = new WebSocketServer({
      server: this.http, path: "/ws", maxPayload: 16 * 1024,
      verifyClient: (info: { origin?: string }) => !this.draining && this.originAllowed(info.origin) !== false,
    });
    this.wss.on("connection", (ws) => this.onConnection(ws));
  }

  listen(): Promise<number> {
    this.timers.push(setInterval(() => this.matchmake(), 1000));
    this.timers.push(setInterval(() => this.heartbeat(), 3000));
    this.timers.push(setInterval(() => { for (const c of this.conns) { if (!c.alive) c.ws.terminate(); else { c.alive = false; c.ws.ping(); } } }, 30000));
    return new Promise((r) => this.http.listen(this.o.port, () => r((this.http.address() as any).port)));
  }
  close() { this.timers.forEach(clearInterval); this.wss.close(); this.http.close(); for (const c of this.conns) c.ws.terminate(); }

  /** Origin policy. Returns the Access-Control-Allow-Origin value, null (no Origin header: native client, allowed, no header needed), or false (blocked). */
  private originAllowed(origin: string | undefined): string | null | false {
    const list = this.o.allowedOrigins ?? "*";
    if (list === "*") return "*";
    if (!origin) return null;
    return list.includes(origin.replace(/\/+$/, "")) ? origin : false;
  }

  /**
   * Graceful shutdown (SIGTERM from `fly deploy` / machine stop). In-memory matches can't survive a process restart
   * (they hold live round subscriptions, timers and the referee's view), so we: stop matchmaking, tell every client
   * `server_restart` (client should reconnect with backoff), close sockets with 1012 "Service Restart", and leave
   * unfinished matches unrecorded (no rating change, no loss). Finished results are already in SQLite.
   */
  async shutdown(graceMs = 1500): Promise<{ abandoned: { id: string; mode: string; phase: string; turns: number }[] }> {
    if (this.draining) return { abandoned: [] };
    this.draining = true;
    this.timers.forEach(clearInterval); this.timers = [];
    this.queue = []; this.privates.clear();
    const abandoned = [...this.matches.values()].filter((m) => m.phase !== "finished").map((m) => ({ id: m.id, mode: m.mode, phase: m.phase, turns: m.turnsPlayed }));
    for (const c of this.conns) this.out(c, { t: "server_restart", reason: "maintenance", reconnectAfterMs: 5000, matchAbandoned: !!(c.matchId && abandoned.some((a) => a.id === c.matchId)) });
    for (const m of this.matches.values()) m.abandon();
    if (graceMs > 0 && this.conns.size) await new Promise((r) => setTimeout(r, graceMs));
    for (const c of this.conns) { try { c.ws.close(1012, "server restart"); } catch { /* already gone */ } }
    await new Promise<void>((r) => { const t = setTimeout(() => { for (const c of this.conns) c.ws.terminate(); r(); }, 1000); this.wss.close(() => { clearTimeout(t); r(); }); });
    await new Promise<void>((r) => { this.http.close(() => r()); (this.http as any).closeAllConnections?.(); });
    return { abandoned };
  }

  // ------------------------------------------------------------------ REST
  private send(res: http.ServerResponse, code: number, body: any) {
    res.writeHead(code, { "content-type": "application/json", "cache-control": "no-store" });
    res.end(json(body));
  }
  oreStatus() {
    const s = this.o.source, w = s.current(), slot = s.slot(), last = s.lastResult();
    return {
      source: s.kind, slot, slotMs: s.slotMs(), roundSlots: s.roundSlots, intermissionSlots: s.intermissionSlots,
      round: w && { ...w, phase: slot < w.endSlot ? "deploying" : "intermission", secondsToEnd: Math.max(0, ((w.endSlot - slot) * s.slotMs()) / 1000) },
      lastResult: last && { roundId: last.roundId, winningSquare: last.winningSquare, motherlode: last.motherlode, split: last.split, totalDeployedSol: Number(last.totalDeployed) / 1e9 },
    };
  }
  private async rest(req: http.IncomingMessage, res: http.ServerResponse) {
    const u = new URL(req.url ?? "/", "http://x");
    const p = u.pathname.replace(/\/+$/, "") || "/";
    const allow = this.originAllowed(req.headers.origin);
    if (allow === false) return this.send(res, 403, { error: "origin not allowed" });
    if (allow) { res.setHeader("access-control-allow-origin", allow); if (allow !== "*") res.setHeader("vary", "Origin"); }
    if (req.method === "OPTIONS") { res.writeHead(204, { "access-control-allow-methods": "GET, OPTIONS", "access-control-allow-headers": "*", "access-control-max-age": "86400" }); return void res.end(); }
    if (req.method !== "GET") return this.send(res, 405, { error: "GET only" });
    let m: RegExpMatchArray | null;
    if (p === "/" || p === "/health") return this.send(res, this.draining ? 503 : 200, { ok: !this.draining, uptimeSeconds: Math.round(process.uptime()), protocol: PROTOCOL_VERSION, ore: this.oreStatus(), matches: this.matches.size, queue: this.queue.length, connections: this.conns.size, db: this.o.store.counts() });
    if (p === "/api/ore/status") return this.send(res, 200, this.oreStatus());
    if (p === "/api/rules") return this.send(res, 200, { protocol: PROTOCOL_VERSION, rules: RULES, stakeOptionsLamports: this.stakeOptions.map(String), defaultStakeLamports: String(this.defaultStake) });
    if (p === "/api/captains") return this.send(res, 200, { captains: CAPTAINS });
    if ((m = p.match(/^\/api\/avatars\/([0-9a-f]{16})(?:\.png)?$/))) {
      const png = this.o.store.getAvatar(m[1]);
      if (!png) return this.send(res, 404, { error: "unknown avatar" });
      res.writeHead(200, { "content-type": "image/png", "cache-control": "public, max-age=31536000, immutable" });
      return void res.end(png);
    }
    if (p === "/api/avatars") return this.send(res, 200, { builtin: BUILTIN_AVATARS, default: DEFAULT_AVATAR, uploadPx: 20 });
    if (p === "/api/names/check") return this.send(res, 200, this.checkName(u.searchParams.get("name"), u.searchParams.get("player")));
    if (p === "/api/leaderboard") {
      const period = (u.searchParams.get("period") === "week" ? "week" : "all") as Period;
      const mode = (["ranked", "solo", "all"].includes(u.searchParams.get("mode") ?? "") ? u.searchParams.get("mode") : "ranked") as LbMode;
      const sort = (SORTS.includes(u.searchParams.get("sort") as Sort) ? u.searchParams.get("sort") : "wins") as Sort;
      const limit = Math.max(1, Math.min(200, Number(u.searchParams.get("limit") ?? 50) || 50));
      const minGames = Math.max(1, Number(u.searchParams.get("minGames") ?? 1) || 1);
      return this.send(res, 200, { period, mode, sort, entries: this.o.store.leaderboard(period, mode, sort, limit, minGames) });
    }
    if ((m = p.match(/^\/api\/players\/([^/]+)$/))) { const s = this.o.store.playerStats(decodeURIComponent(m[1])); return s ? this.send(res, 200, s) : this.send(res, 404, { error: "unknown player" }); }
    if ((m = p.match(/^\/api\/players\/([^/]+)\/matches$/))) return this.send(res, 200, { matches: this.o.store.recentMatches(decodeURIComponent(m[1]), Number(u.searchParams.get("limit") ?? 20)) });
    if ((m = p.match(/^\/api\/matches\/([^/]+)$/))) {
      const live = this.matches.get(m[1]);
      if (live?.result) return this.send(res, 200, { live: false, ...live.result, reveals: live.revealLog });
      if (live) return this.send(res, 200, { live: true, id: live.id, mode: live.mode, phase: live.phase, turn: live.turn, players: live.players.map((x) => ({ name: x.name, isAI: x.isAI, commit: x.commit ?? null, afloat: x.fleet?.afloat().length ?? 3 })) });
      const done = this.o.store.getMatch(m[1]);
      return done ? this.send(res, 200, { live: false, ...done }) : this.send(res, 404, { error: "unknown match" });
    }
    if (p === "/api/session/plan") {
      const stake = BigInt(u.searchParams.get("stakeLamports") ?? String(this.defaultStake));
      const turns = Math.min(RULES.maxTurns, Number(u.searchParams.get("turns") ?? 10));
      const maxShots = 4; // 3 ships + 1 bonus
      const executorFee = 5_000n;
      const budget = stake * BigInt(maxShots * turns) + executorFee * BigInt(turns);
      const wallet = u.searchParams.get("wallet");
      let pdas: any = null;
      try { if (wallet) { const w = new PublicKey(wallet); pdas = { miner: minerPda(w).toBase58(), automation: automationPda(w).toBase58() }; } } catch { pdas = { error: "bad wallet" }; }
      return this.send(res, 200, {
        stakeLamports: String(stake), turns, maxShotsPerTurn: maxShots, executorFeeLamports: String(executorFee),
        automationBudgetLamports: String(budget), sessionKeyFeeFloatLamports: "2000000",
        notes: ["With reload=true, SOL returned by ORE (~89% on losing squares) flows back into the budget, so the real cost is ~11% of shots plus fees.",
          "Automation rent (~0.0015 SOL) and miner rent (~0.0045 SOL, first time only) are refunded/retained per ORE rules.",
          "Teardown: Automate with executor = 11111111111111111111111111111111 closes the automation and refunds the remaining budget."],
        pdas,
      });
    }
    return this.send(res, 404, { error: "not found" });
  }

  // ------------------------------------------------------------------ WS
  private onConnection(ws: WebSocket) {
    const c: Conn = { ws, cid: crypto.randomBytes(4).toString("hex"), nonce: crypto.randomBytes(16).toString("hex"), alive: true };
    this.rememberNonce(c.nonce);
    this.conns.add(c);
    ws.on("pong", () => (c.alive = true));
    // Without this, a protocol error (e.g. a frame over maxPayload) is an uncaught exception that takes the whole server down.
    ws.on("error", (e: any) => { this.log("ws error " + c.cid + ": " + (e?.code ?? e?.message ?? e)); try { ws.terminate(); } catch {} });
    ws.on("message", (raw) => {
      c.alive = true;
      const now = Date.now();
      if (!c.winStart || now - c.winStart >= 1000) { c.winStart = now; c.winCount = 0; }
      if ((c.winCount = (c.winCount ?? 0) + 1) > MAX_MSGS_PER_SEC) { this.log("flood: closing " + c.cid); return ws.close(1008, "rate limit"); }
      let msg: any;
      try { msg = JSON.parse(raw.toString()); } catch { return this.err(c, "bad_json", "invalid JSON"); }
      try { this.handle(c, msg); } catch (e: any) { this.log("handler error " + (e?.stack ?? e)); this.err(c, "internal", "server error", msg?.t); }
    });
    ws.on("close", () => this.onClose(c));
    this.out(c, { t: "challenge", protocol: PROTOCOL_VERSION, nonce: c.nonce, serverTime: Date.now() });
  }
  private out(c: Conn, m: any) { if (c.ws.readyState === WebSocket.OPEN) c.ws.send(json(m)); }
  private err(c: Conn, code: string, message: string, ref?: string) { this.out(c, { t: "error", code, message, ref }); }

  private onClose(c: Conn) {
    this.conns.delete(c);
    this.queue = this.queue.filter((q) => q.conn !== c);
    for (const [code, p] of this.privates) if (p.conn === c) this.privates.delete(code);
    if (c.matchId) { const m = this.matches.get(c.matchId); if (m && c.seat !== undefined && m.players[c.seat].send === this.sender(c)) m.disconnect(c.seat); }
  }

  private senders = new WeakMap<Conn, (m: any) => void>();
  private sender(c: Conn) { let s = this.senders.get(c); if (!s) { s = (m: any) => this.out(c, m); this.senders.set(c, s); } return s; }

  private rememberNonce(n: string) {
    const now = Date.now();
    for (const [k, t] of this.recentNonces) if (now - t > GameServer.NONCE_TTL_MS) this.recentNonces.delete(k);
    for (const [k, t] of this.usedNonces) if (now - t > GameServer.NONCE_TTL_MS) this.usedNonces.delete(k);
    this.recentNonces.set(n, now);
  }

  /** SIWS for hello: the message must carry THIS socket's nonce or a recent one this server issued (<= 5 min, never used
   *  before), name the wallet, and verify ed25519 against it. A successful proof consumes its nonce (single use). */
  verifyHelloSignIn(c: Conn, wallet: string, message: string, sig: string): boolean {
    const m = /Nonce: ([0-9a-f]{32})\b/.exec(message);
    const n = m?.[1] ?? (message.includes(c.nonce) ? c.nonce : null);
    if (!n || this.usedNonces.has(n)) return false;
    const issued = n === c.nonce ? Date.now() : this.recentNonces.get(n);
    if (issued === undefined || Date.now() - issued > GameServer.NONCE_TTL_MS) return false;
    if (!message.includes(wallet)) return false;
    if (!this.verifySignIn(wallet, message, sig, n)) return false;
    this.usedNonces.set(n, Date.now()); this.recentNonces.delete(n);
    return true;
  }

  private verifySignIn(wallet: string, message: string, sig: string, nonce: string): boolean {
    try {
      if (!message.includes(nonce)) return false;
      const pk = new PublicKey(wallet).toBuffer();
      const sigBytes = /^[0-9a-fA-F]{128}$/.test(sig) ? Buffer.from(sig, "hex") : sig.length > 90 && /[+/=]/.test(sig) ? Buffer.from(sig, "base64") : Buffer.from(bs58.decode(sig));
      const key = crypto.createPublicKey({ key: Buffer.concat([Buffer.from("302a300506032b6570032100", "hex"), pk]), format: "der", type: "spki" });
      return crypto.verify(null, Buffer.from(message, "utf8"), key, sigBytes);
    } catch { return false; }
  }

  private handle(c: Conn, m: any) {
    if (m?.t === "ping") return this.out(c, { t: "pong", serverTime: Date.now(), slot: this.o.source.slot(), echo: m.echo });
    if (m?.t === "hello") return this.hello(c, m);
    if (!c.playerId) return this.err(c, "not_authenticated", "send hello first", m?.t);
    const match = c.matchId ? this.matches.get(c.matchId) : undefined;
    switch (m.t) {
      case "queue_ranked": {
        if (this.draining) return this.err(c, "server_restarting", "server is restarting, try again in a few seconds", m.t);
        if (match && match.phase !== "finished") return this.err(c, "in_match", "already in a match", m.t);
        // DEV ONLY (ALLOW_GUEST_RANKED=true): guests may queue; walletless players are always fireMode "virtual" (no deploys).
        if (!c.wallet && !this.o.allowGuestRanked) return this.err(c, "wallet_required", "ranked play needs a connected wallet", m.t);
        if (c.wallet && this.o.requireWalletProof !== false && !c.verified) return this.err(c, "wallet_unverified", "sign in with your wallet (SIWS) to play ranked", m.t);
        const stake = this.parseStake(m.stakeLamports); if (stake === null) return this.err(c, "bad_stake", `stake must be one of ${this.stakeOptions.join(",")}`, m.t);
        const fireMode: FireMode = m.fireMode === "manual" ? "manual" : "auto";
        this.queue = this.queue.filter((q) => q.conn.playerId !== c.playerId);
        this.queue.push({ conn: c, rating: this.o.store.getPlayer(c.playerId!)?.rating ?? 1000, stake, fireMode, since: Date.now() });
        return this.out(c, { t: "queued", queue: "ranked", stakeLamports: String(stake), position: this.queue.length });
      }
      case "cancel_queue":
        this.queue = this.queue.filter((q) => q.conn !== c);
        return this.out(c, { t: "queue_left" });
      case "create_private": {
        if (this.draining) return this.err(c, "server_restarting", "server is restarting, try again in a few seconds", m.t);
        if (!c.wallet && m.fireMode !== "virtual") return this.err(c, "wallet_required", "private matches with real ORE need a wallet (or fireMode 'virtual')", m.t);
        const stake = this.parseStake(m.stakeLamports); if (stake === null) return this.err(c, "bad_stake", "bad stake", m.t);
        let code = ""; do { code = Array.from(crypto.randomBytes(6), (b) => CODE_ALPHABET[b % CODE_ALPHABET.length]).join(""); } while (this.privates.has(code));
        this.privates.set(code, { conn: c, stake, fireMode: (["auto", "manual", "virtual"].includes(m.fireMode) ? m.fireMode : "auto") as FireMode, created: Date.now() });
        return this.out(c, { t: "private_created", code, stakeLamports: String(stake), expiresInSeconds: 600 });
      }
      case "join_private": {
        const code = String(m.code ?? "").toUpperCase().trim();
        const host = this.privates.get(code);
        if (!host || Date.now() - host.created > 600_000) { this.privates.delete(code); return this.err(c, "bad_code", "invite code not found or expired", m.t); }
        if (host.conn.playerId === c.playerId) return this.err(c, "self_join", "cannot join your own match", m.t);
        if (this.o.store.blockedEither(host.conn.playerId!, c.playerId!)) return this.err(c, "bad_code", "invite code not found or expired", m.t); // blocked: same answer as a bad code
        const fireMode: FireMode = host.fireMode === "virtual" ? "virtual" : m.fireMode === "manual" ? "manual" : "auto";
        if (fireMode !== "virtual" && !c.wallet) return this.err(c, "wallet_required", "this match uses real ORE; connect a wallet", m.t);
        this.privates.delete(code);
        this.createPvp("private", host.conn, host.fireMode, c, fireMode, host.stake);
        return;
      }
      case "start_solo": {
        if (this.draining) return this.err(c, "server_restarting", "server is restarting, try again in a few seconds", m.t);
        if (match && match.phase !== "finished") {
          // A solo vs AI left running (app killed / relaunched, resume token restored the seat) must not block a new solo:
          // the player starting a new one resigns the old one (recorded as a loss, AI has no stake). PvP still refuses.
          if (match.mode !== "solo_live" && match.mode !== "practice") return this.err(c, "in_match", "already in a match", m.t);
          this.log(`[${match.id}] ${c.playerId} started a new solo; resigning the abandoned ${match.mode}`);
          match.resign(c.seat!);
          const s = this.sessions.get(c.playerId!); if (s?.matchId === match.id) s.matchId = undefined;
          c.matchId = undefined; c.seat = undefined;
        }
        const cap = captainById(m.captain ?? "barnacle"); if (!cap) return this.err(c, "bad_captain", "unknown captain", m.t);
        const practice = m.mode !== "live";
        let fireMode: FireMode = practice ? "virtual" : m.fireMode === "manual" ? "manual" : m.fireMode === "virtual" ? "virtual" : "auto";
        if (!practice && fireMode !== "virtual" && !c.wallet) fireMode = "virtual"; // no wallet: live ORE rounds, shots not deployed
        const stake = this.parseStake(m.stakeLamports) ?? this.defaultStake;
        const source = practice ? new PracticeRoundSource({ aimSeconds: this.o.practiceAimSeconds ?? 30, tickMs: this.o.practiceTickMs ?? 250, motherlodeOdds: this.o.practiceMotherlodeOdds }) : this.o.source;
        this.startMatch(practice ? "practice" : "solo_live", [this.pinit(c, fireMode), { id: "ai:" + cap.id, name: cap.name, wallet: null, fireMode: "virtual", isAI: true, captain: cap }], [c, null], stake, source, practice);
        return;
      }
      case "place_fleet": {
        if (!match || match.id !== m.matchId) return this.err(c, "no_match", "not in that match", m.t);
        if (m.stakeLamports !== undefined && m.stakeLamports !== null) {   // optional (older clients never send it): SOL PER CANNON chosen at placement
          const st = this.parseStake(m.stakeLamports); if (st === null) return this.err(c, "bad_stake", `stake must be one of ${this.stakeOptions.join(",")}`, m.t);
          if (st !== match.stakeLamports) { const se = match.setStake(c.seat!, st); if (se) return this.err(c, "bad_stake", se, m.t); this.log(`[${match.id}] stake set at placement: ${st}`); }
        }
        const e = match.placeFleet(c.seat!, m.layout, m.salt, m.commit);
        return e ? this.err(c, "bad_fleet", e, m.t) : undefined;
      }
      case "aim": {
        if (!match || match.id !== m.matchId) return this.err(c, "no_match", "not in that match", m.t);
        const e = match.aim(c.seat!, Number(m.roundId), m.squares, !!m.fireNow);
        return e ? this.err(c, "bad_aim", e, m.t) : undefined;
      }
      case "fired": // informational: client reports its deploy tx signature (the referee still reads chain state)
        if (match) this.log(`[${match.id}] seat ${c.seat} fired round ${m.roundId} sig ${String(m.signature).slice(0, 16)}…`);
        return this.out(c, { t: "fired_ack", roundId: m.roundId });
      case "automation_status": { // player's ORE Automation deposit as our executor sees it (read-only)
        if (!this.o.executor || !c.wallet) return this.out(c, { t: "automation_status", executor: null });
        this.o.executor.status(c.wallet).then((s) => this.out(c, { t: "automation_status", ...s })).catch((e) => this.out(c, { t: "automation_status", executor: this.o.executor!.pubkey, error: String(e?.message ?? e) }));
        return;
      }
      case "resign":
        if (match) match.resign(c.seat!);
        return;
      case "leave_match": {   // v1.1.0: HARBOR on the placement screen. Solo vs AI only; PvP must resign.
        if (!match || (m.matchId && match.id !== m.matchId)) return this.out(c, { t: "match_left", matchId: m.matchId ?? null, recorded: false });
        if (match.mode !== "solo_live" && match.mode !== "practice") return this.err(c, "not_allowed", "only a solo match can be left; resign instead", m.t);
        const e = match.leave(c.seat!); if (e) return this.err(c, "too_late", e, m.t);
        const id = match.id;
        const s = this.sessions.get(c.playerId!); if (s?.matchId === id) s.matchId = undefined;
        c.matchId = undefined; c.seat = undefined; this.matches.delete(id);
        return this.out(c, { t: "match_left", matchId: id, recorded: false });
      }
      case "get_state":
        if (!match) return this.err(c, "no_match", "no active match", m.t);
        return this.out(c, match.snapshot(c.seat!));
      case "check_name":
        return this.out(c, { t: "name_check", ...this.checkName(m.name, c.playerId) });
      case "set_profile": {
        // a wallet's profile is the wallet owner's: only a SIWS-verified (or resumed verified) session may name it
        if (c.wallet && this.o.requireWalletProof !== false && !c.verified) return this.err(c, "wallet_unverified", "sign in with your wallet to save its profile", m.t);
        const display = normalizeName(m.name), bad = validateName(display);
        if (bad) return this.err(c, bad, `name rejected: ${bad}`, m.t);
        const avatar = m.avatar ?? DEFAULT_AVATAR;
        if (!validAvatarRef(avatar)) return this.err(c, "bad_avatar", "unknown avatar", m.t);
        if (avatar.startsWith("u:") && !this.o.store.getAvatar(avatar.slice(2))) return this.err(c, "bad_avatar", "upload the avatar first", m.t);
        const e = this.o.store.setProfile(c.playerId!, display, nameKey(display), avatar);
        if (e) return this.err(c, e, e === "name_taken" ? "that name is taken" : e, m.t);
        c.name = display; c.avatar = avatar;
        this.log(`profile ${c.playerId}: ${display} ${avatar}`);
        return this.out(c, { t: "profile", playerId: c.playerId, name: display, avatar, nameClaimed: true, wallet: c.wallet ?? null, pairedWallet: this.o.store.getPlayer(c.playerId!)?.paired_wallet ?? null });
      }
      case "upload_avatar": {
        let buf: Buffer;
        try { buf = Buffer.from(String(m.png ?? ""), "base64"); } catch { return this.err(c, "bad_png", "not base64", m.t); }
        const r = checkAvatarPng(buf);
        if ("error" in r) return this.err(c, r.error, `avatar rejected: ${r.error}`, m.t);
        this.o.store.putAvatar(r.hash, buf, c.playerId!);
        return this.out(c, { t: "avatar_uploaded", avatar: "u:" + r.hash, url: `/api/avatars/${r.hash}.png` });
      }
      case "report_player": { // store policy: report a captain's name or uploaded portrait
        const target = String(m.playerId ?? ""), kind = m.kind === "avatar" ? "avatar" : m.kind === "name" ? "name" : null;
        if (!kind) return this.err(c, "bad_kind", "kind must be name or avatar", m.t);
        if (!target || target === c.playerId || target.startsWith("ai:")) return this.err(c, "bad_target", "cannot report that captain", m.t);
        const r = this.o.store.addReport(c.playerId!, target, kind, String(m.reason ?? ""), this.o.reportThreshold ?? 3);
        if (!r.ok) return this.err(c, r.error, r.error === "report_limit" ? "too many reports today" : "unknown captain", m.t);
        this.log(`report ${c.playerId} -> ${target} ${kind}${r.duplicate ? " (dup)" : ""}${r.actioned ? " ACTIONED" : ""}`);
        return this.out(c, { t: "report_ack", playerId: target, kind, duplicate: r.duplicate });
      }
      case "block_player": case "unblock_player": {
        const target = String(m.playerId ?? "");
        if (!target || target === c.playerId || target.length > 64) return this.err(c, "bad_target", "cannot block that captain", m.t);
        if (m.t === "block_player") { this.o.store.block(c.playerId!, target); }
        else this.o.store.unblock(c.playerId!, target);
        return this.out(c, { t: "blocked", playerId: target, blocked: this.o.store.blockedBy(c.playerId!) });
      }
      case "delete_account": { // store policy: in-app deletion of the profile + online stats
        if (m.confirm !== "DELETE") return this.err(c, "confirm_required", "send confirm:\"DELETE\"", m.t);
        const id = c.playerId!;
        this.queue = this.queue.filter((q) => q.conn !== c);
        for (const [code, p] of this.privates) if (p.conn === c) this.privates.delete(code);
        if (match && match.phase !== "finished") { this.deleted.add(id); match.resign(c.seat!); }
        this.o.store.deletePlayer(id);
        this.sessions.delete(id);
        this.log(`account deleted ${id}`);
        this.out(c, { t: "account_deleted", playerId: id });
        c.playerId = undefined; c.matchId = undefined;
        setTimeout(() => { try { c.ws.close(1000, "account deleted"); } catch { /* gone */ } }, 50);
        return;
      }
      case "pairing_message": // preview text the wallet would sign (harmless; works even while pairing is off)
        return this.out(c, { t: "pairing_message", enabled: !!this.o.allowWalletPairing, message: m.wallet ? pairingMessage(this.o.domain ?? "nugship.game", String(m.wallet), c.playerId!, c.nonce, new Date().toISOString()) : null });
      case "pair_wallet": {
        if (!this.o.allowWalletPairing) return this.err(c, "pairing_disabled", "wallet pairing is switched off on this server", m.t);
        let wallet: string;
        try { wallet = new PublicKey(m.wallet).toBase58(); } catch { return this.err(c, "bad_wallet", "invalid wallet pubkey", m.t); }
        const msg = String(m.message ?? "");
        if (!msg.includes(c.playerId!) || !msg.includes(wallet) || !this.verifySignIn(wallet, msg, String(m.signature ?? ""), c.nonce)) return this.err(c, "bad_signature", "pairing signature invalid", m.t);
        this.o.store.pairWallet(c.playerId!, wallet);
        return this.out(c, { t: "wallet_paired", playerId: c.playerId, wallet });
      }
      default:
        return this.err(c, "unknown_type", `unknown message type ${m.t}`, m.t);
    }
  }

  /** Name availability (validation + uniqueness, case/punctuation-insensitive). */
  checkName(raw: unknown, playerId?: string | null) {
    const display = normalizeName(raw), bad = validateName(display);
    if (bad) return { name: display, ok: false, reason: bad };
    const owner = this.o.store.nameOwner(nameKey(display));
    if (owner && owner !== playerId) return { name: display, ok: false, reason: "name_taken" };
    return { name: display, ok: true, reason: owner ? "yours" : "available" };
  }

  private parseStake(v: any): bigint | null {
    if (v === undefined || v === null) return this.defaultStake;
    try { const b = BigInt(v); return this.stakeOptions.includes(b) ? b : null; } catch { return null; }
  }

  private hello(c: Conn, m: any) {
    const name = String(m.name ?? "Captain").replace(/[^\p{L}\p{N} _.-]/gu, "").slice(0, 20) || "Captain";
    let wallet: string | null = null, verified = false;
    if (m.wallet) {
      try { wallet = new PublicKey(m.wallet).toBase58(); } catch { return this.err(c, "bad_wallet", "invalid wallet pubkey", "hello"); }
      if (m.signIn?.message && m.signIn?.signature) verified = this.verifyHelloSignIn(c, wallet, String(m.signIn.message), String(m.signIn.signature));
      if (m.signIn && !verified) return this.err(c, "bad_signature", "sign-in signature invalid or nonce missing", "hello");
    }
    let playerId = wallet ?? (typeof m.guestId === "string" && /^g_[a-f0-9]{16}$/.test(m.guestId) ? m.guestId : "g_" + crypto.randomBytes(8).toString("hex"));
    // Guest ids are public (leaderboards), so a guest proves ownership with a private guestKey kept on the device:
    // the first hello that presents a key binds it; later hellos for that id must present the same key or get a fresh id.
    if (!wallet) {
      const key = typeof m.guestKey === "string" && /^[0-9a-f]{32,64}$/.test(m.guestKey) ? m.guestKey : null;
      const stored = this.o.store.guestSecret(playerId);
      if (stored && (!key || sha256hex(key) !== stored)) playerId = "g_" + crypto.randomBytes(8).toString("hex");
    }
    // Resume a previous session (reconnect mid-match).
    const sess = this.sessions.get(playerId);
    let resumed = false;
    if (m.resume?.token && sess && sess.token === m.resume.token && m.resume.playerId === playerId) resumed = true;
    // a resumed session (secret token) of a wallet that already proved itself stays verified across reconnects
    if (wallet && !verified && resumed && sess?.verifiedWallet === wallet) verified = true;
    if (wallet && !verified && sess && !resumed && this.o.requireWalletProof !== false) {
      // someone else claiming a wallet with a live session: don't let them hijack it
      if (sess.matchId && this.matches.get(sess.matchId)?.phase !== "finished") return this.err(c, "wallet_unverified", "wallet has an active session; sign in to take it over", "hello");
    }
    const token = resumed ? sess!.token : crypto.randomBytes(16).toString("hex");
    c.playerId = playerId; c.wallet = wallet; c.verified = verified;
    // an unclaimed hello name may not impersonate someone's claimed name
    let helloName = name;
    { const owner = this.o.store.nameOwner(nameKey(normalizeName(name))); if (owner && owner !== playerId) helloName = "Captain"; }
    this.o.store.upsertPlayer(playerId, helloName, wallet);
    if (m.bot === true) this.o.store.setBot(playerId); // scripted clients (sim harness) self-declare; shown as AI on boards
    if (!wallet && typeof m.guestKey === "string" && /^[0-9a-f]{32,64}$/.test(m.guestKey)) this.o.store.setGuestSecret(playerId, sha256hex(m.guestKey));
    const prow = this.o.store.getPlayer(playerId);
    c.name = prow?.name ?? name; c.avatar = prow?.avatar ?? DEFAULT_AVATAR;
    // an unverified, non-resumed hello for a wallet must not replace that wallet's verified session (it would invalidate the
    // owner's persisted resume token and log them out on their next launch); it gets a throwaway token instead.
    const keepOwner = !!wallet && !verified && !resumed && !!sess && sess.verifiedWallet === wallet;
    if (!keepOwner) this.sessions.set(playerId, { token, matchId: resumed ? sess!.matchId : sess?.matchId, seat: sess?.seat, verifiedWallet: verified && wallet ? wallet : resumed ? sess?.verifiedWallet : undefined });
    const rating = Math.round(this.o.store.getPlayer(playerId)?.rating ?? 1000);
    let activeMatch: string | null = null;
    const s2 = this.sessions.get(playerId)!;
    if (s2.matchId) {
      const mt = this.matches.get(s2.matchId);
      if (mt && mt.phase !== "finished" && (resumed || verified)) {
        c.matchId = mt.id; c.seat = s2.seat; activeMatch = mt.id;
      }
    }
    this.out(c, { t: "welcome", protocol: PROTOCOL_VERSION, playerId, token, name: c.name, avatar: c.avatar, nameClaimed: !!prow?.claimed, pairedWallet: prow?.paired_wallet ?? null, walletPairing: !!this.o.allowWalletPairing, blocked: this.o.store.blockedBy(playerId), wallet, verified, rating, activeMatch, ore: this.oreStatus(), stakeOptionsLamports: this.stakeOptions.map(String), executor: this.o.executor ? { pubkey: this.o.executor.pubkey, feeLamports: this.o.executor.feeLamports.toString() } : null });
    if (activeMatch) this.matches.get(activeMatch)!.reconnect(c.seat!, this.sender(c));
  }

  private pinit(c: Conn, fireMode: FireMode): PlayerInit {
    return { id: c.playerId!, name: c.name!, wallet: c.wallet ?? null, fireMode: c.wallet ? fireMode : "virtual", send: this.sender(c), rating: this.o.store.getPlayer(c.playerId!)?.rating ?? 1000, avatar: c.avatar ?? DEFAULT_AVATAR };
  }

  private createPvp(mode: MatchMode, a: Conn, fa: FireMode, b: Conn, fb: FireMode, stake: bigint) {
    this.startMatch(mode, [this.pinit(a, fa), this.pinit(b, fb)], [a, b], stake, this.o.source, false);
  }

  private startMatch(mode: MatchMode, inits: [PlayerInit, PlayerInit], conns: [Conn | null, Conn | null], stake: bigint, source: RoundSource, ownsSource: boolean) {
    const match = new Match({
      mode, source, reader: this.o.reader, stakeLamports: stake, players: inits, ownsSource, log: this.log, aiDelayMs: this.o.aiDelayMs, executor: this.o.executor ?? null,
      onEnd: (r) => {
        let deltas: [number, number] = [0, 0];
        try { deltas = this.o.store.recordMatch(r); } catch (e) { this.log("recordMatch failed " + e); }
        for (const p of r.players) if (this.deleted.has(p.id)) { this.o.store.deletePlayer(p.id); this.deleted.delete(p.id); }
        conns.forEach((cc, i) => {
          if (!cc || !cc.playerId) return;
          this.out(cc, { t: "rating_update", matchId: r.matchId, delta: Math.round(deltas[i]), rating: Math.round(this.o.store.getPlayer(cc.playerId!)?.rating ?? 1000) });
          if (cc.matchId === r.matchId) cc.matchId = undefined;
          const s = this.sessions.get(cc.playerId!); if (s?.matchId === r.matchId) s.matchId = undefined;
        });
        setTimeout(() => this.matches.delete(r.matchId), 60_000);
      },
    });
    this.matches.set(match.id, match);
    conns.forEach((cc, i) => {
      if (!cc) return;
      cc.matchId = match.id; cc.seat = i as 0 | 1;
      const s = this.sessions.get(cc.playerId!); if (s) { s.matchId = match.id; s.seat = i as 0 | 1; }
      this.queue = this.queue.filter((q) => q.conn !== cc);
    });
    this.log(`match ${match.id} ${mode}: ${inits[0].name} vs ${inits[1].name} (stake ${stake})`);
    match.start().catch((e) => this.log("match start failed " + e));
    return match;
  }

  private matchmake() {
    const now = Date.now();
    this.queue = this.queue.filter((q) => q.conn.ws.readyState === WebSocket.OPEN);
    this.queue.sort((a, b) => a.since - b.since);
    const used = new Set<QueueEntry>();
    for (let i = 0; i < this.queue.length; i++) {
      const a = this.queue[i]; if (used.has(a)) continue;
      for (let j = i + 1; j < this.queue.length; j++) {
        const b = this.queue[j]; if (used.has(b) || a.stake !== b.stake || a.conn.playerId === b.conn.playerId) continue;
        if (this.o.store.blockedEither(a.conn.playerId!, b.conn.playerId!)) continue;
        const waited = (now - Math.min(a.since, b.since)) / 1000;
        if (Math.abs(a.rating - b.rating) <= 100 + 25 * waited) {
          used.add(a); used.add(b);
          this.createPvp("ranked", a.conn, a.fireMode, b.conn, b.fireMode, a.stake);
          break;
        }
      }
    }
    this.queue = this.queue.filter((q) => !used.has(q));
    for (const [code, p] of this.privates) if (now - p.created > 600_000) this.privates.delete(code);
  }

  private heartbeat() {
    for (const m of this.matches.values()) {
      if (m.phase !== "aiming" && m.phase !== "locked" && m.phase !== "settling") continue;
      const src = (m as any).source as RoundSource;
      for (const p of m.players) p.send({ t: "clock", matchId: m.id, phase: m.phase, roundId: m.turn?.roundId, slot: src.slot(), lockSlot: m.turn?.lockSlot, endSlot: m.turn?.endSlot, slotMs: src.slotMs(), serverTime: Date.now() });
    }
  }
}
