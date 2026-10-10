// AI captains for solo play. All run on the same rules as humans (salvo), except where noted.
import { SHIPS, ShipType } from "./fleet.js";

export interface Captain {
  id: string; name: string; island: string; difficulty: 1 | 2 | 3 | 4 | 5; tactic: "random" | "hunt" | "density" | "density_noisy";
  /** extra cannons per volley (boss +1; island-1 Barnacle -1 so the first match is winnable; never below 1 shot). */
  bonusShots: number; preferEdges: boolean; boss: boolean; blurb: string; unlocks: string[];
}

export const CAPTAINS: Captain[] = [
  { id: "barnacle", name: "Captain Barnacle", island: "Driftwood Key", difficulty: 1, tactic: "random", bonusShots: -1, preferEdges: false, boss: false,
    blurb: "Fires wherever the gulls point. Mostly at gulls.", unlocks: ["flag_jolly_ore"] },
  { id: "brine", name: "Admiral Brine", island: "Saltspire Fort", difficulty: 2, tactic: "hunt", bonusShots: 0, preferEdges: false, boss: false,
    blurb: "By the book: find a hull, then hammer the squares around it.", unlocks: ["skin_navy_galleon", "hat_tricorn_nuggy"] },
  { id: "molly", name: "Mad Molly", island: "Cinder Reef", difficulty: 3, tactic: "density_noisy", bonusShots: 0, preferEdges: true, boss: false,
    blurb: "Hides her fleet on the edges and guesses with frightening instinct.", unlocks: ["skin_red_sloop", "hat_bandana_nuggy"] },
  { id: "ghost", name: "The Ghost Captain", island: "Fog Banks", difficulty: 4, tactic: "density", bonusShots: 0, preferEdges: true, boss: false,
    blurb: "Calculates every possible fleet. Never wastes a cannonball.", unlocks: ["skin_ghost_galleon", "map_fog_banks"] },
  { id: "kraken_cult", name: "High Priest of the Kraken Cult", island: "Abyssal Maw", difficulty: 5, tactic: "density", bonusShots: 1, preferEdges: true, boss: true,
    blurb: "Boss: fires one extra cannonball every volley.", unlocks: ["skin_kraken_galleon", "hat_crown_nuggy", "map_volcanic_isles"] },
];
export const captainById = (id: string) => CAPTAINS.find((c) => c.id === id);

/** What a shooter knows about the opponent's sea. */
export interface SeaKnowledge { misses: Set<number>; hits: Set<number>; sunkShips: { type: ShipType; cells: number[] }[]; }

const neighbors = (i: number) => {
  const r = Math.floor(i / 5), c = i % 5, out: number[] = [];
  if (r > 0) out.push(i - 5); if (r < 4) out.push(i + 5); if (c > 0) out.push(i - 1); if (c < 4) out.push(i + 1);
  return out;
};

function placements(size: number): number[][] {
  const out: number[][] = [];
  for (let r = 0; r < 5; r++) for (let c = 0; c < 5; c++) {
    if (c + size <= 5) out.push(Array.from({ length: size }, (_, k) => r * 5 + c + k));
    if (size > 1 && r + size <= 5) out.push(Array.from({ length: size }, (_, k) => (r + k) * 5 + c));
  }
  return out;
}
const PLACEMENTS = new Map(SHIPS.map((s) => [s.type, placements(s.size)]));

/** Per-square probability that an un-sunk ship occupies it, consistent with everything observed. */
export function densityMap(k: SeaKnowledge): number[] {
  const sunkCells = new Set(k.sunkShips.flatMap((s) => s.cells));
  const live = SHIPS.filter((s) => !k.sunkShips.some((x) => x.type === s.type));
  const unexplainedHits = [...k.hits].filter((h) => !sunkCells.has(h));
  const counts = Array(25).fill(0);
  const used = new Set<number>();
  let total = 0;
  const rec = (idx: number, chosen: number[][]) => {
    if (idx === live.length) {
      const covered = new Set(chosen.flat());
      if (!unexplainedHits.every((h) => covered.has(h))) return;
      total++;
      covered.forEach((c) => counts[c]++);
      return;
    }
    for (const p of PLACEMENTS.get(live[idx].type)!) {
      if (p.some((c) => used.has(c) || k.misses.has(c) || sunkCells.has(c))) continue;
      p.forEach((c) => used.add(c)); chosen.push(p);
      rec(idx + 1, chosen);
      chosen.pop(); p.forEach((c) => used.delete(c));
    }
  };
  rec(0, []);
  return counts.map((c) => (total ? c / total : 0));
}

export function chooseTargets(tactic: Captain["tactic"], k: SeaKnowledge, n: number, rng: () => number = Math.random): number[] {
  const fired = new Set([...k.misses, ...k.hits]);
  const open = Array.from({ length: 25 }, (_, i) => i).filter((i) => !fired.has(i));
  if (open.length === 0) return [];
  const pickRandom = (pool: number[], m: number) => {
    const a = [...pool]; for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(rng() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; }
    return a.slice(0, m);
  };
  if (tactic === "random") return pickRandom(open, n);
  if (tactic === "hunt") {
    const sunkCells = new Set(k.sunkShips.flatMap((s) => s.cells));
    const frontier = [...new Set([...k.hits].filter((h) => !sunkCells.has(h)).flatMap(neighbors))].filter((i) => !fired.has(i));
    const pri = pickRandom(frontier, n);
    return [...pri, ...pickRandom(open.filter((i) => !pri.includes(i)), n - pri.length)];
  }
  const d = densityMap(k);
  const scored = open.map((i) => ({ i, s: d[i] + (tactic === "density_noisy" ? rng() * 0.25 : rng() * 1e-6) }));
  scored.sort((a, b) => b.s - a.s);
  return scored.slice(0, n).map((x) => x.i);
}
