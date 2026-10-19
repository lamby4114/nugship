// Round sources drive the match clock. Live = real ORE mainnet rounds (read-only), Replay = recent real rounds
// replayed on a compressed synthetic slot clock, Practice = per-match synthetic rounds with fire-now support.
import { EventEmitter } from "node:events";
import crypto from "node:crypto";
import { ReadOnlyRpc, sleep } from "./rpc.js";
import {
  BOARD_ADDRESS, CONFIG_ADDRESS, ONE_ORE, Round, U64_MAX, decodeBoard, decodeConfig, decodeRound, didHitMotherlode, isSplitRound,
  roundPda, roundRng, winningSquare,
} from "../ore/index.js";

export interface RoundWindow { roundId: number; startSlot: number; endSlot: number; }
export interface RoundResult {
  roundId: number; winningSquare: number; motherlode: boolean; motherlodeGrams: bigint; split: boolean; topMiner: string;
  totalDeployed: bigint; deployedOnSquare: bigint[]; slotHashHex: string; round: Round | null; synthetic: boolean;
}
export type SourceKind = "live" | "replay" | "practice";

export interface RoundSource extends EventEmitter {
  readonly kind: SourceKind;
  start(): Promise<void>;
  stop(): void;
  slot(): number;
  slotMs(): number;
  current(): RoundWindow | null;
  lastResult(): RoundResult | null;
  roundSlots: number; intermissionSlots: number;
}

export function resultFromRound(r: Round): RoundResult | null {
  const rng = roundRng(r.slotHash);
  if (rng === null) return null;
  return {
    roundId: Number(r.id), winningSquare: winningSquare(rng), motherlode: didHitMotherlode(rng), motherlodeGrams: r.motherlode,
    split: isSplitRound(r), topMiner: r.topMiner.toBase58(), totalDeployed: r.deployed.reduce((a, b) => a + b, 0n),
    deployedOnSquare: r.deployed, slotHashHex: r.slotHash.toString("hex"), round: r, synthetic: false,
  };
}

// ---------------------------------------------------------------------------------------------------------------
export class LiveRoundSource extends EventEmitter implements RoundSource {
  readonly kind = "live" as const;
  roundSlots = 240; intermissionSlots = 48;
  private _slot = 0; private _slotAt = 0; private _slotMs = 268;
  private win: RoundWindow | null = null;
  private boardRound = 0;
  private last: RoundResult | null = null;
  private timers: NodeJS.Timeout[] = [];
  private subs: number[] = [];
  private stopped = false;
  private settling = new Set<number>();
  log: (m: string) => void = () => {};

  constructor(private rpc: ReadOnlyRpc) { super(); this.setMaxListeners(1000); }

  async start() {
    const cfg = decodeConfig((await this.rpc.getAccount(CONFIG_ADDRESS))!);
    this.roundSlots = Number(cfg.protocol.roundSlots); this.intermissionSlots = Number(cfg.protocol.intermissionSlots);
    this._slotMs = Math.round((await this.rpc.getPerfSlotTime()) * 1000) || 400;
    this.setSlot(await this.rpc.getSlot());
    await this.pollBoard();
    try {
      this.subs.push(this.rpc.conn.onSlotChange((s) => this.setSlot(s.slot)));
      this.subs.push(this.rpc.conn.onAccountChange(BOARD_ADDRESS, (a) => this.onBoard(Buffer.from(a.data)), { commitment: "confirmed" }));
    } catch (e) { this.log("ws subscribe failed, polling only: " + e); }
    // Safety polls: slot every 2s if WS is quiet, board every 6s.
    this.timers.push(setInterval(() => { if (Date.now() - this._slotAt > 2500) this.rpc.getSlot().then((s) => this.setSlot(s)).catch(() => {}); }, 2000));
    this.timers.push(setInterval(() => this.pollBoard().catch(() => {}), 6000));
    // Local slot extrapolation tick so matches see smooth slots between RPC updates.
    this.timers.push(setInterval(() => this.emit("slot", this.slot()), 250));
  }
  stop() {
    this.stopped = true;
    this.timers.forEach(clearInterval);
    for (const id of this.subs) { this.rpc.conn.removeSlotChangeListener(id).catch(() => {}); this.rpc.conn.removeAccountChangeListener(id).catch(() => {}); }
  }
  /** Slot-rate sample (v1.1.0 timer fix): mainnet slot time drifts (~210-420 ms); refine _slotMs every >= 10 s of observed
   *  slots so clock/turn_start deadlines don't run fast or slow for a whole round. */
  private rateAt = 0; private rateSlot = 0;
  private setSlot(s: number) {
    if (s <= this._slot) return;
    const now = Date.now();
    this._slot = s; this._slotAt = now;
    if (!this.rateAt) { this.rateAt = now; this.rateSlot = s; return; }
    if (now - this.rateAt >= 10_000 && s > this.rateSlot) {
      const ms = (now - this.rateAt) / (s - this.rateSlot);
      if (ms >= 100 && ms <= 1000) this._slotMs = Math.round(this._slotMs * 0.7 + ms * 0.3);
      this.rateAt = now; this.rateSlot = s;
    }
  }
  slot() { return this._slot + Math.min(8, Math.floor((Date.now() - this._slotAt) / this._slotMs)); }
  slotMs() { return this._slotMs; }
  current() { return this.win; }
  lastResult() { return this.last; }

