import "server-only";
import { cache } from "react";
import { serverConfig } from "./config";
import { customerToken, resellerToken } from "./session";
import type { CustomerMe, ResellerMe } from "./types";

export type Result<T> = { ok: true; data: T } | { ok: false; status: number; title: string; code?: string };

async function apiFetch<T>(url: string, token: string | undefined): Promise<Result<T>> {
  const response = await fetch(`${serverConfig.apiUrl}${url}`, {
    headers: { Accept: "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    cache: "no-store",
  });
  const text = await response.text();
  const json = text ? JSON.parse(text) : null;
  return response.ok ? { ok: true, data: json as T } : { ok: false, status: response.status, title: json?.title ?? response.statusText, code: json?.code };
}

const clean = (path: string) => path.replace(/^\//, "");

export const resellerFetch = async <T>(path: string) => apiFetch<T>(`/api/v1/reseller/${clean(path)}`, await resellerToken());

/** Signed-in customer's own data (`/api/v1/customer/*`). */
export const customerFetch = async <T>(path: string) => apiFetch<T>(`/api/v1/customer/${clean(path)}`, await customerToken());

/** Anonymous storefront data (`/api/v1/catalog/*`). */
export const storeFetch = <T>(path: string) => apiFetch<T>(`/api/v1/catalog/${clean(path)}`, undefined);

export const getCustomerMe = cache(async (): Promise<CustomerMe | null> => {
  const token = await customerToken();
  if (!token) return null;
  const me = await apiFetch<CustomerMe>("/api/v1/auth/me", token);
  return me.ok && me.data.accountType === "Customer" ? me.data : null;
});

export const getResellerMe = cache(async (): Promise<ResellerMe | null> => {
  if (!(await resellerToken())) return null;
  const me = await resellerFetch<ResellerMe>("me");
  return me.ok ? me.data : null;
});
