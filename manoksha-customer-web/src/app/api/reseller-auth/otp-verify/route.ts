import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { serverConfig } from "@/lib/config";
import { isSameOrigin, type TokenPair, writeResellerSession } from "@/lib/session";

/** Verifies the reseller OTP; tokens go into httpOnly cookies and are never returned to the browser. */
export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const { mobile, code } = (await request.json()) as { mobile?: string; code?: string };
  const upstream = await fetch(`${serverConfig.apiUrl}/api/v1/auth/otp/verify`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ mobile, code, context: "reseller" }),
    cache: "no-store",
  });
  const json = await upstream.json().catch(() => ({}));
  if (!upstream.ok) return NextResponse.json(json, { status: upstream.status });
  writeResellerSession(await cookies(), json.tokens as TokenPair);
  return NextResponse.json({ status: "AUTHENTICATED" });
}
