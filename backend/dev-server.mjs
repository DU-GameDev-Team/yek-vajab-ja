import { createServer } from "node:http";
import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { dirname, extname, join, normalize, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { randomBytes } from "node:crypto";
import { handleRequest } from "./challenge-api.mjs";

const projectDir = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const buildRoot = resolve(process.env.YV_WEB_BUILD || join(projectDir, "ye-vajab-ja", "Build", "Web-Optimized"));
const bindHost = process.env.YV_WEB_HOST || "127.0.0.1";
const secret = process.env.YV_CHALLENGE_SECRET || randomBytes(32).toString("hex");
const requestedPort = Number(process.env.PORT || 0);
const mime = {
  ".html": "text/html; charset=utf-8",
  ".js": "application/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".wasm": "application/wasm",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".ico": "image/x-icon"
};

const server = createServer(async (req, res) => {
  const url = new URL(req.url || "/", `http://${req.headers.host || "localhost"}`);
  if (url.pathname.startsWith("/api/")) {
    try {
      const chunks = [];
      if (req.method !== "GET" && req.method !== "HEAD") {
        for await (const chunk of req) chunks.push(chunk);
      }
      const body = chunks.length ? Buffer.concat(chunks) : undefined;
      const response = await handleRequest(new Request(url, {
        method: req.method,
        headers: req.headers,
        body
      }), secret);
      res.writeHead(response.status, Object.fromEntries(response.headers));
      res.end(Buffer.from(await response.arrayBuffer()));
    } catch {
      res.writeHead(500, { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" });
      res.end(JSON.stringify({ error: "internal error" }));
    }
    return;
  }
  if (req.method !== "GET" && req.method !== "HEAD") {
    res.writeHead(405, { Allow: "GET, HEAD" }); res.end(); return;
  }
  let pathname;
  try { pathname = decodeURIComponent(url.pathname); } catch { res.writeHead(400); res.end(); return; }
  const relative = normalize(pathname.replace(/^\/+/, "") || "index.html");
  const file = resolve(buildRoot, relative);
  if (file !== buildRoot && !file.startsWith(buildRoot + sep)) { res.writeHead(403); res.end(); return; }
  let info;
  try { info = await stat(file); } catch { res.writeHead(404); res.end("Not found"); return; }
  if (!info.isFile()) { res.writeHead(404); res.end("Not found"); return; }
  const compressed = file.endsWith(".br");
  const extension = compressed ? extname(file.slice(0, -3)) : extname(file);
  const headers = {
    "content-type": mime[extension] || "application/octet-stream",
    "content-length": info.size,
    "cache-control": relative.startsWith("Build/") ? "public, max-age=3600" : "no-cache"
  };
  if (compressed) {
    headers["content-encoding"] = "br";
    headers.vary = "Accept-Encoding";
  }
  res.writeHead(200, headers);
  if (req.method === "HEAD") { res.end(); return; }
  createReadStream(file).pipe(res);
});

server.listen(requestedPort, bindHost, () => {
  const { port } = server.address();
  console.log(`Local game: http://localhost:${port}/`);
  if (!process.env.YV_CHALLENGE_SECRET) console.log("Using a temporary local signing key; links expire when this server stops.");
});
