// v1.1.0 (seeker-test13): the round timer must not jump (one smoothed server deadline per turn, sent as lockInMs/endInMs),
// and a fresh SIWS nonce comes on the SAME socket (refresh_nonce) - no reconnect, so no guest hello blip before sign-in.
import test from "node:test";
import assert from "node:assert/strict";
import { WebSocket } from "ws";
import crypto from "node:crypto";
import nacl from "tweetnacl";
import bs58 from "bs58";
import { Keypair } from "@solana/web3.js";
import { Store } from "../src/db/store.js";
import { GameServer } from "../src/net/server.js";
import { PracticeRoundSource, ReplayRoundSource } from "../src/chain/roundSource.js";
import { randomLayout, commitLayout } from "../src/game/fleet.js";
import { Match } from "../src/game/match.js";
import { Round, SPLIT_ADDRESS } from "../src/ore/index.js";

function open(port: number) {
  const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  const inbox: any[] = []; const waiters: [(m: any) => boolean, (m: any) => void][] = [];
  ws.on("message", (raw) => { const m = JSON.parse(raw.toString()); const i = waiters.findIndex(([f]) => f(m)); if (i >= 0) waiters.splice(i, 1)[0][1](m); else inbox.push(m); });
  const next = (f: (m: any) => boolean, ms = 3000) => new Promise<any>((res, rej) => { const i = inbox.findIndex(f); if (i >= 0) return res(inbox.splice(i, 1)[0]); waiters.push([f, res]); setTimeout(() => rej(new Error("timeout")), ms); });
  const ask = (msg: any, t: string) => { ws.send(JSON.stringify(msg)); return next((m) => m.t === t || (m.t === "error" && m.ref === msg.t)); };
  return { ws, next, ask, close: () => ws.close() };
}
const siws = (kp: Keypair, nonce: string) => {
  const message = `lamby4114.github.io wants you to sign in with your Solana account:\n${kp.publicKey.toBase58()}\n\nSign in to NUGSHIP.\n\nNonce: ${nonce}\nIssued At: ${new Date().toISOString()}`;
  return { message, signature: bs58.encode(nacl.sign.detached(Buffer.from(message, "utf8"), kp.secretKey)) };
};

test("refresh_nonce: new nonce on the same socket (before and after hello); SIWS over it verifies without reconnecting", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, requireWalletProof: true, log: () => {} });
  const port = await server.listen();
  const kp = Keypair.generate(), wallet = kp.publicKey.toBase58();
  try {
    const s = open(port); const ch = await s.next((m) => m.t === "challenge");
    const n1 = await s.ask({ t: "refresh_nonce" }, "nonce");
    assert.equal(n1.t, "nonce"); assert.match(n1.nonce, /^[0-9a-f]{32}$/); assert.notEqual(n1.nonce, ch.nonce);
    // relaunch flow: the player is already welcomed (unsigned wallet) when the sign-in starts
    const w0 = await s.ask({ t: "hello", name: "Tester", wallet }, "welcome");
    assert.equal(w0.verified, false);
    const n2 = await s.ask({ t: "refresh_nonce" }, "nonce");
    assert.notEqual(n2.nonce, n1.nonce);
    const w1 = await s.ask({ t: "hello", name: "Tester", wallet, signIn: siws(kp, n2.nonce) }, "welcome");
    assert.equal(w1.t, "welcome"); assert.equal(w1.verified, true); assert.equal(w1.playerId, wallet);
    s.close();
  } finally { server.close(); }
});

test("turn_start carries lockInMs/endInMs (practice solo)", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, log: () => {} });
  const port = await server.listen();
  try {
    const s = open(port); await s.next((m) => m.t === "challenge");
    await s.ask({ t: "hello", name: "Tester", guestKey: crypto.randomBytes(16).toString("hex") }, "welcome");
    const mf = await s.ask({ t: "start_solo", captain: "barnacle", mode: "practice" }, "match_found");
    const layout = randomLayout(), salt = crypto.randomBytes(32).toString("hex");
    await s.ask({ t: "place_fleet", matchId: mf.matchId, layout, salt, commit: commitLayout(layout, salt) }, "fleet_ack");
    const ts = await s.next((m) => m.t === "turn_start", 8000);
    assert.equal(typeof ts.lockInMs, "number"); assert.equal(typeof ts.endInMs, "number");
    assert.ok(ts.lockInMs >= 0 && ts.endInMs >= ts.lockInMs, JSON.stringify({ l: ts.lockInMs, e: ts.endInMs }));
    s.close();
  } finally { server.close(); }
});

function synthRound(id: number, win: number): Round {
  const deployed = Array(25).fill(1_000_000n), count = Array(25).fill(1n);
  return { id: BigInt(id), deployed, slotHash: Buffer.alloc(32, win), count, expiresAt: 0n, motherlode: 0n, rentPayer: SPLIT_ADDRESS, topMiner: SPLIT_ADDRESS, topMinerReward: 0n, totalDeployed: 25_000_000n, totalMiners: 25n, totalVaulted: 0n, totalWinnings: 0n } as any;
}

test("Match.timing: a slot burst moves the deadline only 20% per estimate (no countdown jumps)", async () => {
  const src = new ReplayRoundSource(null, { rounds: [synthRound(1, 1), synthRound(2, 2)], speed: 1 });
  const m = new Match({ mode: "practice", source: src, reader: null, stakeLamports: 1n, players: [{ id: "a", name: "A", wallet: null, fireMode: "virtual" }, { id: "b", name: "B", wallet: null, fireMode: "virtual" }] });
  await src.start(); await m.start();
  m.placeFleet(0, randomLayout(), crypto.randomBytes(32).toString("hex")); m.placeFleet(1, randomLayout(), crypto.randomBytes(32).toString("hex"));
  for (let i = 0; i < 400 && m.phase !== "aiming"; i++) await new Promise((r) => setTimeout(r, 5));
  assert.equal(m.phase, "aiming");
  const t0 = m.timing()!;
  const real = src.slot.bind(src), ms = src.slotMs();
  (src as any).slot = () => real() + 20;   // 20 slots arrive at once
  const t1 = m.timing()!;
  const jump = 20 * ms, moved = t0.lockInMs - t1.lockInMs;
  assert.ok(moved > 0 && moved <= jump * 0.2 + 5, `moved ${moved} of a ${jump} ms burst`);
  for (let i = 0; i < 40; i++) m.timing();
  const t2 = m.timing()!;
  assert.ok(Math.abs(t0.lockInMs - jump - t2.lockInMs) < 20, "converges on the new estimate");
  m.resign(0); src.stop();
});
