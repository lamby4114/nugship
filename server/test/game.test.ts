// Game logic + referee tests (no network). Uses synthetic ORE rounds with chosen winning squares and a SimLedger.
import test from "node:test";
import assert from "node:assert/strict";
import crypto from "node:crypto";
import { PublicKey, Keypair } from "@solana/web3.js";
import { commitLayout, validateLayout, randomLayout, FleetState } from "../src/game/fleet.js";
import { densityMap, chooseTargets } from "../src/game/ai.js";
import { ReplayRoundSource } from "../src/chain/roundSource.js";
import { SimLedger } from "../src/chain/deployReader.js";
import { Match, RULES } from "../src/game/match.js";
import { Store } from "../src/db/store.js";
import { ONE_ORE, Round, SPLIT_ADDRESS } from "../src/ore/index.js";

// galleon A1-A3, sloop C1-D1, raft E5
const L1 = [1,1,1,0,0, 0,0,0,0,0, 2,0,0,0,0, 2,0,0,0,0, 0,0,0,0,3];
// galleon B2-D2 (vertical), sloop A4-A5, raft E1
const L2 = [0,0,0,2,2, 0,1,0,0,0, 0,1,0,0,0, 0,1,0,0,0, 3,0,0,0,0];
const SALT = Buffer.from(Array.from({ length: 32 }, (_, i) => i)).toString("hex");

test("layout validation", () => {
  assert.equal(validateLayout(L1), null);
  assert.equal(validateLayout(L2), null);
  assert.match(validateLayout([1,0,1,1, ...Array(21).fill(0)])!, /galleon|straight/);
  assert.match(validateLayout(Array(25).fill(0))!, /galleon/);
  const diag = [1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0, 2,2,0,0,0, 0,0,0,0,3];
  assert.match(validateLayout(diag)!, /straight/);
  for (let i = 0; i < 200; i++) assert.equal(validateLayout(randomLayout()), null);
});

test("commit = sha256(layout bytes || salt bytes) — fixed vector for the Unity client", () => {
  const c = commitLayout(L1, SALT);
  const expect = crypto.createHash("sha256").update(Buffer.concat([Buffer.from(L1), Buffer.from(SALT, "hex")])).digest("hex");
  assert.equal(c, expect);
  console.log("  vector: layout", JSON.stringify(L1), "salt", SALT, "-> commit", c);
});

test("AI density respects misses and finds the raft last cell", () => {
  const k = { misses: new Set(Array.from({ length: 25 }, (_, i) => i).filter((i) => ![0, 1, 2, 10, 15, 24].includes(i))), hits: new Set([0, 1, 2, 10, 15]), sunkShips: [{ type: "galleon" as const, cells: [0, 1, 2] }, { type: "sloop" as const, cells: [10, 15] }] };
  const d = densityMap(k);
  assert.equal(d[24], 1);
  assert.deepEqual(chooseTargets("density", k, 1), [24]);
});

function synthRound(id: number, win: number, opts: { split?: boolean; motherlode?: bigint } = {}): Round {
  const slotHash = Buffer.alloc(32); slotHash.writeBigUInt64LE(BigInt(win) + 25n * 1000n, 0); // rng % 25 == win
  return {
    id: BigInt(id), deployed: Array(25).fill(400_000_000n), mass: Array(25).fill(0n), count: Array(25).fill(6n), slotHash, expiresAt: 0n, motherlode: opts.motherlode ?? 0n,
    rentPayer: PublicKey.default, rewards: [ONE_ORE, ...Array(24).fill(0n)], totalVaulted: 0n, totalReturnedSol: 0n, totalMiners: 150n,
    topMiner: opts.split === false ? PublicKey.default : SPLIT_ADDRESS,
  };
}

