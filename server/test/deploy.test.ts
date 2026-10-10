// Production/deploy behaviour: env config parsing, origin policy (REST + WS), graceful shutdown (drain + 1012 close).
import test from "node:test";
import assert from "node:assert/strict";
import { WebSocket } from "ws";
import { Store } from "../src/db/store.js";
import { GameServer } from "../src/net/server.js";
import { PracticeRoundSource } from "../src/chain/roundSource.js";
import { loadConfig, redacted } from "../src/config/config.js";

test("config: safe prod defaults, redacted RPC key", () => {
  const c = loadConfig({ NODE_ENV: "production", RPC_URL: "https://mainnet.helius-rpc.com/?api-key=SECRET123" } as any);
  assert.equal(c.allowGuestRanked, false); assert.equal(c.allowWalletPairing, false); assert.equal(c.requireWalletProof, true);
  assert.equal(c.logFormat, "json"); assert.equal(c.allowedOrigins, "*"); assert.equal(c.roundSource, "live");
  assert.ok(!JSON.stringify(redacted(c)).includes("SECRET123"));
  const d = loadConfig({ ALLOW_GUEST_RANKED: "true", ALLOWED_ORIGINS: "https://nugship.gg/, https://x.dev", LOG_FORMAT: "text" } as any);
  assert.equal(d.allowGuestRanked, true); assert.deepEqual(d.allowedOrigins, ["https://nugship.gg", "https://x.dev"]); assert.equal(d.logFormat, "text");
  assert.throws(() => loadConfig({ PORT: "nope" } as any));
});

test("origin allowlist: native (no Origin) ok, listed origin echoed, others blocked", async () => {
  const server = new GameServer({ port: 0, store: new Store(":memory:"), source: new PracticeRoundSource({}), reader: null, log: () => {}, allowedOrigins: ["https://nugship.gg"] });
  const port = await server.listen();
  try {
    const plain = await fetch(`http://127.0.0.1:${port}/health`);
    assert.equal(plain.status, 200); assert.equal(plain.headers.get("access-control-allow-origin"), null);
    const ok = await fetch(`http://127.0.0.1:${port}/api/rules`, { headers: { origin: "https://nugship.gg" } });
    assert.equal(ok.status, 200); assert.equal(ok.headers.get("access-control-allow-origin"), "https://nugship.gg");
    const bad = await fetch(`http://127.0.0.1:${port}/api/rules`, { headers: { origin: "https://evil.example" } });
    assert.equal(bad.status, 403);
    const wsOk = await new Promise<boolean>((r) => { const w = new WebSocket(`ws://127.0.0.1:${port}/ws`); w.on("message", () => { w.close(); r(true); }); w.on("error", () => r(false)); });
    assert.ok(wsOk, "native client without Origin connects");
    const wsBad = await new Promise<boolean>((r) => { const w = new WebSocket(`ws://127.0.0.1:${port}/ws`, { origin: "https://evil.example" }); w.on("open", () => { w.close(); r(true); }); w.on("error", () => r(false)); });
    assert.equal(wsBad, false, "browser client from unlisted origin rejected");
  } finally { server.close(); }
});

test("graceful shutdown: server_restart notice, 1012 close, no new work, health 503", async () => {
  const store = new Store(":memory:");
  const server = new GameServer({ port: 0, store, source: new PracticeRoundSource({ aimSeconds: 30 }), reader: null, log: () => {} });
  const port = await server.listen();
  const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  const msgs: any[] = [];
  const closed = new Promise<number>((r) => ws.on("close", (code) => r(code)));
  ws.on("message", (raw) => msgs.push(JSON.parse(raw.toString())));
  await new Promise((r) => ws.on("open", r));
  ws.send(JSON.stringify({ t: "hello", name: "Tester", guestKey: "c".repeat(32) }));
  ws.send(JSON.stringify({ t: "start_solo", captain: "barnacle" }));
  for (let i = 0; i < 50 && !msgs.some((m) => m.t === "match_state" || m.t === "match_start" || m.t === "match_found"); i++) await new Promise((r) => setTimeout(r, 20));
  assert.equal(server.matches.size, 1);
  const done = server.shutdown(100);
  const health = await fetch(`http://127.0.0.1:${port}/health`).then((r) => r.status).catch(() => 0);
  assert.ok(health === 503 || health === 0);
  const { abandoned } = await done;
  assert.equal(abandoned.length, 1);
  assert.equal(await closed, 1012);
  const notice = msgs.find((m) => m.t === "server_restart");
  assert.ok(notice && notice.matchAbandoned === true && notice.reconnectAfterMs > 0);
  assert.equal(store.counts()?.["matches" as keyof object], 0, "abandoned match not recorded");
  store.close();
});