  private async pollBoard() { const d = await this.rpc.getAccount(BOARD_ADDRESS); if (d) this.onBoard(d); }

  private onBoard(d: Buffer) {
    const b = decodeBoard(d);
    const id = Number(b.roundId);
    if (this.boardRound && id > this.boardRound) {
      for (let r = this.boardRound; r < id; r++) this.settle(r);
    }
    this.boardRound = Math.max(this.boardRound, id);
    if (b.endSlot !== U64_MAX) {
      const w = { roundId: id, startSlot: Number(b.startSlot), endSlot: Number(b.endSlot) };
      if (!this.win || this.win.roundId !== w.roundId || this.win.endSlot !== w.endSlot) {
        this.win = w;
        this.log(`round ${w.roundId} window ${w.startSlot}..${w.endSlot} (slot ${this.slot()})`);
        this.emit("round", w);
      }
    }
  }

  /** After Reset, Round(id).slot_hash is set; fetch it (retrying gently) and emit 'settled'. */
  private async settle(roundId: number) {
    if (this.settling.has(roundId)) return;
    this.settling.add(roundId);
    for (let attempt = 0; attempt < 20 && !this.stopped; attempt++) {
      try {
        const d = await this.rpc.getAccount(roundPda(roundId));
        if (d) {
          const res = resultFromRound(decodeRound(d));
          if (res) { this.last = res; this.log(`round ${roundId} settled: square ${res.winningSquare}${res.motherlode ? " MOTHERLODE" : ""}`); this.emit("settled", res); return; }
        }
      } catch (e) { this.log(`settle ${roundId} fetch error ${e}`); }
      await sleep(1500);
    }
    this.log(`round ${roundId}: gave up waiting for entropy`);
  }
}

// ---------------------------------------------------------------------------------------------------------------
/** Replays recent real (settled) ORE rounds read-only on a compressed synthetic slot clock. */
export class ReplayRoundSource extends EventEmitter implements RoundSource {
  readonly kind = "replay" as const;
  roundSlots = 240; intermissionSlots = 48;
  private rounds: Round[] = [];
  private idx = 0;
  private _slot = 1_000_000;
  private win: RoundWindow | null = null;
  private last: RoundResult | null = null;
  private timer?: NodeJS.Timeout;
  private settleAt = -1;
  log: (m: string) => void = () => {};

  constructor(private rpc: ReadOnlyRpc | null, private opts: { count?: number; speed?: number; rounds?: Round[]; loop?: boolean } = {}) { super(); this.setMaxListeners(1000); }

