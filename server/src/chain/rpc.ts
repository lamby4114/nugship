// Gentle, read-only RPC wrapper: serialized queue with min spacing, 429 backoff, small TTL cache.
// This module NEVER signs or sends transactions; it only exposes read methods.
import { Commitment, Connection, PublicKey, VersionedTransactionResponse } from "@solana/web3.js";

export interface RpcOptions { url?: string; wsUrl?: string; minIntervalMs?: number; commitment?: Commitment; }

export class ReadOnlyRpc {
  readonly conn: Connection;
  private last = 0;
  private chain: Promise<unknown> = Promise.resolve();
  private cache = new Map<string, { t: number; v: unknown }>();
  private minInterval: number;
  stats = { calls: 0, retries: 0, cacheHits: 0 };

  constructor(opts: RpcOptions = {}) {
    const url = opts.url ?? process.env.RPC_URL ?? "https://api.mainnet-beta.solana.com";
    const wsUrl = opts.wsUrl ?? process.env.RPC_WS_URL;
    this.conn = new Connection(url, { commitment: opts.commitment ?? "confirmed", wsEndpoint: wsUrl, disableRetryOnRateLimit: true });
    this.minInterval = opts.minIntervalMs ?? Number(process.env.RPC_MIN_INTERVAL_MS ?? 150);
  }

  /** Run fn through the serialized, rate-limited queue with retry/backoff. */
  call<T>(label: string, fn: () => Promise<T>, ttlMs = 0): Promise<T> {
    if (ttlMs > 0) {
      const c = this.cache.get(label);
      if (c && Date.now() - c.t < ttlMs) { this.stats.cacheHits++; return Promise.resolve(c.v as T); }
    }
    const run = async (): Promise<T> => {
      for (let attempt = 0; ; attempt++) {
        const wait = this.last + this.minInterval - Date.now();
        if (wait > 0) await sleep(wait);
        this.last = Date.now();
        this.stats.calls++;
        try {
          const v = await fn();
          if (ttlMs > 0) { this.cache.set(label, { t: Date.now(), v }); if (this.cache.size > 5000) this.cache.clear(); }
          return v;
        } catch (e: any) {
          const msg = String(e?.message ?? e);
          if (attempt >= 5 || /not supported by the requesting client|-32015|-32009|-32007/.test(msg + (e?.code ?? ""))) throw e;
          this.stats.retries++;
          const backoff = /429|Too Many|rate/i.test(msg) ? 2000 * (attempt + 1) : 500 * (attempt + 1);
          await sleep(backoff);
        }
      }
    };
    const p = this.chain.then(run, run);
    this.chain = p.catch(() => undefined);
    return p;
  }

  getSlot(ttlMs = 0) { return this.call("slot", () => this.conn.getSlot("processed"), ttlMs); }
  getAccount(pk: PublicKey, ttlMs = 0) {
    return this.call("acct:" + pk.toBase58(), async () => (await this.conn.getAccountInfo(pk))?.data ?? null, ttlMs);
  }
  /** Batched account fetch (<=100 per RPC call). Returns data buffers (or null) in input order. */
  async getAccounts(pks: PublicKey[]): Promise<(Buffer | null)[]> {
    const out: (Buffer | null)[] = [];
    for (let i = 0; i < pks.length; i += 100) {
      const chunk = pks.slice(i, i + 100);
      const res = await this.call("multi:" + i, () => this.conn.getMultipleAccountsInfo(chunk));
      for (const a of res) out.push(a ? Buffer.from(a.data) : null);
    }
    return out;
  }
  getSignatures(pk: PublicKey, limit = 100, before?: string) {
    return this.call(`sigs:${pk.toBase58()}:${limit}:${before ?? ""}`, () => this.conn.getSignaturesForAddress(pk, { limit, before }), 0);
  }
  /** Returns null for txs this client cannot parse (e.g. newer tx versions) instead of throwing. */
  async getTransaction(sig: string): Promise<VersionedTransactionResponse | null> {
    try {
      return await this.call("tx:" + sig, () => this.conn.getTransaction(sig, { maxSupportedTransactionVersion: 0, commitment: "confirmed" }), 10 * 60_000);
    } catch (e: any) {
      if (/not supported by the requesting client/.test(String(e?.message))) return null;
      throw e;
    }
  }
  getPerfSlotTime() {
    return this.call("perf", async () => {
      const s = await this.conn.getRecentPerformanceSamples(10);
      const slots = s.reduce((a, x) => a + x.numSlots, 0), secs = s.reduce((a, x) => a + x.samplePeriodSecs, 0);
      return slots > 0 ? secs / slots : 0.4;
    }, 5 * 60_000);
  }
}

export const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
