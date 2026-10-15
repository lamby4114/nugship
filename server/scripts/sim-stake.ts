// Mainnet simulateTransaction (no sends): official ORE staking program (ore-stake-api 0.3.0, stakecNP3...).
// ClaimORE from the miner into the wallet ATA, then stake Deposit / Claim / Withdraw in the same tx (sim can't chain txs).
import { Connection, PublicKey, TransactionInstruction, TransactionMessage, VersionedTransaction } from "@solana/web3.js";
import { claimOreIx } from "../src/ore/instructions.js";
import { associatedTokenAddress } from "../src/ore/pda.js";
import { MINT_ADDRESS, ASSOCIATED_TOKEN_PROGRAM_ID, TOKEN_PROGRAM_ID, SYSTEM_PROGRAM_ID } from "../src/ore/constants.js";
export const STAKE_PROGRAM = new PublicKey("stakecNP3FpiExZPCgZfqRgumVzi6dNqnfrjwXyTgeH");
const pda = (seeds: Buffer[]) => PublicKey.findProgramAddressSync(seeds, STAKE_PROGRAM)[0];
const stakePda = (a: PublicKey) => pda([Buffer.from("stake"), a.toBuffer()]);
const treasury = pda([Buffer.from("treasury")]), vesting = pda([Buffer.from("vesting")]);
const W = (pubkey: PublicKey, isSigner = false) => ({ pubkey, isSigner, isWritable: true }), R = (pubkey: PublicKey) => ({ pubkey, isSigner: false, isWritable: false });
const u64 = (n: bigint) => { const b = Buffer.alloc(8); b.writeBigUInt64LE(n); return b; };
export function stakeDeposit(me: PublicKey, amount: bigint) {
  const st = stakePda(me);
  return new TransactionInstruction({ programId: STAKE_PROGRAM, data: Buffer.concat([Buffer.from([10]), u64(amount), u64(0n), u64(0n)]), keys: [
    W(me, true), W(me, true), R(MINT_ADDRESS), W(associatedTokenAddress(me)), W(st), W(associatedTokenAddress(st)), W(treasury), W(vesting),
    R(SYSTEM_PROGRAM_ID), R(TOKEN_PROGRAM_ID), R(ASSOCIATED_TOKEN_PROGRAM_ID), R(STAKE_PROGRAM)] });
}
export function stakeWithdraw(me: PublicKey, amount: bigint) {
  const st = stakePda(me);
  return new TransactionInstruction({ programId: STAKE_PROGRAM, data: Buffer.concat([Buffer.from([11]), u64(amount)]), keys: [
    W(me, true), R(MINT_ADDRESS), W(associatedTokenAddress(me)), W(st), W(associatedTokenAddress(st)), W(treasury), W(vesting),
    R(SYSTEM_PROGRAM_ID), R(TOKEN_PROGRAM_ID), R(ASSOCIATED_TOKEN_PROGRAM_ID), R(STAKE_PROGRAM)] });
}
export function stakeClaim(me: PublicKey, amount: bigint) {
  return new TransactionInstruction({ programId: STAKE_PROGRAM, data: Buffer.concat([Buffer.from([12]), u64(amount)]), keys: [
    W(me, true), R(MINT_ADDRESS), W(associatedTokenAddress(me)), W(stakePda(me)), W(treasury), W(associatedTokenAddress(treasury)), W(vesting),
    R(SYSTEM_PROGRAM_ID), R(TOKEN_PROGRAM_ID), R(ASSOCIATED_TOKEN_PROGRAM_ID), R(STAKE_PROGRAM)] });
}
const c = new Connection(process.env.RPC_URL ?? "https://api.mainnet-beta.solana.com", "confirmed");
const me = new PublicKey(process.argv[2] ?? "14RMqeviZGaZhYXeRWmQbWj2hUT6p6haKkHAEThmJe69");
const ata = associatedTokenAddress(me), st = stakePda(me);
const ataIx = new TransactionInstruction({ programId: ASSOCIATED_TOKEN_PROGRAM_ID, data: Buffer.from([1]), keys: [W(me, true), W(ata), R(me), R(MINT_ADDRESS), R(SYSTEM_PROGRAM_ID), R(TOKEN_PROGRAM_ID)] });
const [tInfo, sInfo, prog] = await c.getMultipleAccountsInfo([treasury, st, STAKE_PROGRAM]);
console.log(`stake program executable=${prog?.executable} treasury disc=${tInfo?.data[0]} len=${tInfo?.data.length}; my stake account ${sInfo ? "exists len " + sInfo.data.length : "none"}`);
async function sim(name: string, ixs: TransactionInstruction[]) {
  const { blockhash } = await c.getLatestBlockhash();
  const tx = new VersionedTransaction(new TransactionMessage({ payerKey: me, recentBlockhash: blockhash, instructions: ixs }).compileToV0Message());
  const r = await c.simulateTransaction(tx, { sigVerify: false, replaceRecentBlockhash: true, accounts: { encoding: "base64", addresses: [st.toBase58(), ata.toBase58()] } });
  console.log(`== ${name}: ${r.value.err ? "FAIL " + JSON.stringify(r.value.err) : "OK"} CU=${r.value.unitsConsumed}`);
  (r.value.logs ?? []).filter((l) => /Program log|failed|error/i.test(l)).slice(-6).forEach((l) => console.log("   " + l));
  const sa = r.value.accounts?.[0]; if (sa && sa.lamports > 0) { const d = Buffer.from(sa.data[0], "base64"); console.log(`   stake acct: disc ${d[0]} len ${d.length} authority ${new PublicKey(d.subarray(8, 40)).toBase58().slice(0, 6)} balance ${d.readBigUInt64LE(40)} rewards ${d.readBigUInt64LE(104)} compoundFee ${d.readBigUInt64LE(48)}`); }
  const aa = r.value.accounts?.[1]; if (aa && aa.lamports > 0) console.log(`   wallet ORE ATA amount ${Buffer.from(aa.data[0], "base64").readBigUInt64LE(64)}`);
  return r.value;
}
const MAX = 2n ** 64n - 1n;
// exactly the three Treasure-page buttons:
await sim("STAKE ALL  = ATA + ClaimORE(all) + Deposit(MAX)", [ataIx, claimOreIx(me, 10_000n), stakeDeposit(me, MAX)]);
await sim("STAKE ALL with nothing in miner = ATA + Deposit(MAX)", [ataIx, stakeDeposit(me, MAX)]);
await sim("CLAIM STAKE REWARDS = ATA + Claim(MAX)", [ataIx, stakeClaim(me, MAX)]);
await sim("UNSTAKE ALL = ATA + Withdraw(MAX)", [ataIx, stakeWithdraw(me, MAX)]);
