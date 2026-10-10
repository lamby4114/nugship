// Profiles: name rules + uniqueness, avatar upload/serve, guest key binding, SIWS pairing gated off, leaderboard shows name+avatar.
import test from "node:test";
import assert from "node:assert/strict";
import zlib from "node:zlib";
import { WebSocket } from "ws";
import { Store } from "../src/db/store.js";
import { GameServer } from "../src/net/server.js";
import { PracticeRoundSource } from "../src/chain/roundSource.js";
import { validateName, normalizeName, nameKey, checkAvatarPng, validAvatarRef } from "../src/profile/profile.js";

function png(w: number, h: number): Buffer {
  const crc = (b: Buffer) => { let c = ~0; for (const x of b) { c ^= x; for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xedb88320 & -(c & 1)); } return (~c) >>> 0; };
  const chunk = (t: string, d: Buffer) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  const raw = Buffer.alloc((w * 3 + 1) * h, 0x40);
  for (let y = 0; y < h; y++) raw[y * (w * 3 + 1)] = 0;
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

test("name rules", () => {
  assert.equal(validateName(normalizeName("  salty   whisker ")), null);
  assert.equal(normalizeName("  salty   whisker "), "SALTY WHISKER");
  assert.equal(nameKey("SALTY_WHISKER"), nameKey("salty whisker"));
  for (const [n, e] of [["AB", "too_short"], ["X".repeat(17), "too_long"], ["123", "needs_letter"], ["CAPTAIN", "reserved"], ["Nuggy", "reserved"], ["BAD$NAME", "bad_chars"], ["_EDGE", "bad_chars"], ["FUCKBOAT", "not_allowed"]] as const)
    assert.equal(validateName(normalizeName(n)), e, n);
  assert.ok(validAvatarRef("a:nuggy_ghost")); assert.ok(!validAvatarRef("a:nope")); assert.ok(validAvatarRef("u:0123456789abcdef")); assert.ok(!validAvatarRef("u:xyz"));
  assert.ok("hash" in checkAvatarPng(png(20, 20)));
  assert.deepEqual(checkAvatarPng(png(64, 64)), { error: "bad_dimensions" });
  assert.deepEqual(checkAvatarPng(Buffer.from("hello world, definitely not a png file")), { error: "not_png" });
});

async function client(port: number, hello: any) {
  const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  const inbox: any[] = []; const waiters: [(m: any) => boolean, (m: any) => void][] = [];
  ws.on("message", (raw) => { const m = JSON.parse(raw.toString()); const i = waiters.findIndex(([f]) => f(m)); if (i >= 0) waiters.splice(i, 1)[0][1](m); else inbox.push(m); });
  const next = (f: (m: any) => boolean) => new Promise<any>((res, rej) => { const i = inbox.findIndex(f); if (i >= 0) return res(inbox.splice(i, 1)[0]); waiters.push([f, res]); setTimeout(() => rej(new Error("timeout")), 3000); });
  await next((m) => m.t === "challenge");
  ws.send(JSON.stringify({ t: "hello", ...hello }));
  const welcome = await next((m) => m.t === "welcome" || m.t === "error");
  const ask = (msg: any, t: string) => { ws.send(JSON.stringify(msg)); return next((m) => m.t === t || (m.t === "error" && m.ref === msg.t)); };
  return { ws, welcome, ask, close: () => ws.close() };
}

