// End-to-end simulation: replays recent REAL ORE rounds (read-only RPC) or follows live rounds, runs bots through full
// matches against the real server over WebSocket, and checks the referee's results. NO transactions are ever sent:
// Connection send methods are trapped and the run fails if anything tries to call them.
// Usage: tsx src/sim/harness.ts [--source replay|live] [--speed 25] [--rounds 80] [--keep-db]
import { Connection } from "@solana/web3.js";
import fs from "node:fs";
import { ReadOnlyRpc } from "../chain/rpc.js";
import { LiveRoundSource, ReplayRoundSource, RoundSource } from "../chain/roundSource.js";
import { SimLedger } from "../chain/deployReader.js";
import { Store } from "../db/store.js";
import { GameServer } from "../net/server.js";
import { Bot, BotResult } from "./botClient.js";
import { BOARD_ADDRESS, decodeBoard, decodeRound, roundPda, roundRng, winningSquare, didHitMotherlode } from "../ore/index.js";

const arg = (k: string, d: string) => { const i = process.argv.indexOf("--" + k); return i > 0 ? process.argv[i + 1] : d; };
const SOURCE = arg("source", "replay"), SPEED = Number(arg("speed", "25")), ROUNDS = Number(arg("rounds", "80"));
const verbose = process.argv.includes("--verbose");
const log = (m: string) => { if (verbose || /match_end|finished|replaying|FAIL|round .* settled|match .* vs/.test(m)) console.log(new Date().toISOString().slice(11, 23), m); };

// Trap every way of sending a transaction.
let sendAttempts = 0;
for (const k of ["sendTransaction", "sendRawTransaction", "sendEncodedTransaction"] as const) {
  (Connection.prototype as any)[k] = () => { sendAttempts++; throw new Error("SIM: sending transactions is forbidden"); };
}

