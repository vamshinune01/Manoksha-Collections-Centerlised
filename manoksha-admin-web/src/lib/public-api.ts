import "server-only";
import { serverConfig } from "./config";

/** Anonymous backend reads for the setup and invitation pages. */
export async function publicGet<T>(path: string): Promise<{ ok: true; data: T } | { ok: false; status: number; title: string }> {
  const response = await fetch(`${serverConfig.apiUrl}/api/v1/${path}`, { headers: { Accept: "application/json" }, cache: "no-store" }).catch(() => null);
  if (!response) return { ok: false, status: 503, title: "The server is not reachable." };
  const json = await response.json().catch(() => ({}));
  return response.ok ? { ok: true, data: json as T } : { ok: false, status: response.status, title: json?.title ?? response.statusText };
}

export interface SetupStatus {
  ownerExists: boolean;
  setupEnabled: boolean;
}
