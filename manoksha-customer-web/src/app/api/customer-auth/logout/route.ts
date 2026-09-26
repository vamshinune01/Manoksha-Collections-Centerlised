import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { CS_REFRESH, serverConfig } from "@/lib/config";
import { clearCustomerSession, isSameOrigin } from "@/lib/session";

export async function POST(request: Request) {
  if (!isSameOrigin(request)) return NextResponse.json({ title: "Forbidden" }, { status: 403 });
  const jar = await cookies();
  const refreshToken = jar.get(CS_REFRESH)?.value;
  if (refreshToken) {
    await fetch(`${serverConfig.apiUrl}/api/v1/auth/logout`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ refreshToken }), cache: "no-store",
    }).catch(() => undefined);
  }
  clearCustomerSession(jar);
  return new NextResponse(null, { status: 204 });
}