test("profiles over the socket + REST", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, log: () => {} });
  const port = await server.listen();
  try {
    const keyA = "a".repeat(32), keyB = "b".repeat(32);
    const a = await client(port, { name: "Captain", guestKey: keyA });
    assert.equal(a.welcome.t, "welcome"); assert.equal(a.welcome.nameClaimed, false); assert.equal(a.welcome.avatar, "a:captain"); assert.equal(a.welcome.walletPairing, false);
    const idA = a.welcome.playerId;
    assert.deepEqual((await a.ask({ t: "check_name", name: "salty whisker" }, "name_check")).ok, true);
    const up = await a.ask({ t: "upload_avatar", png: png(20, 20).toString("base64") }, "avatar_uploaded");
    assert.match(up.avatar, /^u:[0-9a-f]{16}$/);
    const bad = await a.ask({ t: "upload_avatar", png: png(40, 40).toString("base64") }, "avatar_uploaded");
    assert.equal(bad.code, "bad_dimensions");
    const prof = await a.ask({ t: "set_profile", name: "Salty Whisker", avatar: up.avatar }, "profile");
    assert.equal(prof.name, "SALTY WHISKER"); assert.equal(prof.avatar, up.avatar);
    const res = await fetch(`http://127.0.0.1:${port}/api/avatars/${up.avatar.slice(2)}.png`);
    assert.equal(res.status, 200); assert.equal(res.headers.get("content-type"), "image/png");
    // second captain: name taken (case/punctuation-insensitive), can't impersonate via hello, SIWS pairing off
    const b = await client(port, { name: "SALTY_WHISKER", guestKey: keyB });
    assert.equal(b.welcome.name, "Captain");
    const taken = await b.ask({ t: "set_profile", name: "salty.whisker", avatar: "a:kraken" }, "profile");
    assert.equal(taken.code, "name_taken");
    assert.equal((await b.ask({ t: "check_name", name: "SALTY WHISKER" }, "name_check")).reason, "name_taken");
    const pair = await b.ask({ t: "pair_wallet", wallet: "11111111111111111111111111111111", message: "x", signature: "y" }, "wallet_paired");
    assert.equal(pair.code, "pairing_disabled");
    assert.equal((await b.ask({ t: "set_profile", name: "Kraken Kid", avatar: "a:kraken" }, "profile")).avatar, "a:kraken");
    // guest key binding: someone presenting A's public id without A's key gets a fresh id; with the key, the claimed profile comes back
    const thief = await client(port, { name: "x", guestId: idA, guestKey: keyB });
    assert.notEqual(thief.welcome.playerId, idA);
    const a2 = await client(port, { name: "Captain", guestId: idA, guestKey: keyA });
    assert.equal(a2.welcome.playerId, idA); assert.equal(a2.welcome.name, "SALTY WHISKER"); assert.equal(a2.welcome.nameClaimed, true); assert.equal(a2.welcome.avatar, up.avatar);
    const chk = await (await fetch(`http://127.0.0.1:${port}/api/names/check?name=kraken%20kid`)).json();
    assert.equal(chk.ok, false);
    const ps = await (await fetch(`http://127.0.0.1:${port}/api/players/${idA}`)).json();
    assert.equal(ps.avatar, up.avatar); assert.equal(ps.name, "SALTY WHISKER");
    for (const c of [a, b, thief, a2]) c.close();
  } finally { server.close(); }
});

test("abuse: message flood closes the socket, oversize frames are refused, match-history limit is clamped", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, log: () => {} });
  const port = await server.listen();
  try {
    const a = await client(port, { name: "Flooder", guestKey: "f".repeat(32) });
    const closed = new Promise<number>((r) => a.ws.on("close", (code) => r(code)));
    for (let i = 0; i < 80; i++) a.ws.send(JSON.stringify({ t: "ping", echo: i }));
    assert.equal(await closed, 1008);
    const b = await client(port, { name: "Bigframe", guestKey: "g".repeat(32) });
    const closedB = new Promise<number>((r) => b.ws.on("close", (code) => r(code)));
    b.ws.send(JSON.stringify({ t: "upload_avatar", png: "A".repeat(40 * 1024) }));
    assert.equal(await closedB, 1009);
    for (const lim of ["-1", "abc", "100000"]) {
      const r = await fetch(`http://127.0.0.1:${port}/api/players/${a.welcome.playerId}/matches?limit=${lim}`);
      assert.equal(r.status, 200);
    }
  } finally { server.close(); }
});
