// Byte-for-byte checks of the TS ORE layer against REAL mainnet transactions/accounts
// captured read-only by scripts/fetch-fixtures.ts (test/fixtures/mainnet.json).
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { PublicKey, TransactionInstruction } from "@solana/web3.js";
import {
  AutomationStrategy, BOARD_ADDRESS, CONFIG_ADDRESS, ORE_PROGRAM_ID, TREASURY_ADDRESS, VAR_ADDRESS, automateCloseIx, automateIx,
  automationPda, boardPda, checkpointIx, claimOreIx, claimSolIx, configPda, decodeAutomation, decodeBoard, decodeConfig, decodeMiner,
  decodeOreIxData, decodeRound, decodeTreasury, deployIx, didHitMotherlode, entropyVarPda, minerPda, roundPda, roundRng, treasuryPda,
  winningSquare, coordToSquare, squareToCoord, squaresToMask, maskToSquares,
} from "../src/ore/index.js";

const fx = JSON.parse(fs.readFileSync(new URL("./fixtures/mainnet.json", import.meta.url), "utf8"));
const P = (s: string) => new PublicKey(s);

type Meta = { pubkey: string; isSigner: boolean; isWritable: boolean };
/** Tx-level flags are the union over all metas with the same key; compute the same union for the builder. */
function union(metas: Meta[]) {
  const m = new Map<string, { s: boolean; w: boolean }>();
  for (const a of metas) { const v = m.get(a.pubkey) ?? { s: false, w: false }; v.s ||= a.isSigner; v.w ||= a.isWritable; m.set(a.pubkey, v); }
  return m;
}
/**
 * Compare builder output to the on-chain compiled instruction.
 * "exact": program, data bytes, every account pubkey in order, and signer/writable flags all identical.
 * "compatible": data bytes + all program-validated accounts identical, and every account the real (successful) tx
 * had writable/signer is also writable/signer in ours. Some third-party bots mark Config/Board read-only and, on
 * non-round-starting deploys, pass the ORE program in place of the entropy accounts; the program accepts both.
 */
function compareIx(built: TransactionInstruction, f: any): "exact" | "compatible" {
  assert.equal(built.programId.toBase58(), f.programId, "program id");
  assert.equal(Buffer.from(built.data).toString("hex"), f.dataHex, `data bytes (${f.kind} ${f.signature})`);
  assert.equal(built.keys.length, f.accounts.length, "account count");
  const b: Meta[] = built.keys.map((k) => ({ pubkey: k.pubkey.toBase58(), isSigner: k.isSigner, isWritable: k.isWritable }));
  const ub = union(b);
  const exact = f.accounts.every((a: Meta, i: number) => b[i].pubkey === a.pubkey && ub.get(a.pubkey)!.s === a.isSigner && ub.get(a.pubkey)!.w === a.isWritable);
  if (exact) return "exact";
  const optionalTail = f.kind.startsWith("Deploy") ? 2 : 0; // entropy var + entropy program only used by the round-starting deploy
  f.accounts.forEach((a: Meta, i: number) => {
    if (i >= f.accounts.length - optionalTail) return;
    assert.equal(b[i].pubkey, a.pubkey, `account #${i} pubkey (${f.kind} ${f.signature})`);
    const x = ub.get(a.pubkey)!;
    if (a.isSigner) assert.ok(x.s, `account #${i} must be signer`);
    if (a.isWritable) assert.ok(x.w, `account #${i} must be writable (${a.pubkey})`);
  });
  return "compatible";
}
function assertSameIx(built: TransactionInstruction, f: any) { assert.equal(compareIx(built, f), "exact", `${f.kind} ${f.signature} not exact`); }

const byKind = (k: string) => fx.instructions.filter((f: any) => f.kind === k);
const report: string[] = [];
const tally: Record<string, { exact: number; compatible: number }> = {};
test.after(() => {
  console.log("\n" + report.join("\n"));
  console.log("  tally:", JSON.stringify(tally));
  for (const k of ["Deploy/manual", "Deploy/executor", "Checkpoint", "ClaimSOL", "ClaimORE", "Automate"]) if (!(tally[k]?.exact > 0)) { console.error("NO EXACT MATCH FOR", k); process.exitCode = 1; }
});

test("fixtures present", () => {
  for (const k of ["Deploy/manual", "Deploy/executor", "Checkpoint", "ClaimSOL", "ClaimORE", "Automate"]) assert.ok(byKind(k).length > 0, `no ${k} fixture`);
});

