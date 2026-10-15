// ORE Automation executor: places a player's locked volley from THEIR Automation deposit (Discretionary strategy,
// executor = our key). The player signs once (automate: deposit + per-square amount + flat fee + executor). Each volley the
// server signs checkpoint(prev round)+deploy(exact locked mask) as executor; ORE pays `fee` lamports from the deposit back to
// the executor in the same tx, so our float only fronts the network fee. Executor can only deploy; it cannot withdraw.
import { ComputeBudgetProgram, Connection, Keypair, PublicKey, TransactionInstruction, TransactionMessage, VersionedTransaction } from "@solana/web3.js";
import fs from "node:fs";
import { automationPda, minerPda, roundPda } from "../ore/pda.js";
import { checkpointIx, deployIx } from "../ore/instructions.js";
import { decodeAutomation, decodeMiner, Automation, Miner } from "../ore/accounts.js";
import { AutomationStrategy } from "../ore/constants.js";

export const EXECUTOR_FEE_LAMPORTS = 20_000n;   // what the client puts in automate.fee; our cost must stay below it
export const BASE_FEE_LAMPORTS = 5_000n;        // one signature (executor is the only signer)
export const CU_LIMIT = 100_000;                // mainnet sim: automate+checkpoint+deploy used ~32k CU; deploy alone less
export const SAFETY_SLOTS = 4;                  // never send within this many slots of the round's end slot

/** Highest micro-lamports/CU we may pay so base + priority stays <= fee - margin. */
export function maxMicroLamports(fee = EXECUTOR_FEE_LAMPORTS, cu = CU_LIMIT, margin = 2_000n): bigint {
  const room = fee - BASE_FEE_LAMPORTS - margin;
  return room <= 0n ? 0n : (room * 1_000_000n) / BigInt(cu);
}
export function clampPriority(wanted: bigint, fee = EXECUTOR_FEE_LAMPORTS, cu = CU_LIMIT) { const c = maxMicroLamports(fee, cu); return wanted > c ? c : wanted < 0n ? 0n : wanted; }

export type Eligibility = { ok: true; automation: Automation; miner: Miner | null } | { ok: false; reason: string };

/** Pure gate: may our executor place `mask` x `amount` for this authority right now? */
export function eligible(executor: PublicKey, auto: Automation | null, miner: Miner | null, mask: number, amount: bigint, fee = EXECUTOR_FEE_LAMPORTS): Eligibility {
  if (!auto) return { ok: false, reason: "no_automation" };
  if (!auto.executor.equals(executor)) return { ok: false, reason: "other_executor" };
  if (auto.strategy !== BigInt(AutomationStrategy.Discretionary)) return { ok: false, reason: "strategy" };
  if (auto.fee < fee) return { ok: false, reason: "fee_too_low" };
  if (auto.amount < amount) return { ok: false, reason: "amount_below_stake" };      // ORE would silently deploy less
  const n = BigInt(popcount(mask));
  if (n === 0n) return { ok: false, reason: "empty" };
  if (auto.balance < amount * n + auto.fee) return { ok: false, reason: "balance_low" };
  return { ok: true, automation: auto, miner };
}
export const popcount = (m: number) => { let c = 0; for (let x = m >>> 0; x; x &= x - 1) c++; return c; };

/** Instructions for one volley: compute budget, checkpoint of the miner's previous round when due, deploy of exactly `mask`. */
export function volleyIxs(executor: PublicKey, authority: PublicKey, miner: Miner | null, roundId: bigint, mask: number, amount: bigint, microLamports: bigint): TransactionInstruction[] {
  const ixs = [ComputeBudgetProgram.setComputeUnitLimit({ units: CU_LIMIT })];
  if (microLamports > 0n) ixs.push(ComputeBudgetProgram.setComputeUnitPrice({ microLamports: Number(microLamports) }));
  if (miner && miner.roundId !== miner.checkpointId && miner.roundId < roundId) ixs.push(checkpointIx(executor, authority, miner.roundId));
  ixs.push(deployIx(executor, authority, amount, roundId, mask));
  return ixs;
}

export interface PlaceResult { placed: boolean; sig?: string; reason?: string; lamports?: string; }
export interface VolleyExecutor { pubkey: string; feeLamports: bigint; place(wallet: string, roundId: number, mask: number, amount: bigint, endSlot: number): Promise<PlaceResult>; status(wallet: string): Promise<any>; }

export function loadExecutorKey(env = process.env): Keypair | null {
  const v = env.EXECUTOR_KEY?.trim();
  if (!v) return null;
  const raw = v.startsWith("[") ? v : fs.existsSync(v) ? fs.readFileSync(v, "utf8") : null;
  if (!raw) throw new Error("EXECUTOR_KEY must be a JSON secret-key array (or a path to one)");
  return Keypair.fromSecretKey(Uint8Array.from(JSON.parse(raw)));
}