  async start() {
    if (this.opts.rounds) this.rounds = this.opts.rounds;
    else {
      const board = decodeBoard((await this.rpc!.getAccount(BOARD_ADDRESS))!);
      const n = this.opts.count ?? 60;
      const ids = Array.from({ length: n }, (_, k) => Number(board.roundId) - n + k);
      const datas = await this.rpc!.getAccounts(ids.map((i) => roundPda(i)));
      this.rounds = datas.filter(Boolean).map((d) => decodeRound(d!)).filter((r) => roundRng(r.slotHash) !== null);
    }
    if (!this.rounds.length) throw new Error("no settled rounds to replay");
    this.log(`replaying ${this.rounds.length} real rounds ${this.rounds[0].id}..${this.rounds[this.rounds.length - 1].id} at ${this.opts.speed ?? 10}x`);
    this.openNext();
    const tickMs = Math.max(5, 268 / (this.opts.speed ?? 10));
    this.timer = setInterval(() => this.tick(), tickMs);
  }
  stop() { if (this.timer) clearInterval(this.timer); }
  slot() { return this._slot; }
  slotMs() { return 268 / (this.opts.speed ?? 10); }
  current() { return this.win; }
  lastResult() { return this.last; }
  roundsLoaded() { return this.rounds.map((r) => Number(r.id)); }

  private openNext() {
    if (this.idx >= this.rounds.length) {
      if (!this.opts.loop) { this.win = null; this.emit("exhausted"); return; }
      this.idx = 0;
    }
    const r = this.rounds[this.idx];
    // Keep real round ids so results map 1:1 to mainnet rounds (loops get an offset to stay monotonic).
    const loopOffset = Math.floor(this._slot / 1e9);
    this.win = { roundId: Number(r.id) + loopOffset, startSlot: this._slot + 1, endSlot: this._slot + 1 + this.roundSlots };
    this.settleAt = this.win.endSlot + this.intermissionSlots;
    this.emit("round", this.win);
  }
  private tick() {
    this._slot++;
    this.emit("slot", this._slot);
    if (this.win && this._slot === this.settleAt) {
      const res = resultFromRound(this.rounds[this.idx])!;
      res.roundId = this.win.roundId;
      this.last = res;
      this.emit("settled", res);
      this.idx++;
      this.openNext();
    }
  }
}

// ---------------------------------------------------------------------------------------------------------------
/** Off-chain practice rounds: random entropy, short windows, and `fireNow()` to skip the wait. One per match. */
export class PracticeRoundSource extends EventEmitter implements RoundSource {
  readonly kind = "practice" as const;
  roundSlots: number; intermissionSlots = 4;
  private _slot = 0; private win: RoundWindow | null = null; private last: RoundResult | null = null; private timer?: NodeJS.Timeout;
  private nextId = 1; private settleAt = -1;
  constructor(private opts: { aimSeconds?: number; tickMs?: number; motherlodeOdds?: number } = {}) {
    super();
    this.roundSlots = Math.round(((opts.aimSeconds ?? 30) * 1000) / (opts.tickMs ?? 250));
  }
  async start() { this.open(); this.timer = setInterval(() => this.tick(), this.opts.tickMs ?? 250); }
  stop() { if (this.timer) clearInterval(this.timer); }
  slot() { return this._slot; }
  slotMs() { return this.opts.tickMs ?? 250; }
  current() { return this.win; }
  lastResult() { return this.last; }
  /** Jump the clock to the end of the deploy window (player pressed FIRE in practice). */
  fireNow() { if (this.win && this._slot < this.win.endSlot - 1) { this._slot = this.win.endSlot - 1; this.emit("slot", this._slot); } }
  private open() {
    this.win = { roundId: this.nextId++, startSlot: this._slot + 1, endSlot: this._slot + 1 + this.roundSlots };
    this.settleAt = this.win.endSlot + this.intermissionSlots;
    this.emit("round", this.win);
  }
  private tick() {
    this._slot++;
    this.emit("slot", this._slot);
    if (this.win && this._slot >= this.settleAt) {
      const hash = crypto.randomBytes(32);
      const rng = hash.readBigUInt64LE(0) ^ hash.readBigUInt64LE(8) ^ hash.readBigUInt64LE(16) ^ hash.readBigUInt64LE(24);
      const res: RoundResult = {
        roundId: this.win.roundId, winningSquare: Number(rng % 25n), motherlode: Number(rng % BigInt(Math.max(2, Math.floor(this.opts.motherlodeOdds ?? 500)))) === 0, motherlodeGrams: 0n, split: true,
        topMiner: "", totalDeployed: 0n, deployedOnSquare: Array(25).fill(0n), slotHashHex: hash.toString("hex"), round: null, synthetic: true,
      };
      this.last = res;
      this.emit("settled", res);
      this.open();
    }
  }
}

export const ORE_GRAMS = ONE_ORE;
