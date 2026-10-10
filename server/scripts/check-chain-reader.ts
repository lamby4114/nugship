// READ-ONLY check of the referee's on-chain deploy reader against live mainnet miners:
// for active miners, the Miner-account path and the DeployEvent-log path must agree on the squares deployed.
import { PublicKey } from "@solana/web3.js";
import fs from "node:fs";
import { ReadOnlyRpc } from "../src/chain/rpc.js";
import { ChainDeployReader } from "../src/chain/deployReader.js";
import { BOARD_ADDRESS, decodeBoard, decodeMiner, maskToSquares, minerPda, squareToCoord } from "../src/ore/index.js";

const rpc = new ReadOnlyRpc({ minIntervalMs: 300 });
const reader = new ChainDeployReader(rpc);
const fx = JSON.parse(fs.readFileSync(new URL("../test/fixtures/mainnet.json", import.meta.url), "utf8"));
const candidates: string[] = [...new Set<string>(fx.instructions.filter((f: any) => f.kind.startsWith("Deploy")).map((f: any) => f.params.authority))];
const board = decodeBoard((await rpc.getAccount(BOARD_ADDRESS))!);
const C = Number(board.roundId);
console.log(`board round ${C}; checking ${candidates.length} real miner authorities`);
let agree = 0, compared = 0;
for (const a of candidates.slice(0, 5)) {
  const md = await rpc.getAccount(minerPda(new PublicKey(a)));
  if (!md) continue;
  const m = decodeMiner(md);
  const rid = Number(m.roundId);
  const viaMiner = (await reader.read(rid, [a])).get(a);
  const viaEvents = await (reader as any).fromEvents(rid, a);
  const fmt = (r: any) => (r ? `${r.source}: ${maskToSquares(r.mask).map(squareToCoord).join(",") || "-"} @${r.perSquare.find((x: bigint) => x > 0n) ?? 0n} lamports` : "none");
  console.log(`${a.slice(0, 8)}… round ${rid}${rid === C ? " (current)" : ""}\n   miner  ${fmt(viaMiner)}\n   events ${fmt(viaEvents)}${viaEvents?.signers ? " signer " + viaEvents.signers.map((s: string) => s.slice(0, 8)).join(",") : ""}`);
  if (viaMiner && viaEvents) { compared++; if (viaMiner.mask === viaEvents.mask && viaMiner.perSquare.every((v: bigint, i: number) => v === viaEvents.perSquare[i])) agree++; }
  // older round: miner has moved on -> reader must use the event fallback
  const older = (await reader.read(rid - 1, [a])).get(a);
  console.log(`   round ${rid - 1} (miner moved on) -> ${fmt(older)}`);
}
console.log(`\nminer-account vs DeployEvent agreement: ${agree}/${compared}; rpc calls ${rpc.stats.calls}`);
process.exit(compared > 0 && agree === compared ? 0 : 1);
