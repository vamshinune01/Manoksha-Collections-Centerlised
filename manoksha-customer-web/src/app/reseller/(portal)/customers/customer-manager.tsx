"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Card, Field, Input, Table, formatDateTime } from "@/components/ui";
import { ApiError, rs } from "@/lib/client-api";
import type { Delivery, SavedCustomer } from "@/lib/types";

type Row = SavedCustomer & { updatedAt: string };

function CustomerForm({ initial, onDone, id }: { initial?: Delivery; id?: string; onDone: () => void }) {
  const router = useRouter();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  return (
    <form className="grid gap-3 md:grid-cols-3" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      const details = {
        name: f.get("name"), mobile: f.get("mobile"), email: f.get("email") || null,
        addressLine: f.get("addressLine"), city: f.get("city"), state: f.get("state"), pin: f.get("pin"),
      };
      setBusy(true);
      setError(undefined);
      try {
        await (id ? rs(`customers/${id}`, "PUT", { details }) : rs("customers", "POST", { details }));
        router.refresh();
        onDone();
      } catch (err) {
        setError(err instanceof ApiError ? err.message : "Could not save the customer.");
      } finally {
        setBusy(false);
      }
    }}>
      {error && <div className="md:col-span-3"><Alert>{error}</Alert></div>}
      <Field label="Name"><Input name="name" required defaultValue={initial?.name} /></Field>
      <Field label="Mobile"><Input name="mobile" inputMode="tel" required defaultValue={initial?.mobile} /></Field>
      <Field label="Email (optional)"><Input name="email" type="email" defaultValue={initial?.email ?? ""} /></Field>
      <Field label="Address"><Input name="addressLine" required defaultValue={initial?.addressLine} /></Field>
      <Field label="City"><Input name="city" required defaultValue={initial?.city} /></Field>
      <div className="grid grid-cols-2 gap-2">
        <Field label="State"><Input name="state" required defaultValue={initial?.state ?? "Telangana"} /></Field>
        <Field label="PIN"><Input name="pin" inputMode="numeric" maxLength={6} required defaultValue={initial?.pin} /></Field>
      </div>
      <div className="flex gap-2 md:col-span-3">
        <Button type="submit" disabled={busy}>{id ? "Save changes" : "Add customer"}</Button>
        <Button type="button" variant="ghost" onClick={onDone}>Cancel</Button>
      </div>
    </form>
  );
}

export function CustomerManager({ customers }: { customers: Row[] }) {
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<string | null>(null);
  return (
    <div className="space-y-4">
      {adding ? (
        <Card title="New customer"><CustomerForm onDone={() => setAdding(false)} /></Card>
      ) : (
        <div className="flex justify-end"><Button onClick={() => setAdding(true)}>Add customer</Button></div>
      )}
      <Table head={["Name", "Mobile", "Address", "Last updated", ""]} empty={customers.length === 0}>
        {customers.map((c) => editing === c.id ? (
          <tr key={c.id}><td colSpan={5} className="bg-slate-50 px-4 py-3"><CustomerForm id={c.id} initial={c.details} onDone={() => setEditing(null)} /></td></tr>
        ) : (
          <tr key={c.id}>
            <td className="px-4 py-2.5 font-medium">{c.details.name}</td>
            <td className="px-4 py-2.5">{c.details.mobile}</td>
            <td className="px-4 py-2.5 text-sm text-slate-600">{c.details.addressLine}, {c.details.city}, {c.details.state} {c.details.pin}</td>
            <td className="px-4 py-2.5 text-xs text-slate-500">{formatDateTime(c.updatedAt)}</td>
            <td className="px-4 py-2.5 text-right"><Button variant="ghost" onClick={() => setEditing(c.id)}>Edit</Button></td>
          </tr>
        ))}
      </Table>
    </div>
  );
}
