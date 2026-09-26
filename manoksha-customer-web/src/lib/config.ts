import "server-only";

export const serverConfig = {
  apiUrl: (process.env.MANOKSHA_API_URL ?? "http://localhost:5080").replace(/\/$/, ""),
  secureCookies: process.env.NODE_ENV === "production",
};

/** Reseller session cookies (separate from the customer session: different token audience, ADR-001 §2). */
export const RS_ACCESS = "mk_rs_at";
export const RS_REFRESH = "mk_rs_rt";

/** Customer session cookies. */
export const CS_ACCESS = "mk_cs_at";
export const CS_REFRESH = "mk_cs_rt";
/** Short-lived registration challenge after a first-time OTP (never readable by page scripts). */
export const CS_REGISTRATION = "mk_cs_reg";
