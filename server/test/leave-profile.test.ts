// v1.1.0 seeker-test12: HARBOR on the placement screen drops the solo with no result; a wallet profile needs a verified
// wallet session; SIWS nonces live long enough for a slow sign-in (5 min was too short).
import test from "node:test";
import assert from "node:assert/strict";
import { WebSocket } from "ws";
import crypto from "node:crypto";
import nacl from "tweetnacl";
import bs58 from "bs58";
import { Keypair } from "@solana/web3.js";
import { Store } from "../src/db/store.js";
import { GameServer } from "../src/net/server.js";
import { PracticeRoundSource } from "../src/chain/roundSource.js";
import { randomLayout, commitLayout } from "../src/game/fleet.js";

function open(port: number) {
  const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  const inbox: any[] = []; const waiters: [(m: any) => boolean, (m: any) => void][] = [];
  ws.on("message", (raw) => { const m = JSON.parse(raw.toString()); const i = waiters.findIndex(([f]) => f(m)); if (i >= 0) waiters.splice(i, 1)[0][1](m); else inbox.push(m); });
  const next = (f: (m: any) => boolean) => new Promise<any>((res, rej) => { const i = inbox.findIndex(f); if (i >= 0) return res(inbox.splice(i, 1)[0]); waiters.push([f, res]); setTimeout(() => rej(new Error("timeout")), 3000); });
  const ask = (msg: any, t: string) => { ws.send(JSON.stringify(msg)); return next((m) => m.t === t || (m.t === "error" && m.ref === msg.t)); };
  return { ws, next, ask };
}
const siws = (kp: Keypair, nonce: string) => {
  const message = `lamby4114.github.io wants you to sign in with your Solana account:\n${kp.publicKey.toBase58()}\n\nSign in to NUGSHIP.\n\nURI: https://lamby4114.github.io/nugship/\nVersion: 1\nChain ID: mainnet\nNonce: ${nonce}\nIssued At: ${new Date().toISOString()}`;
  return { message, signature: bs58.encode(nacl.sign.detached(Buffer.from(message, "utf8"), kp.secretKey)) };
};

test("leave_match during placement: no result recorded, no in_match lock; too late once the fleet is placed", async () => {
  const store = new Store(":memory:"); const source = new PracticeRoundSource({});
  const server = new GameServer({ port: 0, store, source, reader: null, log: () => {} });
  const port = await server.listen();
  try {
    const a = open(port); await a.next((m) => m.t === "challenge");
    await a.ask({ t: "hello", name: "Leaver", guestKey: crypto.randomBytes(16).toString("hex") }, "welcome");
    const before = (store.counts() as any).matches;
    const mf = await a.ask({ t: "start_solo", captain: "barnacle", mode: "live", stakeLamports: "1000000" }, "match_found");
    assert.equal(mf.t, "match_found");
    const left = await a.ask({ t: "leave_match", matchId: mf.matchId }, "match_left");
    assert.equal(left.t, "match_left"); assert.equal(left.recorded, false);
    await new Promise((r) => setTimeout(r, 100));
    assert.equal((store.counts() as any).matches, before, "no match row recorded");
    const st = await a.ask({ t: "get_state" }, "match_state"); assert.equal(st.t, "error"); assert.equal(st.code, "no_match");
    const mf2 = await a.ask({ t: "start_solo", captain: "barnacle", mode: "live", stakeLamports: "1000000" }, "match_found");
    assert.equal(mf2.t, "match_found", "new solo starts straight away (no in_match)");
    const layout = randomLayout(), salt = crypto.randomBytes(32).toString("hex");
    const ack = await a.ask({ t: "place_fleet", matchId: mf2.matchId, layout, salt, commit: commitLayout(layout, salt) }, "fleet_ack");
    assert.equal(ack.t, "fleet_ack");
    const late = await a.ask({ t: "leave_match", matchId: mf2.matchId }, "match_left");
    assert.equal(late.t, "error"); assert.equal(late.code, "too_late");
    await a.ask({ t: "resign" }, "match_end"); a.ws.close();
  } finally { server.close(); source.stop(); }
});

test("set_profile on a wallet needs a verified session; SIWS-verified wallet saves its name", async () => {
  const store = new Store(":memory:"); const source = new PracticeRoundSource({});
  const server = new GameServer({ port: 0, store, source, reader: null, requireWalletProof: true, log: () => {} });
  const port = await server.listen();
  const kp = Keypair.generate(), wallet = kp.publicKey.toBase58();
  try {
    const u = open(port); await u.next((m) => m.t === "challenge");
    const wu = await u.ask({ t: "hello", name: "X", wallet }, "welcome"); assert.equal(wu.verified, false);
    const e = await u.ask({ t: "set_profile", name: "IMPOSTOR" }, "profile"); assert.equal(e.t, "error"); assert.equal(e.code, "wallet_unverified");
    u.ws.close();
    const v = open(port); const ch = await v.next((m) => m.t === "challenge");
    const wv = await v.ask({ t: "hello", name: "X", wallet, signIn: siws(kp, ch.nonce) }, "welcome"); assert.equal(wv.verified, true);
    const p = await v.ask({ t: "set_profile", name: "SEA LAMB" }, "profile"); assert.equal(p.t, "profile"); assert.equal(p.name, "SEA LAMB");
    v.ws.close();
    const again = open(port); await again.next((m) => m.t === "challenge");
    const w3 = await again.ask({ t: "hello", name: "X", wallet }, "welcome");
    assert.equal(w3.nameClaimed, true); assert.equal(w3.name, "SEA LAMB");   // persisted per wallet on the server
    again.ws.close();
    assert.ok(GameServer.NONCE_TTL_MS >= 20 * 60_000, "nonce TTL covers a slow wallet sign-in");
  } finally { server.close(); source.stop(); }
});