export class OreExecutor implements VolleyExecutor {
  readonly pubkey: string; readonly feeLamports = EXECUTOR_FEE_LAMPORTS;
  constructor(private kp: Keypair, private conn: Connection, private log: (m: string) => void = () => {}) { this.pubkey = kp.publicKey.toBase58(); }

  private async accounts(authority: PublicKey) {
    const [a, m] = await this.conn.getMultipleAccountsInfo([automationPda(authority), minerPda(authority)], "confirmed");
    return { auto: a ? decodeAutomation(Buffer.from(a.data)) : null, miner: m ? decodeMiner(Buffer.from(m.data)) : null };
  }

  async status(wallet: string) {
    const { auto } = await this.accounts(new PublicKey(wallet));
    return {
      executor: this.pubkey, feeLamports: this.feeLamports.toString(), exists: !!auto,
      ours: !!auto && auto.executor.equals(this.kp.publicKey) && auto.strategy === BigInt(AutomationStrategy.Discretionary) && auto.fee >= this.feeLamports,
      balanceLamports: auto ? auto.balance.toString() : "0", amountLamports: auto ? auto.amount.toString() : "0", feeSet: auto ? auto.fee.toString() : "0",
    };
  }

  private async priority(): Promise<bigint> {
    try {
      const f = await this.conn.getRecentPrioritizationFees();
      const v = f.map((x) => x.prioritizationFee).sort((a, b) => a - b);
      return clampPriority(BigInt(v[Math.floor(v.length * 0.75)] ?? 0));
    } catch { return clampPriority(10_000n); }
  }

  async place(wallet: string, roundId: number, mask: number, amount: bigint, endSlot: number): Promise<PlaceResult> {
    const authority = new PublicKey(wallet);
    const { auto, miner } = await this.accounts(authority);
    const el = eligible(this.kp.publicKey, auto, miner, mask, amount);
    if (!el.ok) return { placed: false, reason: el.reason };
    const ixs = volleyIxs(this.kp.publicKey, authority, miner, BigInt(roundId), mask, amount, await this.priority());
    let lastErr = "";
    for (let attempt = 0; attempt < 4; attempt++) {
      const slot = await this.conn.getSlot("processed");
      if (slot >= endSlot - SAFETY_SLOTS) return { placed: false, reason: lastErr || "round_ending" };
      const { blockhash, lastValidBlockHeight } = await this.conn.getLatestBlockhash("confirmed");
      const tx = new VersionedTransaction(new TransactionMessage({ payerKey: this.kp.publicKey, recentBlockhash: blockhash, instructions: ixs }).compileToV0Message());
      tx.sign([this.kp]);
      const sim = await this.conn.simulateTransaction(tx, { sigVerify: false, commitment: "processed" });
      if (sim.value.err) { lastErr = "sim:" + JSON.stringify(sim.value.err); this.log(`[exec] ${wallet.slice(0, 6)} r${roundId} sim fail ${lastErr} ${(sim.value.logs ?? []).slice(-3).join(" | ")}`); return { placed: false, reason: lastErr }; }
      try {
        const sig = await this.conn.sendRawTransaction(tx.serialize(), { skipPreflight: true, maxRetries: 0 });
        const until = Date.now() + 12_000;
        while (Date.now() < until) {
          const st = (await this.conn.getSignatureStatuses([sig])).value[0];
          if (st?.err) { lastErr = "tx:" + JSON.stringify(st.err); break; }
          if (st && (st.confirmationStatus === "confirmed" || st.confirmationStatus === "finalized")) {
            this.log(`[exec] ${wallet.slice(0, 6)} r${roundId} placed ${sig}`);
            return { placed: true, sig, lamports: (amount * BigInt(popcount(mask))).toString() };
          }
          if ((await this.conn.getBlockHeight("confirmed")) > lastValidBlockHeight) break;
          if ((await this.conn.getSlot("processed")) >= endSlot) return { placed: false, reason: "round_ended_unconfirmed", sig };
          await this.conn.sendRawTransaction(tx.serialize(), { skipPreflight: true, maxRetries: 0 }).catch(() => {});
          await new Promise((r) => setTimeout(r, 800));
        }
        if (lastErr.startsWith("tx:")) return { placed: false, reason: lastErr, sig };
      } catch (e: any) { lastErr = "send:" + (e?.message ?? e); }
    }
    return { placed: false, reason: lastErr || "unconfirmed" };
  }
}
