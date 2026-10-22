// TXN-001 audit: mainnet simulateTransaction (sigVerify off, replaceRecentBlockhash, NOTHING SENT) of every transaction the
// NUGSHIP 1.1.0 client asks the player's wallet to sign, built in the client's exact shape (legacy tx, fee payer = wallet,
// same instruction lists as LiveMining.cs / LiveTx.cs), for a funded wallet and a brand-new empty wallet.
import { Connection, Keypair, PublicKey, Transaction, TransactionInstruction, ComputeBudgetProgram } from "@solana/web3.js";
import { automateSessionIx, automateCloseIx, checkpointIx, claimSolIx, claimOreIx } from "../src/ore/instructions.js";
import { associatedTokenAddress, minerPda, automationPda } from "../src/ore/pda.js";
import { decodeMiner } from "../src/ore/accounts.js";
import { MINT_ADDRESS, ASSOCIATED_TOKEN_PROGRAM_ID, TOKEN_PROGRAM_ID, SYSTEM_PROGRAM_ID } from "../src/ore/constants.js";
import { stakeDeposit, stakeWithdraw, stakeClaim } from "./sim-stake-lib.js";
const c = new Connection(process.env.RPC_URL ?? "https://api.mainnet-beta.solana.com", "confirmed");
const EXEC = new PublicKey(process.env.EXECUTOR ?? Keypair.generate().publicKey.toBase58());
const STAKE = 1_000_000n, FEE = 20_000n, MAX = 2n ** 64n - 1n;
const W = (pubkey: PublicKey, isSigner = false) => ({ pubkey, isSigner, isWritable: true }), R = (pubkey: PublicKey) => ({ pubkey, isSigner: false, isWritable: false });
const ataIx = (me: PublicKey) => new TransactionInstruction({ programId: ASSOCIATED_TOKEN_PROGRAM_ID, data: Buffer.from([1]), keys: [W(me, true), W(associatedTokenAddress(me)), R(me), R(MINT_ADDRESS), R(SYSTEM_PROGRAM_ID), R(TOKEN_PROGRAM_ID)] });
const cu = () => [ComputeBudgetProgram.setComputeUnitLimit({ units: 200_000 }), ComputeBudgetProgram.setComputeUnitPrice({ microLamports: 20_000 })];
async function sim(label: string, me: PublicKey, ixs: TransactionInstruction[]) {
  const { blockhash } = await c.getLatestBlockhash();
  const tx = new Transaction({ feePayer: me, recentBlockhash: blockhash }).add(...ixs);
  const wire = tx.serialize({ requireAllSignatures: false, verifySignatures: false });
  const r: any = await (c as any)._rpcRequest("simulateTransaction", [wire.toString("base64"), { encoding: "base64", sigVerify: false, replaceRecentBlockhash: true, commitment: "confirmed" }]);
  const v = r.result?.value ?? { err: r.error };
  const progs = [...new Set(ixs.map((i) => i.programId.toBase58().slice(0, 8)))].join(",");
  const writ = tx.compileMessage().accountKeys.filter((_, i) => tx.compileMessage().isAccountWritable(i)).length;
  const hasCu = ixs.some((i) => i.programId.equals(ComputeBudgetProgram.programId));
  console.log(`${v.err ? "FAIL" : "OK  "} | ${label} | err=${JSON.stringify(v.err)} CU=${v.unitsConsumed} | progs=${progs} writable=${writ} cuBudget=${hasCu} size=${wire.length}`);
  (v.logs ?? []).filter((l: string) => /Program log|failed|error|insufficient/i.test(l)).slice(-3).forEach((l: string) => console.log("       " + l));
}
async function run(tag: string, me: PublicKey) {
  const [bal, mi, au] = [await c.getBalance(me), await c.getAccountInfo(minerPda(me)), await c.getAccountInfo(automationPda(me))];
  const m = mi ? decodeMiner(Buffer.from(mi.data)) : null;
  console.log(`\n### ${tag} ${me.toBase58()} bal=${bal} miner=${m ? "yes" : "none"} automation=${au ? "yes" : "none"}`);
  const ck = m && (m as any).checkpointId < (m as any).roundId ? [checkpointIx(me, me, (m as any).roundId)] : [];
  await sim("DEPOSIT first-connect automate(stake 0.001, deposit 0.02) [client: no CU ixs]", me, [automateSessionIx(me, EXEC, STAKE, 20_000_000n, FEE, true)]);
  await sim("TOP UP automate(deposit 0.01)", me, [automateSessionIx(me, EXEC, STAKE, 10_000_000n, FEE, true)]);
  await sim("WITHDRAW automate close [client: no CU ixs]", me, [automateCloseIx(me)]);
  await sim("CLAIM SOL+ORE (client ClaimAll)", me, [...cu(), ...ck, claimSolIx(me), ataIx(me), claimOreIx(me, 10_000n)]);
  await sim("CLAIM ORE only", me, [...cu(), ...ck, ataIx(me), claimOreIx(me, 10_000n)]);
  await sim("STAKE ALL (no miner ORE) ATA+Deposit(MAX)", me, [...cu(), ataIx(me), stakeDeposit(me, MAX)]);
  await sim("STAKE REWARDS ATA+Claim(MAX)", me, [...cu(), ataIx(me), stakeClaim(me, MAX)]);
  await sim("UNSTAKE ATA+Withdraw(MAX)", me, [...cu(), ataIx(me), stakeWithdraw(me, MAX)]);
}
await run("FUNDED", new PublicKey(process.argv[2] ?? "14RMqeviZGaZhYXeRWmQbWj2hUT6p6haKkHAEThmJe69"));
const fm = new PublicKey(process.argv[2] ?? "14RMqeviZGaZhYXeRWmQbWj2hUT6p6haKkHAEThmJe69");
await sim("NEAR-EMPTY: deposit 1 SOL > balance", fm, [automateSessionIx(fm, EXEC, STAKE, 1_000_000_000n, FEE, true)]);
await run("EMPTY", Keypair.generate().publicKey);
