"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { type FormEvent, useState } from "react";
import { Alert, Button, Field, Input } from "@/components/ui";
import { ApiError, postJson } from "@/lib/client-api";

type Step =
  | { kind: "credentials" }
  | { kind: "PASSWORD_CHANGE_REQUIRED"; challengeToken: string }
  | { kind: "MFA_REQUIRED"; challengeToken: string }
  | { kind: "MFA_ENROLLMENT_REQUIRED"; challengeToken: string; secret?: string; otpAuthUri?: string };

interface StepResponse {
  status: "AUTHENTICATED" | "PASSWORD_CHANGE_REQUIRED" | "MFA_REQUIRED" | "MFA_ENROLLMENT_REQUIRED";
  challengeToken?: string;
}

export function LoginFlow() {
  const router = useRouter();
  const next = useSearchParams().get("next");
  const [step, setStep] = useState<Step>({ kind: "credentials" });
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  async function run(action: () => Promise<StepResponse | void>) {
    setBusy(true);
    setError(undefined);
    try {
      const result = await action();
      if (!result) return;
      if (result.status === "AUTHENTICATED") {
        router.replace(next && next.startsWith("/") && !next.startsWith("//") ? next : "/");
        router.refresh();
      } else {
        setStep({ kind: result.status, challengeToken: result.challengeToken! });
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Something went wrong. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  function value(form: FormData, name: string) {
    return String(form.get(name) ?? "");
  }

  if (step.kind === "credentials") {
    return (
      <form
        className="space-y-4"
        onSubmit={(e: FormEvent<HTMLFormElement>) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          void run(() => postJson<StepResponse>("/api/auth/login", { email: value(form, "email"), password: value(form, "password") }));
        }}
      >
        {error && <Alert>{error}</Alert>}
        <Field label="Work email">
          <Input name="email" type="email" autoComplete="username" required autoFocus />
        </Field>
        <Field label="Password">
          <Input name="password" type="password" autoComplete="current-password" required />
        </Field>
        <Button type="submit" className="w-full" disabled={busy}>
          {busy ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    );
  }

  if (step.kind === "PASSWORD_CHANGE_REQUIRED") {
    return (
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          if (value(form, "newPassword") !== value(form, "confirm")) {
            setError("The two passwords do not match.");
            return;
          }
          void run(() => postJson<StepResponse>("/api/auth/first-password", { challengeToken: step.challengeToken, newPassword: value(form, "newPassword") }));
        }}
      >
        <Alert tone="info">Set a new password to replace your temporary one.</Alert>
        {error && <Alert>{error}</Alert>}
        <Field label="New password" hint="At least 10 characters with a letter and a digit.">
          <Input name="newPassword" type="password" autoComplete="new-password" required minLength={10} autoFocus />
        </Field>
        <Field label="Confirm new password">
          <Input name="confirm" type="password" autoComplete="new-password" required minLength={10} />
        </Field>
        <Button type="submit" className="w-full" disabled={busy}>
          Save and continue
        </Button>
      </form>
    );
  }

  if (step.kind === "MFA_REQUIRED") {
    return (
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          void run(() => postJson<StepResponse>("/api/auth/mfa-verify", { challengeToken: step.challengeToken, code: value(form, "code") }));
        }}
      >
        <Alert tone="info">Enter the 6-digit code from your authenticator app.</Alert>
        {error && <Alert>{error}</Alert>}
        <Field label="Authenticator code">
          <Input name="code" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} autoComplete="one-time-code" required autoFocus />
        </Field>
        <Button type="submit" className="w-full" disabled={busy}>
          Verify
        </Button>
      </form>
    );
  }

  // MFA enrollment required for this role.
  return (
    <div className="space-y-4">
      <Alert tone="warning">Your role requires two-step verification. Set up an authenticator app to continue.</Alert>
      {error && <Alert>{error}</Alert>}
      {!step.secret ? (
        <Button
          className="w-full"
          disabled={busy}
          onClick={() =>
            run(async () => {
              const r = await postJson<{ secret: string; otpAuthUri: string }>("/api/auth/mfa-enroll-start", { challengeToken: step.challengeToken });
              setStep({ ...step, secret: r.secret, otpAuthUri: r.otpAuthUri });
            })
          }
        >
          Set up authenticator
        </Button>
      ) : (
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            const form = new FormData(e.currentTarget);
            void run(() => postJson<StepResponse>("/api/auth/mfa-enroll-confirm", { challengeToken: step.challengeToken, code: value(form, "code") }));
          }}
        >
          <div className="rounded-md bg-slate-50 p-3 text-xs text-slate-700">
            <p className="font-medium">Add this key to your authenticator app:</p>
            <p className="mt-1 break-all font-mono text-sm tracking-wider text-slate-900">{step.secret}</p>
            <p className="mt-2 break-all text-slate-500">{step.otpAuthUri}</p>
          </div>
          <Field label="Code from the app">
            <Input name="code" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} autoComplete="one-time-code" required autoFocus />
          </Field>
          <Button type="submit" className="w-full" disabled={busy}>
            Confirm and sign in
          </Button>
        </form>
      )}
    </div>
  );
}
