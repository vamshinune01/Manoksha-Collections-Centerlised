"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { SkuPicker } from "@/components/sku-picker";
import { Alert, Button, Card, Field, Input, Select } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Branch, SkuInfo } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function CreateTransferForm({ branches }: { branches: Branch[] }) {
  const router = useRouter();
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  const [lines, setLines] = useState<{ sku: SkuInfo; qty: number }[]>([]);
  if (!open) return <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)}>Request transfer</Button></div>;
  return (
    <Card title="New transfer request" className="mb-6" actions={<Button variant="ghost" onClick={() => setOpen(false)}>Close</Button>}>
      <form className="space-y-4" onSubmit={async (e) => {
        e.preventDefault();
        const f = new FormData(e.currentTarget);
        const t = await run(() => callApi<{ id: string }>("admin/inventory/transfers", "POST", {
          sourceBranchId: f.get("source"), destinationBranchId: f.get("destination"), reason: f.get("reason"),
          lines: lines.map((l) => ({ skuId: l.sku.skuId, quantity: l.qty })),
        }));
        if (t) router.push(`/inventory/transfers/${t.id}`);
      }}>
        {error && <Alert>{error}</Alert>}
        <div className="grid gap-4 md:grid-cols-3">
          <Field label="From (source)"><Select name="source">{branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}</Select></Field>
          <Field label="To (destination)"><Select name="destination" defaultValue={branches[1]?.id}>{branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}</Select></Field>
          <Field label="Reason"><Input name="reason" required /></Field>
        </div>
        <SkuPicker onPick={(sku) => !lines.some((l) => l.sku.skuId === sku.skuId) && setLines([...lines, { sku, qty: 1 }])} />
        <ul className="space-y-2">
          {lines.map((l, i) => (
            <li key={l.sku.skuId} className="flex items-center gap-3 text-sm">
              <span className="flex-1">{l.sku.productName} · {l.sku.variantName} <span className="font-mono text-xs text-slate-400">{l.sku.skuCode}</span></span>
              <Input type="number" min={1} className="w-24" value={l.qty} aria-label="Quantity" onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, qty: Number(e.target.value) } : x))} />
              <Button type="button" variant="ghost" onClick={() => setLines(lines.filter((_, j) => j !== i))}>Remove</Button>
            </li>
          ))}
        </ul>
        <Button type="submit" disabled={busy || lines.length === 0}>Submit request</Button>
      </form>
    </Card>
  );
}
