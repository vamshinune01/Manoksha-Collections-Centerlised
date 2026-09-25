"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Badge, Button, Field, Input, Select, Textarea } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Category, Label, ProductDetail } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function ProductStatusButtons({ product }: { product: ProductDetail }) {
  const { error, busy, run } = useAction();
  const targets = product.status === "Active" ? ["Inactive"] : ["Active"];
  return (
    <span className="ml-auto flex items-center gap-2">
      {error && <span className="text-sm text-red-600">{error}</span>}
      {targets.map((t) => (
        <Button key={t} variant={t === "Active" ? "primary" : "secondary"} disabled={busy} onClick={() => {
          const reason = window.prompt(`Reason for making this product ${t}?`);
          if (reason) void run(() => callApi(`admin/catalog/products/${product.id}/status`, "POST", { status: t, reason }));
        }}>
          {t === "Active" ? "Activate" : "Deactivate"}
        </Button>
      ))}
    </span>
  );
}

export function ProductEditor({ product, categories }: { product: ProductDetail; categories: Category[] }) {
  const { error, busy, run } = useAction();
  const [saved, setSaved] = useState(false);
  return (
    <form className="space-y-3" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      const ok = await run(() => callApi(`admin/catalog/products/${product.id}`, "PUT", {
        categoryId: f.get("categoryId"), name: f.get("name"), description: f.get("description") || null, trackingMode: f.get("trackingMode"),
        availableForRetail: f.get("retail") === "on", availableForReseller: f.get("reseller") === "on", reason: f.get("reason"),
      }));
      setSaved(Boolean(ok));
    }}>
      {error && <Alert>{error}</Alert>}
      {saved && <Alert tone="success">Saved.</Alert>}
      <Field label="Name"><Input name="name" defaultValue={product.name} required /></Field>
      <Field label="Category">
        <Select name="categoryId" defaultValue={product.categoryId}>{categories.map((c) => <option key={c.id} value={c.id}>{c.name}{c.isActive ? "" : " (inactive)"}</option>)}</Select>
      </Field>
      <Field label="Tracking" hint={product.status === "Draft" ? undefined : "Locked after activation."}>
        <Select name="trackingMode" defaultValue={product.trackingMode} disabled={product.status !== "Draft"}>
          <option value="Serialized">Per piece</option><option value="Quantity">By quantity</option>
        </Select>
        {product.status !== "Draft" && <input type="hidden" name="trackingMode" value={product.trackingMode} />}
      </Field>
      <Field label="Description"><Textarea name="description" rows={4} defaultValue={product.description ?? ""} /></Field>
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" name="retail" defaultChecked={product.availableForRetail} className="accent-brand-600" /> Available for retail</label>
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" name="reseller" defaultChecked={product.availableForReseller} className="accent-brand-600" /> Available for resellers</label>
      <Field label="Reason"><Input name="reason" required /></Field>
      <Button type="submit" disabled={busy}>Save details</Button>
    </form>
  );
}

export function AddVariantForm({ product }: { product: ProductDetail }) {
  const { error, busy, run } = useAction();
  const attrs = product.variantAttributes;
  return (
    <form className="mt-5 grid gap-3 border-t border-slate-100 pt-4 md:grid-cols-4" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      const ok = await run(() => callApi(`admin/catalog/products/${product.id}/variants`, "POST", {
        optionIds: attrs.map((a) => f.get(`attr-${a.id}`)),
        skuCode: f.get("skuCode") || null,
        generateBarcode: f.get("barcode") === "on",
        reason: f.get("reason"),
      }));
      if (ok) (e.target as HTMLFormElement).reset();
    }}>
      <p className="text-sm font-medium text-slate-700 md:col-span-4">Add variant</p>
      {error && <div className="md:col-span-4"><Alert>{error}</Alert></div>}
      {attrs.length === 0 && <p className="text-sm text-slate-500 md:col-span-4">No variant attributes: this product has one standard variant.</p>}
      {attrs.map((a) => (
        <Field key={a.id} label={a.name}>
          <Select name={`attr-${a.id}`} required>{a.options.filter((o) => o.isActive).map((o) => <option key={o.id} value={o.id}>{o.value}</option>)}</Select>
        </Field>
      ))}
      <Field label="SKU code" hint="Leave blank to auto-generate (MC-000001)."><Input name="skuCode" /></Field>
      <Field label="Reason"><Input name="reason" required defaultValue="New variant" /></Field>
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" name="barcode" defaultChecked className="accent-brand-600" /> Generate barcode</label>
      <div className="flex items-end"><Button type="submit" disabled={busy}>Add variant</Button></div>
    </form>
  );
}

