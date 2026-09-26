"use client";

import { useState } from "react";
import { ApiError, post } from "@/lib/client-api";
import { Alert, Button, Field, Input } from "@/components/ui";

type Step = "mobile" | "otp" | "register";

/** Customer sign-in with mobile + OTP; first-time customers add name and email (SPEC §5.2). */
export function LoginForm({ next }: { next: string }) {
  const [step, setStep] = useState<Step>("mobile");
  const [mobile, setMobile] = useState("");
  const [code, setCode] = useState("");
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await fn();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Something went wrong. Please try again.");
    } finally {
      setBusy(false);
    }
  };

  // Full navigation so the shared header and server components see the new session.
  const done = () => window.location.assign(next);

  return (
    <div className="mx-auto max-w-sm space-y-5 rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
      <div>
        <h1 className="text-lg font-semibold text-slate-900">Sign in to order</h1>
        <p className="mt-1 text-sm text-slate-600">We will send a one-time code to your mobile number.</p>
      </div>
      {error && <Alert>{error}</Alert>}
      {info && !error && <Alert tone="info">{info}</Alert>}

      {step === "mobile" && (
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); void run(async () => {
          await post("/api/customer-auth/otp-request", { mobile });
          setInfo("Code sent. It is valid for 5 minutes.");
          setStep("otp");
        }); }}>
          <Field label="Mobile number"><Input value={mobile} onChange={(e) => setMobile(e.target.value)} placeholder="10-digit mobile" inputMode="tel" autoComplete="tel" required /></Field>
          <Button type="submit" disabled={busy} className="w-full">Send code</Button>
        </form>
      )}

      {step === "otp" && (
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); void run(async () => {
          const r = await post<{ status: string }>("/api/customer-auth/otp-verify", { mobile, code });
          if (r.status === "REGISTRATION_REQUIRED") {
            setInfo("Welcome! Tell us your name and email to finish creating your account.");
            setStep("register");
          } else {
            done();
          }
        }); }}>
          <Field label="6-digit code"><Input value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" maxLength={6} required /></Field>
          <Button type="submit" disabled={busy} className="w-full">Verify</Button>
          <button type="button" className="w-full text-sm text-slate-500 hover:text-slate-700" onClick={() => { setStep("mobile"); setCode(""); setInfo(null); }}>Use a different number</button>
        </form>
      )}

      {step === "register" && (
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); void run(async () => {
          await post("/api/customer-auth/register", { fullName, email });
          done();
        }); }}>
          <Field label="Full name"><Input value={fullName} onChange={(e) => setFullName(e.target.value)} autoComplete="name" required /></Field>
          <Field label="Email"><Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required /></Field>
          <Button type="submit" disabled={busy} className="w-full">Create account</Button>
        </form>
      )}
    </div>
  );
}
