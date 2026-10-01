import "server-only";
import { headers } from "next/headers";
import { clientIpHeadersFrom } from "./client-ip";

/** fetch() to the API from the server, carrying the caller's real IP (see client-ip.ts). */
export async function upstreamFetch(url: string, init: RequestInit = {}): Promise<Response> {
  let extra: Record<string, string> = {};
  try {
    extra = clientIpHeadersFrom(await headers());
  } catch {
    // Outside a request (build time): nothing to forward.
  }
  const merged = new Headers(init.headers);
  for (const [k, v] of Object.entries(extra)) merged.set(k, v);
  return fetch(url, { ...init, headers: merged });
}
