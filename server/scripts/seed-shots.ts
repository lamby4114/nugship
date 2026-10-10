// Seeds a throwaway DB with demo captains for desktop screenshots (port 8788 test server only; never the real DB).
//   DB_PATH=./data/shots.db npx tsx scripts/seed-shots.ts
import { Store } from "../src/db/store.js";
import { nameKey } from "../src/profile/profile.js";

const db = process.env.DB_PATH;
if (!db || !db.includes("shots")) { console.error("refusing: DB_PATH must be a *shots* db"); process.exit(1); }
const store = new Store(db);
const demo: [string, string, string, number][] = [
  ["g_00000000000000a1", "BARNACLE BILL", "a:nuggy_tricorn", 1184],
  ["g_00000000000000a2", "MOLLY MAELSTROM", "a:corsair", 1142],
  ["g_00000000000000a3", "GOLDTOOTH", "a:nuggy_royal", 1105],
  ["g_00000000000000a4", "SEA SALT", "a:salt", 1061],
  ["g_00000000000000a5", "KRAKEN KID", "a:kraken", 1033],
  ["g_00000000000000a6", "PARROT PETE", "a:parrot", 1012],
  ["g_00000000000000a7", "GHOSTY", "a:nuggy_ghost", 988],
];
for (const [id, name, avatar, rating] of demo) {
  store.upsertPlayer(id, name, null);
  store.setProfile(id, name, nameKey(name), avatar);
  (store as any).db.prepare("UPDATE players SET rating=? WHERE id=?").run(rating, id);
  store.setBot(id); // demo rows are scripted: the leaderboard tags them AI
}
// a few finished solo games each so they appear on the boards (minGames=1)
const stats = (w: boolean) => ({ volleys: 6, shots: 14, hits: w ? 6 : 3, sunk: w ? 3 : 1, crits: w ? 1 : 0, oreGrams: 0n, misfires: 0, kraken: 0, lamportsDeployed: 0n, solReturnedLamports: 0n });
let n = 0;
demo.forEach(([id, name], i) => {
  const games = 9 - i;
  for (let k = 0; k < games; k++) {
    const win = k < games - Math.floor(i / 2) - 1;
    const now = Date.now() - (k + 1) * 3600_000;
    store.recordMatch({
      matchId: `seed_${id}_${k}`, mode: "solo" as any, winnerSeat: win ? 0 : 1, reason: "sunk", turns: 6, startedAt: now - 600_000, endedAt: now,
      players: [
        { seat: 0, id, name, wallet: null, isAI: false, result: win ? "win" : "loss", stats: stats(win), commit: "", layout: [] as any, salt: "", commitVerified: true },
        { seat: 1, id: "ai_barnacle", name: "CAPTAIN BARNACLE", wallet: null, isAI: true, captain: "barnacle", result: win ? "loss" : "win", stats: stats(!win), commit: "", layout: [] as any, salt: "", commitVerified: true },
      ],
      rounds: [], stakeLamports: "0", sourceKind: "seed",
    } as any);
    n++;
  }
});
console.log("seeded", n, "demo games");
console.log("seeded", demo.length, "captains into", db);
