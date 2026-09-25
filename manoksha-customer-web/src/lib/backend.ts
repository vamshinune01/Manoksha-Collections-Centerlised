import "server-only";
import { cache } from "react";
import { serverConfig } from "./config";
import { resellerToken } from "./session";
import type { ResellerMe } from "./types";

export type Result<T> = { ok: true; data: T } | { ok: false; status: number; title: string; code?: string };

export async function resellerFetch<T>(path: string): Promise<Result<T>> {
  const token = await resellerToken();
  const response = await fetch(`${serverConfig.apiUrl}/api/v1/reseller/${path.replace(/^\//, "")}`, {
    headers: { Accept: "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    cache: "no-store",
  });
  const text = await response.text();
  const json = text ? JSON.parse(text) : null;
  return response.ok ? { ok: true, data: json as T } : { ok: false, status: response.status, title: json?.title ?? response.statusText, code: json?.code };
}

export const getResellerMe = cache(async (): Promise<ResellerMe | null> => {
  if (!(await resellerToken())) return null;
  const me = await resellerFetch<ResellerMe>("me");
  return me.ok ? me.data : null;
});
