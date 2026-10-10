// Store policy: report names/portraits (auto take-down at threshold), block (no pairing), in-app account deletion,
// and scripted/bot rows tagged AI on the leaderboard.
import test from "node:test";
import assert from "node:assert/strict";
import zlib from "node:zlib";
import { WebSocket } from "ws";
import { Store } from "../src/db/store.js";
import { GameServer } from "../src/net/server.js";
import { PracticeRoundSource } from "../src/chain/roundSource.js";

function png(w: number, h: number, fill = 0x40): Buffer {
  const crc = (b: Buffer) => { let c = ~0; for (const x of b) { c ^= x; for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xedb88320 & -(c & 1)); } return (~c) >>> 0; };
  const chunk = (t: string, d: Buffer) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  const raw = Buffer.alloc((w * 3 + 1) * h, fill);
  for (let y = 0; y < h; y++) raw[y * (w * 3 + 1)] = 0;
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

async function client(port: number, hello: any) {
  const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  const inbox: any[] = []; const waiters: [(m: any) => boolean, (m: any) => void][] = [];
  ws.on("message", (raw) => { const m = JSON.parse(raw.toString()); const i = waiters.findIndex(([f]) => f(m)); if (i >= 0) waiters.splice(i, 1)[0][1](m); else inbox.push(m); });
  const next = (f: (m: any) => boolean, ms = 3000) => new Promise<any>((res, rej) => { const i = inbox.findIndex(f); if (i >= 0) return res(inbox.splice(i, 1)[0]); waiters.push([f, res]); setTimeout(() => rej(new Error("timeout")), ms); });
  await next((m) => m.t === "challenge");
  ws.send(JSON.stringify({ t: "hello", ...hello }));
  const welcome = await next((m) => m.t === "welcome" || m.t === "error");
  const ask = (msg: any, t: string) => { ws.send(JSON.stringify(msg)); return next((m) => m.t === t || (m.t === "error" && m.ref === msg.t)); };
  return { ws, welcome, ask, next, inbox, close: () => ws.close() };
}

const stats = { volleys: 3, shots: 6, hits: 2, sunk: 1, crits: 0, oreGrams: 0n, misfires: 0, kraken: 0, lamportsDeployed: 0n, solReturnedLamports: 0n };
function fakeMatch(store: Store, id: string, a: { id: string; name: string }, b: { id: string; name: string }) {
  const now = Date.now();
  store.recordMatch({
    matchId: id, mode: "private" as any, winnerSeat: 0, reason: "sunk", turns: 3, startedAt: now - 1000, endedAt: now,
    players: [
      { seat: 0, id: a.id, name: a.name, wallet: null, isAI: false, result: "win", stats, commit: "", layout: [] as any, salt: "", commitVerified: true },
      { seat: 1, id: b.id, name: b.name, wallet: null, isAI: false, result: "loss", stats, commit: "", layout: [] as any, salt: "", commitVerified: true },
    ],
    rounds: [], stakeLamports: "0", sourceKind: "practice",
  } as any);
}

