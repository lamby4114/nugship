// Tiny structured logger. LOG_FORMAT=json (default when NODE_ENV=production) emits one JSON object per line,
// which `fly logs` and any log drain can parse; text keeps the old "ISO-time message" dev format.
import type { LogFormat } from "./config.js";

export type Level = "debug" | "info" | "warn" | "error";
export type LogFn = ((m: string) => void) & { at: (level: Level, m: string, fields?: Record<string, unknown>) => void };

const ERR = /\b(error|failed|fatal|exception)\b/i, WARN = /\b(warn|429|retry|backoff|timeout|void)\b/i;

export function makeLogger(format: LogFormat, base: Record<string, unknown> = {}): LogFn {
  const emit = (level: Level, msg: string, fields?: Record<string, unknown>) => {
    if (format === "json") {
      const line = JSON.stringify({ ts: new Date().toISOString(), level, msg, ...base, ...fields }, (_k, v) => (typeof v === "bigint" ? v.toString() : v));
      (level === "error" ? process.stderr : process.stdout).write(line + "\n");
    } else {
      const extra = fields && Object.keys(fields).length ? " " + JSON.stringify(fields, (_k, v) => (typeof v === "bigint" ? v.toString() : v)) : "";
      (level === "error" ? console.error : console.log)(new Date().toISOString(), level === "info" ? msg : `[${level}] ${msg}`, ...(extra ? [extra.trim()] : []));
    }
  };
  // Existing call sites pass plain strings; infer a level so errors are filterable without touching every caller.
  const log = ((m: string) => emit(ERR.test(m) ? "error" : WARN.test(m) ? "warn" : "info", m)) as LogFn;
  log.at = emit;
  return log;
}
