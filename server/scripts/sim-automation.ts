// Mainnet simulateTransaction (sigVerify off, nothing sent) of the ORE Automation flow for NUGSHIP:
//  S1 automate(deposit) + executor checkpoint/deploy(chosen squares) in one tx (sim can't chain txs, so state flows within one tx)
//  S2 same + withdraw/close  -> balance+rent back to the player
//  S3 executor deploy WITHOUT automation -> must fail (executor cannot spend player funds it wasn't given)
//  S4 executor-only volley tx exactly as the server builds it, against a pre-existing automation  (only if one exists)
import { Connection, Keypair, PublicKey, SystemProgram, TransactionMessage, VersionedTransaction, LAMPORTS_PER_SOL } from "@solana/web3.js";
import { automateSessionIx, automateCloseIx } from "../src/ore/instructions.js";
import { automationPda, boardPda, minerPda } from "../src/ore/pda.js";
import { decodeAutomation, decodeBoard, decodeMiner } from "../src/ore/accounts.js";
import { volleyIxs, EXECUTOR_FEE_LAMPORTS, maxMicroLamports } from "../src/chain/executor.js";
const conn = new Connection(process.env.RPC_URL ?? "https://api.mainnet-beta.solana.com", "confirmed");
const player = new PublicKey(process.argv[2] ?? "14RMqeviZGaZhYXeRWmQbWj2hUT6p6haKkHAEThmJe69");
const exec = Keypair.generate().publicKey;                         // throwaway executor
const STAKE = 1_000_000n, CAP = 20_000_000n, MASK = (1 << 7) | (1 << 12);
const board = decodeBoard(Buffer.from((await conn.getAccountInfo(boardPda()))!.data));
const slot = await conn.getSlot();
const mi = await conn.getAccountInfo(minerPda(player)); const miner = mi ? decodeMiner(Buffer.from(mi.data)) : null;
const bal = await conn.getBalance(player);
console.log(`player ${player.toBase58()} balance ${bal / LAMPORTS_PER_SOL} SOL; miner ${miner ? `round ${miner.roundId} ckpt ${miner.checkpointId} fee ${miner.checkpointFee}` : "none"}`);
console.log(`board round ${board.roundId} slots ${board.startSlot}..${board.endSlot} now ${slot}; executor(throwaway) ${exec.toBase58()}; priority cap ${maxMicroLamports()} µL/CU`);
const watch = [automationPda(player), exec, player];
async function sim(name: string, ixs: any[], payer = player) {
  const { blockhash } = await conn.getLatestBlockhash();
  const tx = new VersionedTransaction(new TransactionMessage({ payerKey: payer, recentBlockhash: blockhash, instructions: ixs }).compileToV0Message());
  const r = await conn.simulateTransaction(tx, { sigVerify: false, replaceRecentBlockhash: true, accounts: { encoding: "base64", addresses: watch.map(String) } });
  const v = r.value; const acc = v.accounts ?? [];
  const auto = acc[0] && acc[0].data && acc[0].lamports > 0 ? decodeAutomation(Buffer.from(acc[0].data[0], "base64")) : null;
  console.log(`\n== ${name}: ${v.err ? "FAIL " + JSON.stringify(v.err) : "OK"}  CU=${v.unitsConsumed}`);
  if (auto) console.log(`   automation: balance ${auto.balance} amount ${auto.amount} fee ${auto.fee} strategy ${auto.strategy} executor ${auto.executor.toBase58().slice(0, 6)} spent ${auto.totalSolSpent}`);
  else console.log("   automation: (closed / none)");
  console.log(`   executor lamports after: ${acc[1]?.lamports ?? 0}; player lamports after: ${acc[2]?.lamports} (delta ${(acc[2]?.lamports ?? bal) - bal})`);
  (v.logs ?? []).filter((l) => /Program log|failed|error/i.test(l)).slice(-8).forEach((l) => console.log("   " + l));
  return v;
}
const fund = SystemProgram.transfer({ fromPubkey: player, toPubkey: exec, lamports: 1_000_000 }); // SIM ONLY: give the throwaway executor a rent-exempt float
const setup = automateSessionIx(player, exec, STAKE, CAP, EXECUTOR_FEE_LAMPORTS, true);
const volley = volleyIxs(exec, player, miner, board.roundId, MASK, STAKE, 0n);
await sim("S1 deposit + executor volley (squares 7,12 @0.001)", [fund, setup, ...volley]);
await sim("S2 deposit + volley + WITHDRAW(close)", [fund, setup, ...volley, automateCloseIx(player)]);
await sim("S3 executor volley with NO automation (must fail)", [fund, ...volley]);
const fresh = await conn.getAccountInfo(automationPda(player));
console.log(`\nexisting automation for player: ${fresh ? "yes" : "no"} (S4 executor-only tx needs one; covered by S1 in-tx)`);
