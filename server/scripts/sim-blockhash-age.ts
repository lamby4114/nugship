// TXN-001 round 2: how long does a client-fetched blockhash stay simulatable WITHOUT replaceRecentBlockhash (wallet-style)?
// Builds the real first-deposit bytes once, then re-simulates the SAME bytes as they age. NOTHING SENT.
import { Connection, PublicKey, Transaction, ComputeBudgetProgram } from "@solana/web3.js";
import { automateSessionIx } from "../src/ore/instructions.js";
const c = new Connection(process.env.RPC_URL ?? "https://api.mainnet-beta.solana.com", "confirmed");
const EXEC = new PublicKey("BFhXQCdrJWxoeKHcK56Mr7BAUGWmeCV4LkMdsnFVGR5K"), me = new PublicKey(process.argv[2]);
const s0 = await c.getSlot("confirmed"); const t0 = Date.now();
const { blockhash, lastValidBlockHeight } = await c.getLatestBlockhash("confirmed");
const h0 = await c.getBlockHeight("confirmed");
console.log(`blockhash ${blockhash} height=${h0} lastValid=${lastValidBlockHeight} (+${lastValidBlockHeight - h0} blocks)`);
const tx = new Transaction({ feePayer: me, recentBlockhash: blockhash }).add(ComputeBudgetProgram.setComputeUnitLimit({ units: 200_000 }), ComputeBudgetProgram.setComputeUnitPrice({ microLamports: 20_000 }), automateSessionIx(me, EXEC, 1_000_000n, 5_000_000n, 20_000n, true));
const wire = tx.serialize({ requireAllSignatures: false, verifySignatures: false }).toString("base64");
for (const at of [0, 15, 30, 45, 60, 75, 90]) {
  while (Date.now() - t0 < at * 1000) await new Promise((r) => setTimeout(r, 200));
  const r: any = await (c as any)._rpcRequest("simulateTransaction", [wire, { encoding: "base64", sigVerify: false, replaceRecentBlockhash: false, commitment: "confirmed" }]);
  const s = await c.getSlot("confirmed");
  console.log(`t+${((Date.now() - t0) / 1000).toFixed(0)}s slots+${s - s0} -> ${r.result?.value?.err ? "FAIL " + JSON.stringify(r.result.value.err) : r.error ? "RPCERR " + r.error.message : "OK CU=" + r.result.value.unitsConsumed}`);
}