test("report: once per reporter, take-down at threshold (name unclaimed, portrait hidden), limits + bad targets", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, log: () => {}, reportThreshold: 2 });
  const port = await server.listen();
  try {
    const bad = await client(port, { name: "x", guestKey: "1".repeat(32) });
    const up = await bad.ask({ t: "upload_avatar", png: png(20, 20, 0x77).toString("base64") }, "avatar_uploaded");
    assert.equal((await bad.ask({ t: "set_profile", name: "Rude Dude", avatar: up.avatar }, "profile")).name, "RUDE DUDE");
    const target = bad.welcome.playerId;
    const r1 = await client(port, { name: "y", guestKey: "2".repeat(32) });
    const r2 = await client(port, { name: "z", guestKey: "3".repeat(32) });
    assert.equal((await r1.ask({ t: "report_player", playerId: target, kind: "name", reason: "rude" }, "report_ack")).duplicate, false);
    assert.equal((await r1.ask({ t: "report_player", playerId: target, kind: "name" }, "report_ack")).duplicate, true);
    assert.equal(store.getPlayer(target)!.name, "RUDE DUDE"); // one reporter (twice) is not enough
    assert.equal((await r1.ask({ t: "report_player", playerId: target, kind: "bogus" }, "report_ack")).code, "bad_kind");
    assert.equal((await r1.ask({ t: "report_player", playerId: r1.welcome.playerId, kind: "name" }, "report_ack")).code, "bad_target");
    assert.equal((await r1.ask({ t: "report_player", playerId: "ai:barnacle", kind: "name" }, "report_ack")).code, "bad_target");
    assert.equal((await r1.ask({ t: "report_player", playerId: "g_ffffffffffffffff", kind: "name" }, "report_ack")).code, "unknown_player");
    await r2.ask({ t: "report_player", playerId: target, kind: "name" }, "report_ack");
    let p = store.getPlayer(target)!;
    assert.equal(p.name, "Captain"); assert.equal(p.claimed, 0);
    assert.equal((await (await fetch(`http://127.0.0.1:${port}/api/names/check?name=rude%20dude`)).json()).ok, true); // name freed
    // portrait: hidden + reverted, can't be re-used
    await r1.ask({ t: "report_player", playerId: target, kind: "avatar" }, "report_ack");
    await r2.ask({ t: "report_player", playerId: target, kind: "avatar" }, "report_ack");
    p = store.getPlayer(target)!;
    assert.equal(p.avatar, null);
    assert.equal((await fetch(`http://127.0.0.1:${port}/api/avatars/${up.avatar.slice(2)}.png`)).status, 404);
    assert.equal((await bad.ask({ t: "set_profile", name: "Rude Dude", avatar: up.avatar }, "profile")).code, "bad_avatar");
    assert.equal(store.reportsFor(target).length, 4);
    assert.ok(store.reportsFor(target).every((r) => r.resolved === 1));
    // daily cap per reporter
    const ids: string[] = [];
    for (let i = 0; i < 31; i++) { const id = `g_${i.toString(16).padStart(16, "0")}`; store.upsertPlayer(id, "Victim", null); ids.push(id); }
    let last: any;
    for (const id of ids) last = await r1.ask({ t: "report_player", playerId: id, kind: "name" }, "report_ack");
    assert.equal(last.code, "report_limit");
    for (const c of [bad, r1, r2]) c.close();
  } finally { server.close(); }
});

test("block: listed in welcome, private codes and the ranked queue never pair blocked captains; unblock restores", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, log: () => {}, allowGuestRanked: true });
  const port = await server.listen();
  try {
    const a = await client(port, { name: "x", guestKey: "4".repeat(32) });
    const b = await client(port, { name: "y", guestKey: "5".repeat(32) });
    const idA = a.welcome.playerId, idB = b.welcome.playerId;
    assert.deepEqual((await a.ask({ t: "block_player", playerId: idB }, "blocked")).blocked, [idB]);
    assert.equal((await a.ask({ t: "block_player", playerId: idA }, "blocked")).code, "bad_target");
    const a2 = await client(port, { name: "x", guestId: idA, guestKey: "4".repeat(32) });
    assert.deepEqual(a2.welcome.blocked, [idB]);
    const code = (await b.ask({ t: "create_private", fireMode: "virtual" }, "private_created")).code;
    assert.equal((await a2.ask({ t: "join_private", code }, "match_found")).code, "bad_code"); // blocked looks like a bad code
    await a2.ask({ t: "queue_ranked" }, "queued"); await b.ask({ t: "queue_ranked" }, "queued");
    await new Promise((r) => setTimeout(r, 2300));
    assert.ok(!a2.inbox.some((m) => m.t === "match_found") && !b.inbox.some((m) => m.t === "match_found"), "blocked pair matched");
    assert.deepEqual((await a2.ask({ t: "unblock_player", playerId: idB }, "blocked")).blocked, []);
    await a2.next((m) => m.t === "match_found", 3000);
    for (const c of [a, b, a2]) c.close();
  } finally { server.close(); }
});

