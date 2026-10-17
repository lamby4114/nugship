// E2E against the DEPLOYED server (guest, nothing on chain): stake chosen at placement (place_fleet.stakeLamports) is the match stake,
// then a relaunch with the resume token sees activeMatch and a new start_solo resigns the abandoned one instead of in_match.
import { WebSocket } from "ws";
import crypto from "node:crypto";
import { randomLayout, commitLayout } from "../src/game/fleet.js";
const URL = process.env.URL ?? "wss://nugship-server.fly.dev/ws";
const log = (...a: any[]) => console.log(new Date().toISOString().slice(11, 19), ...a);
function open() {
  const ws = new WebSocket(URL); const inbox: any[] = []; const waiters: [(m: any) => boolean, (m: any) => void][] = [];
  ws.on("message", (raw) => { const m = JSON.parse(raw.toString()); const i = waiters.findIndex(([f]) => f(m)); if (i >= 0) waiters.splice(i, 1)[0][1](m); else inbox.push(m); });
  const next = (f: (m: any) => boolean, ms = 8000) => new Promise<any>((res, rej) => { const i = inbox.findIndex(f); if (i >= 0) return res(inbox.splice(i, 1)[0]); waiters.push([f, res]); setTimeout(() => rej(new Error("timeout")), ms); });
  const ask = (msg: any, t: string) => { ws.send(JSON.stringify(msg)); return next((m) => m.t === t || (m.t === "error" && m.ref === msg.t)); };
  return { ws, next, ask };
}
let fail = 0; const check = (ok: boolean, what: string) => { log(ok ? "PASS" : "FAIL", what); if (!ok) fail++; };
const guestKey = crypto.randomBytes(16).toString("hex");
const A = open(); await A.next((m) => m.t === "challenge");
const w = await A.ask({ t: "hello", name: "E2E", guestKey }, "welcome"); check(w.t === "welcome", "hello " + w.playerId);
const mf = await A.ask({ t: "start_solo", captain: "barnacle", mode: "live", stakeLamports: "1000000" }, "match_found");
check(mf.t === "match_found" && mf.mode === "solo_live" && mf.stakeLamports === "1000000", `start_solo live stake=${mf.stakeLamports} fireMode=${mf.fireMode}`);
const layout = randomLayout(), salt = crypto.randomBytes(32).toString("hex"), commit = commitLayout(layout, salt);
const ack = await A.ask({ t: "place_fleet", matchId: mf.matchId, layout, salt, commit, stakeLamports: "10000000" }, "fleet_ack");
check(ack.t === "fleet_ack" && ack.stakeLamports === "10000000", "place_fleet with SOL PER CANNON 0.010 -> fleet_ack stake=" + ack.stakeLamports);
const st = await A.ask({ t: "get_state" }, "match_state"); check(st.stakeLamports === "10000000", "match_state stake=" + st.stakeLamports);
A.ws.terminate(); log("socket A killed (app relaunch)");
const B = open(); await B.next((m) => m.t === "challenge");
const w2 = await B.ask({ t: "hello", name: "E2E", guestId: w.playerId, guestKey, resume: { playerId: w.playerId, token: w.token } }, "welcome");
check(w2.activeMatch === mf.matchId, "relaunch with token: activeMatch=" + w2.activeMatch);
const mf2 = await B.ask({ t: "start_solo", captain: "barnacle", mode: "live", stakeLamports: "5000000" }, "match_found");
check(mf2.t === "match_found" && mf2.matchId !== mf.matchId, "new start_solo after relaunch: " + (mf2.t === "error" ? mf2.code : "new match " + mf2.matchId));
const ack2 = await B.ask({ t: "place_fleet", matchId: mf2.matchId, layout, salt, commit }, "fleet_ack");
check(ack2.stakeLamports === "5000000", "old-client place_fleet (no stake) keeps start stake=" + ack2.stakeLamports);
await B.ask({ t: "resign" }, "match_end");
// v1.1.0 HARBOR on the placement screen: leave_match drops the solo with no result and no lock
const h0 = await (await fetch(URL.replace("wss://", "https://").replace("/ws", "/health"))).json() as any;
const mf3 = await B.ask({ t: "start_solo", captain: "barnacle", mode: "live", stakeLamports: "1000000" }, "match_found");
const left = await B.ask({ t: "leave_match", matchId: mf3.matchId }, "match_left");
check(left.t === "match_left" && left.recorded === false, "leave_match during placement -> " + (left.t === "error" ? left.code : "match_left recorded=" + left.recorded));
await new Promise((r) => setTimeout(r, 500));
const h1 = await (await fetch(URL.replace("wss://", "https://").replace("/ws", "/health"))).json() as any;
check(h1.db.matches === h0.db.matches, `no match row recorded (db.matches ${h0.db.matches} -> ${h1.db.matches})`);
const mf4 = await B.ask({ t: "start_solo", captain: "barnacle", mode: "live", stakeLamports: "1000000" }, "match_found");
check(mf4.t === "match_found", "next start_solo after leaving: " + (mf4.t === "error" ? mf4.code : "ok " + mf4.matchId));
await B.ask({ t: "leave_match", matchId: mf4.matchId }, "match_left");
B.ws.close();
log(fail ? `${fail} FAILED` : "ALL PASS"); process.exit(fail ? 1 : 0);
