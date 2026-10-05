"use client";

import { useState } from "react";
import { Alert, Button, Field, Input } from "@/components/ui";
import { inr } from "@/lib/access";
import { callApi } from "@/lib/client-api";
import type { Vendor } from "@/lib/types";
import { useAction } from "@/lib/use-action";

const num = (v: FormDataEntryValue | null) => (v === null || String(v).trim() === "" ? null : Number(v));

export function CreateVendorForm() {
  const { error, busy, run } = useAction();
  const [done, setDone] = useState<string>();
  return (
    <form
      className="grid gap-3 md:grid-cols-6"
      onSubmit={async (e) => {
        e.preventDefault();
        const form = e.currentTarget;
        const f = new FormData(form);
        setDone(undefined);
        const v = await run(() => callApi<Vendor>("admin/catalog/vendors", "POST", {
          code: String(f.get("code") ?? "").toUpperCase(), name: f.get("name"), shippingFee: num(f.get("shippingFee")) ?? 0,
          ownerMarginPct: num(f.get("ownerMarginPct")), reason: "New vendor",
        }));
        if (v) {
          form.reset();
          setDone(`${v.name} added — its products will be numbered ${v.code}-000001, ${v.code}-000002, …`);
        }
      }}
    >
      {error && <div className="md:col-span-6"><Alert>{error}</Alert></div>}
      {done && <div className="md:col-span-6"><Alert tone="success">{done}</Alert></div>}
      <Field label="Code" hint="2–4 letters, starts every product ID (e.g. ZR)."><Input name="code" required maxLength={4} pattern="[A-Za-z]{2,4}" className="uppercase" /></Field>
      <div className="md:col-span-2"><Field label="Vendor name"><Input name="name" required maxLength={100} placeholder="Zara" /></Field></div>
      <Field label="Shipping (₹)" hint="Charged once per order containing this vendor's items."><Input name="shippingFee" type="number" min="0" step="0.01" defaultValue="100" required /></Field>
      <Field label="Your margin (%)" hint="What the vendor gives you, e.g. 17. Owner-only; used for profit."><Input name="ownerMarginPct" type="number" min="0" max="99.99" step="0.01" /></Field>
      <div className="flex items-end"><Button type="submit" disabled={busy}>Add vendor</Button></div>
    </form>
  );
}

export function EditVendorRow({ vendor, canManage }: { vendor: Vendor; canManage: boolean }) {
  const { error, busy, run } = useAction();
  const [editing, setEditing] = useState(false);
  if (!editing) {
    return (
      <tr className={vendor.isActive ? "" : "opacity-60"}>
        <td className="px-4 py-2 font-mono text-sm">{vendor.code}</td>
        <td className="px-4 py-2 font-medium">{vendor.name}{!vendor.isActive && <span className="ml-2 text-xs text-red-600">inactive</span>}</td>
        <td className="px-4 py-2">{inr(vendor.shippingFee)}</td>
        <td className="px-4 py-2">{vendor.ownerMarginPct === null ? "—" : `${vendor.ownerMarginPct}%`}</td>
        <td className="px-4 py-2 text-right">{vendor.productCount}</td>
        <td className="px-4 py-2 text-right">{canManage && <Button variant="ghost" onClick={() => setEditing(true)}>Edit</Button>}</td>
      </tr>
    );
  }
  return (
    <tr>
      <td colSpan={6} className="px-4 py-3">
        <form
          className="grid gap-3 md:grid-cols-6"
          onSubmit={async (e) => {
            e.preventDefault();
            const f = new FormData(e.currentTarget);
            const ok = await run(() => callApi(`admin/catalog/vendors/${vendor.id}`, "PUT", {
              name: f.get("name"), shippingFee: num(f.get("shippingFee")) ?? 0, ownerMarginPct: num(f.get("ownerMarginPct")),
              isActive: f.get("isActive") === "on", reason: f.get("reason"),
            }));
            if (ok !== undefined) setEditing(false);
          }}
        >
          {error && <div className="md:col-span-6"><Alert>{error}</Alert></div>}
          <Field label={`Name (${vendor.code})`}><Input name="name" defaultValue={vendor.name} required /></Field>
          <Field label="Shipping (₹)"><Input name="shippingFee" type="number" min="0" step="0.01" defaultValue={vendor.shippingFee} required /></Field>
          <Field label="Your margin (%)"><Input name="ownerMarginPct" type="number" min="0" max="99.99" step="0.01" defaultValue={vendor.ownerMarginPct ?? ""} /></Field>
          <label className="flex items-end gap-2 pb-2 text-sm"><input type="checkbox" name="isActive" defaultChecked={vendor.isActive} className="accent-brand-600" /> Active (sold online)</label>
          <Field label="Reason"><Input name="reason" required placeholder="Why the change" /></Field>
          <div className="flex items-end gap-2"><Button type="submit" disabled={busy}>Save</Button><Button type="button" variant="ghost" onClick={() => setEditing(false)}>Cancel</Button></div>
        </form>
      </td>
    </tr>
  );
}