test("delete_account: needs confirm, wipes profile/stats/portrait/reports/blocks, scrubs match records, frees the name", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({ aimSeconds: 2, tickMs: 20 }), reader: null, log: () => {}, aiDelayMs: 10 });
  const port = await server.listen();
  try {
    const keyA = "6".repeat(32);
    const a = await client(port, { name: "x", guestKey: keyA });
    const b = await client(port, { name: "y", guestKey: "7".repeat(32) });
    const idA = a.welcome.playerId, idB = b.welcome.playerId;
    const up = await a.ask({ t: "upload_avatar", png: png(20, 20, 0x99).toString("base64") }, "avatar_uploaded");
    await a.ask({ t: "set_profile", name: "Gone Soon", avatar: up.avatar }, "profile");
    await b.ask({ t: "set_profile", name: "Stays Here", avatar: "a:kraken" }, "profile");
    fakeMatch(store, "m_del", { id: idA, name: "GONE SOON" }, { id: idB, name: "STAYS HERE" });
    await a.ask({ t: "block_player", playerId: idB }, "blocked");
    await b.ask({ t: "report_player", playerId: idA, kind: "name" }, "report_ack");
    let lb = await (await fetch(`http://127.0.0.1:${port}/api/leaderboard?mode=all`)).json();
    assert.equal(lb.entries.length, 2);
    assert.equal((await a.ask({ t: "delete_account" }, "account_deleted")).code, "confirm_required");
    // mid-match deletion: resigns, and the result row is scrubbed after the referee records it
    await a.ask({ t: "start_solo", captain: "barnacle", mode: "practice" }, "match_found");
    const closed = new Promise<number>((r) => a.ws.on("close", (c) => r(c)));
    assert.equal((await a.ask({ t: "delete_account", confirm: "DELETE" }, "account_deleted")).playerId, idA);
    assert.equal(await closed, 1000);
    await new Promise((r) => setTimeout(r, 100));
    assert.equal(store.getPlayer(idA), undefined);
    assert.equal((await fetch(`http://127.0.0.1:${port}/api/players/${idA}`)).status, 404);
    assert.equal((await fetch(`http://127.0.0.1:${port}/api/avatars/${up.avatar.slice(2)}.png`)).status, 404);
    lb = await (await fetch(`http://127.0.0.1:${port}/api/leaderboard?mode=all`)).json();
    assert.deepEqual(lb.entries.map((e: any) => e.id), [idB]);
    const rec = store.getMatch("m_del");
    assert.equal(rec.players[0].name, "DELETED CAPTAIN"); assert.equal(rec.players[0].id, "deleted"); assert.equal(rec.players[1].name, "STAYS HERE");
    assert.ok(!JSON.stringify(store.db.prepare("SELECT json FROM matches").all()).includes(idA));
    assert.equal(store.reportsFor(idA).length, 0);
    assert.equal((store.db.prepare("SELECT COUNT(*) AS n FROM blocks").get() as any).n, 0);
    assert.equal((await (await fetch(`http://127.0.0.1:${port}/api/names/check?name=gone%20soon`)).json()).ok, true);
    // the same phone (same guest id + key) comes back as a fresh, unclaimed captain
    const again = await client(port, { name: "Captain", guestId: idA, guestKey: keyA });
    assert.equal(again.welcome.nameClaimed, false); assert.equal(again.welcome.avatar, "a:captain");
    for (const c of [b, again]) c.close();
  } finally { server.close(); }
});

test("leaderboard tags scripted/bot accounts as AI (hello bot:true, demo seed)", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({}), reader: null, log: () => {} });
  const port = await server.listen();
  try {
    const bot = await client(port, { name: "Hunter", guestKey: "8".repeat(32), bot: true });
    const human = await client(port, { name: "Real", guestKey: "9".repeat(32) });
    fakeMatch(store, "m_ai", { id: bot.welcome.playerId, name: "Hunter" }, { id: human.welcome.playerId, name: "Real" });
    const lb = await (await fetch(`http://127.0.0.1:${port}/api/leaderboard?mode=all`)).json();
    const by = Object.fromEntries(lb.entries.map((e: any) => [e.id, e.ai]));
    assert.equal(by[bot.welcome.playerId], true); assert.equal(by[human.welcome.playerId], false);
    assert.equal((await (await fetch(`http://127.0.0.1:${port}/api/players/${bot.welcome.playerId}`)).json()).ai, true);
    bot.close(); human.close();
  } finally { server.close(); }
});
