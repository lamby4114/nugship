# ORE turn timing: read-only findings (sampled Oct 7 2026, ~9:35-9:55 PM EDT)
Code: regolith-labs/ore @48c203b (2026-10-02). Entropy: regolith-labs/entropy @f26ae03.
Scripts: ore_timing.mjs (snapshot|poll|sigs), classify.mjs, analyze.mjs. Data: snapshot.json, poll.jsonl, sigs.json, classify.json.

- Config (on-chain 9c9X7a…): round_slots=240, intermission_slots=48, admin fee 100 bps, protocol fee 1000 bps.
- Measured slot time 0.266-0.276 s (getRecentPerformanceSamples; blockTime over 1,980 slots = 0.268 s).
- Rounds 431892-431900: start-to-start = 289 slots every time (~77 s); deploy window 240 slots (~64 s);
  end_slot -> Reset = 48 slots (~13 s, observed exactly 48 in 3/3); next round starts 1 slot after reset (first deploy).
- Deploy allowed iff start_slot <= slot < end_slot (program/src/deploy.rs:33). Round starts on first deploy (deploy.rs:47-69).
- Entropy: slot hash of end_slot sampled + provider seed revealed; Var.value is copied to round.slot_hash in Reset (reset.rs:78-96).
  The polls never saw Var.value set before the reset slot, so treat the result as known at about end_slot+48.
- Winning square = xor(4 x u64 LE of slot_hash) % 25 (round.rs:63-77). Motherlode if rng.reverse_bits() % 500 == 0 (round.rs:107).
- Fees (round.rs:86-99, checkpoint.rs:93-153): winning square 1%; losing squares 1% + 10% of remainder = 10.9%. Expected about 10.5% per shot.
- No minimum deploy amount in code. One deploy per square per round per miner (deploy.rs:296-299). Same amount for every square within one Deploy ix.
- Deploy does NOT auto-checkpoint. It asserts the prior round was checkpointed (deploy.rs:250-256). Bundle Checkpoint(prev)+Deploy in one tx
  (32 of about 38 checkpoint txs in sampled rounds did exactly this).
- Deploy = 12 accounts (sdk.rs:110-158). Checkpoint+Deploy tx about 11-18 keys. Observed CU 34k-116k (bundle), about 21-24k (lone deploy). Base fee 5,000 lamports (+ small priority).
- Miner account rent 0.0044704 SOL one-time (752 bytes) + 10,000 lamport checkpoint reserve. Automation account rent 0.00146304 SOL (160 bytes).
- Typical round (431878-431891): total about 10.3-10.8 SOL, about 0.41-0.43 SOL per square (very even), 144-152 miners.
