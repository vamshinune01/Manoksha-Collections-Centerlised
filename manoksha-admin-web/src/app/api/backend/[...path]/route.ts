import { NextResponse } from "next/server";
import { serverConfig } from "@/lib/config";
import { getAccessToken } from "@/lib/session";
import { isSameOrigin } from "@/lib/auth-step";

/**
 * Same-origin proxy from browser components to the backend, attaching the httpOnly access token.
 * CSRF defence: state-changing calls must be same-origin AND carry the custom header (not settable cross-site
 * without CORS). Only authenticated admin/account routes are reachable through it.
 */
const ALLOWED = [/^admin\//, /^auth\/me$/, /^auth\/internal\/password\/change$/, /^auth\/internal\/mfa\/enroll\/(start|confirm)$/];

async function handle(request: Request, context: { params: Promise<{ path: string[] }> }) {
  const path = (await context.params).path.join("/");
  if (!ALLOWED.some((re) => re.test(path))) {
    return NextResponse.json({ status: 404, title: "Not found" }, { status: 404 });
  }
  if (request.method !== "GET" && (!isSameOrigin(request) || request.headers.get("x-manoksha-client") !== "admin-web")) {
    return NextResponse.json({ status: 403, title: "Forbidden", code: "CSRF_REJECTED" }, { status: 403 });
  }

  const token = await getAccessToken();
  const url = new URL(request.url);
  const body = request.method === "GET" || request.method === "HEAD" ? undefined : await request.text();
  const upstream = await fetch(`${serverConfig.apiUrl}/api/v1/${path}${url.search}`, {
    method: request.method,
    headers: {
      Accept: "application/json",
      ...(body ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(request.headers.get("idempotency-key") ? { "Idempotency-Key": request.headers.get("idempotency-key")! } : {}),
    },
    body,
    cache: "no-store",
  });
  return new NextResponse(upstream.status === 204 ? null : await upstream.text(), {
    status: upstream.status,
    headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json" },
  });
}

export { handle as GET, handle as POST, handle as PUT, handle as PATCH, handle as DELETE };
