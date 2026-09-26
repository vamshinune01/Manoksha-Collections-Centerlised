import "server-only";
import { NextResponse } from "next/server";
import { serverConfig } from "./config";
import { isSameOrigin } from "./session";

/**
 * Same-origin BFF forwarding to one backend area. State-changing calls must be same-origin and carry the custom header (CSRF).
 * The Idempotency-Key header is forwarded so repeated clicks are one logical operation.
 */
export async function forward(request: Request, apiPrefix: string, path: string[], token: string | undefined) {
  const joined = path.join("/");
  if (joined.includes("..")) return NextResponse.json({ status: 400, title: "Bad path" }, { status: 400 });
  if (request.method !== "GET" && (!isSameOrigin(request) || request.headers.get("x-manoksha-client") !== "customer-web")) {
    return NextResponse.json({ status: 403, title: "Forbidden", code: "CSRF_REJECTED" }, { status: 403 });
  }
  const url = new URL(request.url);
  const contentType = request.headers.get("content-type");
  const body = request.method === "GET" ? undefined : await request.arrayBuffer();
  const idempotencyKey = request.headers.get("idempotency-key");
  const upstream = await fetch(`${serverConfig.apiUrl}${apiPrefix}/${joined}${url.search}`, {
    method: request.method,
    headers: {
      Accept: "application/json",
      ...(contentType && body ? { "Content-Type": contentType } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(idempotencyKey ? { "Idempotency-Key": idempotencyKey } : {}),
    },
    body,
    cache: "no-store",
  });
  return new NextResponse(upstream.status === 204 ? null : upstream.body, {
    status: upstream.status,
    headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json", "Cache-Control": "no-store" },
  });
}
