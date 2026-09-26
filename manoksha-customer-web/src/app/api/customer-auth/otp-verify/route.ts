import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { CS_REGISTRATION, serverConfig } from "@/lib/config";
import { isSameOrigin, type TokenPair, writeCustomerSession } from "@/lib/session";

/**
 * Verifies the customer OTP. Returning customers get httpOnly session cookies; first-time customers get a short-lived
 * httpOnly registration cookie and are asked for name and email (SPEC §5.2). Tokens never reach page scripts.
 */
export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const { mobile, code } = (await request.json()) as { mobile?: string; code?: string };
  const upstream = await fetch(`${serverConfig.apiUrl}/api/v1/auth/otp/verify`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ mobile, code, context: "customer" }),
    cache: "no-store",
  });
  const json = await upstream.json().catch(() => ({}));
  if (!upstream.ok) return NextResponse.json(json, { status: upstream.status });
  const jar = await cookies();
  if (json.status === "REGISTRATION_REQUIRED") {
    jar.set(CS_REGISTRATION, json.challengeToken, { httpOnly: true, secure: serverConfig.secureCookies, sameSite: "strict", path: "/api/customer-auth", maxAge: 600 });
    return NextResponse.json({ status: "REGISTRATION_REQUIRED" });
  }
  writeCustomerSession(jar, json.tokens as TokenPair);
  return NextResponse.json({ status: "AUTHENTICATED" });
}
