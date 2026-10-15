// Match state machine. One turn = one ORE round. Both players aim during the deploy window, targets lock for both at
// the same slot (end_slot - lockOffset), each client fires its locked squares as a real ORE Deploy, and after ORE
// settles the round the referee reads what each player actually deployed on chain, scores it against the opponent's
// hidden fleet, and sends a reveal. Fleets are committed (sha256(layout||salt)) at start and revealed at the end.
import crypto from "node:crypto";
import { PublicKey } from "@solana/web3.js";
import { RoundResult, RoundSource, RoundWindow, PracticeRoundSource } from "../chain/roundSource.js";
import { DeployReader, DeployRecord } from "../chain/deployReader.js";
import type { VolleyExecutor } from "../chain/executor.js";
import { Captain, SeaKnowledge, chooseTargets } from "./ai.js";
import { FleetState, Layout, SHIPS, ShipType, commitLayout, randomLayout, randomSalt, validateLayout } from "./fleet.js";
import { ONE_ORE, oreEarned, solReturned, squareToCoord, squaresToMask } from "../ore/index.js";

export type MatchMode = "ranked" | "private" | "solo_live" | "practice";
export type FireMode = "auto" | "manual" | "virtual";
export type Phase = "placing" | "waiting_round" | "aiming" | "locked" | "settling" | "revealing" | "finished";
export type Fx = "splash" | "explosion" | "gold_bubbles" | "critical";

export const RULES = {
  fleet: SHIPS.map((s) => ({ type: s.type, code: s.code, size: s.size })),
  boardSize: 5,
  lockOffsetSlots: { auto: 20, manual: 60, virtual: 20, practice: 1 },
  minAimSlots: 40,            // if fewer slots than this remain before lock when a round opens, wait for the next round
  bonusShotOnSinkOrCrit: 1,
  maxTurns: 15,
  placementSeconds: 60,
  disconnectGraceSeconds: 90,
  maxEmptyTurns: 3,           // consecutive turns without a valid volley -> forfeit (humans only)
  settleTimeoutSlots: 260,    // past end_slot+intermission without ORE settlement -> void the turn
};

export interface Stats { volleys: number; shots: number; hits: number; sunk: number; crits: number; oreGrams: bigint; misfires: number; kraken: number; lamportsDeployed: bigint; solReturnedLamports: bigint; }
const newStats = (): Stats => ({ volleys: 0, shots: 0, hits: 0, sunk: 0, crits: 0, oreGrams: 0n, misfires: 0, kraken: 0, lamportsDeployed: 0n, solReturnedLamports: 0n });

export interface PlayerInit { id: string; name: string; wallet: string | null; fireMode: FireMode; isAI?: boolean; captain?: Captain; send?: (m: any) => void; rating?: number; avatar?: string | null; }

export class Player {
  seat: 0 | 1; id: string; name: string; wallet: string | null; fireMode: FireMode; isAI: boolean; captain?: Captain; rating: number; avatar: string | null;
  send: (m: any) => void; connected = true; dcTimer?: NodeJS.Timeout;
  fleet?: FleetState; layout?: Layout; salt?: string; commit?: string; autoPlaced = false;
  aim: number[] = []; locked: number[] | null = null; maxShots = 0; bonus = 0; bonusReason: string | null = null;
  knowledge: SeaKnowledge = { misses: new Set(), hits: new Set(), sunkShips: [] };
  stats = newStats(); emptyTurns = 0;
  constructor(seat: 0 | 1, p: PlayerInit) {
    this.seat = seat; this.id = p.id; this.name = p.name; this.wallet = p.wallet; this.fireMode = p.fireMode; this.isAI = !!p.isAI; this.captain = p.captain;
    this.send = p.send ?? (() => {}); this.rating = p.rating ?? 1000; this.avatar = p.avatar ?? null;
  }
  get onChain() { return !this.isAI && this.fireMode !== "virtual" && !!this.wallet; }
}

export interface Turn { n: number; roundId: number; startSlot: number; endSlot: number; lockSlot: number; startedAt: number; }

