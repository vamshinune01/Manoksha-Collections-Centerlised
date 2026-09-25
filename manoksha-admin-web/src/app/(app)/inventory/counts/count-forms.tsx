"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { SkuPicker } from "@/components/sku-picker";
import { Alert, Button, Card, Field, Input, Select } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Branch, StockCount } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function StartCountForm({ branches }: { branches: Branch[] }) {
  const router = useRouter();
  const { error, busy, run } = useAction();
  if (branches.length === 0) return null;
  return (
    <Card className="mb-6">
      <form className="flex flex-wrap items-end gap-3" onSubmit={async (e) => {
        e.preventDefault();
        const f = new FormData(e.currentTarget);
        const c = await run(() => callApi<{ id: string }>("admin/inventory/counts", "POST", { branchId: f.get("branchId"), notes: f.get("notes") || null }));
        if (c) router.push(`/inventory/counts/${c.id}`);
      }}>
        {error && <Alert>{error}</Alert>}
        <Field label="Branch"><Select name="branchId">{branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}</Select></Field>
        <Field label="Notes"><Input name="notes" placeholder="e.g. Monthly saree section count" /></Field>
        <Button type="submit" disabled={busy}>Start count (all SKUs in stock)</Button>
      </form>
    </Card>
  );
}

export function CountEditor({ count }: { count: StockCount }) {
  const { error, busy, run } = useAction();
  const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(count.lines.map((l) => [l.skuId, l.countedQty?.toString() ?? ""])));
  const [extra, setExtra] = useState<{ skuId: string; label: string }[]>([]);
  const lines = [...count.lines.map((l) => ({ skuId: l.skuId, label: `${l.productName} · ${l.variantName} (${l.skuCode})` })), ...extra];

  const save = () => run(() => callApi(`admin/inventory/counts/${count.id}/lines`, "PUT", {
    lines: lines.filter((l) => values[l.skuId] !== "" && values[l.skuId] !== undefined).map((l) => ({ skuId: l.skuId, countedQty: Number(values[l.skuId]) })),
  }));

  return (
    <div className="space-y-3">
      {error && <Alert>{error}</Alert>}
      {lines.map((l) => (
        <div key={l.skuId} className="flex items-center gap-3 text-sm">
          <span className="flex-1">{l.label}</span>
          <Input type="number" min={0} className="w-28" value={values[l.skuId] ?? ""} aria-label={`Counted ${l.label}`}
            onChange={(e) => setValues({ ...values, [l.skuId]: e.target.value })} />
        </div>
      ))}
      <div className="max-w-md"><SkuPicker placeholder="Add an item found on the shelf" onPick={(s) => !lines.some((l) => l.skuId === s.skuId) && setExtra([...extra, { skuId: s.skuId, label: `${s.productName} · ${s.variantName} (${s.skuCode})` }])} /></div>
      <div className="flex gap-2">
        <Button variant="secondary" disabled={busy} onClick={save}>Save progress</Button>
        <Button disabled={busy} onClick={async () => {
          if (!window.confirm("Submit the count? It can no longer be edited and differences will be raised as discrepancies.")) return;
          const saved = await save();
          if (saved !== undefined) await run(() => callApi(`admin/inventory/counts/${count.id}/submit`, "POST"));
        }}>Submit count</Button>
      </div>
    </div>
  );
}
