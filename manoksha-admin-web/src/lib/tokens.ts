/** Token pair as returned by the backend. Shared by server code only (cookies are httpOnly). */
export interface TokenPair {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export interface AuthResponse {
  status: "AUTHENTICATED" | "PASSWORD_CHANGE_REQUIRED" | "MFA_REQUIRED" | "MFA_ENROLLMENT_REQUIRED";
  tokens?: TokenPair | null;
  challengeToken?: string | null;
}

export interface CookieWriter {
  set(name: string, value: string, options: Record<string, unknown>): unknown;
  delete(name: string): unknown;
}

export function secondsUntil(iso: string): number {
  return Math.max(0, Math.floor((new Date(iso).getTime() - Date.now()) / 1000));
}