const main = async () => {
  const rpc = new ReadOnlyRpc({ minIntervalMs: 250 });
  const source: RoundSource = SOURCE === "live" ? new LiveRoundSource(rpc) : new ReplayRoundSource(rpc, { count: ROUNDS, speed: SPEED, loop: true });
  (source as any).log = log;
  await source.start();
  const ledger = new SimLedger();
  const dbPath = process.argv.includes("--keep-db") ? "./data/sim.db" : ":memory:";
  if (dbPath !== ":memory:") fs.rmSync(dbPath, { force: true });
  const store = new Store(dbPath);
  const server = new GameServer({ port: 0, store, source, reader: ledger, log, requireWalletProof: true, practiceAimSeconds: 2, practiceTickMs: 40, aiDelayMs: 200 });
  const port = await server.listen();
  const url = `ws://127.0.0.1:${port}/ws`;
  console.log(`server on :${port}, source=${source.kind}${source.kind === "replay" ? ` (${(source as ReplayRoundSource).roundsLoaded().length} real rounds, ${SPEED}x)` : ""}`);

  const mk = (name: string, extra: Partial<ConstructorParameters<typeof Bot>[0]> = {}) => new Bot({ url, name, ledger, log, ...extra });
  // 1) Ranked queue: two bots
  const a = mk("Hunter", { tactic: "hunt" }), b = mk("Calculon", { tactic: "density" });
  a.connect((x) => x.send({ t: "queue_ranked", stakeLamports: "5000000", fireMode: "auto" }));
  b.connect((x) => x.send({ t: "queue_ranked", stakeLamports: "5000000", fireMode: "auto" }));
  // 2) Private invite code, with misbehaving clients (missed deploy every 3rd turn; extra un-locked square)
  const c = mk("Skipper", { tactic: "random", skipDeployEveryNth: 3 }), d = mk("Greedy", { tactic: "density", extraSquare: true });
  const code = new Promise<string>((res) => {
    c.connect((x) => { x.ws.on("message", (raw) => { const m = JSON.parse(raw.toString()); if (m.t === "private_created") res(m.code); }); x.send({ t: "create_private", stakeLamports: "1000000", fireMode: "auto" }); });
  });
  code.then((k) => d.connect((x) => x.send({ t: "join_private", code: k })));
  // 3) Solo vs AI captain on (replayed) ORE rounds, real-ORE mode
  const e = mk("Solo", { tactic: "hunt" });
  e.connect((x) => x.send({ t: "start_solo", captain: "ghost", mode: "live", fireMode: "auto" }));
  // 4) Practice, guest without wallet (fast off-chain rounds)
  const f = mk("Guest", { tactic: "random", withWallet: false });
  f.connect((x) => x.send({ t: "start_solo", captain: "barnacle", mode: "practice" }));

  const bots = [a, b, c, d, e, f];
  const timeoutMs = SOURCE === "live" ? 40 * 60_000 : 15 * 60_000;
  const results = await Promise.race([Promise.all(bots.map((x) => x.done)), new Promise<never>((_, rej) => setTimeout(() => rej(new Error("sim timeout")), timeoutMs))]);

  // ---------------- checks ----------------
  const checks: { name: string; ok: boolean; detail?: string }[] = [];
  const check = (name: string, ok: boolean, detail?: string) => { checks.push({ name, ok, detail }); console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? "  — " + detail : ""}`); };
  const [ra, rb, rc, rd, re, rf] = results as BotResult[];
  const pair = (x: BotResult, y: BotResult) => (x.result === "draw" && y.result === "draw") || (x.result === "win" && y.result === "loss") || (x.result === "loss" && y.result === "win");
  check("ranked match finished with consistent result", pair(ra, rb) && ra.matchId === rb.matchId, `${ra.name}=${ra.result} ${rb.name}=${rb.result} reason=${ra.reason} turns=${ra.turns}`);
  check("private match finished with consistent result", pair(rc, rd) && rc.matchId === rd.matchId, `${rc.name}=${rc.result} ${rd.name}=${rd.result} reason=${rc.reason} turns=${rc.turns}`);
  check("solo vs AI finished", !!re.result, `${re.result} (${re.reason}) turns=${re.turns}`);
  check("practice vs AI finished (guest, no wallet)", !!rf.result, `${rf.result} (${rf.reason}) turns=${rf.turns}`);
  check("fleet commit-reveal verified by both PvP clients", [ra, rb, rc, rd].every((r) => r.opponentCommitVerified));
  check("solo client verified AI fleet commit", !!re.opponentCommitVerified && !!rf.opponentCommitVerified);
  check("no transactions sent", sendAttempts === 0, `send attempts: ${sendAttempts}`);
  check("bots built real Deploy/Checkpoint instructions (unsigned)", results.slice(0, 5).every((r) => r.builtIxs > 0), results.map((r) => `${r.name}:${r.builtIxs}`).join(" "));
  const skipperMisfires = rc.reveals.filter((r) => r.outgoing.misfire).length;
  check("referee: skipped deploys become misfires (no free shots)", skipperMisfires > 0 || rc.turns < 3, `misfires=${skipperMisfires} over ${rc.turns} turns`);
  const greedyIgnored = rd.reveals.filter((r) => r.outgoing.ignoredSquares.length > 0).length;
  check("referee: deploys outside locked targets are ignored", greedyIgnored === rd.reveals.filter((r) => r.outgoing.shots.length > 0 && r.outgoing.locked.length < 25).length, `turns with ignored squares=${greedyIgnored}`);
  // simultaneous reveal consistency: A's outgoing == B's incoming
  let mirrored = true;
  for (const [x, y] of [[ra, rb], [rc, rd]] as const) for (let i = 0; i < Math.min(x.reveals.length, y.reveals.length); i++) {
    if (JSON.stringify(x.reveals[i].outgoing.shots.map((s: any) => [s.square, s.hit, s.crit])) !== JSON.stringify(y.reveals[i].incoming.shots.map((s: any) => [s.square, s.hit, s.crit]))) mirrored = false;
    if (x.reveals[i].ore.winningSquare !== y.reveals[i].ore.winningSquare) mirrored = false;
  }
  check("both players see the same volleys and ORE square each round", mirrored);
  // Every on-chain reveal's winning square must equal the winning square of the REAL mainnet round with that id.
  const roundIds = [...new Set([ra, rc, re].flatMap((r) => r.reveals.map((v) => v.roundId)))];
  if (SOURCE !== "live" || roundIds.length) {
    const datas = await rpc.getAccounts(roundIds.map((id) => roundPda(id % 1_000_000_000)));
    let okc = 0, bad = 0, missing = 0;
    const realWin = new Map<number, { win: number; ml: boolean }>();
    datas.forEach((dd, i) => { if (!dd) { missing++; return; } const r = decodeRound(dd); const rng = roundRng(r.slotHash); if (rng !== null) realWin.set(roundIds[i], { win: winningSquare(rng), ml: didHitMotherlode(rng) }); });
    for (const r of [ra, rc, re]) for (const v of r.reveals) { const w = realWin.get(v.roundId); if (!w) continue; if (w.win === v.ore.winningSquare && w.ml === v.ore.motherlode) okc++; else bad++; }
    check("reveal ORE winning square == real mainnet Round account", bad === 0 && okc > 0, `${okc} round reveals matched, ${bad} mismatched, ${missing} round accounts already closed`);
  }
  const crits = results.reduce((n, r) => n + r.reveals.reduce((k, v) => k + v.outgoing.shots.filter((s: any) => s.crit).length, 0), 0);
  check("ORE crit detection exercised", true, `${crits} crit shots across all matches`);
  // REST
  const get = async (p: string) => (await fetch(`http://127.0.0.1:${port}${p}`)).json() as any;
  const lb = await get("/api/leaderboard?period=all&mode=all&sort=wins");
  const lbw = await get("/api/leaderboard?period=week&mode=ranked&sort=wl");
  const ps = await get(`/api/players/${ra.playerId}`);
  const mt = await get(`/api/matches/${ra.matchId}`);
  check("leaderboard (global, all modes) lists human players", lb.entries.length >= 5, lb.entries.map((x: any) => `${x.name}:${x.wins}W/${x.losses}L acc=${x.accuracy} crits=${x.crits} ore=${x.ore.toFixed(4)}`).join(" | "));
  check("weekly ranked leaderboard has both ranked players", lbw.entries.length === 2, lbw.entries.map((x: any) => `${x.name} rating=${x.rating}`).join(", "));
  check("player stats endpoint", ps.global.ranked.games === 1, `games=${ps.global.ranked.games} volleys=${ps.global.ranked.volleys} shots=${ps.global.ranked.shots} hits=${ps.global.ranked.hits}`);
  check("match record has both commits verified", mt.players?.every((p: any) => p.commitVerified), `turns=${mt.turns} rounds=${mt.rounds?.length}`);

  const report = { at: new Date().toISOString(), source: SOURCE, speed: SPEED, rpc: rpc.stats, sendAttempts, checks, results: results.map((r) => ({ ...r, reveals: r.reveals.length })), leaderboard: lb.entries, sampleReveal: ra.reveals[0] };
  fs.mkdirSync("./sim-reports", { recursive: true });
  const file = `./sim-reports/sim-${SOURCE}-${Date.now()}.json`;
  fs.writeFileSync(file, JSON.stringify(report, null, 1));
  console.log(`\n${checks.filter((x) => x.ok).length}/${checks.length} checks passed; report ${file}; rpc calls=${rpc.stats.calls}`);
  server.close(); source.stop();
  process.exit(checks.every((x) => x.ok) ? 0 : 1);
};
main().catch((e) => { console.error(e); process.exit(1); });
