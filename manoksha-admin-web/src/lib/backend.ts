import "server-only";
import { cache } from "react";
import type { ApiProblem } from "@manoksha/api-client";
import { serverConfig } from "./config";
import { getAccessToken } from "./session";
import type { Me } from "./access";

export type BackendResult<T> = { ok: true; status: number; data: T } | { ok: false; status: number; problem: ApiProblem };

/** Server-side call to manoksha-backend with the caller's access token. */
export async function backendFetch<T>(path: string, init: { method?: string; body?: unknown; token?: string | null } = {}): Promise<BackendResult<T>> {
  const token = init.token === undefined ? await getAccessToken() : init.token;
  const response = await fetch(`${serverConfig.apiUrl}/api/v1/${path.replace(/^\//, "")}`, {
    method: init.method ?? "GET",
    headers: {
      Accept: "application/json",
      ...(init.body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: init.body !== undefined ? JSON.stringify(init.body) : undefined,
    cache: "no-store",
  });
  const text = await response.text();
  const json = text ? JSON.parse(text) : null;
  if (response.ok) {
    return { ok: true, status: response.status, data: json as T };
  }
  return { ok: false, status: response.status, problem: (json ?? { status: response.status, title: response.statusText }) as ApiProblem };
}

/** The signed-in user's identity and effective permissions (once per request). */
export const getMe = cache(async (): Promise<Me | null> => {
  const token = await getAccessToken();
  if (!token) return null;
  const result = await backendFetch<Me>("auth/me", { token });
  return result.ok ? result.data : null;
});
