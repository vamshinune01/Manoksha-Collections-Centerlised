"use client";

import { useState } from "react";
import { ApiError, cs } from "@/lib/client-api";
import type { CustomerProfile } from "@/lib/types";
import { Alert, Button, Card, Field, Input, PageHeader, formatDateTime } from "@/components/ui";

/** The customer's own profile. The mobile number is the sign-in identity and cannot be changed here. */
export function ProfileForm({ profile }: { profile: CustomerProfile }) {
  const [fullName, setFullName] = useState(profile.fullName);
  const [email, setEmail] = useState(profile.email ?? "");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  return (
    <div className="mx-auto max-w-lg">
      <PageHeader title="My account" description={`Member since ${formatDateTime(profile.memberSince)}`} />
      <Card title="Profile">
        <form className="space-y-4" onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          setMessage(null);
          try {
            await cs("profile", "PUT", { fullName, email });
            setMessage({ tone: "success", text: "Saved." });
          } catch (err) {
            setMessage({ tone: "error", text: err instanceof ApiError ? err.message : "Could not save." });
          } finally {
            setBusy(false);
          }
        }}>
          {message && <Alert tone={message.tone}>{message.text}</Alert>}
          <Field label="Mobile number" hint="Used to sign in; it cannot be changed here."><Input value={profile.mobile ?? ""} disabled /></Field>
          <Field label="Full name"><Input value={fullName} onChange={(e) => setFullName(e.target.value)} required autoComplete="name" /></Field>
          <Field label="Email"><Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email" /></Field>
          <Button type="submit" disabled={busy}>{busy ? "Saving…" : "Save"}</Button>
        </form>
      </Card>
    </div>
  );
}
