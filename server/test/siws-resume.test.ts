// v1.1.0: a SIWS proof signed over the nonce of a socket that died during the wallet round-trip (Phantom) is accepted ONCE
// on the next socket (5 min TTL), and a verified wallet stays verified when it resumes its session on a new socket.
import test from "node:test";
import assert from "node:assert/strict";
import { WebSocket } from "ws";
import nacl from "tweetnacl";
import bs58 from "bs58";
import { Keypair } from "@solana/web3.js";
import { Store } from "../src/db/store.js";
import { GameServer } from "../src/net/server.js";
import { PracticeRoundSource } from "../src/chain/roundSource.js";

function open(port: number) {
  const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  const inbox: any[] = []; const waiters: [(m: any) => boolean, (m: any) => void][] = [];
  ws.on("message", (raw) => { const m = JSON.parse(raw.toString()); const i = waiters.findIndex(([f]) => f(m)); if (i >= 0) waiters.splice(i, 1)[0][1](m); else inbox.push(m); });
  const next = (f: (m: any) => boolean) => new Promise<any>((res, rej) => { const i = inbox.findIndex(f); if (i >= 0) return res(inbox.splice(i, 1)[0]); waiters.push([f, res]); setTimeout(() => rej(new Error("timeout")), 3000); });
  const hello = async (h: any) => { ws.send(JSON.stringify({ t: "hello", name: "Tester", ...h })); return next((m) => m.t === "welcome" || m.t === "error"); };
  const ask = (msg: any, t: string) => { ws.send(JSON.stringify(msg)); return next((m) => m.t === t || (m.t === "error" && m.ref === msg.t)); };
  return { ws, next, hello, ask, challenge: () => next((m) => m.t === "challenge"), close: () => ws.close() };
}
const siws = (kp: Keypair, nonce: string) => {
  const message = `lamby4114.github.io wants you to sign in with your Solana account:\n${kp.publicKey.toBase58()}\n\nSign in to NUGSHIP.\n\nURI: https://lamby4114.github.io/nugship/\nVersion: 1\nChain ID: mainnet\nNonce: ${nonce}\nIssued At: ${new Date().toISOString()}`;
  return { message, signature: bs58.encode(nacl.sign.detached(Buffer.from(message, "utf8"), kp.secretKey)) };
};
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

test("SIWS over a dead socket's nonce verifies once on the next socket; resume keeps verification; ranked + live solo", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, requireWalletProof: true, log: () => {} });
  const port = await server.listen();
  const kp = Keypair.generate(), wallet = kp.publicKey.toBase58();
  try {
    // socket A: get a nonce, then die (the phone went to Phantom)
    const a = open(port); const chA = await a.challenge(); a.close(); await sleep(50);
    const proof = siws(kp, chA.nonce);
    // socket B: hello carrying the proof for A's nonce -> verified
    const b = open(port); await b.challenge();
    const wB = await b.hello({ wallet, signIn: proof });
    assert.equal(wB.t, "welcome"); assert.equal(wB.verified, true); assert.equal(wB.playerId, wallet);
    // ranked accepts a verified wallet
    const q = await b.ask({ t: "queue_ranked", stakeLamports: "1000000", fireMode: "manual" }, "queued");
    assert.equal(q.t, "queued");
    await b.ask({ t: "cancel_queue" }, "queue_left");
    b.close(); await sleep(50);
    // replay: the same proof on socket C is refused (single use)
    const c = open(port); await c.challenge();
    const wC = await c.hello({ wallet, signIn: proof });
    assert.equal(wC.t, "error"); assert.equal(wC.code, "bad_signature");
    c.close(); await sleep(50);
    // resume on socket D with B's token, no signature -> still verified
    const d = open(port); await d.challenge();
    const wD = await d.hello({ wallet, resume: { playerId: wallet, token: wB.token } });
    assert.equal(wD.t, "welcome"); assert.equal(wD.verified, true);
    // wrong token -> unverified (no free verification)
    const e = open(port); await e.challenge();
    const wE = await e.hello({ wallet, resume: { playerId: wallet, token: "00".repeat(16) } });
    assert.equal(wE.verified, false);
    const qE = await e.ask({ t: "queue_ranked", stakeLamports: "1000000", fireMode: "manual" }, "queued");
    assert.equal(qE.t, "error"); assert.equal(qE.code, "wallet_unverified");
    e.close();
    // live solo on D: manual fire with the requested stake (not practice/virtual)
    const mf = await d.ask({ t: "start_solo", captain: "barnacle", mode: "live", fireMode: "manual", stakeLamports: "1000000" }, "match_found");
    assert.equal(mf.t, "match_found"); assert.equal(mf.fireMode, "manual"); assert.equal(mf.stakeLamports, "1000000"); assert.notEqual(mf.mode, "practice");
    d.close(); await sleep(50);
    // app killed mid-match, relaunched with the persisted resume token: the old solo must not block a new one (was "in_match")
    const f = open(port); await f.challenge();
    const wF = await f.hello({ wallet, resume: { playerId: wallet, token: wB.token } });
    assert.equal(wF.t, "welcome", JSON.stringify(wF).slice(0, 200)); assert.equal(wF.activeMatch, mf.matchId);
    const mf2 = await f.ask({ t: "start_solo", captain: "barnacle", mode: "live", fireMode: "manual", stakeLamports: "1000000" }, "match_found");
    assert.equal(mf2.t, "match_found"); assert.notEqual(mf2.matchId, mf.matchId);
    f.close();
  } finally { server.close(); }
});

test("SIWS refused: other wallet's signature, unknown nonce, expired nonce", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, requireWalletProof: true, log: () => {} });
  const port = await server.listen();
  const kp = Keypair.generate(), other = Keypair.generate();
  try {
    const a = open(port); const ch = await a.challenge();
    const forged = siws(other, ch.nonce); // signed by someone else, claiming kp's wallet
    const w1 = await a.hello({ wallet: kp.publicKey.toBase58(), signIn: { message: forged.message.replace(other.publicKey.toBase58(), kp.publicKey.toBase58()), signature: forged.signature } });
    assert.equal(w1.code, "bad_signature");
    const b = open(port); await b.challenge();
    const w2 = await b.hello({ wallet: kp.publicKey.toBase58(), signIn: siws(kp, "ab".repeat(16)) }); // never issued
    assert.equal(w2.code, "bad_signature");
    const c = open(port); const chC = await c.challenge(); c.close(); await sleep(30);
    (server as any).recentNonces.set(chC.nonce, Date.now() - GameServer.NONCE_TTL_MS - 1000); // age it past the TTL
    const d = open(port); await d.challenge();
    const w3 = await d.hello({ wallet: kp.publicKey.toBase58(), signIn: siws(kp, chC.nonce) });
    assert.equal(w3.code, "bad_signature");
    for (const s of [a, b, d]) s.close();
  } finally { server.close(); }
});
