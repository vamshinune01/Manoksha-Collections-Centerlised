"use client";

import { useState } from "react";
import { Alert, Button, Field, Input } from "@/components/ui";
import { ApiError, postJson } from "@/lib/client-api";

export function AcceptForm({ token, email }: { token: string; email: string }) {
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  if (done) return <Alert tone="success">Your password is set. <a href="/login" className="font-medium underline">Sign in</a> with your work email.</Alert>;
  return (
    <form className="space-y-4" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      if (f.get("password") !== f.get("confirm")) { setError("The passwords do not match."); return; }
      setBusy(true);
      setError(undefined);
      try {
        await postJson(`/api/invitations/${encodeURIComponent(token)}/accept`, { password: f.get("password") });
        setDone(true);
      } catch (err) {
        setError(err instanceof ApiError ? err.message : "Could not set the password.");
      } finally {
        setBusy(false);
      }
    }}>
      <p className="text-sm text-slate-600">Choose a password for your staff account <strong>{email}</strong>.</p>
      {error && <Alert>{error}</Alert>}
      <Field label="New password" hint="At least 10 characters, with a letter and a digit."><Input name="password" type="password" autoComplete="new-password" required minLength={10} /></Field>
      <Field label="Confirm password"><Input name="confirm" type="password" autoComplete="new-password" required minLength={10} /></Field>
      <Button type="submit" disabled={busy} className="w-full">{busy ? "Saving…" : "Set password"}</Button>
    </form>
  );
}
