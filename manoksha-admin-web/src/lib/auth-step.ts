import "server-only";
import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { serverConfig } from "./config";
import { writeSessionCookies } from "./session";
import type { AuthResponse } from "./tokens";
import { upstreamFetch } from "./upstream";

/**
 * Forwards one sign-in step to the backend. When the backend returns tokens they are stored in httpOnly cookies and
 * never returned to the browser; challenge tokens (short-lived, single purpose) are returned for the next step.
 */
export async function forwardAuthStep(backendPath: string, body: unknown): Promise<NextResponse> {
  const response = await upstreamFetch(`${serverConfig.apiUrl}/api/v1/${backendPath}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });
  const text = await response.text();
  const json = text ? JSON.parse(text) : {};
  if (!response.ok) {
    return NextResponse.json(json, { status: response.status });
  }
  if (response.status === 204) {
    return new NextResponse(null, { status: 204 });
  }
  const auth = json as AuthResponse & Record<string, unknown>;
  if (auth.tokens) {
    writeSessionCookies(await cookies(), auth.tokens);
    const { tokens: _tokens, ...rest } = auth;
    void _tokens;
    return NextResponse.json(rest);
  }
  return NextResponse.json(auth);
}

export function isSameOrigin(request: Request): boolean {
  const origin = request.headers.get("origin");
  if (!origin) return false;
  // Compare with the host the browser addressed. Behind Cloud Run / a load balancer the server's own request URL is an
  // internal address (e.g. 0.0.0.0:8080), so it cannot be used; the Host header carries the public host.
  const host = request.headers.get("host");
  try {
    return !!host && new URL(origin).host === host;
  } catch {
    return false;
  }
}