export interface ShotView { square: number; coord: string; hit: boolean; ship: ShipType | null; sunk: ShipType | null; crit: boolean; fx: Fx; oreGrams?: string; }
export interface VolleyView {
  shooter: string; seat: 0 | 1; locked: number[]; shots: ShotView[]; misfire: boolean; verification: "chain" | "event" | "sim" | "virtual" | "ai";
  ignoredSquares: number[]; underfundedSquares: number[]; oreGrams: string; ore: number; oreEstimated: boolean; lamportsDeployed: string; solReturnedLamports: string;
}

export interface MatchResult {
  matchId: string; mode: MatchMode; winnerSeat: 0 | 1 | null; reason: string; turns: number; startedAt: number; endedAt: number;
  players: { seat: 0 | 1; id: string; name: string; wallet: string | null; isAI: boolean; captain?: string; result: "win" | "loss" | "draw"; stats: Stats; commit: string; layout: Layout; salt: string; commitVerified: boolean }[];
  rounds: number[]; stakeLamports: string; sourceKind: string; reveals?: any[];
}

export interface MatchOpts {
  id?: string; mode: MatchMode; source: RoundSource; reader: DeployReader | null; stakeLamports: bigint;
  players: [PlayerInit, PlayerInit]; onEnd?: (r: MatchResult, m: Match) => void; log?: (m: string) => void; rng?: () => number;
  ownsSource?: boolean; aiDelayMs?: number;
  /** ORE Automation executor: places manual-fire volleys from the player's deposit (no wallet prompt). */
  executor?: VolleyExecutor | null;
}

const fx = (hit: boolean, crit: boolean): Fx => (hit ? (crit ? "critical" : "explosion") : crit ? "gold_bubbles" : "splash");
const oreNum = (g: bigint) => Number(g) / Number(ONE_ORE);

export class Match {
  readonly id: string; readonly mode: MatchMode; readonly stakeLamports: bigint; readonly createdAt = Date.now();
  readonly players: [Player, Player];
  phase: Phase = "placing"; turn: Turn | null = null; turnsPlayed = 0; lastRoundUsed = -1;
  revealLog: any[] = []; roundsPlayed: number[] = [];
  private source: RoundSource; private reader: DeployReader | null; private onEnd?: MatchOpts["onEnd"]; private log: (m: string) => void;
  private rng: () => number; private placeTimer?: NodeJS.Timeout; private listeners: [string, (...a: any[]) => void][] = [];
  private ownsSource: boolean; private aiDelayMs: number; private executor: VolleyExecutor | null; result: MatchResult | null = null;

  constructor(o: MatchOpts) {
    this.id = o.id ?? crypto.randomBytes(6).toString("hex");
    this.mode = o.mode; this.source = o.source; this.reader = o.reader; this.stakeLamports = o.stakeLamports;
    this.players = [new Player(0, o.players[0]), new Player(1, o.players[1])];
    this.onEnd = o.onEnd; this.log = o.log ?? (() => {}); this.rng = o.rng ?? Math.random; this.ownsSource = !!o.ownsSource; this.aiDelayMs = o.aiDelayMs ?? 1500; this.executor = o.executor ?? null;
  }

  get sourceKind() { return this.source.kind; }
  opp(p: Player) { return this.players[1 - p.seat]; }
  private lockOffset() {
    if (this.source.kind === "practice") return RULES.lockOffsetSlots.practice;
    return Math.max(...this.players.filter((p) => !p.isAI).map((p) => RULES.lockOffsetSlots[p.fireMode]), RULES.lockOffsetSlots.auto);
  }