export function VariantsTable({ product, canManage, canPrint }: { product: ProductDetail; canManage: boolean; canPrint: boolean }) {
  const router = useRouter();
  const { error, busy, run } = useAction();
  const [selected, setSelected] = useState<string[]>([]);
  const [copies, setCopies] = useState(1);

  async function print() {
    const labels = await run(() => callApi<Label[]>("admin/catalog/barcodes/print", "POST", { barcodeIds: selected, copies }));
    if (labels) {
      try { sessionStorage.setItem("manoksha.labels", JSON.stringify(labels)); } catch { /* storage unavailable */ }
      router.push("/print/labels");
    }
  }

  return (
    <div className="space-y-3">
      {error && <Alert>{error}</Alert>}
      {product.variants.length === 0 && <p className="text-sm text-slate-500">No variants yet.</p>}
      <ul className="divide-y divide-slate-100">
        {product.variants.map((v) => (
          <li key={v.id} className="py-3">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-medium text-slate-800">{v.name}</span>
              <span className="font-mono text-xs text-slate-500">{v.skuCode}</span>
              {v.status !== "Active" && <Badge tone="red">Inactive</Badge>}
              {canManage && (
                <span className="ml-auto flex gap-1">
                  <Button variant="ghost" disabled={busy} onClick={() => run(() => callApi(`admin/catalog/skus/${v.skuId}/barcodes`, "POST", { reason: "Additional label barcode" }))}>+ Barcode</Button>
                  <Button variant="ghost" disabled={busy} onClick={() => {
                    const code = window.prompt("Supplier/manufacturer barcode (scan or type):");
                    const reason = code && window.prompt("Reason?", "Supplier barcode");
                    if (code && reason) void run(() => callApi(`admin/catalog/skus/${v.skuId}/barcodes/external`, "POST", { code, reason }));
                  }}>+ Supplier code</Button>
                  <Button variant="ghost" disabled={busy} onClick={() => {
                    const next = v.status === "Active" ? "Inactive" : "Active";
                    const reason = window.prompt(`Reason for making ${v.name} ${next}?`);
                    if (reason) void run(() => callApi(`admin/catalog/variants/${v.id}/status`, "POST", { status: next, reason }));
                  }}>{v.status === "Active" ? "Deactivate" : "Activate"}</Button>
                </span>
              )}
            </div>
            <ul className="mt-2 space-y-1">
              {v.barcodes.map((b) => (
                <li key={b.id} className="flex flex-wrap items-center gap-2 text-sm">
                  {canPrint && b.status === "Active" && (
                    <input type="checkbox" aria-label={`Select ${b.code}`} className="accent-brand-600" checked={selected.includes(b.id)}
                      onChange={(e) => setSelected(e.target.checked ? [...selected, b.id] : selected.filter((x) => x !== b.id))} />
                  )}
                  <span className="font-mono">{b.code}</span>
                  <Badge tone={b.kind === "External" ? "slate" : "brand"}>{b.kind === "External" ? "Supplier" : "Internal"}</Badge>
                  {b.status !== "Active" && <Badge tone="red">Retired</Badge>}
                  <span className="text-xs text-slate-500">printed {b.printCount}×</span>
                  {canManage && b.status === "Active" && (
                    <Button variant="ghost" disabled={busy} onClick={() => {
                      const reason = window.prompt(`Reason for retiring ${b.code}? It can never be reused.`);
                      if (reason) void run(() => callApi(`admin/catalog/barcodes/${b.id}/retire`, "POST", { reason }));
                    }}>Retire</Button>
                  )}
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ul>
      {canPrint && selected.length > 0 && (
        <div className="flex items-center gap-3 border-t border-slate-100 pt-3">
          <span className="text-sm text-slate-600">{selected.length} selected</span>
          <Input type="number" min={1} max={100} value={copies} onChange={(e) => setCopies(Number(e.target.value))} className="w-20" aria-label="Copies" />
          <Button onClick={print} disabled={busy}>Print labels</Button>
        </div>
      )}
    </div>
  );
}
