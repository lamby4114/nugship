import { test } from "node:test"; import assert from "node:assert/strict";
import { volleyFallback } from "../src/game/match.js";
test("player-signed fallback only without any automation", () => {
  assert.equal(volleyFallback({ placed: false, reason: "no_automation" }, 100, 200), true);
  for (const reason of ["balance_low", "amount_below_stake", "fee_too_low", "other_executor", "strategy", "sim:{}", "error:x", "round_ending", undefined])
    assert.equal(volleyFallback({ placed: false, reason }, 100, 200), false, String(reason));
  assert.equal(volleyFallback({ placed: true }, 100, 200), false);
  assert.equal(volleyFallback({ placed: false, reason: "no_automation", sig: "s" }, 100, 200), false);
  assert.equal(volleyFallback({ placed: false, reason: "no_automation" }, 195, 200), false);
});
