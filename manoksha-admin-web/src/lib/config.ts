import "server-only";

/** Server-side configuration. The backend URL is never sent to the browser. */
export const serverConfig = {
  apiUrl: (process.env.MANOKSHA_API_URL ?? "http://localhost:5080").replace(/\/$/, ""),
  secureCookies: process.env.NODE_ENV === "production",
};

export const COOKIE_ACCESS = "mk_at";
export const COOKIE_REFRESH = "mk_rt";
