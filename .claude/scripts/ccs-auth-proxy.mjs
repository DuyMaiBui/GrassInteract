// t1k-origin: kit=theonekit-model-router | repo=The1Studio/theonekit-model-router | module=null | protected=false
import http from "node:http";
import https from "node:https";
import { execSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";

// Security config env vars:
//   BIND_ADDR          — network interface to bind (default: 127.0.0.1; set to 0.0.0.0 to allow LAN)
//   TRUST_CF_HEADERS   — set to "1" ONLY when this proxy is behind Cloudflare Access (default: off)
const ALLOWED_ORG = process.env.ALLOWED_ORG || "The1Studio";
const UPSTREAM = process.env.UPSTREAM || "http://127.0.0.1:8317";
const PORT = parseInt(process.env.PORT || "8318", 10);
const BIND_ADDR = process.env.BIND_ADDR || "127.0.0.1";
const TRUST_CF_HEADERS = process.env.TRUST_CF_HEADERS === "1";
const CACHE_TTL_MS = 60 * 60 * 1000;

const cache = new Map();
setInterval(() => {
  const now = Date.now();
  for (const [k, v] of cache) {
    if (now > v.expiresAt) cache.delete(k);
  }
}, 5 * 60 * 1000);

function ghFetch(url, token) {
  return new Promise((resolve) => {
    const req = https.request(url, {
      headers: {
        Authorization: "Bearer " + token,
        "User-Agent": "ccs-auth-proxy",
        Accept: "application/vnd.github+json",
      },
    }, (res) => {
      let body = "";
      res.on("data", (c) => (body += c));
      res.on("end", () => resolve({ status: res.statusCode, body }));
    });
    req.on("error", () => resolve({ status: 0, body: "" }));
    req.end();
  });
}

async function validateGhToken(token) {
  const cached = cache.get(token);
  if (cached && Date.now() < cached.expiresAt) {
    console.log("[auth] cache HIT user=" + (cached.user || "?") + " valid=" + cached.valid);
    return cached;
  }
  console.log("[auth] cache MISS — calling GitHub API");

  const userRes = await ghFetch("https://api.github.com/user", token);
  if (userRes.status !== 200) {
    const entry = { valid: false, expiresAt: Date.now() + CACHE_TTL_MS };
    cache.set(token, entry);
    return entry;
  }

  let login;
  try { login = JSON.parse(userRes.body).login; } catch { return { valid: false }; }

  const orgRes = await ghFetch(
    "https://api.github.com/orgs/" + ALLOWED_ORG + "/members/" + login,
    token
  );
  const valid = orgRes.status === 204;
  const entry = { valid, user: login, expiresAt: Date.now() + CACHE_TTL_MS };
  cache.set(token, entry);
  console.log("[auth] GitHub API result: user=" + login + " valid=" + valid);
  return entry;
}

async function authenticate(req) {
  // Method 1: Authorization: Bearer <gh-token>
  const auth = req.headers["authorization"];
  if (auth && auth.startsWith("Bearer ")) {
    const result = await validateGhToken(auth.slice(7));
    return result.valid ? { ok: true, user: result.user, method: "gh-token" } : { ok: false };
  }
  // Method 2: x-api-key header (Claude Code sends ANTHROPIC_AUTH_TOKEN here)
  const apiKey = req.headers["x-api-key"];
  if (apiKey && apiKey !== "ccs-internal-managed") {
    const result = await validateGhToken(apiKey);
    return result.valid ? { ok: true, user: result.user, method: "x-api-key" } : { ok: false };
  }
  // Method 3: Cloudflare Access (only trusted when TRUST_CF_HEADERS=1, i.e. proxy is behind CF Access)
  // Default off — local proxy must never trust this spoofable header.
  const cfUser = req.headers["cf-access-authenticated-user-email"];
  if (TRUST_CF_HEADERS && cfUser) return { ok: true, user: cfUser, method: "cf-access" };
  return { ok: false };
}

// ─── Provider discovery (cached) ───
let providersCache = { data: null, expiresAt: 0 };
const PROVIDERS_CACHE_TTL = 5 * 60 * 1000; // 5 min

function discoverProviders() {
  const now = Date.now();
  if (providersCache.data && now < providersCache.expiresAt) return providersCache.data;

  try {
    const providers = [];
    const home = os.homedir();

    // Read known CLIProxy providers from config.yaml
    const configPath = path.join(home, ".ccs", "config.yaml");
    let knownProviders = [];
    if (fs.existsSync(configPath)) {
      const yaml = fs.readFileSync(configPath, "utf8");
      const providerMatch = yaml.match(/providers:\n((?:\s+- \w[\w-]*\n)+)/);
      if (providerMatch) {
        knownProviders = providerMatch[1].match(/- ([\w-]+)/g)?.map((m) => m.slice(2)) || [];
      }
    }

    // Check cliproxy/auth/ for token files — match against known providers
    const authDir = path.join(home, ".ccs", "cliproxy", "auth");
    const authFiles = fs.existsSync(authDir) ? fs.readdirSync(authDir) : [];

    for (const provider of knownProviders) {
      const hasToken = authFiles.some((f) => f.startsWith(provider));
      if (hasToken) {
        providers.push({ name: provider, status: "authenticated" });
      }
    }

    // Also check API profiles (opencode-go, etc.) — only if they have credentials
    const ccsDir = path.join(home, ".ccs");
    const profilesInConfig = [];
    if (fs.existsSync(configPath)) {
      const yaml = fs.readFileSync(configPath, "utf8");
      const profileSection = yaml.match(/^profiles:\n((?:\s+\S.*\n)*)/m);
      if (profileSection) {
        const names = profileSection[1].match(/^\s+([\w-]+):/gm);
        if (names) {
          for (const n of names) {
            profilesInConfig.push(n.trim().replace(":", ""));
          }
        }
      }
    }

    for (const name of profilesInConfig) {
      const settingsFile = path.join(ccsDir, name + ".settings.json");
      if (fs.existsSync(settingsFile) && !providers.find((p) => p.name === name)) {
        providers.push({ name, status: "configured" });
      }
    }

    providersCache = { data: providers, expiresAt: now + PROVIDERS_CACHE_TTL };
    console.log("[providers] discovered: " + providers.map((p) => p.name + "(" + p.status + ")").join(", "));
    return providers;
  } catch (err) {
    console.log("[providers] discovery failed: " + err.message);
    return providersCache.data || [];
  }
}

const server = http.createServer(async (req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ status: "ok", cache_size: cache.size }));
    return;
  }

  if (req.url === "/providers") {
    const auth = await authenticate(req);
    if (!auth.ok) {
      res.writeHead(403, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ error: "forbidden" }));
      return;
    }
    const providers = discoverProviders();
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ providers, cached: Date.now() < providersCache.expiresAt }));
    return;
  }

  const auth = await authenticate(req);
  if (!auth.ok) {
    res.writeHead(403, { "Content-Type": "application/json" });
    res.end(JSON.stringify({
      error: "forbidden",
      message: "Requires " + ALLOWED_ORG + " GitHub org membership.",
      hint: "Send Authorization: Bearer <gh-auth-token> (from 'gh auth token')",
    }));
    return;
  }

  const url = new URL(req.url, UPSTREAM);
  // Replace x-api-key with ccs-internal-managed (what CLIProxy expects).
  // Strip caller's Authorization header — upstream uses x-api-key only; forwarding
  // raw tokens over plain HTTP to CLIProxy is an information-disclosure risk.
  const fwdHeaders = { ...req.headers, host: url.host, "x-auth-user": auth.user, "x-api-key": "ccs-internal-managed" };
  delete fwdHeaders.authorization;
  delete fwdHeaders.Authorization; // case-insensitive header table sanity
  const proxyReq = http.request(url, {
    method: req.method,
    headers: fwdHeaders,
  }, (proxyRes) => {
    res.writeHead(proxyRes.statusCode, proxyRes.headers);
    proxyRes.pipe(res);
  });
  proxyReq.on("error", (err) => {
    res.writeHead(502, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ error: err.message }));
  });
  req.pipe(proxyReq);
});

server.listen(PORT, BIND_ADDR, () => {
  console.log("[ccs-auth-proxy] " + BIND_ADDR + ":" + PORT + " -> " + UPSTREAM + " (org: " + ALLOWED_ORG + ")");
});
