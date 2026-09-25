import "server-only";

export const serverConfig = {
  apiUrl: (process.env.MANOKSHA_API_URL ?? "http://localhost:5080").replace(/\/$/, ""),
  secureCookies: process.env.NODE_ENV === "production",
};

/** Reseller session cookies (separate from any future customer session: different token audience). */
export const RS_ACCESS = "mk_rs_at";
export const RS_REFRESH = "mk_rs_rt";
