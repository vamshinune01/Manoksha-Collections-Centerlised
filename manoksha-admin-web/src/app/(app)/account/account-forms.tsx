"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Field, Input } from "@/components/ui";
import { ApiError, callApi } from "@/lib/client-api";

export function ChangePasswordForm() {
  const router = useRouter();
  const [error, setError] = useState<string>();
  return (
    <form
      className="space-y-4"
      onSubmit={async (e) => {
        e.preventDefault();
        const form = new FormData(e.currentTarget);
        if (form.get("newPassword") !== form.get("confirm")) {
          setError("The two passwords do not match.");
          return;
        }
        setError(undefined);
        try {
          await callApi("auth/internal/password/change", "POST", { currentPassword: form.get("currentPassword"), newPassword: form.get("newPassword") });
          // Changing the password signs out existing sessions (security stamp rotates).
          await fetch("/api/auth/logout", { method: "POST" });
          router.replace("/login");
        } catch (err) {
          setError(err instanceof ApiError ? err.message : "Could not change the password.");
        }
      }}
    >
      {error && <Alert>{error}</Alert>}
      <Field label="Current password"><Input name="currentPassword" type="password" autoComplete="current-password" required /></Field>
      <Field label="New password" hint="At least 10 characters with a letter and a digit."><Input name="newPassword" type="password" autoComplete="new-password" minLength={10} required /></Field>
      <Field label="Confirm new password"><Input name="confirm" type="password" autoComplete="new-password" minLength={10} required /></Field>
      <Button type="submit">Change password</Button>
    </form>
  );
}

export function MfaSetup({ enabled }: { enabled: boolean }) {
  const router = useRouter();
  const [secret, setSecret] = useState<{ secret: string; otpAuthUri: string }>();
  const [error, setError] = useState<string>();

  if (enabled) return <Alert tone="success">Two-step verification is on for this account.</Alert>;

  return (
    <div className="space-y-4">
      <p className="text-sm text-slate-600">Protect your account with an authenticator app (Google Authenticator, Microsoft Authenticator, …).</p>
      {error && <Alert>{error}</Alert>}
      {!secret ? (
        <Button
          onClick={async () => {
            try {
              setSecret(await callApi("auth/internal/mfa/enroll/start", "POST", {}));
            } catch (e) {
              setError(e instanceof ApiError ? e.message : "Could not start setup.");
            }
          }}
        >
          Set up authenticator
        </Button>
      ) : (
        <form
          className="space-y-3"
          onSubmit={async (e) => {
            e.preventDefault();
            const form = new FormData(e.currentTarget);
            try {
              await callApi("auth/internal/mfa/enroll/confirm", "POST", { code: form.get("code") });
              await fetch("/api/auth/logout", { method: "POST" });
              router.replace("/login");
            } catch (err) {
              setError(err instanceof ApiError ? err.message : "The code was not accepted.");
            }
          }}
        >
          <div className="rounded-md bg-slate-50 p-3 text-xs">
            <p className="font-medium text-slate-700">Key</p>
            <p className="break-all font-mono text-sm tracking-wider">{secret.secret}</p>
            <p className="mt-2 break-all text-slate-500">{secret.otpAuthUri}</p>
          </div>
          <Field label="Code from the app"><Input name="code" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} required /></Field>
          <Button type="submit">Turn on</Button>
        </form>
      )}
    </div>
  );
}
