import { NextResponse, type NextRequest } from "next/server";

const COOKIE_ACCESS = "mk_at";
const COOKIE_REFRESH = "mk_rt";
const API_URL = (process.env.MANOKSHA_API_URL ?? "http://localhost:5080").replace(/\/$/, "");
const SECURE = process.env.NODE_ENV === "production";

/**
 * Keeps the session fresh: when the short-lived access cookie has expired but a refresh cookie exists, rotate
 * tokens with the backend before rendering. Without any session, pages redirect to /login.
 */
export async function proxy(request: NextRequest) {
  const access = request.cookies.get(COOKIE_ACCESS)?.value;
  const refresh = request.cookies.get(COOKIE_REFRESH)?.value;
  const isApi = request.nextUrl.pathname.startsWith("/api/");

  if (access) return NextResponse.next();

  if (refresh) {
    const res = await fetch(`${API_URL}/api/v1/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: refresh }),
      cache: "no-store",
    }).catch(() => null);

    if (res?.ok) {
      const tokens = (await res.json()) as { accessToken: string; accessTokenExpiresAt: string; refreshToken: string; refreshTokenExpiresAt: string };
      const accessMaxAge = Math.max(1, Math.floor((Date.parse(tokens.accessTokenExpiresAt) - Date.now()) / 1000) - 30);
      const refreshMaxAge = Math.max(1, Math.floor((Date.parse(tokens.refreshTokenExpiresAt) - Date.now()) / 1000));
      const cookieOptions = { httpOnly: true, secure: SECURE, sameSite: "lax" as const, path: "/" };

      // Make the new access token visible to this same request, and persist both cookies.
      request.cookies.set(COOKIE_ACCESS, tokens.accessToken);
      const response = NextResponse.next({ request: { headers: request.headers } });
      response.cookies.set(COOKIE_ACCESS, tokens.accessToken, { ...cookieOptions, maxAge: accessMaxAge });
      response.cookies.set(COOKIE_REFRESH, tokens.refreshToken, { ...cookieOptions, maxAge: refreshMaxAge });
      return response;
    }
  }

  if (isApi) {
    return NextResponse.json({ status: 401, title: "Your session has expired. Please sign in again.", code: "SESSION_EXPIRED" }, { status: 401 });
  }
  const login = new URL("/login", request.url);
  if (request.nextUrl.pathname !== "/") login.searchParams.set("next", request.nextUrl.pathname);
  const response = NextResponse.redirect(login);
  response.cookies.delete(COOKIE_REFRESH);
  return response;
}

export const config = {
  // Everything except the login page, auth route handlers, Next internals and static files.
  matcher: ["/((?!login|api/auth|_next/static|_next/image|favicon.ico|.*\\.(?:png|svg|ico|jpg|webp)$).*)"],
};
