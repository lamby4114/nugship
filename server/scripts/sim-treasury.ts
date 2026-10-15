// Mainnet simulateTransaction (no sends) of the Treasure page txs for a wallet: ClaimSOL, ClaimORE (+ATA idempotent),
// Automate close (withdraw deposit). Also checks the client's live refining formula against the program's own post-state.
import { ComputeBudgetProgram, Connection, PublicKey, TransactionInstruction, TransactionMessage, VersionedTransaction } from "@solana/web3.js";
import { claimSolIx, claimOreIx, automateCloseIx, checkpointIx } from "../src/ore/instructions.js";
import { minerPda, automationPda, treasuryPda, associatedTokenAddress } from "../src/ore/pda.js";
import { decodeMiner, decodeTreasury, decodeAutomation } from "../src/ore/accounts.js";
import { MINT_ADDRESS, ASSOCIATED_TOKEN_PROGRAM_ID, TOKEN_PROGRAM_ID, SYSTEM_PROGRAM_ID } from "../src/ore/constants.js";
const c = new Connection(process.env.RPC_URL ?? "https://api.mainnet-beta.solana.com", "confirmed");
const me = new PublicKey(process.argv[2] ?? "14RMqeviZGaZhYXeRWmQbWj2hUT6p6haKkHAEThmJe69");
const ata = associatedTokenAddress(me);
const ataIx = new TransactionInstruction({ programId: ASSOCIATED_TOKEN_PROGRAM_ID, data: Buffer.from([1]), keys: [
  { pubkey: me, isSigner: true, isWritable: true }, { pubkey: ata, isSigner: false, isWritable: true }, { pubkey: me, isSigner: false, isWritable: false },
  { pubkey: MINT_ADDRESS, isSigner: false, isWritable: false }, { pubkey: SYSTEM_PROGRAM_ID, isSigner: false, isWritable: false }, { pubkey: TOKEN_PROGRAM_ID, isSigner: false, isWritable: false }] });
const i80 = (b: Buffer) => { let x = 0n; for (let i = 15; i >= 0; i--) x = (x << 8n) | BigInt(b[i]); if (x >> 127n) x -= 1n << 128n; return x; };
const [mi, tr, au] = await c.getMultipleAccountsInfo([minerPda(me), treasuryPda(), automationPda(me)]);
const m = decodeMiner(Buffer.from(mi!.data)), t = decodeTreasury(Buffer.from(tr!.data));
const pending = ((i80(t.minerRewardsFactor) - i80(m.rewardsFactor)) * m.rewardsOre) >> 48n;
console.log(`miner rewardsSol ${m.rewardsSol} rewardsOre ${m.rewardsOre} refinedOre ${m.refinedOre} predicted pending refine ${pending}`);
if (au) { const a = decodeAutomation(Buffer.from(au.data)); console.log(`automation balance ${a.balance}`); }
async function sim(name: string, ixs: TransactionInstruction[], watch: PublicKey[]) {
  const { blockhash } = await c.getLatestBlockhash();
  const tx = new VersionedTransaction(new TransactionMessage({ payerKey: me, recentBlockhash: blockhash, instructions: ixs }).compileToV0Message());
  const r = await c.simulateTransaction(tx, { sigVerify: false, replaceRecentBlockhash: true, accounts: { encoding: "base64", addresses: watch.map(String) } });
  console.log(`== ${name}: ${r.value.err ? "FAIL " + JSON.stringify(r.value.err) : "OK"} CU=${r.value.unitsConsumed}`);
  (r.value.logs ?? []).filter((l) => /Program log|failed/.test(l)).slice(-4).forEach((l) => console.log("   " + l));
  return r.value;
}
const pre = await c.getBalance(me);
// exactly as the client's LiveTx.ClaimAll builds it: CU limit 200k, price 20k µL, Checkpoint(miner.roundId) if due, then claims
const cb = [ComputeBudgetProgram.setComputeUnitLimit({ units: 200_000 }), ComputeBudgetProgram.setComputeUnitPrice({ microLamports: 20_000 })];
const ck = m.checkpointId !== m.roundId ? [checkpointIx(me, me, m.roundId)] : [];
console.log("client adds checkpoint:", ck.length > 0, "round", m.roundId);
const s1 = await sim("ClaimSOL (client layout)", [...cb, ...ck, claimSolIx(me)], [me]);
console.log("   wallet delta", (s1.accounts?.[0]?.lamports ?? pre) - pre);
const s2 = await sim("ClaimORE (client layout)", [...cb, ...ck, ataIx, claimOreIx(me, 10_000n)], [minerPda(me), ata]);
if (s2.accounts?.[0]) { const pm = decodeMiner(Buffer.from(s2.accounts[0].data[0], "base64")); console.log(`   post: rewardsOre ${pm.rewardsOre} refinedOre ${pm.refinedOre}; ATA ore ${s2.accounts[1] ? Buffer.from(s2.accounts[1].data[0], "base64").readBigUInt64LE(64) : "?"} (expected ≈ refined ${m.refinedOre}+pending ${pending} + 90% of ${m.rewardsOre})`); }
if (au) { const s3 = await sim("Withdraw deposit (automate close)", [automateCloseIx(me)], [me, automationPda(me)]); console.log("   wallet delta", (s3.accounts?.[0]?.lamports ?? pre) - pre, "automation lamports after", s3.accounts?.[1]?.lamports ?? 0); }