test("referee: on-chain shots, misfire, ignored/underfunded squares, crit, bonus, sink, win, leaderboard", async () => {
  // winning squares per turn: t1=0 (A1, a galleon cell of L1 -> crit for P1? no: P1 fires at L2), choose explicitly below
  const rounds = [synthRound(5000, 6), synthRound(5001, 3), synthRound(5002, 20), synthRound(5003, 11), synthRound(5004, 16), synthRound(5005, 9)];
  const src = new ReplayRoundSource(null, { rounds, speed: 2000 });
  const ledger = new SimLedger();
  const w = [Keypair.generate().publicKey.toBase58(), Keypair.generate().publicKey.toBase58()];
  const inbox: any[][] = [[], []];
  const store = new Store(":memory:");
  let ended: any = null;
  const stake = 5_000_000n;
  // scripted volleys: [p0 targets (at L2), p1 targets (at L1)], plus what actually gets deployed
  const script = [
    { aim: [[6, 11, 0], [0, 7, 8]], deploy: [[6, 11, 0], null] },                 // t1: p0 hits galleon x2 + crit(6); p1 misfires (no deploy)
    { aim: [[16, 3, 1, 2], [1, 2, 9]], deploy: [[16, 3, 1, 2, 4], [1, 2]] },        // t2: p0 bonus shot (crit) -> 4 shots; sinks galleon (6,11,16), hits sloop A4(3); extra 4 ignored; p1 only deployed 1,2
    { aim: [[4, 20, 5], [10, 15]], deploy: [[4, 20, 5], [10, 15]] },                // t3: p1 has 2 ships afloat -> 2 shots, sinks sloop; p0 sinks sloop (3,4) + raft (20) -> p1 destroyed
  ];
  const turnSeen: number[] = [];
  const match = new Match({
    mode: "ranked", source: src, reader: ledger, stakeLamports: stake,
    players: [
      { id: w[0], name: "P0", wallet: w[0], fireMode: "auto", send: (m) => inbox[0].push(m) },
      { id: w[1], name: "P1", wallet: w[1], fireMode: "auto", send: (m) => inbox[1].push(m) },
    ],
    onEnd: (r) => { ended = r; store.recordMatch(r); },
  });
  store.upsertPlayer(w[0], "P0", w[0]); store.upsertPlayer(w[1], "P1", w[1]);
  // drive: respond to turn_start / lock via polling the inbox
  const handled = new Set<any>();
  const pump = setInterval(() => {
    for (const seat of [0, 1] as const) for (const m of inbox[seat]) {
      if (handled.has(m)) continue; handled.add(m);
      if (m.t === "turn_start") {
        const s = script[m.turn - 1]; if (!s) continue;
        if (seat === 0) turnSeen.push(m.maxShots);
        const err = match.aim(seat, m.roundId, s.aim[seat]); assert.equal(err, null, `aim t${m.turn} seat${seat}: ${err}`);
      }
      if (m.t === "lock" && m.fire) {
        const turn = match.turn!.n; const dep = script[turn - 1]?.deploy[seat];
        if (dep) ledger.deploy(w[seat], m.roundId, dep.reduce((a: number, x: number) => a | (1 << x), 0), seat === 1 && turn === 2 ? stake : stake);
      }
    }
  }, 2);
  await src.start();
  await match.start();
  assert.equal(match.placeFleet(0, L1, SALT, commitLayout(L1, SALT)), null);
  assert.equal(match.placeFleet(1, L2, SALT), null);
  assert.match(match.placeFleet(1, L2, SALT) ?? "", /already|locked/);
  while (!ended) await new Promise((r) => setTimeout(r, 20));
  clearInterval(pump); src.stop();

  const rev0 = inbox[0].filter((m) => m.t === "reveal"), rev1 = inbox[1].filter((m) => m.t === "reveal");
  assert.equal(rev0.length, 3);
  // t1
  assert.deepEqual(rev0[0].outgoing.shots.map((s: any) => [s.square, s.hit, s.crit, s.fx]), [[0, false, false, "splash"], [6, true, true, "critical"], [11, true, false, "explosion"]]);
  assert.equal(rev0[0].outgoing.shots[1].ship, null, "outgoing hit does not leak ship type before it sinks");
  assert.equal(rev0[0].incoming.misfire, true);
  assert.equal(rev1[0].outgoing.misfire, true);
  assert.ok(BigInt(rev0[0].outgoing.oreGrams) > 0n, "crit on split round earns (estimated) ORE");
  assert.equal(rev0[0].next.maxShots, 4); assert.equal(rev0[0].next.bonusReason, "crit");
  // t2
  assert.deepEqual(turnSeen, [3, 4, 4]);
  assert.deepEqual(rev0[1].outgoing.ignoredSquares, [4]);
  assert.deepEqual(rev0[1].outgoing.shots.find((s: any) => s.square === 16).sunk, "galleon");
  assert.deepEqual(rev1[1].outgoing.shots.map((s: any) => s.square), [1, 2]);   // 9 locked but not deployed -> not a shot
  assert.deepEqual(rev1[1].incoming.shots.filter((s: any) => s.hit).map((s: any) => s.ship), ["sloop", "galleon"]); // incoming view names your own ship
  assert.equal(rev1[1].next.maxShots, 2);
  assert.equal(rev0[2].incoming.shots.find((s: any) => s.square === 15).sunk, "sloop");
  assert.deepEqual(rev0[1].opponent.sunkShips, [{ type: "galleon", cells: [6, 11, 16] }]);
  // t3: p0 destroys p1's fleet
  assert.equal(ended.winnerSeat, 0); assert.equal(ended.reason, "fleet_sunk");
  const end0 = inbox[0].find((m) => m.t === "match_end"), end1 = inbox[1].find((m) => m.t === "match_end");
  assert.equal(end0.result, "win"); assert.equal(end1.result, "loss");
  assert.equal(commitLayout(end1.fleets.opponent.layout, end1.fleets.opponent.salt), end1.fleets.opponent.commit);
  assert.ok(ended.players.every((p: any) => p.commitVerified));
  // stats + leaderboard
  const lb = store.leaderboard("all", "ranked", "wins");
  assert.equal(lb[0].id, w[0]); assert.equal(lb[0].wins, 1); assert.equal(lb[1].losses, 1);
  assert.equal(lb[0].sunk, 3); assert.equal(lb[0].crits, 3); // squares 6, 3, 20 were the winning ORE squares of t1..t3
  assert.equal(lb[1].misfires, 1);
  assert.ok(lb[0].rating > 1000 && lb[1].rating < 1000);
  const wk = store.leaderboard("week", "ranked", "accuracy");
  assert.equal(wk.length, 2);
  const ps = store.playerStats(w[0])!;
  assert.equal(ps.global.ranked.games, 1); assert.equal(ps.weekly.ranked.wins, 1);
});

