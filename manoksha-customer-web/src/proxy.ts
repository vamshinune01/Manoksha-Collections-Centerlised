import { NextResponse, type NextRequest } from "next/server";

const ACCESS = "mk_rs_at";
const REFRESH = "mk_rs_rt";
const API_URL = (process.env.MANOKSHA_API_URL ?? "http://localhost:5080").replace(/\/$/, "");
const SECURE = process.env.NODE_ENV === "production";

/** Keeps the reseller session fresh and sends signed-out visitors of /reseller/* to the reseller login. */
export async function proxy(request: NextRequest) {
  if (request.cookies.get(ACCESS)?.value) return NextResponse.next();
  const refresh = request.cookies.get(REFRESH)?.value;
  if (refresh) {
    const res = await fetch(`${API_URL}/api/v1/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: refresh }),
      cache: "no-store",
    }).catch(() => null);
    if (res?.ok) {
      const t = (await res.json()) as { accessToken: string; accessTokenExpiresAt: string; refreshToken: string; refreshTokenExpiresAt: string };
      const opts = { httpOnly: true, secure: SECURE, sameSite: "lax" as const, path: "/" };
      request.cookies.set(ACCESS, t.accessToken);
      const response = NextResponse.next({ request: { headers: request.headers } });
      response.cookies.set(ACCESS, t.accessToken, { ...opts, maxAge: Math.max(1, Math.floor((Date.parse(t.accessTokenExpiresAt) - Date.now()) / 1000) - 30) });
      response.cookies.set(REFRESH, t.refreshToken, { ...opts, maxAge: Math.max(1, Math.floor((Date.parse(t.refreshTokenExpiresAt) - Date.now()) / 1000)) });
      return response;
    }
  }
  if (request.nextUrl.pathname.startsWith("/api/")) {
    return NextResponse.json({ status: 401, title: "Please sign in again.", code: "SESSION_EXPIRED" }, { status: 401 });
  }
  const response = NextResponse.redirect(new URL("/reseller/login", request.url));
  response.cookies.delete(REFRESH);
  return response;
}

export const config = { matcher: ["/reseller/((?!login).*)", "/reseller", "/api/rs/:path*"] };
