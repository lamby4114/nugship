// ORE Automation executor: gating, exact squares, fee/priority math, never-after-round-end, simulate-before-send.
import test from "node:test";
import assert from "node:assert/strict";
import { Keypair, PublicKey } from "@solana/web3.js";
import { eligible, volleyIxs, maxMicroLamports, clampPriority, OreExecutor, EXECUTOR_FEE_LAMPORTS, BASE_FEE_LAMPORTS, CU_LIMIT, loadExecutorKey } from "../src/chain/executor.js";
import { decodeOreIxData } from "../src/ore/instructions.js";
import { ORE_PROGRAM_ID, AutomationStrategy, OreAccount } from "../src/ore/constants.js";
import { automationPda, minerPda } from "../src/ore/pda.js";

const exec = Keypair.generate(), player = Keypair.generate().publicKey;
const auto = (o: any = {}) => ({ amount: 1_000_000n, authority: player, balance: 20_000_000n, executor: exec.publicKey, fee: 20_000n, strategy: BigInt(AutomationStrategy.Discretionary), mask: 0n, reload: 1n, totalSolSpent: 0n, totalOreEarned: 0n, conditions: {} as any, ...o });
const miner = (o: any = {}) => ({ roundId: 100n, checkpointId: 100n, checkpointFee: 10_000n, ...o }) as any;
const M = (1 << 7) | (1 << 12);

test("eligibility gates", () => {
  assert.equal(eligible(exec.publicKey, null, null, M, 1_000_000n).ok, false);
  assert.equal((eligible(exec.publicKey, auto({ executor: Keypair.generate().publicKey }), null, M, 1_000_000n) as any).reason, "other_executor");
  assert.equal((eligible(exec.publicKey, auto({ strategy: BigInt(AutomationStrategy.Random) }), null, M, 1_000_000n) as any).reason, "strategy");
  assert.equal((eligible(exec.publicKey, auto({ fee: 5_000n }), null, M, 1_000_000n) as any).reason, "fee_too_low");
  assert.equal((eligible(exec.publicKey, auto({ amount: 500_000n }), null, M, 1_000_000n) as any).reason, "amount_below_stake");
  assert.equal((eligible(exec.publicKey, auto({ balance: 2_010_000n }), null, M, 1_000_000n) as any).reason, "balance_low");
  assert.equal(eligible(exec.publicKey, auto({ balance: 2_020_000n }), null, M, 1_000_000n).ok, true);
  assert.equal((eligible(exec.publicKey, auto(), null, 0, 1_000_000n) as any).reason, "empty");
});

test("volley tx = exactly the locked squares, executor is the only signer, checkpoint only when due", () => {
  const ixs = volleyIxs(exec.publicKey, player, miner(), 101n, M, 1_000_000n, 1000n);
  const ore = ixs.filter((i) => i.programId.equals(ORE_PROGRAM_ID));
  assert.equal(ore.length, 1);
  const d = decodeOreIxData(Buffer.from(ore[0].data)) as any;
  assert.equal(d.kind, "Deploy"); assert.equal(d.mask, M); assert.equal(d.amount, 1_000_000n);
  assert.deepEqual(ixs.flatMap((i) => i.keys.filter((k) => k.isSigner).map((k) => k.pubkey.toBase58())), [exec.publicKey.toBase58()]);
  assert.ok(ore[0].keys[1].pubkey.equals(player)); assert.ok(ore[0].keys[2].pubkey.equals(automationPda(player))); assert.ok(ore[0].keys[5].pubkey.equals(minerPda(player)));
  const due = volleyIxs(exec.publicKey, player, miner({ roundId: 100n, checkpointId: 99n }), 101n, M, 1_000_000n, 0n).filter((i) => i.programId.equals(ORE_PROGRAM_ID));
  assert.deepEqual(due.map((i) => (decodeOreIxData(Buffer.from(i.data)) as any).kind), ["Checkpoint", "Deploy"]);
  assert.equal(volleyIxs(exec.publicKey, player, null, 101n, M, 1n, 0n).filter((i) => i.programId.equals(ORE_PROGRAM_ID)).length, 1);
});

