import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { CS_REGISTRATION, serverConfig } from "@/lib/config";
import { isSameOrigin, type TokenPair, writeCustomerSession } from "@/lib/session";

/** Completes first-time registration (name + email) using the httpOnly registration cookie set at OTP verification. */
export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const jar = await cookies();
  const registrationToken = jar.get(CS_REGISTRATION)?.value;
  if (!registrationToken) {
    return NextResponse.json({ title: "Your verification expired. Please request a new code.", code: "REGISTRATION_EXPIRED" }, { status: 401 });
  }
  const { fullName, email } = (await request.json()) as { fullName?: string; email?: string };
  const upstream = await fetch(`${serverConfig.apiUrl}/api/v1/auth/customer/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ registrationToken, fullName, email }),
    cache: "no-store",
  });
  const json = await upstream.json().catch(() => ({}));
  if (!upstream.ok) return NextResponse.json(json, { status: upstream.status });
  jar.delete(CS_REGISTRATION);
  writeCustomerSession(jar, json.tokens as TokenPair);
  return NextResponse.json({ status: "AUTHENTICATED" });
}
