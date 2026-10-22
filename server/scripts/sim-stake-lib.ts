import { Connection, PublicKey, TransactionInstruction, TransactionMessage, VersionedTransaction } from "@solana/web3.js";
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
