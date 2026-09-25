"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { SkuPicker } from "@/components/sku-picker";
import { Alert, Button, Field, Input, Select } from "@/components/ui";
import { ApiError, callApi } from "@/lib/client-api";
import type { GoodsReceipt, Label, PurchaseOrder } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function PoActions({ po }: { po: PurchaseOrder }) {
  const { error, busy, run } = useAction();
  const act = (path: string, label: string) => {
    const reason = window.prompt(`Reason to ${label}?`);
    if (reason) void run(() => callApi(`admin/purchasing/purchase-orders/${po.id}/${path}`, "POST", { reason }));
  };
  return (
    <span className="ml-auto flex items-center gap-2">
      {error && <span className="text-sm text-red-600">{error}</span>}
      {po.status === "Draft" && <Button disabled={busy} onClick={() => act("issue", "issue this PO to the supplier")}>Issue</Button>}
      {(po.status === "Issued" || po.status === "PartiallyReceived") && <Button variant="secondary" disabled={busy} onClick={() => act("close", "close the remaining quantity")}>Close</Button>}
      {(po.status === "Draft" || po.status === "Issued") && <Button variant="danger" disabled={busy} onClick={() => act("cancel", "cancel this PO")}>Cancel</Button>}
    </span>
  );
}

export function AmendLineForm({ po }: { po: PurchaseOrder }) {
  const { error, busy, run } = useAction();
  const [lineId, setLineId] = useState<string>(po.lines[0]?.id ?? "");
  const [newSku, setNewSku] = useState<{ skuId: string; label: string } | null>(null);
  const current = po.lines.find((l) => l.id === lineId);
  return (
    <form className="mt-4 grid gap-3 border-t border-slate-100 pt-4 md:grid-cols-5" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      const ok = await run(() => callApi(`admin/purchasing/purchase-orders/${po.id}/lines`, "POST", {
        lineId: lineId === "new" ? null : lineId, skuId: lineId === "new" ? newSku?.skuId : null,
        orderedQty: Number(f.get("qty")), expectedUnitCost: Number(f.get("cost")), reason: f.get("reason"),
      }));
      if (ok) setNewSku(null);
    }}>
      <p className="text-sm font-medium text-slate-700 md:col-span-5">Amend PO (change a quantity or add a SKU)</p>
      {error && <div className="md:col-span-5"><Alert>{error}</Alert></div>}
      <Field label="Line">
        <Select value={lineId} onChange={(e) => setLineId(e.target.value)}>
          {po.lines.map((l) => <option key={l.id} value={l.id}>{l.skuCode} — {l.productName}</option>)}
          <option value="new">+ Add a new SKU</option>
        </Select>
      </Field>
      {lineId === "new" ? (
        <Field label="SKU">{newSku ? <p className="py-2 text-sm">{newSku.label} <button type="button" className="text-brand-700" onClick={() => setNewSku(null)}>change</button></p>
          : <SkuPicker onPick={(s) => setNewSku({ skuId: s.skuId, label: `${s.productName} · ${s.variantName}` })} />}</Field>
      ) : <div />}
      <Field label="Ordered qty"><Input name="qty" type="number" min={1} key={`q-${lineId}`} defaultValue={current?.orderedQty ?? 1} required /></Field>
      <Field label="Expected unit cost"><Input name="cost" type="number" min={0} step="0.01" key={`c-${lineId}`} defaultValue={current?.expectedUnitCost ?? 0} required /></Field>
      <Field label="Reason"><Input name="reason" required /></Field>
      <div><Button type="submit" variant="secondary" disabled={busy || (lineId === "new" && !newSku)}>Save amendment</Button></div>
    </form>
  );
}

export function ReceiveForm({ po, canPrint }: { po: PurchaseOrder; canPrint: boolean }) {
  const router = useRouter();
  const { error, busy, run } = useAction();
  const open = po.lines.filter((l) => l.remainingQty > 0);
  const [values, setValues] = useState(() => Object.fromEntries(open.map((l) => [l.id, { received: l.remainingQty, damaged: 0, cost: l.expectedUnitCost ?? 0 }])));
  const [result, setResult] = useState<GoodsReceipt>();
  // One key per form instance: a double click or retry cannot create stock twice.
  const idempotencyKey = useMemo(() => crypto.randomUUID(), []);

  async function printLabels() {
    const labels = await run(() => callApi<Label[]>("admin/catalog/barcodes/print", "POST", { barcodeIds: result!.items.map((i) => i.barcodeId), copies: 1 }));
    if (labels) {
      try { sessionStorage.setItem("manoksha.labels", JSON.stringify(labels)); } catch { /* storage unavailable */ }
      router.push("/print/labels");
    }
  }

  if (result) {
    return (
      <div className="space-y-3">
        <Alert tone="success">Recorded {result.number}. Stock is now available at {result.branchName}.</Alert>
        {result.items.length > 0 && (
          <p className="text-sm text-slate-700">{result.items.length} piece(s) received with individual barcodes.
            {canPrint && <Button className="ml-3" onClick={printLabels} disabled={busy}>Print piece labels</Button>}</p>
        )}
      </div>
    );
  }

  return (
    <form className="space-y-4" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      const lines = open.map((l) => ({ poLineId: l.id, receivedQty: values[l.id]!.received, damagedQty: values[l.id]!.damaged, unitCost: values[l.id]!.cost }))
        .filter((l) => l.receivedQty > 0);
      const r = await run(async () => {
        const response = await fetch(`/api/backend/admin/purchasing/purchase-orders/${po.id}/receipts`, {
          method: "POST",
          headers: { "Content-Type": "application/json", "x-manoksha-client": "admin-web", "Idempotency-Key": idempotencyKey },
          body: JSON.stringify({ supplierInvoiceRef: f.get("invoice"), notes: f.get("notes") || null, lines }),
        });
        const body = await response.json();
        if (!response.ok) throw new ApiError(response.status, body.code, body.title);
        return body as GoodsReceipt;
      });
      if (r) setResult(r);
    }}>
      {error && <Alert>{error}</Alert>}
      <p className="text-sm text-slate-600">Count what actually arrived. Damaged pieces are recorded as damaged stock. You cannot receive more than remains on the PO.</p>
      <table className="w-full text-sm">
        <thead><tr className="text-left text-xs uppercase text-slate-500"><th className="py-1">SKU</th><th>Remaining</th><th>Received</th><th>of which damaged</th><th>Actual unit cost (₹)</th></tr></thead>
        <tbody>
          {open.map((l) => (
            <tr key={l.id}>
              <td className="py-1">{l.productName} · {l.variantName} <span className="font-mono text-xs text-slate-400">{l.skuCode}</span></td>
              <td>{l.remainingQty}</td>
              {(["received", "damaged", "cost"] as const).map((k) => (
                <td key={k}>
                  <Input type="number" min={0} step={k === "cost" ? "0.01" : "1"} max={k === "received" ? l.remainingQty : undefined} className="w-28"
                    value={values[l.id]![k]} aria-label={`${k} for ${l.skuCode}`}
                    onChange={(e) => setValues({ ...values, [l.id]: { ...values[l.id]!, [k]: Number(e.target.value) } })} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <div className="grid gap-3 md:grid-cols-3">
        <Field label="Supplier invoice / delivery ref"><Input name="invoice" required /></Field>
        <Field label="Notes"><Input name="notes" /></Field>
        <div className="flex items-end"><Button type="submit" disabled={busy}>Record receipt</Button></div>
      </div>
    </form>
  );
}
