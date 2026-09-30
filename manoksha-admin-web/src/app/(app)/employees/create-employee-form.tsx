"use client";

import { useState } from "react";
import { Alert, Button, Card, Field, Input, Select } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Branch } from "@/lib/types";
import { useAction } from "@/lib/use-action";
import { InviteLink } from "@/components/invite-link";

export function CreateEmployeeForm({ branches }: { branches: Branch[] }) {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  const [created, setCreated] = useState<{ email: string; password: string | null; inviteToken: string | null; inviteExpiresAt: string | null }>();

  if (!open) return <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)}>Add employee</Button></div>;
  return (
    <Card title="New employee" className="mb-6" actions={<Button variant="ghost" onClick={() => { setOpen(false); setCreated(undefined); }}>Close</Button>}>
      {created ? (
        created.inviteToken && created.inviteExpiresAt ? (
          <div className="space-y-3">
            <Alert tone="success">Login created for <strong>{created.email}</strong>. The Owner must assign a role before the employee can use the system.</Alert>
            <InviteLink token={created.inviteToken} expiresAt={created.inviteExpiresAt} />
          </div>
        ) : (
          <Alert tone="success">
            Login created for <strong>{created.email}</strong>. Temporary password (shown once):{" "}
            <code className="rounded bg-white px-1.5 py-0.5 font-mono">{created.password}</code>. The Owner must assign a role before the employee can use the system.
          </Alert>
        )
      ) : (
        <form
          className="grid gap-4 md:grid-cols-2"
          onSubmit={async (e) => {
            e.preventDefault();
            const f = new FormData(e.currentTarget);
            const email = String(f.get("email"));
            const r = await run(() =>
              callApi<{ temporaryPassword: string | null; inviteToken: string | null; inviteExpiresAt: string | null }>("admin/employees", "POST", {
                fullName: f.get("fullName"),
                email,
                mobile: f.get("mobile") || null,
                assignedBranchId: f.get("branchId"),
                joinedOn: f.get("joinedOn") || null,
                reason: f.get("reason"),
                sendInvite: f.get("sendInvite") === "on",
              }),
            );
            if (r) setCreated({ email, password: r.temporaryPassword, inviteToken: r.inviteToken, inviteExpiresAt: r.inviteExpiresAt });
          }}
        >
          {error && <div className="md:col-span-2"><Alert>{error}</Alert></div>}
          <Field label="Full name"><Input name="fullName" required /></Field>
          <Field label="Work email"><Input name="email" type="email" required /></Field>
          <Field label="Mobile"><Input name="mobile" inputMode="tel" /></Field>
          <Field label="Branch">
            <Select name="branchId" required>{branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}</Select>
          </Field>
          <Field label="Joined on"><Input name="joinedOn" type="date" /></Field>
          <Field label="Reason"><Input name="reason" required /></Field>
          <label className="flex items-center gap-2 text-sm text-slate-700 md:col-span-2">
            <input type="checkbox" name="sendInvite" defaultChecked /> Send an invite link (they choose their own password) instead of a temporary password
          </label>
          <div className="md:col-span-2"><Button type="submit" disabled={busy}>Create employee</Button></div>
        </form>
      )}
    </Card>
  );
}
