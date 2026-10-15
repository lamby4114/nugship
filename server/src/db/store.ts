// SQLite persistence: players, matches, per-player match stats; global + weekly leaderboards.
import Database from "better-sqlite3";
import fs from "node:fs";
import path from "node:path";
import type { MatchResult } from "../game/match.js";

export type Period = "all" | "week";
export type LbMode = "ranked" | "solo" | "all";
export const SORTS = ["wins", "wl", "games", "ore", "accuracy", "crits", "sunk", "volleys", "kraken", "rating"] as const;
export type Sort = (typeof SORTS)[number];

/** Start of the current leaderboard week: Monday 00:00 UTC. */
export function weekStart(now = new Date()): number {
  const d = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
  const dow = (d.getUTCDay() + 6) % 7;
  return d.getTime() - dow * 86400_000;
}

export class Store {
  db: Database.Database;
  constructor(file: string) {
    if (file !== ":memory:") fs.mkdirSync(path.dirname(file), { recursive: true });
    this.db = new Database(file);
    this.db.pragma("journal_mode = WAL");
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS players (
        id TEXT PRIMARY KEY, name TEXT NOT NULL, wallet TEXT, rating REAL NOT NULL DEFAULT 1000,
        created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
      CREATE TABLE IF NOT EXISTS matches (
        id TEXT PRIMARY KEY, mode TEXT NOT NULL, source TEXT NOT NULL, started_at INTEGER NOT NULL, ended_at INTEGER NOT NULL,
        winner_seat INTEGER, reason TEXT, turns INTEGER, stake_lamports TEXT, rounds TEXT, json TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS match_players (
        match_id TEXT NOT NULL, seat INTEGER NOT NULL, player_id TEXT NOT NULL, is_ai INTEGER NOT NULL, captain TEXT,
        result TEXT NOT NULL, volleys INTEGER, shots INTEGER, hits INTEGER, sunk INTEGER, crits INTEGER, ore_grams INTEGER,
        misfires INTEGER, kraken INTEGER, lamports_deployed INTEGER, rating_delta REAL, ended_at INTEGER NOT NULL, mode TEXT NOT NULL,
        PRIMARY KEY (match_id, seat));
      CREATE INDEX IF NOT EXISTS mp_player ON match_players(player_id, ended_at);
      CREATE INDEX IF NOT EXISTS mp_time ON match_players(ended_at, mode);
      CREATE TABLE IF NOT EXISTS avatars (hash TEXT PRIMARY KEY, png BLOB NOT NULL, owner TEXT, created_at INTEGER NOT NULL);
    `);
    // profile columns (added after v1; ALTER is idempotent via the column check)
    const cols = new Set((this.db.prepare(`PRAGMA table_info(players)`).all() as any[]).map((c) => c.name));
    const add = (c: string, def: string) => { if (!cols.has(c)) this.db.exec(`ALTER TABLE players ADD COLUMN ${c} ${def}`); };
    add("avatar", "TEXT"); add("name_key", "TEXT"); add("claimed", "INTEGER NOT NULL DEFAULT 0"); add("guest_secret", "TEXT"); add("paired_wallet", "TEXT");
    add("is_bot", "INTEGER NOT NULL DEFAULT 0");
    this.db.exec(`CREATE UNIQUE INDEX IF NOT EXISTS players_name_key ON players(name_key) WHERE name_key IS NOT NULL`);
    const acols = new Set((this.db.prepare(`PRAGMA table_info(avatars)`).all() as any[]).map((c) => c.name));
    if (!acols.has("hidden")) this.db.exec(`ALTER TABLE avatars ADD COLUMN hidden INTEGER NOT NULL DEFAULT 0`);
    // store policy: user reports on names / portraits, and per-player blocks (also honoured by matchmaking)
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS reports (
        id INTEGER PRIMARY KEY AUTOINCREMENT, reporter TEXT NOT NULL, target TEXT NOT NULL, kind TEXT NOT NULL,
        name TEXT, avatar TEXT, reason TEXT, created_at INTEGER NOT NULL, resolved INTEGER NOT NULL DEFAULT 0);
      CREATE UNIQUE INDEX IF NOT EXISTS reports_once ON reports(reporter, target, kind) WHERE resolved = 0;
      CREATE INDEX IF NOT EXISTS reports_target ON reports(target, kind, resolved);
      CREATE TABLE IF NOT EXISTS blocks (blocker TEXT NOT NULL, blocked TEXT NOT NULL, created_at INTEGER NOT NULL, PRIMARY KEY (blocker, blocked));
    `);
  }

  upsertPlayer(id: string, name: string, wallet: string | null) {
    const now = Date.now();
    this.db.prepare(`INSERT INTO players(id,name,wallet,created_at,updated_at) VALUES(?,?,?,?,?)
      ON CONFLICT(id) DO UPDATE SET name=CASE WHEN players.claimed=1 THEN players.name ELSE excluded.name END,
      wallet=COALESCE(excluded.wallet, players.wallet), updated_at=excluded.updated_at`).run(id, name.slice(0, 24), wallet, now, now);
  }
  getPlayer(id: string): { id: string; name: string; wallet: string | null; rating: number; avatar: string | null; claimed: number; paired_wallet: string | null; is_bot: number } | undefined {
    return this.db.prepare(`SELECT id,name,wallet,rating,avatar,claimed,paired_wallet,is_bot FROM players WHERE id=?`).get(id) as any;
  }
  /** Marks a scripted/bot account (sim harness, demo seed) so leaderboards can tag it AI instead of passing it off as a person. */
  setBot(id: string) { this.db.prepare(`UPDATE players SET is_bot=1 WHERE id=?`).run(id); }

  // ---------------- profiles ----------------
  /** Player id that owns a claimed name key, if any. */
  nameOwner(key: string): string | undefined { return (this.db.prepare(`SELECT id FROM players WHERE name_key=?`).get(key) as any)?.id; }
  /** Claims name (unique by key) + sets avatar. Returns an error code or null. */
  setProfile(id: string, display: string, key: string, avatar: string): string | null {
    const tx = this.db.transaction(() => {
      const owner = this.nameOwner(key);
      if (owner && owner !== id) return "name_taken";
      const r = this.db.prepare(`UPDATE players SET name=?, name_key=?, claimed=1, avatar=?, updated_at=? WHERE id=?`).run(display, key, avatar, Date.now(), id);
      return r.changes ? null : "unknown_player";
    });
    try { return tx(); } catch (e: any) { return String(e?.message ?? e).includes("UNIQUE") ? "name_taken" : "db_error"; }
  }
  guestSecret(id: string): string | null | undefined { const r = this.db.prepare(`SELECT guest_secret FROM players WHERE id=?`).get(id) as any; return r ? r.guest_secret : undefined; }
  setGuestSecret(id: string, h: string) { this.db.prepare(`UPDATE players SET guest_secret=? WHERE id=? AND guest_secret IS NULL`).run(h, id); }
  putAvatar(hash: string, png: Buffer, owner: string) { this.db.prepare(`INSERT OR IGNORE INTO avatars(hash,png,owner,created_at) VALUES(?,?,?,?)`).run(hash, png, owner, Date.now()); }
  /** Hidden (moderated) portraits are never served and can't be re-used. */
  getAvatar(hash: string): Buffer | undefined { return (this.db.prepare(`SELECT png FROM avatars WHERE hash=? AND hidden=0`).get(hash) as any)?.png; }
  /** SIWS pairing (only reachable when the server runs with ALLOW_WALLET_PAIRING=true). */
  pairWallet(id: string, wallet: string) { this.db.prepare(`UPDATE players SET paired_wallet=?, updated_at=? WHERE id=?`).run(wallet, Date.now(), id); }

  // ---------------- store policy: reports, blocks, account deletion ----------------
  /**
   * Files a report (once per reporter/target/kind until resolved). When `threshold` distinct captains have reported the
   * same name or portrait, it is taken down automatically: the name reverts to an unclaimed "Captain" (free for anyone),
   * the portrait reverts to the default and the uploaded PNG is hidden. Reports are kept (resolved=1) for review.
   */
  addReport(reporter: string, target: string, kind: "name" | "avatar", reason: string, threshold: number, maxPerDay = 30):
    { ok: true; duplicate: boolean; actioned: boolean } | { ok: false; error: string } {
    const tx = this.db.transaction(() => {
      const t = this.getPlayer(target);
      if (!t) return { ok: false as const, error: "unknown_player" };
      const day = Date.now() - 86400_000;
      const n = (this.db.prepare(`SELECT COUNT(*) AS n FROM reports WHERE reporter=? AND created_at>=?`).get(reporter, day) as any).n;
      if (n >= maxPerDay) return { ok: false as const, error: "report_limit" };
      const r = this.db.prepare(`INSERT OR IGNORE INTO reports(reporter,target,kind,name,avatar,reason,created_at) VALUES(?,?,?,?,?,?,?)`)
        .run(reporter, target, kind, t.name, t.avatar, reason.slice(0, 200), Date.now());
      const distinct = (this.db.prepare(`SELECT COUNT(DISTINCT reporter) AS n FROM reports WHERE target=? AND kind=? AND resolved=0`).get(target, kind) as any).n;
      let actioned = false;
      if (distinct >= threshold) {
        if (kind === "name") this.db.prepare(`UPDATE players SET name='Captain', name_key=NULL, claimed=0, updated_at=? WHERE id=?`).run(Date.now(), target);
        else {
          if (t.avatar?.startsWith("u:")) this.db.prepare(`UPDATE avatars SET hidden=1 WHERE hash=?`).run(t.avatar.slice(2));
          this.db.prepare(`UPDATE players SET avatar=NULL, updated_at=? WHERE id=?`).run(Date.now(), target);
        }
        this.db.prepare(`UPDATE reports SET resolved=1 WHERE target=? AND kind=? AND resolved=0`).run(target, kind);
        actioned = true;
      }
      return { ok: true as const, duplicate: r.changes === 0, actioned };
    });
    return tx();
  }
  reportsFor(target: string): any[] { return this.db.prepare(`SELECT reporter,kind,name,avatar,reason,resolved,created_at FROM reports WHERE target=? ORDER BY id`).all(target); }
  block(blocker: string, blocked: string) { this.db.prepare(`INSERT OR IGNORE INTO blocks(blocker,blocked,created_at) VALUES(?,?,?)`).run(blocker, blocked, Date.now()); }
  unblock(blocker: string, blocked: string) { this.db.prepare(`DELETE FROM blocks WHERE blocker=? AND blocked=?`).run(blocker, blocked); }
  blockedBy(blocker: string): string[] { return (this.db.prepare(`SELECT blocked FROM blocks WHERE blocker=? ORDER BY created_at`).all(blocker) as any[]).map((r) => r.blocked); }
  /** True if either captain blocked the other (matchmaking + invite codes never pair them). */
  blockedEither(a: string, b: string): boolean { return !!this.db.prepare(`SELECT 1 FROM blocks WHERE (blocker=? AND blocked=?) OR (blocker=? AND blocked=?)`).get(a, b, b, a); }

  /**
   * In-app account deletion (store policy). Removes the player row (name, portrait ref, guest key hash, wallet links),
   * their per-match stat rows (so they vanish from every leaderboard), portraits they uploaded (unless another captain
   * uses the same image), their reports/blocks in both directions, and scrubs their id/name/wallet out of the stored
   * match records (the rival's history keeps the result against "DELETED CAPTAIN").
   */
  deletePlayer(id: string): boolean {
    const tx = this.db.transaction(() => {
      const existed = !!this.getPlayer(id);
      const mids = (this.db.prepare(`SELECT match_id FROM match_players WHERE player_id=?`).all(id) as any[]).map((r) => r.match_id);
      for (const mid of new Set(mids)) {
        const row = this.db.prepare(`SELECT json FROM matches WHERE id=?`).get(mid) as any; if (!row) continue;
        const j = JSON.parse(row.json);
        let hit = false;
        for (const p of j.players ?? []) if (p.id === id) { p.id = "deleted"; p.name = "DELETED CAPTAIN"; p.wallet = null; p.avatar = null; hit = true; }
        if (hit) this.db.prepare(`UPDATE matches SET json=? WHERE id=?`).run(JSON.stringify(j), mid);
      }
      this.db.prepare(`DELETE FROM match_players WHERE player_id=?`).run(id);
      this.db.prepare(`DELETE FROM avatars WHERE owner=? AND ('u:' || hash) NOT IN (SELECT avatar FROM players WHERE id<>? AND avatar IS NOT NULL)`).run(id, id);
      this.db.prepare(`DELETE FROM reports WHERE reporter=? OR target=?`).run(id, id);
      this.db.prepare(`DELETE FROM blocks WHERE blocker=? OR blocked=?`).run(id, id);
      this.db.prepare(`DELETE FROM players WHERE id=?`).run(id);
      return existed;
    });
    return tx();
  }

  /** Records a finished match; applies Elo for ranked PvP. Returns rating deltas per seat. */
  recordMatch(r: MatchResult): [number, number] {
    const deltas: [number, number] = [0, 0];
    const tx = this.db.transaction(() => {
      if (r.mode === "ranked" && r.players.every((p) => !p.isAI)) {
        const [a, b] = r.players.map((p) => this.getPlayer(p.id)?.rating ?? 1000);
        const ea = 1 / (1 + 10 ** ((b - a) / 400));
        const sa = r.winnerSeat === null ? 0.5 : r.winnerSeat === 0 ? 1 : 0;
        const K = 32;
        deltas[0] = K * (sa - ea); deltas[1] = -deltas[0];
        r.players.forEach((p, i) => this.db.prepare(`UPDATE players SET rating = rating + ? WHERE id=?`).run(deltas[i], p.id));
      }
      const json = JSON.stringify(r, (_k, v) => (typeof v === "bigint" ? v.toString() : v));
      this.db.prepare(`INSERT OR REPLACE INTO matches(id,mode,source,started_at,ended_at,winner_seat,reason,turns,stake_lamports,rounds,json) VALUES(?,?,?,?,?,?,?,?,?,?,?)`)
        .run(r.matchId, r.mode, r.sourceKind, r.startedAt, r.endedAt, r.winnerSeat, r.reason, r.turns, r.stakeLamports, JSON.stringify(r.rounds), json);
      const lbMode = r.mode === "ranked" || r.mode === "private" ? "pvp" : "solo";
      for (const p of r.players) {
        if (p.isAI) continue;
        const s = p.stats;
        this.db.prepare(`INSERT OR REPLACE INTO match_players VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)`).run(
          r.matchId, p.seat, p.id, 0, p.captain ?? null, p.result, s.volleys, s.shots, s.hits, s.sunk, s.crits, Number(s.oreGrams), s.misfires, s.kraken,
          Number(s.lamportsDeployed), deltas[p.seat], r.endedAt, r.mode === "ranked" ? "ranked" : lbMode);
      }
    });
    tx();
    return deltas;
  }

  private aggSql(where: string) {
    return `SELECT mp.player_id AS id, p.name AS name, p.wallet AS wallet, p.rating AS rating, p.avatar AS avatar, MAX(p.is_bot) AS isBot,
      COUNT(*) AS games, SUM(result='win') AS wins, SUM(result='loss') AS losses, SUM(result='draw') AS draws,
      SUM(volleys) AS volleys, SUM(shots) AS shots, SUM(hits) AS hits, SUM(sunk) AS sunk, SUM(crits) AS crits,
      SUM(ore_grams) AS oreGrams, SUM(kraken) AS kraken, SUM(misfires) AS misfires, SUM(COALESCE(lamports_deployed,0)) AS lamports
      FROM match_players mp JOIN players p ON p.id = mp.player_id ${where} GROUP BY mp.player_id`;
  }
  private shape(row: any) {
    if (!row) return null;
    return {
      id: row.id, name: row.name, avatar: row.avatar ?? "a:captain", ai: !!row.isBot, wallet: row.wallet, rating: Math.round(row.rating), games: row.games, wins: row.wins, losses: row.losses, draws: row.draws,
      wl: row.losses ? +(row.wins / row.losses).toFixed(2) : row.wins, battles: row.games, volleys: row.volleys, shots: row.shots, hits: row.hits,
      accuracy: row.shots ? +(row.hits / row.shots).toFixed(3) : 0, sunk: row.sunk, crits: row.crits, ore: row.oreGrams / 1e11, oreGrams: String(row.oreGrams),
      kraken: row.kraken, misfires: row.misfires, motherlodes: row.kraken, roundsHit: row.hits,
      winRate: row.games ? +(row.wins / row.games).toFixed(3) : 0, lamportsDeployed: String(row.lamports ?? 0), solDeployed: Number(row.lamports ?? 0) / 1e9,
    };
  }
  private filter(period: Period, mode: LbMode, extra = "") {
    const conds: string[] = [];
    if (period === "week") conds.push(`mp.ended_at >= ${weekStart()}`);
    if (mode === "ranked") conds.push(`mp.mode = 'ranked'`);
    if (mode === "solo") conds.push(`mp.mode = 'solo'`);
    if (extra) conds.push(extra);
    return conds.length ? "WHERE " + conds.join(" AND ") : "";
  }

  leaderboard(period: Period = "all", mode: LbMode = "ranked", sort: Sort = "wins", limit = 50, minGames = 1) {
    const order: Record<Sort, string> = {
      wins: "wins DESC, wl DESC", wl: "wl DESC, wins DESC", games: "games DESC", ore: "oreGrams DESC", accuracy: "accuracy DESC, shots DESC",
      crits: "crits DESC", sunk: "sunk DESC", volleys: "volleys DESC", kraken: "kraken DESC", rating: "rating DESC",
    };
    const rows = this.db.prepare(`SELECT *, CASE WHEN losses=0 THEN wins*1.0 ELSE wins*1.0/losses END AS wl, CASE WHEN shots=0 THEN 0 ELSE hits*1.0/shots END AS accuracy
      FROM (${this.aggSql(this.filter(period, mode))}) WHERE games >= ? ORDER BY ${order[sort]} LIMIT ?`).all(minGames, Math.min(200, limit));
    return rows.map((r, i) => ({ rank: i + 1, ...this.shape(r)! }));
  }

  playerStats(id: string) {
    const p = this.getPlayer(id);
    if (!p) return null;
    const one = (period: Period, mode: LbMode) => this.shape(this.db.prepare(this.aggSql(this.filter(period, mode, `mp.player_id = @id`))).get({ id }))
      ?? { games: 0, wins: 0, losses: 0, draws: 0, wl: 0, battles: 0, volleys: 0, shots: 0, hits: 0, accuracy: 0, sunk: 0, crits: 0, ore: 0, oreGrams: "0", kraken: 0, misfires: 0, motherlodes: 0, roundsHit: 0, winRate: 0, lamportsDeployed: "0", solDeployed: 0 };
    return { id: p.id, name: p.name, avatar: p.avatar ?? "a:captain", ai: !!p.is_bot, claimed: !!p.claimed, wallet: p.wallet, rating: Math.round(p.rating), global: { all: one("all", "all"), ranked: one("all", "ranked"), solo: one("all", "solo") }, weekly: { all: one("week", "all"), ranked: one("week", "ranked"), solo: one("week", "solo") } };
  }

  recentMatches(playerId: string, limit = 20) {
    return this.db.prepare(`SELECT m.id, m.mode, m.source, m.started_at AS startedAt, m.ended_at AS endedAt, m.reason, m.turns, mp.result, mp.hits, mp.shots, mp.crits, mp.ore_grams AS oreGrams, mp.kraken AS motherlodes, COALESCE(mp.lamports_deployed,0) AS lamportsDeployed
      FROM match_players mp JOIN matches m ON m.id = mp.match_id WHERE mp.player_id = ? ORDER BY m.ended_at DESC LIMIT ?`).all(playerId, Math.max(1, Math.min(100, Math.floor(Number(limit)) || 20)));  // NaN / negative (SQLite LIMIT -1 = unlimited) clamp
  }
  getMatch(id: string) {
    const row = this.db.prepare(`SELECT json FROM matches WHERE id=?`).get(id) as any;
    return row ? JSON.parse(row.json) : null;
  }
  /** Flush the WAL into the main file and close (called on graceful shutdown). */
  close() { try { this.db.pragma("wal_checkpoint(TRUNCATE)"); } finally { this.db.close(); } }
  counts() { return this.db.prepare(`SELECT (SELECT COUNT(*) FROM players) AS players, (SELECT COUNT(*) FROM matches) AS matches`).get(); }
}
