// READ-ONLY: pulls real mainnet ORE transactions + accounts into test/fixtures/mainnet.json
// for byte-for-byte encoding tests (TS and C#). Never signs or sends anything.
import { PublicKey, VersionedTransactionResponse } from "@solana/web3.js";
import bs58 from "bs58";
import fs from "node:fs";
import path from "node:path";
import { ReadOnlyRpc, sleep } from "../src/chain/rpc.js";
import {
  BOARD_ADDRESS, CONFIG_ADDRESS, ORE_PROGRAM_ID, TREASURY_ADDRESS, VAR_ADDRESS, automationPda, decodeBoard, decodeEvent, decodeOreIxData,
  minerPda, roundPda, treasuryTokensAddress,
} from "../src/ore/index.js";

const rpc = new ReadOnlyRpc({ minIntervalMs: 260 });
const OUT = path.join(path.dirname(new URL(import.meta.url).pathname), "../test/fixtures/mainnet.json");
const WANT: Record<string, number> = { "Deploy/manual": 3, "Deploy/executor": 3, Checkpoint: 4, ClaimSOL: 2, ClaimORE: 2, Automate: 3 };
const got: Record<string, number> = {};
const fixtures: any[] = [];
const seen = new Set<string>();
const authoritiesWithExecutor = new Set<string>();
const minersSeen = new Set<string>();

function findRoundId(roundKey: PublicKey, hint: bigint, span = 4000): bigint | null {
  for (let d = 0n; d < BigInt(span); d++) {
    for (const id of [hint - d, hint + d]) if (id >= 0n && roundPda(id).equals(roundKey)) return id;
  }
  return null;
}

function extract(sig: string, tx: VersionedTransactionResponse, boardRound: bigint) {
  const msg = tx.transaction.message;
  const keys = msg.getAccountKeys({ accountKeysFromLookups: tx.meta?.loadedAddresses ?? undefined });
  const logs = tx.meta?.logMessages ?? [];
  const inner = tx.meta?.innerInstructions ?? [];
  msg.compiledInstructions.forEach((ix, ixIndex) => {
    if (!keys.get(ix.programIdIndex)!.equals(ORE_PROGRAM_ID)) return;
    const data = Buffer.from(ix.data);
    const dec = decodeOreIxData(data);
    const accounts = ix.accountKeyIndexes.map((k) => ({
      pubkey: keys.get(k)!.toBase58(), isSigner: msg.isAccountSigner(k), isWritable: msg.isAccountWritable(k),
    }));
    const events = inner.filter((g) => g.index === ixIndex).flatMap((g) => g.instructions)
      .filter((ii: any) => keys.get(ii.programIdIndex)!.equals(ORE_PROGRAM_ID))
      .map((ii: any) => decodeOreIxData(Buffer.from(bs58.decode(ii.data))))
      .flatMap((d) => (d.kind === "Log" ? [decodeEvent(d.payload)] : [])).filter(Boolean) as any[];
    let kind = dec.kind as string, params: any = {};
    if (dec.kind === "Deploy") {
      const signer = accounts[0].pubkey, authority = accounts[1].pubkey;
      kind = signer === authority ? "Deploy/manual" : "Deploy/executor";
      const ev = events.find((e) => e.type === "Deploy")?.ev;
      const roundId = ev ? ev.roundId : findRoundId(new PublicKey(accounts[6].pubkey), boardRound);
      if (roundId == null) return;
      params = { signer, authority, amount: dec.amount.toString(), roundId: roundId.toString(), mask: dec.mask, roundIdSource: ev ? "DeployEvent" : "pda-search",
        event: ev ? { authority: ev.authority.toBase58(), signer: ev.signer.toBase58(), amount: ev.amount.toString(), mask: ev.mask, roundId: ev.roundId.toString(), strategy: ev.strategy.toString() } : null };
      if (kind === "Deploy/executor") authoritiesWithExecutor.add(authority);
      minersSeen.add(authority);
    } else if (dec.kind === "Checkpoint") {
      const logId = logs.map((l) => /Round ID: (\d+)/.exec(l)?.[1]).find(Boolean);
      let roundId = logId ? BigInt(logId) : findRoundId(new PublicKey(accounts[5].pubkey), boardRound);
      if (roundId == null) return;
      params = { signer: accounts[0].pubkey, authority: accounts[1].pubkey, roundId: roundId.toString(), roundIdSource: logId ? "log" : "pda-search" };
    } else if (dec.kind === "ClaimSOL") {
      params = { signer: accounts[0].pubkey };
    } else if (dec.kind === "ClaimORE") {
      params = { signer: accounts[0].pubkey, bps: dec.bps.toString() };
    } else if (dec.kind === "Automate") {
      params = { signer: accounts[0].pubkey, executor: accounts[2].pubkey, version: dec.version, amount: dec.amount.toString(), deposit: dec.deposit.toString(), fee: dec.fee.toString(),
        mask: dec.mask.toString(), strategy: dec.strategy, reload: dec.reload.toString(),
        conditions: dec.conditions ? { maxProductionCost: dec.conditions.maxProductionCost.toString(), minMotherlode: dec.conditions.minMotherlode, maxMotherlode: dec.conditions.maxMotherlode, splitTiles: dec.conditions.splitTiles, soloTiles: dec.conditions.soloTiles, buffer: dec.conditions.buffer.toString() } : null };
    } else return;
    if ((got[kind] ?? 0) >= (WANT[kind] ?? 0)) return;
    // prefer variety: one fixture per (kind, signer)
    const key = kind + ":" + accounts[0].pubkey;
    if (seen.has(key)) return;
    seen.add(key);
    got[kind] = (got[kind] ?? 0) + 1;
    fixtures.push({ kind, signature: sig, slot: tx.slot, blockTime: tx.blockTime, ixIndex, err: tx.meta?.err ?? null, programId: ORE_PROGRAM_ID.toBase58(), dataHex: data.toString("hex"), accounts, params });
    console.error(`+ ${kind} ${sig.slice(0, 12)}… (${got[kind]}/${WANT[kind]})`);
  });
}

