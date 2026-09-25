"use client";

import { useState } from "react";
import { Alert, Button, Card, Field, Input } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Supplier } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function SupplierForm({ existing }: { existing?: Supplier }) {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  if (!open) return existing
    ? <Button variant="ghost" onClick={() => setOpen(true)}>Edit</Button>
    : <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)}>Add supplier</Button></div>;
  const form = (
    <form className="grid gap-3 text-left md:grid-cols-4" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      const body = {
        name: f.get("name"), contactName: f.get("contactName") || null, mobile: f.get("mobile") || null, email: f.get("email") || null,
        gstin: f.get("gstin") || null, address: f.get("address") || null, isActive: f.get("isActive") === "on", reason: f.get("reason"),
      };
      const ok = await run(() => existing ? callApi(`admin/purchasing/suppliers/${existing.id}`, "PUT", body) : callApi("admin/purchasing/suppliers", "POST", body));
      if (ok) setOpen(false);
    }}>
      {error && <div className="md:col-span-4"><Alert>{error}</Alert></div>}
      <Field label="Name"><Input name="name" required defaultValue={existing?.name} /></Field>
      <Field label="Contact person"><Input name="contactName" defaultValue={existing?.contactName ?? ""} /></Field>
      <Field label="Mobile"><Input name="mobile" defaultValue={existing?.mobile ?? ""} /></Field>
      <Field label="Email"><Input name="email" type="email" defaultValue={existing?.email ?? ""} /></Field>
      <Field label="GSTIN"><Input name="gstin" defaultValue={existing?.gstin ?? ""} /></Field>
      <Field label="Address"><Input name="address" defaultValue={existing?.address ?? ""} /></Field>
      <Field label="Reason"><Input name="reason" required /></Field>
      <div className="flex items-end gap-3">
        <label className="flex items-center gap-1 text-sm"><input type="checkbox" name="isActive" defaultChecked={existing?.isActive ?? true} className="accent-brand-600" /> Active</label>
        <Button type="submit" disabled={busy}>Save</Button>
        <Button type="button" variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
      </div>
    </form>
  );
  return existing ? form : <Card title="New supplier" className="mb-6">{form}</Card>;
}
