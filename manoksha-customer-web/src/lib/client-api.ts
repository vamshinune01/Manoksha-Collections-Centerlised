"use client";

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly code: string | undefined, message: string, public readonly details?: Record<string, unknown>) {
    super(message);
  }
}

/** Browser → same-origin BFF → backend `/api/v1/reseller/...`. Prices and totals are always the backend's. */
export async function rs<T = unknown>(path: string, method: "GET" | "POST" | "PUT" = "GET", body?: unknown, headers?: Record<string, string>): Promise<T> {
  const isForm = typeof FormData !== "undefined" && body instanceof FormData;
  const response = await fetch(`/api/rs/${path.replace(/^\//, "")}`, {
    method,
    headers: { "x-manoksha-client": "customer-web", ...(body !== undefined && !isForm ? { "Content-Type": "application/json" } : {}), ...headers },
    body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
  });
  const text = await response.text();
  const json = text ? JSON.parse(text) : undefined;
  if (!response.ok) {
    if (response.status === 401 && typeof window !== "undefined") {
      // Full reload on purpose: drops client state from the expired session.
      // eslint-disable-next-line @next/next/no-location-assign-relative-destination
      window.location.href = "/reseller/login";
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