test("aim validation", async () => {
  const src = new ReplayRoundSource(null, { rounds: [synthRound(1, 1), synthRound(2, 2)], speed: 2000 });
  const m = new Match({ mode: "practice", source: src, reader: null, stakeLamports: 1n, players: [{ id: "a", name: "A", wallet: null, fireMode: "virtual" }, { id: "b", name: "B", wallet: null, fireMode: "virtual" }] });
  await src.start(); await m.start();
  m.placeFleet(0, L1, SALT); m.placeFleet(1, L2, SALT);
  while (m.phase !== "aiming") await new Promise((r) => setTimeout(r, 5));
  const rid = m.turn!.roundId;
  assert.match(m.aim(0, rid, [1, 2, 3, 4])!, /max 3/);
  assert.match(m.aim(0, rid, [25])!, /0\.\.24/);
  assert.match(m.aim(0, rid + 1, [1])!, /stale/);
  assert.equal(m.aim(0, rid, [3, 3, 1]), null);
  m.resign(0); src.stop();
  assert.equal(m.result!.winnerSeat, 1);
});

test("FleetState sinking", () => {
  const f = new FleetState(L1);
  [0, 1].forEach((i) => f.hits.add(i));
  assert.deepEqual(f.sunk(), []);
  f.hits.add(2); assert.deepEqual(f.sunk(), ["galleon"]);
  f.hits.add(24); assert.deepEqual(f.afloat(), ["sloop"]);
});
