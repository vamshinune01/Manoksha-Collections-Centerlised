#!/usr/bin/env node
// Load test for the Manoksha API — no dependencies (Node 18+). Read-only by default: storefront browsing and health checks.
//
//   BASE_URL=https://<api-host> VUS=20 DURATION=60 node tests/load/load-test.mjs
//
// Optional:
//   PROXY_KEY=<Security:ProxyKey>   spread requests over synthetic client IPs (otherwise one IP hits the per-IP limit of 300/min,
//                                   which is itself what a single abusive client should see)
//   ACCESS_TOKEN=<admin token>      also load the Owner dashboard and Exception Center (heavier read queries)
//   THRESHOLD_P95_MS=800            fail (exit 1) when p95 latency is above this
//   MAX_ERROR_RATE=0.01             fail when more than 1% of requests error (5xx / network); 429s are reported separately

const base = (process.env.BASE_URL ?? "http://localhost:5080").replace(/\/$/, "");
const vus = Number(process.env.VUS ?? 10);
const durationS = Number(process.env.DURATION ?? 30);
const proxyKey = process.env.PROXY_KEY;
const token = process.env.ACCESS_TOKEN;
const p95Limit = Number(process.env.THRESHOLD_P95_MS ?? 800);
const maxErrorRate = Number(process.env.MAX_ERROR_RATE ?? 0.01);

const stats = new Map(); // name -> { ms: number[], status: Map<code, n> }
const record = (name, ms, status) => {
  const s = stats.get(name) ?? { ms: [], status: new Map() };
  s.ms.push(ms);
  s.status.set(status, (s.status.get(status) ?? 0) + 1);
  stats.set(name, s);
};

async function call(name, path, vu, auth = false) {
  const headers = { Accept: "application/json" };
  if (proxyKey) {
    headers["X-Manoksha-Client-IP"] = `198.18.${Math.floor(vu / 250)}.${(vu % 250) + 1}`;
    headers["X-Manoksha-Proxy-Key"] = proxyKey;
  }
  if (auth) headers.Authorization = `Bearer ${token}`;
  const started = performance.now();
  let status = "ERR";
  let body = null;
  try {
    const res = await fetch(base + path, { headers, signal: AbortSignal.timeout(15000) });
    status = res.status;
    body = res.headers.get("content-type")?.includes("json") ? await res.json() : await res.text();
  } catch {
    status = "ERR";
  }
  record(name, performance.now() - started, status);
  return body;
}

async function shopper(vu, deadline) {
  while (Date.now() < deadline) {
    await call("catalog: categories", "/api/v1/catalog/categories", vu);
    const page = await call("catalog: products", "/api/v1/catalog/products?page=1&pageSize=24", vu);
    const items = page?.items ?? [];
    const pick = items[Math.floor(Math.random() * items.length)];
    const id = pick?.productId ?? pick?.id;
    if (id) await call("catalog: product detail", `/api/v1/catalog/products/${id}`, vu);
    if (token && vu % 5 === 0) {
      await call("admin: dashboard", "/api/v1/admin/dashboard", vu, true);
      await call("admin: exceptions", "/api/v1/admin/exceptions", vu, true);
    }
    await new Promise((r) => setTimeout(r, 200 + Math.random() * 800)); // think time
  }
}

const pct = (sorted, p) => (sorted.length ? sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))] : 0);

console.log(`Load test: ${base} · ${vus} virtual users · ${durationS}s${proxyKey ? " · synthetic client IPs" : ""}${token ? " · with admin reads" : ""}`);
await call("health", "/health/ready", 0);
const deadline = Date.now() + durationS * 1000;
await Promise.all(Array.from({ length: vus }, (_, i) => shopper(i + 1, deadline)));

let total = 0, errors = 0, limited = 0, worstP95 = 0;
const rows = [];
for (const [name, s] of stats) {
  const sorted = [...s.ms].sort((a, b) => a - b);
  const n = sorted.length;
  const err = [...s.status].filter(([c]) => c === "ERR" || c >= 500).reduce((a, [, v]) => a + v, 0);
  const lim = s.status.get(429) ?? 0;
  total += n; errors += err; limited += lim;
  const p95 = pct(sorted, 95);
  if (name !== "health") worstP95 = Math.max(worstP95, p95);
  rows.push({ endpoint: name, requests: n, "p50 ms": Math.round(pct(sorted, 50)), "p95 ms": Math.round(p95), "p99 ms": Math.round(pct(sorted, 99)),
    errors: err, "429": lim, statuses: [...s.status].map(([c, v]) => `${c}×${v}`).join(" ") });
}
console.table(rows);
const errorRate = total ? errors / total : 0;
console.log(`Total ${total} requests · ${(total / durationS).toFixed(1)} req/s · error rate ${(errorRate * 100).toFixed(2)}% · rate-limited ${limited} · worst p95 ${Math.round(worstP95)} ms`);
if (errorRate > maxErrorRate || worstP95 > p95Limit) {
  console.error(`FAILED thresholds (error rate ≤ ${maxErrorRate * 100}%, p95 ≤ ${p95Limit} ms)`);
  process.exit(1);
}
console.log("PASSED thresholds");