  async start() {
    const on = (ev: string, fn: (...a: any[]) => void) => { this.source.on(ev, fn); this.listeners.push([ev, fn]); };
    on("slot", (s: number) => this.onSlot(s));
    on("round", () => this.tryStartTurn());
    on("settled", (r: RoundResult) => this.onSettled(r));
    if (this.ownsSource) await this.source.start();
    for (const p of this.players) {
      const o = this.opp(p);
      p.send({
        t: "match_found", matchId: this.id, mode: this.mode, seat: p.seat, fireMode: p.fireMode, stakeLamports: this.stakeLamports.toString(),
        source: this.source.kind, rules: RULES, placementSeconds: RULES.placementSeconds,
        opponent: { name: o.name, id: o.isAI ? null : o.id, isAI: o.isAI, captain: o.captain?.id ?? null, rating: o.rating, wallet: o.wallet, avatar: o.isAI ? null : (o.avatar ?? "a:captain") },
      });
      if (p.isAI) this.placeFleet(p.seat, randomLayout(this.rng, p.captain?.preferEdges), randomSalt());
    }
    this.placeTimer = setTimeout(() => {
      for (const p of this.players) if (!p.fleet) { p.autoPlaced = true; this.placeFleet(p.seat, randomLayout(this.rng), randomSalt()); }
    }, RULES.placementSeconds * 1000);
  }

  placeFleet(seat: number, layout: Layout, saltHex: string, commit?: string): string | null {
    const p = this.players[seat];
    if (this.phase !== "placing") return "fleet already locked";
    if (p.fleet) return "fleet already placed";
    const err = validateLayout(layout);
    if (err) return err;
    if (!/^[0-9a-fA-F]{64}$/.test(saltHex ?? "")) return "salt must be 32 bytes hex";
    const c = commitLayout(layout, saltHex);
    if (commit && commit.toLowerCase() !== c) return "commit does not match sha256(layout||salt)";
    p.layout = [...layout]; p.salt = saltHex.toLowerCase(); p.commit = c; p.fleet = new FleetState(p.layout);
    p.send({ t: "fleet_ack", matchId: this.id, commit: c, autoPlaced: p.autoPlaced, layout: p.autoPlaced ? p.layout : undefined, salt: p.autoPlaced ? p.salt : undefined });
    this.opp(p).send({ t: "opponent_ready", matchId: this.id });
    if (this.players.every((x) => x.fleet)) {
      clearTimeout(this.placeTimer);
      for (const x of this.players) x.send({ t: "fleets_committed", matchId: this.id, yourCommit: x.commit, opponentCommit: this.opp(x).commit });
      this.phase = "waiting_round";
      this.log(`[${this.id}] fleets committed; waiting for ORE round`);
      setImmediate(() => this.tryStartTurn());
    }
    return null;
  }

  private tryStartTurn() {
    if (this.phase !== "waiting_round") return;
    const w = this.source.current();
    if (!w || w.roundId <= this.lastRoundUsed) return;
    const lockSlot = w.endSlot - this.lockOffset();
    if (this.source.slot() > lockSlot - (this.source.kind === "practice" ? 0 : RULES.minAimSlots)) return; // too late in this round
    this.startTurn(w, lockSlot);
  }

  private startTurn(w: RoundWindow, lockSlot: number) {
    this.lastRoundUsed = w.roundId;
    this.turn = { n: this.turnsPlayed + 1, roundId: w.roundId, startSlot: w.startSlot, endSlot: w.endSlot, lockSlot, startedAt: Date.now() };
    this.phase = "aiming";
    for (const p of this.players) {
      p.aim = []; p.locked = null;
      p.maxShots = Math.max(1, Math.max(1, p.fleet!.afloat().length) + p.bonus + (p.captain?.bonusShots ?? 0));
    }
    for (const p of this.players) {
      p.send({
        t: "turn_start", matchId: this.id, turn: this.turn.n, roundId: w.roundId, startSlot: w.startSlot, endSlot: w.endSlot, lockSlot,
        slot: this.source.slot(), slotMs: this.source.slotMs(), serverTime: Date.now(), maxShots: p.maxShots, bonusShots: p.bonus, bonusReason: p.bonusReason,
        afloat: p.fleet!.afloat(), opponentAfloat: this.opp(p).fleet!.afloat().length,
        alreadyFired: [...p.knowledge.hits, ...p.knowledge.misses].sort((a, b) => a - b),
      });
      if (p.isAI) {
        const doAim = () => { if (this.phase === "aiming" && this.turn?.roundId === w.roundId) this.aim(p.seat, w.roundId, chooseTargets(p.captain?.tactic ?? "random", p.knowledge, p.maxShots, this.rng)); };
        this.source.kind === "live" ? setTimeout(doAim, this.aiDelayMs + this.rng() * 3000) : doAim();
      }
    }
    this.log(`[${this.id}] turn ${this.turn.n} on round ${w.roundId}, lock@${lockSlot} end@${w.endSlot}`);
  }

