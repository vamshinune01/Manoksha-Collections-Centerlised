import "server-only";
import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { serverConfig } from "./config";
import { writeSessionCookies } from "./session";
import type { AuthResponse } from "./tokens";

/**
 * Forwards one sign-in step to the backend. When the backend returns tokens they are stored in httpOnly cookies and
 * never returned to the browser; challenge tokens (short-lived, single purpose) are returned for the next step.
 */
export async function forwardAuthStep(backendPath: string, body: unknown): Promise<NextResponse> {
  const response = await fetch(`${serverConfig.apiUrl}/api/v1/${backendPath}`, {
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
  return origin === new URL(request.url).origin;
}
