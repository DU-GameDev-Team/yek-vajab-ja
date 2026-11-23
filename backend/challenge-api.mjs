/**
 * Stateless challenge API core for the Fetch API (Arvan edge adapter can call handleRequest).
 * Set CHALLENGE_HMAC_KEY to a private random secret; never expose it to the browser bundle.
 */
const encoder = new TextEncoder();
const RULES = {
  1: { gaps: [120, 108, 100], amplitudes: [70, 74, 78], durationsMs: [3200, 3000, 2800] },
  2: { gaps: [116, 104, 92], amplitudes: [72, 78, 84], durationsMs: [3000, 2700, 2400] },
  3: { gaps: [112, 100, 88], amplitudes: [76, 82, 88], durationsMs: [2900, 2600, 2300] },
  4: { gaps: [112, 100, 88], amplitudes: [76, 82, 88], durationsMs: [2900, 2450, 1950] },
  5: { gaps: [112, 100, 88], amplitudes: [76, 82, 88], durationsMs: [2900, 2250, 1600], carLength: 60 },
  6: { gaps: [112, 98, 84], amplitudes: [76, 82, 88], durationsMs: [2900, 2250, 1600], carLength: 56.8973 },
  7: { gaps: [112, 98, 84], amplitudes: [76, 82, 88], durationsMs: [2900, 2250, 1600], carLength: 62.4633 }
};
const CENTER_BASE = 290;
const LEGACY_CAR_LENGTH = 60;
const MAX_SAFE = Number.MAX_SAFE_INTEGER;

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff" }
  });
}
function base64url(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}
function decodeBase64url(value) {
  if (!/^[A-Za-z0-9_-]+$/.test(value)) throw new Error("invalid base64url");
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((value.length + 3) % 4);
  const binary = atob(base64);
  return Uint8Array.from(binary, c => c.charCodeAt(0));
}
function validateSubmission(data) {
  return data && Object.hasOwn(RULES, data.v) && Number.isInteger(data.seed) && data.seed >= 0 && data.seed <= 511 &&
    Array.isArray(data.tapsMs) && data.tapsMs.length === 3 &&
    data.tapsMs.every(t => Number.isSafeInteger(t) && t >= 0 && t <= MAX_SAFE);
}
function calculateScores(version, seed, tapsMs) {
  const rules = RULES[version];
  return tapsMs.map((tapMs, round) => {
    const digit = (seed >> (round * 3)) & 7;
    const center = CENTER_BASE + 18 * (digit % 4);
    const phase = digit >= 4 ? 0.5 : 0;
    const u = ((Math.round(tapMs) / rules.durationsMs[round] + phase) % 1 + 1) % 1;
    const y = center + rules.amplitudes[round] * (1 - 4 * Math.abs(u - 0.5));
    const error = Math.abs(y - center);
    const margin = (rules.gaps[round] - (rules.carLength ?? LEGACY_CAR_LENGTH)) / 2;
    if (error > margin) return 0;
    return Math.max(1, Math.floor(100 * (1 - error / margin) + 0.5));
  });
}
async function importKey(secret) {
  return crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]);
}
async function sign(payloadBytes, key) {
  return new Uint8Array(await crypto.subtle.sign("HMAC", key, payloadBytes));
}
async function verify(payloadBytes, signature, key) {
  return crypto.subtle.verify("HMAC", key, signature, payloadBytes);
}

export async function handleRequest(request, secret) {
  if (!secret || typeof secret !== "string" || secret.length < 32) return json({ error: "service unavailable" }, 503);
  const url = new URL(request.url);
  if (url.pathname === "/api/challenges" && request.method === "POST") {
    let data;
    try {
      const raw = await request.text();
      if (raw.length > 4096) return json({ error: "request too large" }, 413);
      data = JSON.parse(raw);
    } catch { return json({ error: "invalid json" }, 400); }
    if (!validateSubmission(data)) return json({ error: "invalid challenge data" }, 400);
    const scores = calculateScores(data.v, data.seed, data.tapsMs);
    const payload = { v: data.v, seed: data.seed, scores, total: scores.reduce((sum, score) => sum + score, 0) };
    const bytes = encoder.encode(JSON.stringify(payload));
    const key = await importKey(secret);
    const token = `${base64url(bytes)}.${base64url(await sign(bytes, key))}`;
    return json({ token, total: payload.total });
  }
  const match = url.pathname.match(/^\/api\/challenges\/([^/]+)$/);
  if (match && request.method === "GET") {
    try {
      if (match[1].length > 256) return json({ error: "challenge not found" }, 404);
      const [payloadPart, signaturePart, extra] = match[1].split(".");
      if (!payloadPart || !signaturePart || extra !== undefined) return json({ error: "challenge not found" }, 404);
      const bytes = decodeBase64url(payloadPart);
      const signature = decodeBase64url(signaturePart);
      const key = await importKey(secret);
      if (!(await verify(bytes, signature, key))) return json({ error: "challenge not found" }, 404);
      const payload = JSON.parse(new TextDecoder().decode(bytes));
      if (!Object.hasOwn(RULES, payload.v) || !Number.isInteger(payload.seed) || payload.seed < 0 || payload.seed > 511 ||
          !Array.isArray(payload.scores) || payload.scores.length !== 3 || payload.scores.some(s => !Number.isInteger(s) || s < 0 || s > 100) ||
          !Number.isInteger(payload.total) || payload.total !== payload.scores.reduce((sum, score) => sum + score, 0)) {
        return json({ error: "challenge not found" }, 404);
      }
      return json(payload);
    } catch {
      return json({ error: "challenge not found" }, 404);
    }
  }
  return json({ error: "not found" }, 404);
}


