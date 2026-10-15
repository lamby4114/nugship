// Lifetime stats card: per-wallet totals (win rate, rounds hit, SOL deployed, motherlodes) via store + REST, read-only.
import test from "node:test";
import assert from "node:assert/strict";
import { Store } from "../src/db/store.js";
import { GameServer } from "../src/net/server.js";
import { PracticeRoundSource } from "../src/chain/roundSource.js";

function result(id: string, w: string, res: "win" | "loss", hits: number, lamports: bigint, kraken: number, mode = "solo_live") {
  return {
    matchId: id, mode, sourceKind: "live", startedAt: Date.now() - 1000, endedAt: Date.now(), winnerSeat: res === "win" ? 0 : 1, reason: "fleet_sunk", turns: 3,
    stakeLamports: "1000000", rounds: [1, 2, 3],
    players: [
      { seat: 0, id: w, name: "V", wallet: w, isAI: false, result: res, commit: "", layout: [], salt: "", commitVerified: true,
        stats: { volleys: 3, shots: 6, hits, sunk: 1, crits: 0, oreGrams: 150_000_000_000n, misfires: 0, kraken, lamportsDeployed: lamports, solReturnedLamports: 0n } },
      { seat: 1, id: "ai:x", name: "AI", wallet: null, isAI: true, result: res === "win" ? "loss" : "win", commit: "", layout: [], salt: "", commitVerified: true,
        stats: { volleys: 0, shots: 0, hits: 0, sunk: 0, crits: 0, oreGrams: 0n, misfires: 0, kraken: 0, lamportsDeployed: 0n, solReturnedLamports: 0n } },
    ],
  } as any;
}

test("stats: lifetime totals per wallet incl. SOL deployed, motherlodes, win rate, rounds hit; REST read-only", async () => {
  const store = new Store(":memory:"); const w = "14RMqeviZGaZhYXeRWmQbWj2hUT6p6haKkHAEThmJe69";
  store.upsertPlayer(w, "V", w);
  store.recordMatch(result("m1", w, "win", 4, 20_000_000n, 1));
  store.recordMatch(result("m2", w, "loss", 2, 9_000_000n, 0));
  const s = store.playerStats(w)!.global.all as any;
  assert.equal(s.games, 2); assert.equal(s.wins, 1); assert.equal(s.losses, 1); assert.equal(s.winRate, 0.5);
  assert.equal(s.roundsHit, 6); assert.equal(s.motherlodes, 1); assert.equal(s.lamportsDeployed, "29000000"); assert.equal(s.solDeployed, 0.029); assert.equal(s.ore, 3);
  const recent = store.recentMatches(w) as any[];
  assert.equal(recent.length, 2); assert.ok(recent.some((r) => r.motherlodes === 1 && r.lamportsDeployed === 20_000_000));
  const empty = store.playerStats(w)!.weekly.ranked as any; assert.equal(empty.solDeployed, 0); assert.equal(empty.motherlodes, 0);
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, log: () => {} });
  const port = await server.listen();
  try {
    const r = await fetch(`http://127.0.0.1:${port}/api/players/${w}`); assert.equal(r.status, 200);
    const j = await r.json(); assert.equal(j.global.all.motherlodes, 1); assert.equal(j.global.all.solDeployed, 0.029);
    const h = await (await fetch(`http://127.0.0.1:${port}/api/players/${w}/matches?limit=5`)).json(); assert.equal(h.matches.length, 2);
  } finally { server.close(); }
});