for (const f of fx.instructions) {
  test(`${f.kind} ${f.signature.slice(0, 16)}… reproduces byte-for-byte`, () => {
    const p = f.params;
    let built: TransactionInstruction;
    switch (f.kind) {
      case "Deploy/manual":
      case "Deploy/executor":
        built = deployIx(P(p.signer), P(p.authority), BigInt(p.amount), BigInt(p.roundId), p.mask);
        // decode side agrees with the on-chain event
        const d = decodeOreIxData(Buffer.from(f.dataHex, "hex"));
        assert.equal(d.kind, "Deploy");
        if (p.event) { assert.equal(p.event.roundId, p.roundId); assert.equal(p.event.authority, p.authority); assert.equal(p.event.signer, p.signer); }
        break;
      case "Checkpoint": built = checkpointIx(P(p.signer), P(p.authority), BigInt(p.roundId)); break;
      case "ClaimSOL": built = claimSolIx(P(p.signer)); break;
      case "ClaimORE": built = claimOreIx(P(p.signer), BigInt(p.bps)); break;
      case "Automate": {
        const c = p.conditions;
        built = automateIx(P(p.signer), {
          amount: BigInt(p.amount), deposit: BigInt(p.deposit), executor: P(p.executor), fee: BigInt(p.fee), mask: BigInt(p.mask),
          strategy: p.strategy as AutomationStrategy, reload: BigInt(p.reload) > 0n,
          conditions: c && { maxProductionCost: BigInt(c.maxProductionCost), minMotherlode: c.minMotherlode, maxMotherlode: c.maxMotherlode, splitTiles: c.splitTiles, soloTiles: c.soloTiles, buffer: BigInt(c.buffer) },
        });
        if (p.executor === PublicKey.default.toBase58()) assertSameIx(automateCloseIx(P(p.signer)), f); // teardown helper is exact too
        break;
      }
      default: throw new Error("unknown kind " + f.kind);
    }
    const res = compareIx(built, f);
    tally[f.kind] ??= { exact: 0, compatible: 0 }; tally[f.kind][res]++;
    report.push(`  ${res === "exact" ? "EXACT     " : "COMPATIBLE"} ${f.kind.padEnd(16)} ${f.signature}  data=${f.dataHex.length / 2}B accounts=${f.accounts.length}`);
  });
}

const acct = (prefix: string) => fx.accounts.filter((a: any) => a.name.startsWith(prefix)).map((a: any) => ({ ...a, data: Buffer.from(a.dataBase64, "base64") }));

test("PDAs match canonical mainnet addresses", () => {
  assert.equal(boardPda().toBase58(), BOARD_ADDRESS.toBase58());
  assert.equal(configPda().toBase58(), CONFIG_ADDRESS.toBase58());
  assert.equal(treasuryPda().toBase58(), TREASURY_ADDRESS.toBase58());
  assert.equal(entropyVarPda(0).toBase58(), VAR_ADDRESS.toBase58());
});

test("Board/Config/Treasury decode from real accounts", () => {
  const [board] = acct("board"); const b = decodeBoard(board.data);
  assert.equal(board.data.length, 40); assert.equal(board.owner, ORE_PROGRAM_ID.toBase58());
  assert.ok(b.roundId > 400000n);
  assert.ok(b.endSlot === 18446744073709551615n || b.endSlot - b.startSlot === 240n, "240-slot round (or waiting for first deploy)");
  const c = decodeConfig(acct("config")[0].data);
  assert.equal(c.protocol.roundSlots, 240n); assert.equal(c.protocol.intermissionSlots, 48n);
  // Note: config.protocol.entropy_var_address differs from consts::VAR_ADDRESS on mainnet; deploy.rs checks VAR_ADDRESS.
  const t = decodeTreasury(acct("treasury")[0].data); assert.ok(t.motherlode > 0n);
  report.push(`  OK Board round=${b.roundId} start=${b.startSlot} end=${b.endSlot}; Config round_slots=240 intermission=48; Treasury motherlode=${Number(t.motherlode) / 1e11} ORE`);
});

test("Round decode + PDA + winning square from real settled rounds", () => {
  for (const r of acct("round")) {
    assert.equal(r.data.length, 952);
    const round = decodeRound(r.data);
    assert.equal(roundPda(round.id).toBase58(), r.address, "round PDA derivation");
    const total = round.deployed.reduce((a, x) => a + x, 0n);
    if (r.name.startsWith("roundSettled")) {
      const rng = roundRng(round.slotHash); assert.ok(rng !== null, "settled round has entropy");
      const win = winningSquare(rng!);
      assert.ok(win >= 0 && win < 25);
      assert.ok(round.totalReturnedSol > 0n && round.totalReturnedSol < total);
      report.push(`  OK Round ${round.id}: ${Number(total) / 1e9} SOL, ${round.totalMiners} miners, winning square ${win} (${squareToCoord(win)}), motherlode=${didHitMotherlode(rng!)}, split=${round.topMiner.toBase58().startsWith("SpLiT")}`);
    }
  }
});

test("Miner and Automation decode + PDA from real accounts", () => {
  for (const m of acct("miner:")) {
    const miner = decodeMiner(m.data); assert.equal(m.data.length, 752);
    assert.equal(miner.authority.toBase58(), m.name.split(":")[1]);
    assert.equal(minerPda(miner.authority).toBase58(), m.address);
    assert.ok(miner.checkpointId <= miner.roundId);
  }
  for (const a of acct("automation:")) {
    const au = decodeAutomation(a.data); assert.equal(a.data.length, 160);
    assert.equal(au.authority.toBase58(), a.name.split(":")[1]);
    assert.equal(automationPda(au.authority).toBase58(), a.address);
    assert.ok(au.strategy <= 3n);
    report.push(`  OK Automation ${a.address.slice(0, 8)}… strategy=${au.strategy} amount=${au.amount} balance=${au.balance} executor=${au.executor.toBase58().slice(0, 8)}…`);
  }
});

test("coordinates <-> squares", () => {
  assert.equal(coordToSquare("A1"), 0); assert.equal(coordToSquare("A5"), 4); assert.equal(coordToSquare("B1"), 5); assert.equal(coordToSquare("E5"), 24);
  for (let i = 0; i < 25; i++) assert.equal(coordToSquare(squareToCoord(i)), i);
  assert.equal(squaresToMask([0, 24]), 0x1000001);
  assert.deepEqual(maskToSquares(0x1000001), [0, 24]);
});
