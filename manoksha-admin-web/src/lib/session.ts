import "server-only";
import { cookies } from "next/headers";
import { COOKIE_ACCESS, COOKIE_REFRESH, serverConfig } from "./config";
import { type CookieWriter, type TokenPair, secondsUntil } from "./tokens";

/**
 * Tokens live only in httpOnly, Secure (prod), SameSite=Lax cookies — never in localStorage or JS-readable
 * storage. The access cookie expires with the access token so proxy.ts knows when to refresh.
 */
export function writeSessionCookies(jar: CookieWriter, tokens: TokenPair) {
  const base = { httpOnly: true, secure: serverConfig.secureCookies, sameSite: "lax", path: "/" };
  jar.set(COOKIE_ACCESS, tokens.accessToken, { ...base, maxAge: Math.max(1, secondsUntil(tokens.accessTokenExpiresAt) - 30) });
  jar.set(COOKIE_REFRESH, tokens.refreshToken, { ...base, maxAge: secondsUntil(tokens.refreshTokenExpiresAt) });
}

export function clearSessionCookies(jar: CookieWriter) {
  jar.delete(COOKIE_ACCESS);
  jar.delete(COOKIE_REFRESH);
}

export async function getAccessToken(): Promise<string | undefined> {
  return (await cookies()).get(COOKIE_ACCESS)?.value;
}
