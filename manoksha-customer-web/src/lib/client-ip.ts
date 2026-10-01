/**
 * The real browser IP, passed to the API with the shared proxy key so the API can apply per-person rate limits and record the
 * right IP in login history and audit (the API ignores the IP without the key). Cloud Run's front end appends the address it saw
 * as the last X-Forwarded-For entry; behind an extra load balancer set CLIENT_IP_HOPS=2.
 */
export function clientIpHeadersFrom(incoming: Headers): Record<string, string> {
  const key = process.env.MANOKSHA_PROXY_KEY;
  if (!key) return {};
  const hops = Math.max(1, Number(process.env.CLIENT_IP_HOPS ?? "1") || 1);
  const chain = (incoming.get("x-forwarded-for") ?? "").split(",").map((s) => s.trim()).filter(Boolean);
  const ip = chain.length >= hops ? chain[chain.length - hops] : incoming.get("x-real-ip");
  return ip ? { "X-Manoksha-Client-IP": ip, "X-Manoksha-Proxy-Key": key } : {};
}
