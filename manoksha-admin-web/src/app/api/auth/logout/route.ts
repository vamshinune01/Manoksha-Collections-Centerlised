import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { COOKIE_REFRESH, serverConfig } from "@/lib/config";
import { clearSessionCookies } from "@/lib/session";
import { isSameOrigin } from "@/lib/auth-step";

export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const jar = await cookies();
  const refreshToken = jar.get(COOKIE_REFRESH)?.value;
  if (refreshToken) {
    await fetch(`${serverConfig.apiUrl}/api/v1/auth/logout`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    }).catch(() => undefined);
  }
  clearSessionCookies(jar);
  return new NextResponse(null, { status: 204 });
}
