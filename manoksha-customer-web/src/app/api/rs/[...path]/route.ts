import { NextResponse } from "next/server";
import { serverConfig } from "@/lib/config";
import { isSameOrigin, resellerToken } from "@/lib/session";

/**
 * Same-origin proxy to the backend's reseller API only (`/api/v1/reseller/*`). State-changing calls must be same-origin and
 * carry the custom header (CSRF). The Idempotency-Key header is forwarded so repeated clicks create one order.
 */
async function handle(request: Request, context: { params: Promise<{ path: string[] }> }) {
  const path = (await context.params).path.join("/");
  if (path.includes("..")) return NextResponse.json({ status: 400, title: "Bad path" }, { status: 400 });
  if (request.method !== "GET" && (!isSameOrigin(request) || request.headers.get("x-manoksha-client") !== "customer-web")) {
    return NextResponse.json({ status: 403, title: "Forbidden", code: "CSRF_REJECTED" }, { status: 403 });
  }
  const token = await resellerToken();
  const url = new URL(request.url);
  const contentType = request.headers.get("content-type");
  const body = request.method === "GET" ? undefined : await request.arrayBuffer();
  const upstream = await fetch(`${serverConfig.apiUrl}/api/v1/reseller/${path}${url.search}`, {
    method: request.method,
    headers: {
      Accept: "application/json",
      ...(contentType && body ? { "Content-Type": contentType } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(request.headers.get("idempotency-key") ? { "Idempotency-Key": request.headers.get("idempotency-key")! } : {}),
    },
    body,
    cache: "no-store",
  });
  return new NextResponse(upstream.status === 204 ? null : upstream.body, {
    status: upstream.status,
    headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json", "Cache-Control": "no-store" },
  });
}

export { handle as GET, handle as POST, handle as PUT };
