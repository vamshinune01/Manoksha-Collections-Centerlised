"use client";

import { useState } from "react";
import { Alert, Button, Field, Input } from "@/components/ui";
import { ApiError, postJson } from "@/lib/client-api";

export function SetupForm() {
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  if (done) {
    return <Alert tone="success">The Owner account is ready. <a href="/login" className="font-medium underline">Sign in</a> with your email and password.</Alert>;
  }
  return (
    <form className="space-y-4" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      if (f.get("password") !== f.get("confirm")) { setError("The passwords do not match."); return; }
      setBusy(true);
      setError(undefined);
      try {
        await postJson("/api/setup/owner", { setupCode: f.get("setupCode"), fullName: f.get("fullName"), email: f.get("email"), password: f.get("password") });
        setDone(true);
      } catch (err) {
        setError(err instanceof ApiError ? err.message : "Setup failed.");
      } finally {
        setBusy(false);
      }
    }}>
      <p className="text-sm text-slate-600">This works only once, while no Owner exists. You need the setup code configured on the server.</p>
      {error && <Alert>{error}</Alert>}
      <Field label="Setup code"><Input name="setupCode" type="password" autoComplete="off" required /></Field>
      <Field label="Your full name"><Input name="fullName" autoComplete="name" required /></Field>
      <Field label="Email (your sign-in)"><Input name="email" type="email" autoComplete="email" required /></Field>
      <Field label="Password" hint="At least 10 characters, with a letter and a digit."><Input name="password" type="password" autoComplete="new-password" required minLength={10} /></Field>
      <Field label="Confirm password"><Input name="confirm" type="password" autoComplete="new-password" required minLength={10} /></Field>
      <Button type="submit" disabled={busy} className="w-full">{busy ? "Creating…" : "Create Owner account"}</Button>
    </form>
  );
}