  aim(seat: number, roundId: number, squares: number[], fireNow = false): string | null {
    const p = this.players[seat];
    if (this.phase !== "aiming" || !this.turn) return "not aiming now";
    if (roundId !== this.turn.roundId) return "stale round";
    if (!Array.isArray(squares) || squares.some((s) => !Number.isInteger(s) || s < 0 || s > 24)) return "squares must be integers 0..24";
    const uniq = [...new Set(squares)];
    if (uniq.length > p.maxShots) return `max ${p.maxShots} shots this volley`;
    if (uniq.some((s) => p.knowledge.hits.has(s) || p.knowledge.misses.has(s))) return "already fired at one of those squares";
    p.aim = uniq.sort((a, b) => a - b);
    p.send({ t: "aim_ack", matchId: this.id, roundId, squares: p.aim, maxShots: p.maxShots });
    this.opp(p).send({ t: "opponent_aiming", matchId: this.id, count: p.aim.length });
    if (fireNow && this.source.kind === "practice" && this.opp(p).isAI) (this.source as PracticeRoundSource).fireNow();
    return null;
  }

  private onSlot(s: number) {
    if (!this.turn) return;
    if (this.phase === "aiming" && s >= this.turn.lockSlot) this.lock();
    if (this.phase === "locked" && s >= this.turn.endSlot) {
      this.phase = "settling";
      for (const p of this.players) p.send({ t: "round_closed", matchId: this.id, roundId: this.turn.roundId, slot: s });
      const onChain = this.players.filter((p) => p.onChain).map((p) => p.wallet!);
      if (onChain.length && this.reader) this.reader.snapshot(this.turn.roundId, onChain);
    }
    if (this.phase === "settling" && s > this.turn.endSlot + this.source.intermissionSlots + RULES.settleTimeoutSlots) {
      this.log(`[${this.id}] round ${this.turn.roundId} never settled; voiding turn`);
      for (const p of this.players) p.send({ t: "turn_void", matchId: this.id, roundId: this.turn.roundId, reason: "ore_round_not_settled" });
      this.phase = "waiting_round";
      this.tryStartTurn();
    }
  }

  private lock() {
    const t = this.turn!;
    this.phase = "locked";
    for (const p of this.players) {
      p.locked = [...p.aim];
      p.send({
        t: "lock", matchId: this.id, roundId: t.roundId, squares: p.locked, coords: p.locked.map(squareToCoord), mask: squaresToMask(p.locked),
        amountLamports: this.stakeLamports.toString(), fire: p.onChain && p.locked.length > 0, fireMode: p.fireMode, endSlot: t.endSlot, slot: this.source.slot(),
        serverPlaces: this.serverPlaces(p),
      });
      if (this.serverPlaces(p)) void this.placeForPlayer(p, t.roundId, squaresToMask(p.locked ?? []), t.endSlot);
    }
  }

  /** Manual-fire live volleys go through the executor first; client signs itself only on `volley.fallback`. */
  private serverPlaces(p: Player) { return !!this.executor && p.onChain && p.fireMode === "manual" && (p.locked?.length ?? 0) > 0 && this.source.kind === "live"; }
  private async placeForPlayer(p: Player, roundId: number, mask: number, endSlot: number) {
    let r: Awaited<ReturnType<VolleyExecutor["place"]>>;
    try { r = await this.executor!.place(p.wallet!, roundId, mask, this.stakeLamports, endSlot); }
    catch (e: any) { r = { placed: false, reason: "error:" + (e?.message ?? e) }; }
    this.log(`[${this.id}] executor ${p.wallet!.slice(0, 6)} r${roundId} placed=${r.placed} ${r.reason ?? ""} ${r.sig ?? ""}`);
    const fallback = volleyFallback(r, this.source.slot(), endSlot);
    p.send({ t: "volley", matchId: this.id, roundId, placed: r.placed, sig: r.sig ?? null, lamports: r.lamports ?? null, reason: r.reason ?? null, fallback, mask, amountLamports: this.stakeLamports.toString(), endSlot });
  }

