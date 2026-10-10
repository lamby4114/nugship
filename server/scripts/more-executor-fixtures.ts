import { PublicKey } from "@solana/web3.js";
import fs from "node:fs";
import bs58 from "bs58";
import { ReadOnlyRpc } from "../src/chain/rpc.js";
import { ORE_PROGRAM_ID, automationPda, decodeOreIxData, decodeEvent } from "../src/ore/index.js";
const rpc = new ReadOnlyRpc({ minIntervalMs: 300 });
const OUT = new URL("../test/fixtures/mainnet.json", import.meta.url).pathname;
const fx = JSON.parse(fs.readFileSync(OUT, "utf8"));
const have = new Set(fx.instructions.map((f: any) => f.signature + ":" + f.ixIndex));
let added = 0;
for (const a of fx.accounts.filter((x: any) => x.name.startsWith("automation:")).map((x: any) => x.name.split(":")[1])) {
  const sigs = await rpc.getSignatures(automationPda(new PublicKey(a)), 20);
  for (const s of sigs) {
    if (s.err || added >= 4) continue;
    const tx = await rpc.getTransaction(s.signature); if (!tx) continue;
    const msg = tx.transaction.message; const keys = msg.getAccountKeys({ accountKeysFromLookups: tx.meta?.loadedAddresses ?? undefined });
    msg.compiledInstructions.forEach((ix, ixIndex) => {
      if (!keys.get(ix.programIdIndex)!.equals(ORE_PROGRAM_ID)) return;
      const data = Buffer.from(ix.data); const dec = decodeOreIxData(data);
      if (dec.kind !== "Deploy" || have.has(s.signature + ":" + ixIndex) || added >= 4) return;
      const accounts = ix.accountKeyIndexes.map((k) => ({ pubkey: keys.get(k)!.toBase58(), isSigner: msg.isAccountSigner(k), isWritable: msg.isAccountWritable(k) }));
      if (accounts[0].pubkey === accounts[1].pubkey) return;
      const ev = (tx.meta?.innerInstructions ?? []).filter((g) => g.index === ixIndex).flatMap((g) => g.instructions)
        .map((ii: any) => decodeOreIxData(Buffer.from(bs58.decode(ii.data)))).flatMap((d) => d.kind === "Log" ? [decodeEvent(d.payload)] : []).find((e: any) => e?.type === "Deploy")?.ev as any;
      if (!ev) return;
      fx.instructions.push({ kind: "Deploy/executor", signature: s.signature, slot: tx.slot, blockTime: tx.blockTime, ixIndex, err: null, programId: ORE_PROGRAM_ID.toBase58(), dataHex: data.toString("hex"), accounts,
        params: { signer: accounts[0].pubkey, authority: accounts[1].pubkey, amount: dec.amount.toString(), roundId: ev.roundId.toString(), mask: dec.mask, roundIdSource: "DeployEvent",
          event: { authority: ev.authority.toBase58(), signer: ev.signer.toBase58(), amount: ev.amount.toString(), mask: ev.mask, roundId: ev.roundId.toString(), strategy: ev.strategy.toString() } } });
      added++; console.error("+ exec deploy", s.signature.slice(0, 10), "strategy", ev.strategy);
    });
  }
}
fs.writeFileSync(OUT, JSON.stringify(fx, null, 1)); console.error("added", added);
