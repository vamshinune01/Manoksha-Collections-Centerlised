"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Card, Field, Input } from "@/components/ui";
import { ApiError, callApi } from "@/lib/client-api";
import { InviteLink } from "@/components/invite-link";

export function CreateUserForm() {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string>();
  const [created, setCreated] = useState<{ email: string; temporaryPassword: string | null; inviteToken: string | null; inviteExpiresAt: string | null }>();
  const [busy, setBusy] = useState(false);

  if (!open) {
    return (
      <div className="mb-4 flex justify-end">
        <Button onClick={() => setOpen(true)}>Add user</Button>
      </div>
    );
  }

  return (
    <Card title="Add internal user" className="mb-6" actions={<Button variant="ghost" onClick={() => { setOpen(false); setCreated(undefined); }}>Close</Button>}>
      {created ? (
        created.inviteToken && created.inviteExpiresAt ? (
          <div className="space-y-3">
            <Alert tone="success">Account created for <strong>{created.email}</strong>. Assign a role from the user page.</Alert>
            <InviteLink token={created.inviteToken} expiresAt={created.inviteExpiresAt} />
          </div>
        ) : (
          <Alert tone="success">
            Account created for <strong>{created.email}</strong>. Temporary password (shown once):{" "}
            <code className="rounded bg-white px-1.5 py-0.5 font-mono">{created.temporaryPassword}</code>. Share it privately; the user must change it on first sign-in. Assign a role from the user page.
          </Alert>
        )
      ) : (
        <form
          className="grid gap-4 md:grid-cols-2"
          onSubmit={async (e) => {
            e.preventDefault();
            const form = new FormData(e.currentTarget);
            setBusy(true);
            setError(undefined);
            try {
              const email = String(form.get("email"));
              const r = await callApi<{ userId: string; temporaryPassword: string | null; inviteToken: string | null; inviteExpiresAt: string | null }>("admin/users", "POST", {
                email,
                displayName: form.get("displayName"),
                mobile: form.get("mobile") || null,
                reason: form.get("reason"),
                sendInvite: form.get("sendInvite") === "on",
              });
              setCreated({ email, ...r });
              router.refresh();
            } catch (err) {
              setError(err instanceof ApiError ? err.message : "Could not create the user.");
            } finally {
              setBusy(false);
            }
          }}
        >
          {error && <div className="md:col-span-2"><Alert>{error}</Alert></div>}
          <Field label="Full name"><Input name="displayName" required /></Field>
          <Field label="Work email"><Input name="email" type="email" required /></Field>
          <Field label="Mobile (optional)"><Input name="mobile" inputMode="tel" /></Field>
          <Field label="Reason" hint="Recorded in the audit log."><Input name="reason" required placeholder="e.g. New sales staff, Karimnagar" /></Field>
          <label className="flex items-center gap-2 text-sm text-slate-700 md:col-span-2">
            <input type="checkbox" name="sendInvite" defaultChecked /> Send an invite link (they choose their own password) instead of a temporary password
          </label>
          <div className="md:col-span-2"><Button type="submit" disabled={busy}>Create account</Button></div>
        </form>
      )}
    </Card>
  );
}