  private async onSettled(res: RoundResult) {
    if (!this.turn || res.roundId !== this.turn.roundId) return;
    if (this.phase !== "settling" && this.phase !== "locked") return;
    if (this.phase === "locked") { // slot clock lagged; make sure targets are frozen
      this.phase = "settling";
    }
    this.phase = "revealing";
    try { await this.resolve(res); }
    catch (e: any) {
      this.log(`[${this.id}] resolve error ${e?.stack ?? e}`);
      for (const p of this.players) p.send({ t: "turn_void", matchId: this.id, roundId: res.roundId, reason: "referee_error" });
      this.phase = "waiting_round"; this.tryStartTurn();
    }
  }

  private async resolve(res: RoundResult) {
    const t = this.turn!;
    const wallets = this.players.filter((p) => p.onChain).map((p) => p.wallet!);
    const records = wallets.length && this.reader ? await this.reader.read(t.roundId, wallets) : new Map<string, DeployRecord | null>();
    const win = res.winningSquare;

    // 1) Determine effective shots per player.
    const vol = this.players.map((p) => {
      const locked = p.locked ?? [];
      let shots = locked, ignored: number[] = [], underfunded: number[] = [];
      let verification: VolleyView["verification"] = p.isAI ? "ai" : "virtual";
      let rec: DeployRecord | null = null;
      if (p.onChain) {
        rec = records.get(p.wallet!) ?? null;
        verification = rec ? (rec.source === "miner" ? "chain" : rec.source) : "chain";
        const deployed = rec?.mask ?? 0;
        shots = locked.filter((s) => (deployed >>> s) & 1 && rec!.perSquare[s] >= this.stakeLamports);
        underfunded = locked.filter((s) => (deployed >>> s) & 1 && rec!.perSquare[s] < this.stakeLamports);
        ignored = Array.from({ length: 25 }, (_, i) => i).filter((i) => (deployed >>> i) & 1 && !locked.includes(i));
      }
      return { p, locked, shots, ignored, underfunded, verification, rec };
    });

    // 2) Apply simultaneously.
    const sunkBefore = this.players.map((p) => new Set(p.fleet!.sunk()));
    for (const v of vol) for (const s of v.shots) this.opp(v.p).fleet!.hits.add(s);
    const views: VolleyView[] = vol.map((v) => {
      const target = this.opp(v.p).fleet!;
      const newlySunk = target.sunk().filter((x) => !sunkBefore[1 - v.p.seat].has(x));
      let oreG = 0n, estimated = false, deployedL = 0n, returnedL = 0n;
      if (v.rec && v.shots.length) {
        for (let i = 0; i < 25; i++) if (v.rec.perSquare[i] > 0n) deployedL += v.rec.perSquare[i];
        const onWin = v.rec.perSquare[win] ?? 0n;
        if (res.round && v.rec.source !== "sim") {
          oreG = oreEarned(res.round, new PublicKey(v.p.wallet!), onWin, win);
          for (let i = 0; i < 25; i++) if (v.rec.perSquare[i] > 0n) returnedL += solReturned(res.round, i, v.rec.perSquare[i], win);
        } else if (onWin > 0n) { // sim: our deploy was not really in the pot, estimate the split share
          const denom = (res.deployedOnSquare[win] ?? 0n) + onWin;
          oreG = (res.split ? (ONE_ORE * onWin) / denom : 0n) + (res.motherlode ? (res.motherlodeGrams * onWin) / denom : 0n);
          estimated = true;
        }
      }
      const shots: ShotView[] = v.shots.map((s) => {
        const ship = target.shipAt(s);
        const sunk = ship && newlySunk.includes(ship) ? ship : null;
        const crit = s === win;
        return { square: s, coord: squareToCoord(s), hit: !!ship, ship, sunk, crit, fx: fx(!!ship, crit), oreGrams: crit && oreG > 0n ? oreG.toString() : undefined };
      });
      // only flag `sunk` on one shot per ship (the last one listed)
      const seen = new Set<string>();
      for (let i = shots.length - 1; i >= 0; i--) { const sk = shots[i].sunk; if (sk) { if (seen.has(sk)) shots[i].sunk = null; else seen.add(sk); } }
      // stats + knowledge
      const st = v.p.stats;
      if (v.shots.length) st.volleys++;
      st.shots += v.shots.length; st.hits += shots.filter((x) => x.hit).length; st.sunk += newlySunk.length;
      st.crits += shots.filter((x) => x.crit).length; st.oreGrams += oreG; st.lamportsDeployed += deployedL; st.solReturnedLamports += returnedL;
      const misfire = v.locked.length > 0 && v.shots.length === 0;
      if (misfire) st.misfires++;
      for (const x of shots) (x.hit ? v.p.knowledge.hits : v.p.knowledge.misses).add(x.square);
      for (const s of newlySunk) {
        const code = SHIPS.find((z) => z.type === s)!.code;
        v.p.knowledge.sunkShips.push({ type: s, cells: target.layout.map((c, i) => (c === code ? i : -1)).filter((i) => i >= 0) });
      }
      if (!v.p.isAI) v.p.emptyTurns = v.shots.length ? 0 : v.p.emptyTurns + 1;
      // bonus shot for next volley
      const crit = shots.some((x) => x.crit);
      v.p.bonus = newlySunk.length || crit ? RULES.bonusShotOnSinkOrCrit : 0;
      v.p.bonusReason = newlySunk.length ? "sank" : crit ? "crit" : null;
      if (res.motherlode && !v.p.isAI) st.kraken++;
      return {
        shooter: v.p.name, seat: v.p.seat, locked: v.locked, shots, misfire, verification: v.verification, ignoredSquares: v.ignored, underfundedSquares: v.underfunded,
        oreGrams: oreG.toString(), ore: oreNum(oreG), oreEstimated: estimated, lamportsDeployed: deployedL.toString(), solReturnedLamports: returnedL.toString(),
      };
    });

    this.turnsPlayed++;
    this.roundsPlayed.push(t.roundId);
    const oreInfo = {
      winningSquare: win, winningCoord: squareToCoord(win), motherlode: res.motherlode, motherlodeOre: oreNum(res.motherlodeGrams), split: res.split,
      totalDeployedSol: Number(res.totalDeployed) / 1e9, slotHash: res.slotHashHex, synthetic: res.synthetic, roundId: res.roundId,
    };
    this.revealLog.push({ turn: t.n, roundId: t.roundId, ore: oreInfo, volleys: views });

    for (const p of this.players) {
      const o = this.opp(p);
      const incoming = views[o.seat], outgoing = views[p.seat];
      // Outgoing shots: don't leak which ship was hit unless it sank.
      const outView = { ...outgoing, shots: outgoing.shots.map((s) => ({ ...s, ship: s.sunk ? s.ship : null })) };
      const log: string[] = [];
      const desc = (v: VolleyView, yours: boolean) => {
        const who = yours ? "You" : v.shooter;
        if (v.misfire) return `${who} misfired (no matching ORE deploy found).`;
        if (!v.shots.length) return `${who} held fire.`;
        const hits = v.shots.filter((s) => s.hit);
        let line = `${who} fired ${v.shots.map((s) => s.coord).join(", ")}.`;
        for (const h of hits) line += yours ? ` ${h.coord} hit!` : ` ${h.coord} hit your ${h.ship}!`;
        for (const s of v.shots.filter((x) => x.sunk)) line += ` ${s.sunk!.toUpperCase()} SUNK!`;
        for (const s of v.shots.filter((x) => x.crit)) line += ` ${s.coord} struck ORE!`;
        return line;
      };
      log.push(desc(incoming, false), desc(outgoing, true));
      if (res.motherlode) log.push("THE KRAKEN RISES! The Motherlode hit this round.");
      p.send({
        t: "reveal", matchId: this.id, turn: t.n, roundId: t.roundId, ore: oreInfo, kraken: res.motherlode, order: ["incoming", "outgoing"],
        incoming, outgoing: outView,
        you: { afloat: p.fleet!.afloat(), sunk: p.fleet!.sunk(), hitsTaken: [...p.fleet!.hits].filter((i) => p.fleet!.layout[i] !== 0).sort((a, b) => a - b), missesTaken: [...p.fleet!.hits].filter((i) => p.fleet!.layout[i] === 0).sort((a, b) => a - b) },
        opponent: { afloatCount: o.fleet!.afloat().length, sunk: o.fleet!.sunk(), sunkShips: p.knowledge.sunkShips.map((s) => ({ type: s.type, cells: [...s.cells] })), hitsOnThem: [...p.knowledge.hits].sort((a, b) => a - b), missesOnThem: [...p.knowledge.misses].sort((a, b) => a - b) },
        next: { maxShots: Math.max(1, Math.max(1, p.fleet!.afloat().length) + p.bonus + (p.captain?.bonusShots ?? 0)), bonusShots: p.bonus, bonusReason: p.bonusReason },
        totals: { ore: oreNum(p.stats.oreGrams), crits: p.stats.crits, hits: p.stats.hits, shots: p.stats.shots },
        log,
      });
    }
    this.log(`[${this.id}] turn ${t.n} round ${t.roundId}: win=${win} p0 ${views[0].shots.length}sh/${views[0].shots.filter((s) => s.hit).length}h p1 ${views[1].shots.length}sh/${views[1].shots.filter((s) => s.hit).length}h`);

    // 3) End conditions.
    const d = this.players.map((p) => p.fleet!.destroyed());
    if (d[0] || d[1]) return this.finish(d[0] && d[1] ? null : d[0] ? 1 : 0, d[0] && d[1] ? "mutual_destruction" : "fleet_sunk");
    for (const p of this.players) if (!p.isAI && p.emptyTurns >= RULES.maxEmptyTurns) return this.finish(this.opp(p).seat, "inactive");
    if (this.turnsPlayed >= RULES.maxTurns) {
      const score = this.players.map((p) => this.opp(p).fleet!.cellsHit() * 10 + this.opp(p).fleet!.sunk().length * 100);
      return this.finish(score[0] === score[1] ? null : score[0] > score[1] ? 0 : 1, "turn_limit");
    }
    this.phase = "waiting_round";
    setImmediate(() => this.tryStartTurn());
  }

