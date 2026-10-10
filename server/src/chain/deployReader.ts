// How the referee learns which squares each player ACTUALLY deployed on in a round.
//  - ChainDeployReader: reads the player's ORE Miner account (authority = wallet; deploys signed by their session key
//    land there). If the miner already moved on to a later round, falls back to the DeployEvent logs in the miner PDA's
//    recent transactions. Read-only.
//  - SimLedger: in-memory stand-in used by the simulation harness (records what bots *would* have deployed; nothing sent).
import { PublicKey } from "@solana/web3.js";
import bs58 from "bs58";
import { ReadOnlyRpc } from "./rpc.js";
import { ORE_PROGRAM_ID, decodeEvent, decodeMiner, decodeOreIxData, minerPda } from "../ore/index.js";

export interface DeployRecord {
  mask: number;                 // squares with deployed > 0 in that round
  perSquare: bigint[];          // lamports per square (25)
  source: "miner" | "event" | "sim";
  signers?: string[];           // who signed the deploy(s) (event source only)
}
export interface DeployReader {
  /** Called right after the deploy window closes: a good moment to snapshot (miner can't change for this round anymore). */
  snapshot(roundId: number, authorities: string[]): void;
  read(roundId: number, authorities: string[]): Promise<Map<string, DeployRecord | null>>;
}

export class ChainDeployReader implements DeployReader {
  private snaps = new Map<string, DeployRecord>(); // `${round}:${authority}`
  log: (m: string) => void = () => {};
  constructor(private rpc: ReadOnlyRpc) {}

  snapshot(roundId: number, authorities: string[]) { this.fetchMiners(roundId, authorities).catch((e) => this.log("snapshot err " + e)); }

  private async fetchMiners(roundId: number, authorities: string[]) {
    const need = authorities.filter((a) => !this.snaps.has(`${roundId}:${a}`));
    if (!need.length) return new Map<string, "later" | "none">();
    const datas = await this.rpc.getAccounts(need.map((a) => minerPda(new PublicKey(a))));
    const status = new Map<string, "later" | "none">();
    datas.forEach((d, i) => {
      if (!d) { status.set(need[i], "none"); return; }
      const m = decodeMiner(d);
      const rid = Number(m.roundId);
      if (rid === roundId) {
        let mask = 0; m.deployed.forEach((v, k) => { if (v > 0n) mask |= 1 << k; });
        this.snaps.set(`${roundId}:${need[i]}`, { mask: mask >>> 0, perSquare: m.deployed, source: "miner" });
      } else status.set(need[i], rid > roundId ? "later" : "none");
    });
    if (this.snaps.size > 20000) this.snaps.clear();
    return status;
  }

  async read(roundId: number, authorities: string[]) {
    const status = await this.fetchMiners(roundId, authorities);
    const out = new Map<string, DeployRecord | null>();
    for (const a of authorities) {
      const s = this.snaps.get(`${roundId}:${a}`);
      if (s) { out.set(a, s); continue; }
      out.set(a, status.get(a) === "later" ? await this.fromEvents(roundId, a) : null);
    }
    return out;
  }

  /** Fallback: scan the miner PDA's recent txs for ORE DeployEvents with this round id. */
  private async fromEvents(roundId: number, authority: string): Promise<DeployRecord | null> {
    const sigs = await this.rpc.getSignatures(minerPda(new PublicKey(authority)), 25);
    const per = Array(25).fill(0n) as bigint[];
    const signers = new Set<string>();
    let found = false;
    for (const s of sigs) {
      if (s.err) continue;
      const tx = await this.rpc.getTransaction(s.signature);
      if (!tx) continue;
      const keys = tx.transaction.message.getAccountKeys({ accountKeysFromLookups: tx.meta?.loadedAddresses ?? undefined });
      for (const g of tx.meta?.innerInstructions ?? []) for (const ii of g.instructions as any[]) {
        if (!keys.get(ii.programIdIndex)?.equals(ORE_PROGRAM_ID)) continue;
        const d = decodeOreIxData(Buffer.from(bs58.decode(ii.data)));
        if (d.kind !== "Log") continue;
        const ev = decodeEvent(d.payload);
        if (ev?.type === "Deploy" && Number(ev.ev.roundId) === roundId && ev.ev.authority.toBase58() === authority) {
          found = true; signers.add(ev.ev.signer.toBase58());
          for (let k = 0; k < 25; k++) if ((ev.ev.mask >>> k) & 1) per[k] += ev.ev.amount;
        }
      }
    }
    if (!found) return null;
    let mask = 0; per.forEach((v, k) => { if (v > 0n) mask |= 1 << k; });
    return { mask: mask >>> 0, perSquare: per, source: "event", signers: [...signers] };
  }
}

/** Simulation ledger: bots "fire" here instead of sending transactions. */
export class SimLedger implements DeployReader {
  private m = new Map<string, DeployRecord>();
  /** Record what a Deploy(amount, mask) for `authority` in `roundId` would have done (one deploy per square per round). */
  deploy(authority: string, roundId: number, mask: number, amount: bigint) {
    const k = `${roundId}:${authority}`;
    const rec = this.m.get(k) ?? { mask: 0, perSquare: Array(25).fill(0n), source: "sim" as const };
    for (let i = 0; i < 25; i++) if ((mask >>> i) & 1 && rec.perSquare[i] === 0n) { rec.perSquare[i] = amount; rec.mask |= 1 << i; }
    rec.mask >>>= 0;
    this.m.set(k, rec);
  }
  snapshot() {}
  async read(roundId: number, authorities: string[]) {
    return new Map(authorities.map((a) => [a, this.m.get(`${roundId}:${a}`) ?? null] as const));
  }
}
