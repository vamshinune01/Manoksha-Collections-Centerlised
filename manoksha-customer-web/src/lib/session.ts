import "server-only";
import { cookies } from "next/headers";
import { CS_ACCESS, CS_REFRESH, RS_ACCESS, RS_REFRESH, serverConfig } from "./config";

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
function writeSession(jar: CookieWriter, tokens: TokenPair, access: string, refresh: string) {
  const base = { httpOnly: true, secure: serverConfig.secureCookies, sameSite: "lax", path: "/" };
  jar.set(access, tokens.accessToken, { ...base, maxAge: Math.max(1, secondsUntil(tokens.accessTokenExpiresAt) - 30) });
  jar.set(refresh, tokens.refreshToken, { ...base, maxAge: secondsUntil(tokens.refreshTokenExpiresAt) });
}

export const writeResellerSession = (jar: CookieWriter, tokens: TokenPair) => writeSession(jar, tokens, RS_ACCESS, RS_REFRESH);

export const writeCustomerSession = (jar: CookieWriter, tokens: TokenPair) => writeSession(jar, tokens, CS_ACCESS, CS_REFRESH);

export function clearResellerSession(jar: CookieWriter) {
  jar.delete(RS_ACCESS);
  jar.delete(RS_REFRESH);
}

export function clearCustomerSession(jar: CookieWriter) {
  jar.delete(CS_ACCESS);
  jar.delete(CS_REFRESH);
}

export async function resellerToken() {
  return (await cookies()).get(RS_ACCESS)?.value;
}

export async function customerToken() {
  return (await cookies()).get(CS_ACCESS)?.value;
}

export function isSameOrigin(request: Request) {
  const origin = request.headers.get("origin");
  if (!origin) return false;
  // Compare with the host the browser addressed. Behind Cloud Run / a load balancer the server's own request URL is an
  // internal address (e.g. 0.0.0.0:8080), so it cannot be used; the Host header carries the public host.
  const host = request.headers.get("host");
  try {
    return !!host && new URL(origin).host === host;
  } catch {
    return false;
  }
}
