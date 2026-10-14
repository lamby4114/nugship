// E2E against the DEPLOYED server: SIWS over a dead socket's nonce -> verified on the next socket, resume keeps it,
// live solo (manual fire, stake), real lock{fire:true} -> exact client volley tx simulated on mainnet. Nothing sent.
import { WebSocket } from "ws"; import nacl from "tweetnacl"; import bs58 from "bs58"; import { Keypair } from "@solana/web3.js";
import { execFileSync } from "node:child_process";
import { randomLayout, commitLayout } from "../src/game/fleet.js"; import crypto from "node:crypto";
const URL = process.env.URL ?? "wss://nugship-server.fly.dev/ws"; const SIMWALLET = process.env.SIMWALLET!;
const log = (...a: any[]) => console.log(new Date().toISOString().slice(11, 19), ...a);
function open() {
  const ws = new WebSocket(URL); const inbox: any[] = []; const waiters: [(m: any) => boolean, (m: any) => void][] = [];
  ws.on("message", (raw) => { const m = JSON.parse(raw.toString()); const i = waiters.findIndex(([f]) => f(m)); if (i >= 0) waiters.splice(i, 1)[0][1](m); else inbox.push(m); });
  const next = (f: (m: any) => boolean, ms = 15000) => new Promise<any>((res, rej) => { const i = inbox.findIndex(f); if (i >= 0) return res(inbox.splice(i, 1)[0]); waiters.push([f, res]); setTimeout(() => rej(new Error("timeout")), ms); });
  return { ws, next, send: (m: any) => ws.send(JSON.stringify(m)), hello: (h: any) => { ws.send(JSON.stringify({ t: "hello", name: "E2E Probe", bot: true, ...h })); return next((m) => m.t === "welcome" || m.t === "error"); } };
}
const kp = Keypair.generate(), wallet = kp.publicKey.toBase58();
const siws = (nonce: string) => { const message = `lamby4114.github.io wants you to sign in with your Solana account:\n${wallet}\n\nSign in to NUGSHIP. This is not a transaction and costs nothing.\n\nURI: https://lamby4114.github.io/nugship/\nVersion: 1\nChain ID: mainnet\nNonce: ${nonce}\nIssued At: ${new Date().toISOString()}`; return { message, signature: bs58.encode(nacl.sign.detached(Buffer.from(message), kp.secretKey)) }; };
const A = open(); const chA = await A.next((m) => m.t === "challenge"); A.ws.close(); log("socket A nonce", chA.nonce.slice(0, 8), "closed (simulated Phantom round-trip)");
const proof = siws(chA.nonce);
const B = open(); await B.next((m) => m.t === "challenge"); const wB = await B.hello({ wallet, signIn: proof });
log("socket B hello(proof for A's nonce):", wB.t, "verified=" + wB.verified, "player=" + wB.playerId?.slice(0, 8)); B.ws.close();
const R = open(); await R.next((m) => m.t === "challenge"); const wR = await R.hello({ wallet, signIn: proof });
log("replay same proof:", wR.t, wR.code ?? ""); R.ws.close();
const C = open(); await C.next((m) => m.t === "challenge"); const wC = await C.hello({ wallet, resume: { playerId: wallet, token: wB.token } });
log("socket C resume (no signature):", wC.t, "verified=" + wC.verified);
C.send({ t: "queue_ranked", stakeLamports: "1000000", fireMode: "manual" }); const q = await C.next((m) => m.t === "queued" || m.t === "error"); log("queue_ranked:", q.t, q.code ?? ""); C.send({ t: "cancel_queue" }); await C.next((m) => m.t === "queue_left");
C.send({ t: "start_solo", captain: "barnacle", mode: "live", fireMode: "manual", stakeLamports: "1000000" });
const mf = await C.next((m) => m.t === "match_found" || m.t === "error"); log("start_solo live:", mf.t, "mode=" + mf.mode, "fireMode=" + mf.fireMode, "stake=" + mf.stakeLamports, "source=" + mf.source);
const layout = randomLayout(), salt = crypto.randomBytes(32).toString("hex");
C.send({ t: "place_fleet", matchId: mf.matchId, layout, salt, commit: commitLayout(layout, salt) });
const ts = await C.next((m) => m.t === "turn_start", 180000); log("turn_start round", ts.roundId, "maxShots", ts.maxShots);
C.send({ t: "aim", matchId: mf.matchId, roundId: ts.roundId, squares: [7, 12] });
const lk = await C.next((m) => m.t === "lock", 180000); log("lock:", JSON.stringify({ roundId: lk.roundId, squares: lk.squares, mask: lk.mask, amountLamports: lk.amountLamports, fire: lk.fire, fireMode: lk.fireMode, endSlot: lk.endSlot, slot: lk.slot }));
try { const out = execFileSync(process.env.VOLLEY!, [SIMWALLET, String(lk.roundId), String(lk.mask), String(lk.amountLamports)], { encoding: "utf8", env: { ...process.env } }); console.log(out); log("VOLLEY SIM OK"); }
catch (e: any) { console.log(e.stdout); log("VOLLEY SIM FAILED"); }
C.send({ t: "resign", matchId: mf.matchId }); await C.next((m) => m.t === "match_end", 20000).then((m) => log("match_end", m.result, m.reason)).catch(() => log("no match_end"));
C.ws.close(); process.exit(0);
