// Deterministic ORE SEAS instruction vectors from the TS builders, consumed by the C# tests (cross-language parity).
// Uses fixed, key-less public keys (no private keys exist for them). Nothing is signed or sent.
import { PublicKey } from "@solana/web3.js";
import fs from "node:fs";
import { automateCloseIx, automateSessionIx, checkpointIx, claimOreIx, claimSolIx, deployIx, squaresToMask, coordToSquare } from "../src/ore/index.js";

const wallet = new PublicKey("7j1DZ9xdNQ6Qcfvkh8GexjCrgiTisAGRo12UqXjrwM9Y");
const session = new PublicKey("5HdaugVSYWp5ALSb1JzX2DgmsNpa5MWatFg1zCyympi3");
const ser = (name: string, ix: any) => ({ name, programId: ix.programId.toBase58(), dataHex: Buffer.from(ix.data).toString("hex"),
  accounts: ix.keys.map((k: any) => ({ pubkey: k.pubkey.toBase58(), isSigner: k.isSigner, isWritable: k.isWritable })) });
const mask = squaresToMask(["C3", "B2", "E5"].map(coordToSquare));
const vectors = [
  ser("deploy_session_C3_B2_E5", deployIx(session, wallet, 5_000_000n, 431960n, mask)),
  ser("deploy_manual_A1", deployIx(wallet, wallet, 1_000_000n, 431961n, 1)),
  ser("checkpoint_session", checkpointIx(session, wallet, 431959n)),
  ser("claim_sol", claimSolIx(wallet)),
  ser("claim_ore_all", claimOreIx(wallet, 10_000n)),
  ser("automate_session", automateSessionIx(wallet, session, 5_000_000n, 60_000_000n, 5_000n, true)),
  ser("automate_close", automateCloseIx(wallet)),
];
const out = new URL("../test/fixtures/golden-oreseas.json", import.meta.url).pathname;
fs.writeFileSync(out, JSON.stringify({ wallet: wallet.toBase58(), session: session.toBase58(), mask, vectors }, null, 1));
console.log("wrote", out, vectors.length, "vectors");
