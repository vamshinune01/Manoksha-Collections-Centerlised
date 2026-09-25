"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { SkuPicker } from "@/components/sku-picker";
import { Alert, Button, Card, Field, Input, Select } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Branch, SkuInfo, Supplier } from "@/lib/types";
import { useAction } from "@/lib/use-action";

type Line = { sku: SkuInfo; qty: number; cost: number };

export function CreatePoForm({ suppliers, branches }: { suppliers: Supplier[]; branches: Branch[] }) {
  const router = useRouter();
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  const [lines, setLines] = useState<Line[]>([]);

  if (!open) return <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)} disabled={suppliers.length === 0}>New purchase order</Button></div>;
  return (
    <Card title="New purchase order (draft)" className="mb-6" actions={<Button variant="ghost" onClick={() => setOpen(false)}>Close</Button>}>
      <form className="space-y-4" onSubmit={async (e) => {
        e.preventDefault();
        const f = new FormData(e.currentTarget);
        const po = await run(() => callApi<{ id: string }>("admin/purchasing/purchase-orders", "POST", {
          supplierId: f.get("supplierId"), receivingBranchId: f.get("branchId"), supplierReference: f.get("ref") || null,
          expectedDate: f.get("expectedDate") || null, notes: f.get("notes") || null,
          lines: lines.map((l) => ({ skuId: l.sku.skuId, orderedQty: l.qty, expectedUnitCost: l.cost })), reason: f.get("reason"),
        }));
        if (po) router.push(`/purchasing/po/${po.id}`);
      }}>
        {error && <Alert>{error}</Alert>}
        <div className="grid gap-4 md:grid-cols-4">
          <Field label="Supplier"><Select name="supplierId">{suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</Select></Field>
          <Field label="Receiving branch"><Select name="branchId">{branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}</Select></Field>
          <Field label="Supplier reference"><Input name="ref" /></Field>
          <Field label="Expected date"><Input name="expectedDate" type="date" /></Field>
        </div>
        <SkuPicker onPick={(sku) => !lines.some((l) => l.sku.skuId === sku.skuId) && setLines([...lines, { sku, qty: 1, cost: 0 }])} />
        {lines.length > 0 && (
          <table className="w-full text-sm">
            <thead><tr className="text-left text-xs uppercase text-slate-500"><th className="py-1">SKU</th><th>Qty</th><th>Expected unit cost (₹)</th><th /></tr></thead>
            <tbody>
              {lines.map((l, i) => (
                <tr key={l.sku.skuId}>
                  <td className="py-1">{l.sku.productName} · {l.sku.variantName} <span className="font-mono text-xs text-slate-400">{l.sku.skuCode}</span></td>
                  <td><Input type="number" min={1} className="w-24" value={l.qty} onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, qty: Number(e.target.value) } : x))} aria-label="Quantity" /></td>
                  <td><Input type="number" min={0} step="0.01" className="w-32" value={l.cost} onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, cost: Number(e.target.value) } : x))} aria-label="Unit cost" /></td>
                  <td><Button type="button" variant="ghost" onClick={() => setLines(lines.filter((_, j) => j !== i))}>Remove</Button></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <div className="grid gap-4 md:grid-cols-3">
          <Field label="Notes"><Input name="notes" /></Field>
          <Field label="Reason"><Input name="reason" required defaultValue="Restock" /></Field>
          <div className="flex items-end"><Button type="submit" disabled={busy || lines.length === 0}>Create draft PO</Button></div>
        </div>
      </form>
    </Card>
  );
}
