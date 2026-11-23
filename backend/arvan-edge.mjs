// Single-file Arvan Edge application for Yek-Vajab-Ja.
// Required environment variables:
//   CHALLENGE_HMAC_KEY: private random value, at least 32 characters.
//   GAME_ASSET_ORIGIN: public Arvan Object Storage origin containing the WebGL build.
// Keep both values out of this source file and the browser bundle.

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
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff"
    }
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
  return Uint8Array.from(binary, character => character.charCodeAt(0));
}

function validateSubmission(data) {
  return data && Object.hasOwn(RULES, data.v) && Number.isInteger(data.seed) &&
    data.seed >= 0 && data.seed <= 511 && Array.isArray(data.tapsMs) && data.tapsMs.length === 3 &&
    data.tapsMs.every(value => Number.isSafeInteger(value) && value >= 0 && value <= MAX_SAFE);
}

function calculateScores(version, seed, tapsMs) {
  const rules = RULES[version];
  return tapsMs.map((tapMs, round) => {
    const digit = (seed >> (round * 3)) & 7;
    const center = CENTER_BASE + 18 * (digit % 4);
    const phase = digit >= 4 ? 0.5 : 0;
    const progress = ((Math.round(tapMs) / rules.durationsMs[round] + phase) % 1 + 1) % 1;
    const position = center + rules.amplitudes[round] * (1 - 4 * Math.abs(progress - 0.5));
    const error = Math.abs(position - center);
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

async function handleChallengeRequest(request, secret) {
  if (!secret || typeof secret !== "string" || secret.length < 32) return json({ error: "service unavailable" }, 503);
  const url = new URL(request.url);

  if (url.pathname === "/api/challenges" && request.method === "POST") {
    let data;
    try {
      const raw = await request.text();
      if (raw.length > 4096) return json({ error: "request too large" }, 413);
      data = JSON.parse(raw);
    } catch {
      return json({ error: "invalid json" }, 400);
    }
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
          !Array.isArray(payload.scores) || payload.scores.length !== 3 ||
          payload.scores.some(score => !Number.isInteger(score) || score < 0 || score > 100) ||
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

const MIME_TYPES = {
  ".html": "text/html; charset=utf-8",
  ".js": "application/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".wasm": "application/wasm",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".ico": "image/x-icon",
  ".txt": "text/plain; charset=utf-8",
  ".woff2": "font/woff2"
};

function assetPathFor(url) {
  let decoded;
  try {
    decoded = decodeURIComponent(url.pathname);
  } catch {
    return null;
  }
  const relative = decoded.replace(/^\/+/, "") || "index.html";
  const parts = relative.split("/");
  if (parts.some(part => !part || part === "." || part === ".." || part.includes("\\") || part.includes("\0"))) return null;
  if (relative !== "index.html" && !relative.startsWith("Build/") && !relative.startsWith("TemplateData/")) return null;
  return `Web-Optimized/${parts.map(encodeURIComponent).join("/")}`;
}

async function serveGameAsset(request, assetOrigin) {
  if (!assetOrigin) return new Response("Game assets are not configured.", { status: 503 });
  if (request.method !== "GET" && request.method !== "HEAD") return new Response("Method not allowed", { status: 405, headers: { allow: "GET, HEAD" } });

  const url = new URL(request.url);
  const assetPath = assetPathFor(url);
  if (!assetPath) return new Response("Not found", { status: 404 });

  let origin;
  try {
    origin = new URL(assetOrigin);
    if (origin.protocol !== "https:") return new Response("Asset origin must use HTTPS.", { status: 503 });
  } catch {
    return new Response("Game asset origin is invalid.", { status: 503 });
  }

  if (!origin.pathname.endsWith("/")) origin.pathname += "/";
  const upstreamUrl = new URL(assetPath, origin);
  try {
    const upstream = await fetch(upstreamUrl, { method: request.method, redirect: "follow" });
    const headers = new Headers(upstream.headers);
    headers.set("x-content-type-options", "nosniff");
    const compressed = assetPath.endsWith(".br");
    const extension = compressed ? assetPath.slice(0, -3).match(/\.[^.]+$/)?.[0] : assetPath.match(/\.[^.]+$/)?.[0];
    headers.set("content-type", MIME_TYPES[extension] || "application/octet-stream");
    if (compressed) headers.set("content-encoding", "br");
    if (assetPath === "Web-Optimized/index.html") headers.set("cache-control", "no-cache");
    else headers.set("cache-control", "public, max-age=3600");
    return new Response(request.method === "HEAD" ? null : upstream.body, { status: upstream.status, headers });
  } catch {
    return new Response("Game asset could not be loaded.", { status: 502 });
  }
}

export default {
  async fetch(request) {
    // Arvan exposes configured environment variables as the global `env` object.
    const runtimeEnv = globalThis.env || {};
    const pathname = new URL(request.url).pathname;
    if (pathname === "/api/challenges" || pathname.startsWith("/api/challenges/")) {
      return handleChallengeRequest(request, runtimeEnv.CHALLENGE_HMAC_KEY);
    }
    return serveGameAsset(request, runtimeEnv.GAME_ASSET_ORIGIN);
  }
};
