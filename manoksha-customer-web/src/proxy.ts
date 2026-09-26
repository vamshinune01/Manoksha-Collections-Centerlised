import { NextResponse, type NextRequest } from "next/server";

const API_URL = (process.env.MANOKSHA_API_URL ?? "http://localhost:5080").replace(/\/$/, "");
const SECURE = process.env.NODE_ENV === "production";

/** The two signed-in areas keep separate sessions (different token audiences, ADR-001 §2). */
const AREAS = {
  reseller: { access: "mk_rs_at", refresh: "mk_rs_rt", login: () => "/reseller/login" },
  customer: { access: "mk_cs_at", refresh: "mk_cs_rt", login: (path: string) => `/login?next=${encodeURIComponent(path)}` },
} as const;

/** Keeps sessions fresh and sends signed-out visitors of protected pages to the right sign-in page. */
export async function proxy(request: NextRequest) {
  const path = request.nextUrl.pathname;
  const area = path.startsWith("/reseller") || path.startsWith("/api/rs") ? AREAS.reseller : AREAS.customer;
  if (request.cookies.get(area.access)?.value) return NextResponse.next();

  const refresh = request.cookies.get(area.refresh)?.value;
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
      request.cookies.set(area.access, t.accessToken);
      const response = NextResponse.next({ request: { headers: request.headers } });
      response.cookies.set(area.access, t.accessToken, { ...opts, maxAge: Math.max(1, Math.floor((Date.parse(t.accessTokenExpiresAt) - Date.now()) / 1000) - 30) });
      response.cookies.set(area.refresh, t.refreshToken, { ...opts, maxAge: Math.max(1, Math.floor((Date.parse(t.refreshTokenExpiresAt) - Date.now()) / 1000)) });
      return response;
    }
  }
  if (path.startsWith("/api/")) {
    return NextResponse.json({ status: 401, title: "Please sign in again.", code: "SESSION_EXPIRED" }, { status: 401 });
  }
  const response = NextResponse.redirect(new URL(area.login(path + request.nextUrl.search), request.url));
  response.cookies.delete(area.refresh);
  return response;
}

export const config = {
  matcher: ["/reseller/((?!login).*)", "/reseller", "/api/rs/:path*", "/checkout", "/account", "/orders", "/orders/:path*", "/api/cs/:path*"],
};
