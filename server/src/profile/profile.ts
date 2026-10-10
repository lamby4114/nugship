// Captain profiles: unique display names, avatar refs ("a:<builtin>" or "u:<hash16>" uploaded 20x20 PNG), SIWS pairing message.
import crypto from "node:crypto";

/** Built-in avatar ids (Unity atlas av_<id>); keep in sync with Avatars.cs and unity_export/src/avatars.js. */
export const BUILTIN_AVATARS = ["nuggy", "nuggy_ginger", "nuggy_snow", "nuggy_ghost", "nuggy_royal", "nuggy_tricorn", "captain", "barnacle", "brine",
  "molly", "ghost", "cult", "salt", "corsair", "skull", "kraken", "parrot", "nugget"] as const;
export const DEFAULT_AVATAR = "a:captain";
export const AVATAR_PX = 20;
export const MAX_AVATAR_BYTES = 6 * 1024;

const RESERVED = new Set(["captain", "guest", "admin", "administrator", "moderator", "mod", "nugship", "nuggy", "ore", "server", "system", "support",
  "official", "dev", "developer", "staff", "kraken", "captain barnacle", "barnacle", "admiral brine", "brine", "mad molly", "molly", "the ghost captain",
  "ghost captain", "high priest of the kraken cult", "kraken cult", "you", "rival", "anonymous", "null", "undefined"]);
// tiny blocklist (substring match on the squashed key); the real fix for abuse is reporting/moderation, not a giant list
const BLOCK = ["fuck", "shit", "cunt", "nigg", "fag", "rape", "hitler", "nazi", "whore", "slut", "bitch", "dick", "pussy", "retard"];

/** Display form: trimmed, single spaces, upper case (the pixel font is upper case only). */
export function normalizeName(raw: unknown): string {
  return String(raw ?? "").normalize("NFKC").replace(/\s+/g, " ").trim().toUpperCase();
}
/** Uniqueness key: case/space/punctuation-insensitive ("Salty_Whisker" == "salty whisker" == "SALTY.WHISKER"). */
export function nameKey(display: string): string { return display.toLowerCase().replace(/[^a-z0-9]/g, ""); }

/** Returns null when valid, else an error code. */
export function validateName(display: string): string | null {
  if (display.length < 3) return "too_short";
  if (display.length > 16) return "too_long";
  if (!/^[A-Z0-9 _.\-]+$/.test(display)) return "bad_chars";
  if (!/[A-Z]/.test(display)) return "needs_letter";
  if (/^[ _.\-]|[ _.\-]$/.test(display) || /[_.\-]{2,}/.test(display)) return "bad_chars";
  const key = nameKey(display);
  if (key.length < 3) return "too_short";
  if (RESERVED.has(display.toLowerCase()) || RESERVED.has(key)) return "reserved";
  if (BLOCK.some((b) => key.includes(b))) return "not_allowed";
  return null;
}

export function validAvatarRef(ref: unknown): ref is string {
  if (typeof ref !== "string") return false;
  if (ref.startsWith("a:")) return (BUILTIN_AVATARS as readonly string[]).includes(ref.slice(2));
  return /^u:[0-9a-f]{16}$/.test(ref);
}

/** Validates an uploaded avatar: real PNG, exactly 20x20, small. Returns { hash16 } or an error code. */
export function checkAvatarPng(buf: Buffer): { hash: string } | { error: string } {
  if (buf.length < 33 || buf.length > MAX_AVATAR_BYTES) return { error: "bad_size" };
  if (!buf.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]))) return { error: "not_png" };
  if (buf.toString("ascii", 12, 16) !== "IHDR") return { error: "not_png" };
  const w = buf.readUInt32BE(16), h = buf.readUInt32BE(20);
  if (w !== AVATAR_PX || h !== AVATAR_PX) return { error: "bad_dimensions" };
  if (!buf.includes(Buffer.from("IEND"))) return { error: "not_png" };
  return { hash: crypto.createHash("sha256").update(buf).digest("hex").slice(0, 16) };
}

/** Sign-In-With-Solana style pairing message (the wallet signs this; the server checks nonce + signature). */
export function pairingMessage(domain: string, wallet: string, playerId: string, nonce: string, issuedAt: string): string {
  return `${domain} wants you to sign in with your Solana account:\n${wallet}\n\nPair this wallet with NUGSHIP captain ${playerId}. This is not a transaction and costs nothing.\n\nNonce: ${nonce}\nIssued At: ${issuedAt}`;
}

export const sha256hex = (s: string) => crypto.createHash("sha256").update(s).digest("hex");
