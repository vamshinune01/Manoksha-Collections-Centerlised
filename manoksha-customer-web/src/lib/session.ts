import "server-only";
import { cookies } from "next/headers";
import { RS_ACCESS, RS_REFRESH, serverConfig } from "./config";

export interface TokenPair {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

interface CookieWriter {
  set(name: string, value: string, options: Record<string, unknown>): unknown;
  delete(name: string): unknown;
}

const secondsUntil = (iso: string) => Math.max(1, Math.floor((new Date(iso).getTime() - Date.now()) / 1000));

/** httpOnly cookies only — tokens are never readable by page scripts. */
export function writeResellerSession(jar: CookieWriter, tokens: TokenPair) {
  const base = { httpOnly: true, secure: serverConfig.secureCookies, sameSite: "lax", path: "/" };
  jar.set(RS_ACCESS, tokens.accessToken, { ...base, maxAge: Math.max(1, secondsUntil(tokens.accessTokenExpiresAt) - 30) });
  jar.set(RS_REFRESH, tokens.refreshToken, { ...base, maxAge: secondsUntil(tokens.refreshTokenExpiresAt) });
}

export function clearResellerSession(jar: CookieWriter) {
  jar.delete(RS_ACCESS);
  jar.delete(RS_REFRESH);
}

export async function resellerToken() {
  return (await cookies()).get(RS_ACCESS)?.value;
}

export function isSameOrigin(request: Request) {
  const origin = request.headers.get("origin");
  return !!origin && origin === new URL(request.url).origin;
}
