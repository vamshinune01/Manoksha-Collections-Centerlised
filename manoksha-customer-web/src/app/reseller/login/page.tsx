"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Field, Input } from "@/components/ui";
import { ApiError, post } from "@/lib/client-api";

/** Resellers sign in with their registered mobile number + OTP. The first successful sign-in activates the account. */
export default function ResellerLogin() {
  const router = useRouter();
  const [mobile, setMobile] = useState("");
  const [sent, setSent] = useState(false);
  const [code, setCode] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  async function act(fn: () => Promise<void>) {
    setBusy(true);
    setError(undefined);
    try { await fn(); } catch (e) { setError(e instanceof ApiError ? e.message : "Something went wrong. Please try again."); } finally { setBusy(false); }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-gradient-to-br from-brand-50 via-white to-slate-100 px-4">
      <div className="w-full max-w-sm rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
        <p className="text-xs font-semibold uppercase tracking-[0.25em] text-brand-600">Manoksha Collections</p>
        <h1 className="mt-2 text-xl font-semibold text-slate-900">Reseller sign in</h1>
        <p className="mt-1 text-sm text-slate-600">Use the mobile number registered with Manoksha Collections.</p>
        <div className="mt-5 space-y-4">
          {error && <Alert>{error}</Alert>}
          {!sent ? (
            <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); void act(async () => { await post("/api/reseller-auth/otp-request", { mobile }); setSent(true); }); }}>
              <Field label="Mobile number"><Input value={mobile} onChange={(e) => setMobile(e.target.value)} inputMode="tel" autoComplete="tel" placeholder="98xxxxxxxx" required autoFocus /></Field>
              <Button type="submit" className="w-full" disabled={busy}>Send OTP</Button>
            </form>
          ) : (
            <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); void act(async () => { await post("/api/reseller-auth/otp-verify", { mobile, code }); router.replace("/reseller"); router.refresh(); }); }}>
              <Alert tone="info">If {mobile} is registered as a reseller, a 6-digit code has been sent by SMS.</Alert>
              <Field label="OTP"><Input value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" pattern="[0-9]{6}" maxLength={6} autoComplete="one-time-code" required autoFocus /></Field>
              <Button type="submit" className="w-full" disabled={busy}>Verify &amp; sign in</Button>
              <button type="button" className="w-full text-sm text-slate-500" onClick={() => { setSent(false); setCode(""); }}>Change number</button>
            </form>
          )}
        </div>
      </div>
    </main>
  );
}