  resign(seat: number) { if (this.phase !== "finished") this.finish(this.players[1 - seat].seat, "resign"); }

  /** Server shutdown: stop everything without a result (not recorded, no rating change, onEnd not called). */
  abandon() {
    if (this.phase === "finished") return;
    this.phase = "finished"; this.onEnd = undefined;
    clearTimeout(this.placeTimer);
    for (const [ev, fn] of this.listeners) this.source.off(ev, fn);
    if (this.ownsSource) this.source.stop();
    for (const p of this.players) { clearTimeout(p.dcTimer); p.send = () => {}; }
  }

  disconnect(seat: number) {
    const p = this.players[seat];
    if (this.phase === "finished" || p.isAI) return;
    p.connected = false;
    this.opp(p).send({ t: "opponent_disconnected", matchId: this.id, graceSeconds: RULES.disconnectGraceSeconds });
    p.dcTimer = setTimeout(() => { if (!p.connected && this.phase !== "finished") this.finish(this.opp(p).seat, "disconnect"); }, RULES.disconnectGraceSeconds * 1000);
  }

  reconnect(seat: number, send: (m: any) => void) {
    const p = this.players[seat];
    p.send = send; p.connected = true; clearTimeout(p.dcTimer);
    this.opp(p).send({ t: "opponent_reconnected", matchId: this.id });
    send(this.snapshot(seat));
  }

