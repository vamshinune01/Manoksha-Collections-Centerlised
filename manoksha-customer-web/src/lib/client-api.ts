"use client";

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly code: string | undefined, message: string, public readonly details?: Record<string, unknown>) {
    super(message);
  }
}

type Method = "GET" | "POST" | "PUT";

/** Browser → same-origin BFF → backend `/api/v1/reseller/...`. Prices and totals are always the backend's. */
export const rs = <T = unknown>(path: string, method: Method = "GET", body?: unknown, headers?: Record<string, string>) =>
  bff<T>("/api/rs", "/reseller/login", path, method, body, headers);

/** Browser → same-origin BFF → backend `/api/v1/customer/...` (signed-in customer). */
export const cs = <T = unknown>(path: string, method: Method = "GET", body?: unknown, headers?: Record<string, string>) =>
  bff<T>("/api/cs", `/login?next=${encodeURIComponent(typeof window === "undefined" ? "/" : window.location.pathname)}`, path, method, body, headers);

/** Browser → same-origin BFF → anonymous storefront API `/api/v1/catalog/...`. */
export const store = <T = unknown>(path: string, method: Method = "GET", body?: unknown) => bff<T>("/api/store", null, path, method, body);

async function bff<T>(base: string, loginPath: string | null, path: string, method: Method, body?: unknown, headers?: Record<string, string>): Promise<T> {
  const isForm = typeof FormData !== "undefined" && body instanceof FormData;
  const response = await fetch(`${base}/${path.replace(/^\//, "")}`, {
    method,
    headers: { "x-manoksha-client": "customer-web", ...(body !== undefined && !isForm ? { "Content-Type": "application/json" } : {}), ...headers },
    body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
  });
  const text = await response.text();
  const json = text ? JSON.parse(text) : undefined;
  if (!response.ok) {
    if (response.status === 401 && loginPath && typeof window !== "undefined") {
      // Full reload on purpose: drops client state from the expired session.
      window.location.href = loginPath;
    }
    throw new ApiError(response.status, json?.code, json?.title ?? `Request failed (${response.status})`, json);
  }
  return json as T;
}

export async function post<T = unknown>(url: string, body: unknown): Promise<T> {
  const response = await fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  const json = await response.json().catch(() => ({}));
  if (!response.ok) throw new ApiError(response.status, json?.code, json?.title ?? "Request failed", json);
  return json as T;
}
