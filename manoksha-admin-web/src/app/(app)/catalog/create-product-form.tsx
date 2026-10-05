"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Card, Field, Input, Select, Textarea } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Attribute, Category, Vendor } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function CreateProductForm({ categories, attributes, vendors, vendorRequired }: {
  categories: Category[]; attributes: Attribute[]; vendors: Vendor[]; vendorRequired: boolean;
}) {
  const router = useRouter();
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);

  if (!open) return <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)}>Add product</Button></div>;
  if (categories.length === 0) return <Alert tone="info">Create a category first (Catalog → Categories).</Alert>;
  if (vendorRequired && vendors.length === 0) return <Alert tone="info">Add a vendor first (Catalog → Vendors): every product comes from a vendor.</Alert>;

  return (
    <Card title="New product" className="mb-6" actions={<Button variant="ghost" onClick={() => setOpen(false)}>Close</Button>}>
      <form
        className="grid gap-4 md:grid-cols-2"
        onSubmit={async (e) => {
          e.preventDefault();
          const f = new FormData(e.currentTarget);
          const p = await run(() =>
            callApi<{ id: string }>("admin/catalog/products", "POST", {
              categoryId: f.get("categoryId"),
              vendorId: f.get("vendorId") || null,
              name: f.get("name"),
              description: f.get("description") || null,
              trackingMode: f.get("trackingMode"),
              variantAttributeIds: selected,
              availableForRetail: f.get("retail") === "on",
              availableForReseller: f.get("reseller") === "on",
              reason: f.get("reason"),
            }),
          );
          if (p) router.push(`/catalog/products/${p.id}`);
        }}
      >
        {error && <div className="md:col-span-2"><Alert>{error}</Alert></div>}
        <Field label="Vendor" hint="Who supplies and ships it. The product ID (e.g. ZR-000123) is created from the vendor code and cannot change.">
          <Select name="vendorId" required={vendorRequired} defaultValue={vendors[0]?.id ?? ""}>
            {!vendorRequired && <option value="">None — own stock</option>}
            {vendors.map((v) => <option key={v.id} value={v.id}>{v.name} ({v.code})</option>)}
          </Select>
        </Field>
        <Field label="Name"><Input name="name" required /></Field>
        <Field label="Category"><Select name="categoryId">{categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</Select></Field>
        <Field label="Inventory tracking" hint="Per piece for unique/high-value items; by quantity for high-volume items. Fixed once activated.">
          <Select name="trackingMode" defaultValue="Quantity"><option value="Quantity">By quantity</option><option value="Serialized">Per piece (serialized)</option></Select>
        </Field>
        <Field label="Variant attributes" hint="Each variant picks one option per attribute. Leave empty for a single standard variant.">
          <div className="flex flex-wrap gap-2 rounded-md border border-slate-200 p-2">
            {attributes.map((a) => (
              <label key={a.id} className="flex items-center gap-1.5 text-sm">
                <input type="checkbox" className="accent-brand-600" checked={selected.includes(a.id)}
                  onChange={(e) => setSelected(e.target.checked ? [...selected, a.id] : selected.filter((x) => x !== a.id))} />
                {a.name}
              </label>
            ))}
            {attributes.length === 0 && <span className="text-xs text-slate-500">No attributes yet.</span>}
          </div>
        </Field>
        <div className="md:col-span-2"><Field label="Description"><Textarea name="description" rows={3} /></Field></div>
        <div className="flex gap-6 text-sm md:col-span-2">
          <label className="flex items-center gap-2"><input type="checkbox" name="retail" defaultChecked className="accent-brand-600" /> Available for retail customers</label>
          <label className="flex items-center gap-2"><input type="checkbox" name="reseller" defaultChecked className="accent-brand-600" /> Available for resellers</label>
        </div>
        <Field label="Reason"><Input name="reason" required defaultValue="New product" /></Field>
        <div className="flex items-end"><Button type="submit" disabled={busy}>Create draft product</Button></div>
      </form>
    </Card>
  );
}
