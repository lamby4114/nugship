// Headless WebSocket bot that plays ORE SEAS exactly like the Unity client would, but "fires" into a SimLedger
// instead of sending a transaction. It builds the real Deploy/Checkpoint instructions it WOULD sign (to exercise the
// encoders) and never signs or sends them. The ephemeral keypair is only used to sign the SIWS login text; it is
// never funded or persisted.
import WebSocket from "ws";
import crypto from "node:crypto";
import { Keypair, PublicKey } from "@solana/web3.js";
import bs58 from "bs58";
import { SimLedger } from "../chain/deployReader.js";
import { chooseTargets, SeaKnowledge, Captain } from "../game/ai.js";
import { commitLayout, randomLayout, randomSalt } from "../game/fleet.js";
import { checkpointIx, deployIx } from "../ore/index.js";
import nacl from "tweetnacl";

export interface BotOpts {
  url: string; name: string; ledger: SimLedger; tactic?: Captain["tactic"]; withWallet?: boolean;
  /** Misbehaviour knobs for referee tests. */
  skipDeployEveryNth?: number; extraSquare?: boolean;
  log?: (m: string) => void;
}
export interface BotResult { name: string; wallet: string | null; playerId?: string; matchId?: string; result?: string; reason?: string; opponentCommitVerified?: boolean; reveals: any[]; stats?: any; builtIxs: number; errors: any[]; turns: number; }

export class Bot {
  ws!: WebSocket; kp = Keypair.generate(); session = Keypair.generate().publicKey; // session key pubkey only (never used to sign here)
  res: BotResult; knowledge: SeaKnowledge = { misses: new Set(), hits: new Set(), sunkShips: [] };
  private opponentCommit?: string; private turnCount = 0; private lastDeployRound: number | null = null;
  private resolveEnd!: (r: BotResult) => void; done: Promise<BotResult>;
  private onWelcome?: (b: Bot) => void;
  constructor(public o: BotOpts) {
    this.res = { name: o.name, wallet: o.withWallet === false ? null : this.kp.publicKey.toBase58(), reveals: [], builtIxs: 0, errors: [], turns: 0 };
    this.done = new Promise((r) => (this.resolveEnd = r));
  }
  send(m: any) { this.ws.send(JSON.stringify(m)); }
  connect(onWelcome: (b: Bot) => void) {
    this.onWelcome = onWelcome;
    this.ws = new WebSocket(this.o.url);
    this.ws.on("message", (d) => this.onMsg(JSON.parse(d.toString())));
    this.ws.on("error", (e) => this.res.errors.push(String(e)));
    return this;
  }
  private log(m: string) { this.o.log?.(`[${this.o.name}] ${m}`); }

  private onMsg(m: any) {
    switch (m.t) {
      case "challenge": {
        const hello: any = { t: "hello", name: this.o.name, bot: true }; // scripted client: boards tag it AI
        if (this.o.withWallet !== false) {
          const message = `ore-seas.app wants you to sign in with your Solana account:\n${this.kp.publicKey.toBase58()}\n\nNonce: ${m.nonce}`;
          const sig = nacl.sign.detached(Buffer.from(message), this.kp.secretKey);
          hello.wallet = this.kp.publicKey.toBase58(); hello.signIn = { message, signature: bs58.encode(sig) };
        }
        return this.send(hello);
      }
      case "welcome": this.res.playerId = m.playerId; return this.onWelcome?.(this);
      case "match_found": {
        this.res.matchId = m.matchId;
        const layout = randomLayout(); const salt = randomSalt();
        this.log(`match ${m.matchId} vs ${m.opponent.name} (${m.mode}, source ${m.source})`);
        return this.send({ t: "place_fleet", matchId: m.matchId, layout, salt, commit: commitLayout(layout, salt) });
      }
      case "fleets_committed": this.opponentCommit = m.opponentCommit; return;
      case "turn_start": {
        this.turnCount++;
        const targets = chooseTargets(this.o.tactic ?? "hunt", this.knowledge, m.maxShots);
        return this.send({ t: "aim", matchId: m.matchId, roundId: m.roundId, squares: targets });
      }
      case "lock": {
        if (!m.fire) return;
        let mask = m.mask as number;
        if (this.o.skipDeployEveryNth && this.turnCount % this.o.skipDeployEveryNth === 0) { this.log(`(test) skipping deploy for round ${m.roundId}`); return; }
        if (this.o.extraSquare) { const extra = [...Array(25).keys()].find((i) => !((mask >>> i) & 1)); if (extra !== undefined) mask |= 1 << extra; }
        // Build the exact instructions a real client would sign with its session key (NOT signed, NOT sent).
        const authority = this.kp.publicKey;
        const ixs = [...(this.lastDeployRound !== null ? [checkpointIx(this.session, authority, BigInt(this.lastDeployRound))] : []), deployIx(this.session, authority, BigInt(m.amountLamports), BigInt(m.roundId), mask >>> 0)];
        this.res.builtIxs += ixs.length;
        this.lastDeployRound = m.roundId;
        this.o.ledger.deploy(authority.toBase58(), m.roundId, mask >>> 0, BigInt(m.amountLamports));
        return this.send({ t: "fired", matchId: m.matchId, roundId: m.roundId, signature: "SIMULATED-" + crypto.randomBytes(4).toString("hex") });
      }
      case "reveal": {
        this.res.turns++;
        for (const s of m.outgoing.shots) (s.hit ? this.knowledge.hits : this.knowledge.misses).add(s.square);
        // rebuild sunk knowledge from server view (cells of sunk ships are inferred by AI from hits)
        this.knowledge.sunkShips = m.opponent.sunkShips;
        this.res.reveals.push(m);
        return;
      }
      case "match_end": {
        this.res.result = m.result; this.res.reason = m.reason; this.res.stats = m.stats;
        const opp = m.fleets.opponent;
        this.res.opponentCommitVerified = !!opp.layout?.length && commitLayout(opp.layout, opp.salt) === this.opponentCommit && opp.commit === this.opponentCommit;
        this.log(`match_end ${m.result} (${m.reason}) turns=${m.turns} opponentCommitVerified=${this.res.opponentCommitVerified}`);
        setTimeout(() => { this.resolveEnd(this.res); this.ws.close(); }, 200);
        return;
      }
      case "error": this.res.errors.push(m); this.log(`error ${m.code}: ${m.message}`); return;
    }
  }
}

export { PublicKey };