  snapshot(seat: number) {
    const p = this.players[seat], o = this.opp(p);
    return {
      t: "match_state", matchId: this.id, mode: this.mode, phase: this.phase, source: this.source.kind, stakeLamports: this.stakeLamports.toString(), seat,
      turn: this.turn && { ...this.turn }, slot: this.source.slot(), slotMs: this.source.slotMs(), serverTime: Date.now(),
      you: { name: p.name, fireMode: p.fireMode, layout: p.layout ?? null, salt: p.salt ?? null, commit: p.commit ?? null, afloat: p.fleet?.afloat() ?? [], hitsTaken: p.fleet ? [...p.fleet.hits] : [], aim: p.aim, locked: p.locked, maxShots: p.maxShots },
      opponent: { name: o.name, isAI: o.isAI, captain: o.captain?.id ?? null, commit: o.commit ?? null, afloatCount: o.fleet?.afloat().length ?? 3, sunk: o.fleet?.sunk() ?? [], sunkShips: p.knowledge.sunkShips.map((s) => ({ type: s.type, cells: [...s.cells] })), hitsOnThem: [...p.knowledge.hits], missesOnThem: [...p.knowledge.misses] },
      reveals: this.revealLog.length, result: this.result,
    };
  }

  private finish(winnerSeat: 0 | 1 | null, reason: string) {
    if (this.phase === "finished") return;
    this.phase = "finished";
    clearTimeout(this.placeTimer);
    for (const [ev, fn] of this.listeners) this.source.off(ev, fn);
    if (this.ownsSource) this.source.stop();
    for (const p of this.players) clearTimeout(p.dcTimer);
    const r: MatchResult = {
      matchId: this.id, mode: this.mode, winnerSeat, reason, turns: this.turnsPlayed, startedAt: this.createdAt, endedAt: Date.now(),
      rounds: this.roundsPlayed, stakeLamports: this.stakeLamports.toString(), sourceKind: this.source.kind, reveals: this.revealLog,
      players: this.players.map((p) => ({
        seat: p.seat, id: p.id, name: p.name, wallet: p.wallet, isAI: p.isAI, captain: p.captain?.id,
        result: winnerSeat === null ? "draw" : winnerSeat === p.seat ? "win" : "loss", stats: p.stats,
        commit: p.commit ?? "", layout: p.layout ?? [], salt: p.salt ?? "", commitVerified: !!p.layout && commitLayout(p.layout, p.salt!) === p.commit,
      })),
    };
    this.result = r;
    for (const p of this.players) {
      const me = r.players[p.seat], them = r.players[1 - p.seat];
      p.send({
        t: "match_end", matchId: this.id, result: me.result, reason, winnerSeat, turns: r.turns,
        stats: { ...me.stats, oreGrams: me.stats.oreGrams.toString(), ore: oreNum(me.stats.oreGrams), lamportsDeployed: me.stats.lamportsDeployed.toString(), solReturnedLamports: me.stats.solReturnedLamports.toString() },
        fleets: {
          you: { layout: me.layout, salt: me.salt, commit: me.commit },
          opponent: { layout: them.layout, salt: them.salt, commit: them.commit, commitVerified: them.commitVerified },
        },
        verifyHint: "sha256(bytes(layout[25]) || bytes(salt)) must equal the opponentCommit you received in fleets_committed",
        claim: p.onChain && me.stats.oreGrams > 0n ? { claimableOreApprox: oreNum(me.stats.oreGrams), note: "ClaimORE moves mined ORE to the wallet (10% refining fee on unrefined ORE)" } : null,
      });
    }
    this.log(`[${this.id}] finished: ${reason}, winner seat ${winnerSeat}`);
    this.onEnd?.(r, this);
  }
}

/** Player signs the volley itself ONLY when the wallet has no Automation at all: ORE rejects a wallet-signed Deploy while any
 *  Automation exists (deploy.rs: signer must be its executor), and nothing may have landed and the round must still be open. */
export function volleyFallback(r: { placed: boolean; sig?: string; reason?: string }, slot: number, endSlot: number): boolean {
  return !r.placed && !r.sig && r.reason === "no_automation" && slot < endSlot - 8;
}
