// Smoke test against a running server: 2 guests connect, ping, quick-match ranked, one resigns.
// Usage: node scripts/ws-smoke.mjs [wss://nugship-server.fly.dev/ws]   (default ws://127.0.0.1:8787/ws)
// Ranked step needs ALLOW_GUEST_RANKED=true on the server; otherwise expect error "wallet_required". Nothing touches the chain.
import { WebSocket } from "ws";
const url = process.argv[2] ?? "ws://127.0.0.1:8787/ws";
const rnd = () => [...crypto.getRandomValues(new Uint8Array(16))].map((b) => b.toString(16).padStart(2, "0")).join("");
function client(name) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url); const log = [];
    ws.on("message", (raw) => { const m = JSON.parse(raw); log.push(m);
      if (m.t === "challenge") ws.send(JSON.stringify({ t: "hello", name, guestKey: rnd() }));
      if (m.t === "welcome") resolve({ ws, log, welcome: m }); });
    ws.on("error", reject); setTimeout(() => reject(new Error("timeout connecting to " + url)), 10000);
  });
}
const types = (l) => [...new Set(l.map((m) => m.t))].join(",");
const a = await client("Smoke A"), b = await client("Smoke B");
console.log("connected", url, "| ore round", a.welcome.ore?.round?.roundId, "| source", a.welcome.ore?.source);
a.ws.send(JSON.stringify({ t: "queue_ranked" })); b.ws.send(JSON.stringify({ t: "queue_ranked" }));
await new Promise((r) => setTimeout(r, 3000));
const err = a.log.find((m) => m.t === "error");
if (err) console.log("ranked:", err.code, "-", err.message);
else { a.ws.send(JSON.stringify({ t: "resign" })); await new Promise((r) => setTimeout(r, 1500)); }
console.log("A saw:", types(a.log)); console.log("B saw:", types(b.log));
const ok = a.log.some((m) => m.t === "welcome") && (err?.code === "wallet_required" || a.log.some((m) => m.t === "match_end"));
a.ws.close(); b.ws.close(); process.exit(ok ? 0 : 1);