test("priority fee capped so base + priority < executor fee (server never pays out of pocket)", () => {
  const cap = maxMicroLamports();
  assert.ok(BASE_FEE_LAMPORTS + (cap * BigInt(CU_LIMIT)) / 1_000_000n < EXECUTOR_FEE_LAMPORTS);
  assert.equal(clampPriority(10n ** 12n), cap); assert.equal(clampPriority(5n), 5n);
});

test("EXECUTOR_KEY parsing", () => {
  assert.equal(loadExecutorKey({} as any), null);
  const k = Keypair.generate();
  assert.ok(loadExecutorKey({ EXECUTOR_KEY: JSON.stringify(Array.from(k.secretKey)) } as any)!.publicKey.equals(k.publicKey));
});

function automationAccount(a: any): Buffer { // 160-byte Automation layout (see accounts.ts)
  const b = Buffer.alloc(160); b.writeBigUInt64LE(BigInt(OreAccount.Automation), 0);
  b.writeBigUInt64LE(a.amount, 8); a.authority.toBuffer().copy(b, 16); b.writeBigUInt64LE(a.balance, 48); a.executor.toBuffer().copy(b, 56);
  b.writeBigUInt64LE(a.fee, 88); b.writeBigUInt64LE(a.strategy, 96); b.writeBigUInt64LE(0xffffffffffffffffn, 136); b.writeUInt16LE(0xffff, 146); return b;
}
function fakeConn(o: { slot: number; simErr?: any; auto?: any }) {
  const calls: string[] = [];
  return { calls, conn: {
    getMultipleAccountsInfo: async () => [o.auto ? { data: automationAccount(o.auto) } : null, null],
    getRecentPrioritizationFees: async () => [{ prioritizationFee: 10 ** 9 }],
    getSlot: async () => o.slot, getBlockHeight: async () => 1,
    getLatestBlockhash: async () => ({ blockhash: "11111111111111111111111111111111", lastValidBlockHeight: 1000 }),
    simulateTransaction: async () => { calls.push("sim"); return { value: { err: o.simErr ?? null, logs: [] } }; },
    sendRawTransaction: async () => { calls.push("send"); return "SIG"; },
    getSignatureStatuses: async () => ({ value: [{ err: null, confirmationStatus: "confirmed" }] }),
  } as any };
}

test("place: simulate before send; sim failure never sends; never sends at/after round end", async () => {
  let f = fakeConn({ slot: 100, auto: auto() });
  let r = await new OreExecutor(exec, f.conn).place(player.toBase58(), 5, M, 1_000_000n, 200);
  assert.equal(r.placed, true); assert.equal(r.sig, "SIG"); assert.equal(r.lamports, "2000000"); assert.deepEqual(f.calls.slice(0, 2), ["sim", "send"]);
  f = fakeConn({ slot: 100, auto: auto(), simErr: { InstructionError: [2, "Custom"] } });
  r = await new OreExecutor(exec, f.conn).place(player.toBase58(), 5, M, 1_000_000n, 200);
  assert.equal(r.placed, false); assert.ok(!f.calls.includes("send"));
  f = fakeConn({ slot: 197, auto: auto() });
  r = await new OreExecutor(exec, f.conn).place(player.toBase58(), 5, M, 1_000_000n, 200);
  assert.equal(r.placed, false); assert.equal(f.calls.length, 0);
  f = fakeConn({ slot: 100, auto: null });
  r = await new OreExecutor(exec, f.conn).place(player.toBase58(), 5, M, 1_000_000n, 200);
  assert.equal(r.reason, "no_automation"); assert.equal(f.calls.length, 0);
});
