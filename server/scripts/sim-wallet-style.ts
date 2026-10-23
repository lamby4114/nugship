// TXN-001 round 2: simulate EXACTLY like a wallet does: the exact bytes (fresh blockhash, NO replaceRecentBlockhash),
// for fresh funded wallets (no miner/automation) and the real executor. NOTHING SENT.
import { Connection, PublicKey, Transaction, ComputeBudgetProgram } from "@solana/web3.js";
import { automateSessionIx } from "../src/ore/instructions.js";
const c = new Connection(process.env.RPC_URL ?? "https://api.mainnet-beta.solana.com", "confirmed");
const EXEC = new PublicKey(process.env.EXECUTOR ?? "BFhXQCdrJWxoeKHcK56Mr7BAUGWmeCV4LkMdsnFVGR5K");
const cu = () => [ComputeBudgetProgram.setComputeUnitLimit({ units: 200_000 }), ComputeBudgetProgram.setComputeUnitPrice({ microLamports: 20_000 })];
async function sim(label: string, me: PublicKey, ixs: any[], replace = false) {
  const { blockhash } = await c.getLatestBlockhash("confirmed");
  const tx = new Transaction({ feePayer: me, recentBlockhash: blockhash }).add(...ixs);
  const wire = tx.serialize({ requireAllSignatures: false, verifySignatures: false });
  const r: any = await (c as any)._rpcRequest("simulateTransaction", [wire.toString("base64"), { encoding: "base64", sigVerify: false, replaceRecentBlockhash: replace, commitment: "confirmed" }]);
  const v = r.result?.value ?? { err: r.error };
  console.log(`${v.err ? "FAIL" : "OK  "} | ${label} | err=${JSON.stringify(v.err)} CU=${v.unitsConsumed}`);
  (v.logs ?? []).slice(-6).forEach((l: string) => console.log("       " + l));
}
for (const a of process.argv.slice(2)) {
  const me = new PublicKey(a); const bal = await c.getBalance(me);
  console.log(`\n### ${a} bal=${bal}`);
  for (const dep of [5_000_000n, 10_000_000n, 20_000_000n]) await sim(`first deposit ${dep} stake 0.001`, me, [...cu(), automateSessionIx(me, EXEC, 1_000_000n, dep, 20_000n, true)]);
}
