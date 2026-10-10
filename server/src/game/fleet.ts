// Fleet layout, validation and commit-reveal: commit = sha256(layout[25] || salt[32]) (hex).
import crypto from "node:crypto";

export type ShipType = "galleon" | "sloop" | "raft";
export const SHIPS: { type: ShipType; code: number; size: number }[] = [
  { type: "galleon", code: 1, size: 3 },
  { type: "sloop", code: 2, size: 2 },
  { type: "raft", code: 3, size: 1 },
];
export const SHIP_BY_CODE = new Map(SHIPS.map((s) => [s.code, s]));
/** layout: 25 bytes, byte i = ship code on square i (0 water, 1 galleon, 2 sloop, 3 raft). Square i = row*5+col (A1=0, E5=24). */
export type Layout = number[];

export function validateLayout(layout: unknown): string | null {
  if (!Array.isArray(layout) || layout.length !== 25) return "layout must be an array of 25 integers";
  if (!layout.every((v) => Number.isInteger(v) && v >= 0 && v <= 3)) return "layout values must be 0..3";
  for (const s of SHIPS) {
    const cells = layout.map((v, i) => (v === s.code ? i : -1)).filter((i) => i >= 0);
    if (cells.length !== s.size) return `${s.type} must occupy exactly ${s.size} squares`;
    if (s.size > 1) {
      const rows = cells.map((c) => Math.floor(c / 5)), cols = cells.map((c) => c % 5);
      const horiz = rows.every((r) => r === rows[0]) && cols.every((c, k) => k === 0 || c === cols[k - 1] + 1);
      const vert = cols.every((c) => c === cols[0]) && rows.every((r, k) => k === 0 || r === rows[k - 1] + 1);
      if (!horiz && !vert) return `${s.type} must be a straight contiguous line`;
    }
  }
  return null;
}

export function commitLayout(layout: Layout, saltHex: string): string {
  const salt = Buffer.from(saltHex, "hex");
  if (salt.length !== 32) throw new Error("salt must be 32 bytes hex");
  return crypto.createHash("sha256").update(Buffer.concat([Buffer.from(layout), salt])).digest("hex");
}

export const randomSalt = () => crypto.randomBytes(32).toString("hex");

export function randomLayout(rng: () => number = Math.random, preferEdges = false): Layout {
  for (;;) {
    const lay: Layout = Array(25).fill(0);
    let ok = true;
    for (const s of SHIPS) {
      let placed = false;
      for (let tries = 0; tries < 200 && !placed; tries++) {
        const vert = rng() < 0.5;
        const r = Math.floor(rng() * (vert ? 6 - s.size : 5)), c = Math.floor(rng() * (vert ? 5 : 6 - s.size));
        const cells = Array.from({ length: s.size }, (_, k) => (vert ? (r + k) * 5 + c : r * 5 + c + k));
        if (preferEdges && rng() < 0.6 && !cells.some((x) => x < 5 || x >= 20 || x % 5 === 0 || x % 5 === 4)) continue;
        if (cells.every((x) => lay[x] === 0)) { cells.forEach((x) => (lay[x] = s.code)); placed = true; }
      }
      if (!placed) { ok = false; break; }
    }
    if (ok && validateLayout(lay) === null) return lay;
  }
}

/** Mutable damage tracking for one fleet. */
export class FleetState {
  readonly hits = new Set<number>();
  constructor(readonly layout: Layout) {}
  shipAt(sq: number): ShipType | null { return SHIP_BY_CODE.get(this.layout[sq])?.type ?? null; }
  isSunk(type: ShipType): boolean {
    const code = SHIPS.find((s) => s.type === type)!.code;
    return this.layout.every((v, i) => v !== code || this.hits.has(i));
  }
  afloat(): ShipType[] { return SHIPS.filter((s) => !this.isSunk(s.type)).map((s) => s.type); }
  sunk(): ShipType[] { return SHIPS.filter((s) => this.isSunk(s.type)).map((s) => s.type); }
  destroyed(): boolean { return this.afloat().length === 0; }
  cellsHit(): number { return [...this.hits].filter((i) => this.layout[i] !== 0).length; }
}
