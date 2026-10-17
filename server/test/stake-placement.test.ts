// v1.1.0: SOL PER CANNON is chosen on the placement screen and sent with place_fleet (optional; older clients omit it).
import test from "node:test";
import assert from "node:assert/strict";
import { WebSocket } from "ws";
import crypto from "node:crypto";
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
async function solo(port: number, stake: string) {
  const s = open(port); await s.next((m) => m.t === "challenge");
  const w = await s.ask({ t: "hello", name: "Tester", guestKey: crypto.randomBytes(16).toString("hex") }, "welcome");
  assert.equal(w.t, "welcome");
  const mf = await s.ask({ t: "start_solo", captain: "barnacle", mode: "practice", stakeLamports: stake }, "match_found");
  assert.equal(mf.t, "match_found"); assert.equal(mf.stakeLamports, stake);
  const layout = randomLayout(), salt = crypto.randomBytes(32).toString("hex");
  return { s, mf, fleet: { layout, salt, commit: commitLayout(layout, salt) } };
}

test("stake sent with place_fleet sets SOL PER CANNON; invalid refused; locked after placement; omitted keeps the start stake", async () => {
  const source = new PracticeRoundSource({});
  const server = new GameServer({ port: 0, store: new Store(":memory:"), source, reader: null, log: () => {} });
  const port = await server.listen();
  try {
    const a = await solo(port, "1000000");
    const bad = await a.s.ask({ t: "place_fleet", matchId: a.mf.matchId, ...a.fleet, stakeLamports: "7777" }, "fleet_ack");
    assert.equal(bad.t, "error"); assert.equal(bad.code, "bad_stake");
    const ok = await a.s.ask({ t: "place_fleet", matchId: a.mf.matchId, ...a.fleet, stakeLamports: "10000000" }, "fleet_ack");
    assert.equal(ok.t, "fleet_ack"); assert.equal(ok.stakeLamports, "10000000");
    const st = await a.s.ask({ t: "get_state" }, "match_state");
    assert.equal(st.stakeLamports, "10000000");
    const again = await a.s.ask({ t: "place_fleet", matchId: a.mf.matchId, ...a.fleet, stakeLamports: "1000000" }, "fleet_ack");
    assert.equal(again.t, "error");
    await a.s.ask({ t: "resign" }, "match_end"); a.s.ws.close();
    const b = await solo(port, "5000000");   // older client: no stake on place_fleet
    const okB = await b.s.ask({ t: "place_fleet", matchId: b.mf.matchId, ...b.fleet }, "fleet_ack");
    assert.equal(okB.t, "fleet_ack"); assert.equal(okB.stakeLamports, "5000000");
    await b.s.ask({ t: "resign" }, "match_end"); b.s.ws.close();
  } finally { server.close(); source.stop(); }
});