const done = () => Object.keys(WANT).every((k) => (got[k] ?? 0) >= WANT[k]);

async function scan(addr: PublicKey, maxTx: number, boardRound: bigint, pages = 3) {
  let before: string | undefined;
  let n = 0;
  for (let p = 0; p < pages && n < maxTx && !done(); p++) {
    const sigs = await rpc.getSignatures(addr, 100, before);
    if (!sigs.length) break;
    before = sigs[sigs.length - 1].signature;
    for (const s of sigs) {
      if (s.err || n >= maxTx || done()) continue;
      const tx = await rpc.getTransaction(s.signature);
      n++;
      if (tx) extract(s.signature, tx, boardRound);
    }
  }
}

const MERGE = process.argv.includes("--merge");
const main = async () => {
  const boardData = (await rpc.getAccount(BOARD_ADDRESS))!;
  const board = decodeBoard(boardData);
  console.error("board round", board.roundId);
  let prevAccounts: any[] = [];
  if (MERGE && fs.existsSync(OUT)) {
    const prev = JSON.parse(fs.readFileSync(OUT, "utf8"));
    prevAccounts = prev.accounts;
    for (const f of prev.instructions) {
      fixtures.push(f); got[f.kind] = (got[f.kind] ?? 0) + 1; seen.add(f.kind + ":" + f.accounts[0].pubkey);
      if (f.kind.startsWith("Deploy")) minersSeen.add(f.params.authority);
      if (f.kind === "Deploy/executor") authoritiesWithExecutor.add(f.params.authority);
    }
    // ClaimORE always touches the treasury ORE token account; executor deploys touch automation PDAs.
    await scan(treasuryTokensAddress(), 60, board.roundId, 3);
    await scan(ORE_PROGRAM_ID, 150, board.roundId, 4);
  } else {
    await scan(ORE_PROGRAM_ID, 120, board.roundId, 4);
  }
  // Automate txs are rare on the program feed: look at automation PDAs of executor-driven miners.
  for (const a of [...authoritiesWithExecutor].slice(0, 6)) {
    if ((got.Automate ?? 0) >= WANT.Automate) break;
    await scan(automationPda(new PublicKey(a)), 25, board.roundId, 2);
  }
  // Claim txs: try a few miner PDAs' histories too.
  for (const a of [...minersSeen].slice(0, 6)) {
    if ((got.ClaimSOL ?? 0) >= WANT.ClaimSOL && (got.ClaimORE ?? 0) >= WANT.ClaimORE) break;
    await scan(minerPda(new PublicKey(a)), 25, board.roundId, 1);
  }

  // Accounts
  const acct: any[] = [];
  const add = async (name: string, pk: PublicKey) => {
    const info = await rpc.call("ai:" + pk.toBase58(), () => rpc.conn.getAccountInfo(pk));
    if (info) acct.push({ name, address: pk.toBase58(), owner: info.owner.toBase58(), lamports: info.lamports, dataBase64: Buffer.from(info.data).toString("base64") });
  };
  await add("board", BOARD_ADDRESS);
  await add("config", CONFIG_ADDRESS);
  await add("treasury", TREASURY_ADDRESS);
  await add("entropyVar", VAR_ADDRESS);
  await add("roundCurrent", roundPda(board.roundId));
  await add("roundSettled", roundPda(board.roundId - 1n));
  await add("roundSettled2", roundPda(board.roundId - 2n));
  for (const a of [...minersSeen].slice(0, 3)) await add("miner:" + a, minerPda(new PublicKey(a)));
  for (const a of [...authoritiesWithExecutor].slice(0, 3)) await add("automation:" + a, automationPda(new PublicKey(a)));

  if (MERGE) for (const a of prevAccounts) if (!acct.some((x) => x.name === a.name)) acct.push(a);
  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, JSON.stringify({ fetchedAt: new Date().toISOString(), boardRoundId: board.roundId.toString(), instructions: fixtures, accounts: acct }, null, 1));
  console.error("wrote", OUT, "ix:", got, "accounts:", acct.length, "rpc:", rpc.stats);
};
main().then(() => process.exit(0), (e) => { console.error(e); process.exit(1); });
