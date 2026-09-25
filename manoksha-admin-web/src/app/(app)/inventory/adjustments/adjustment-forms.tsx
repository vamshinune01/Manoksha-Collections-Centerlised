"use client";

import { useState } from "react";
import { SkuPicker } from "@/components/sku-picker";
import { Alert, Button, Card, Field, Input, Select } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Adjustment, Branch, SkuInfo } from "@/lib/types";
import { useAction } from "@/lib/use-action";

const STATUSES = ["Available", "Damaged", "Repair", "Blocked", "Lost"];

export function RequestAdjustmentForm({ branches }: { branches: Branch[] }) {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  const [sku, setSku] = useState<SkuInfo | null>(null);
  const [kind, setKind] = useState("StatusChange");
  if (!open) return <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)}>Request adjustment</Button></div>;
  const serialized = sku?.trackingMode === "Serialized" && kind !== "Found";
  return (
    <Card title="Request inventory adjustment" className="mb-6" actions={<Button variant="ghost" onClick={() => setOpen(false)}>Close</Button>}>
      <form className="grid gap-3 md:grid-cols-4" onSubmit={async (e) => {
        e.preventDefault();
        const f = new FormData(e.currentTarget);
        let itemIds: string[] | null = null;
        if (serialized) {
          itemIds = [];
          for (const code of String(f.get("barcodes") ?? "").split(/\s+/).filter(Boolean)) {
            const r = await callApi<{ inventoryItemId: string | null }>(`admin/catalog/barcodes/lookup/${encodeURIComponent(code)}`).catch(() => null);
            if (r?.inventoryItemId) itemIds.push(r.inventoryItemId);
          }
        }
        const ok = await run(() => callApi("admin/inventory/adjustments", "POST", {
          branchId: f.get("branchId"), skuId: sku?.skuId, kind, fromStatus: kind === "Found" ? null : f.get("from"), toStatus: kind === "StatusChange" ? f.get("to") : null,
          quantity: serialized ? null : Number(f.get("qty")), itemIds, reasonCode: f.get("reasonCode"), notes: f.get("notes"),
        }));
        if (ok) { setOpen(false); setSku(null); }
      }}>
        {error && <div className="md:col-span-4"><Alert>{error}</Alert></div>}
        <Field label="Branch"><Select name="branchId">{branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}</Select></Field>
        <div className="md:col-span-3"><Field label="SKU">{sku ? <p className="py-2 text-sm">{sku.productName} · {sku.variantName} <button type="button" className="text-brand-700" onClick={() => setSku(null)}>change</button></p> : <SkuPicker onPick={setSku} />}</Field></div>
        <Field label="Type">
          <Select value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="StatusChange">Change status</option><option value="WriteOff">Write off (damaged/lost)</option><option value="Found">Found stock</option>
          </Select>
        </Field>
        {kind !== "Found" && <Field label="From"><Select name="from">{(kind === "WriteOff" ? ["Damaged", "Lost"] : STATUSES).map((s) => <option key={s}>{s}</option>)}</Select></Field>}
        {kind === "StatusChange" && <Field label="To"><Select name="to" defaultValue="Damaged">{STATUSES.map((s) => <option key={s}>{s}</option>)}</Select></Field>}
        {serialized
          ? <div className="md:col-span-2"><Field label="Piece barcodes"><textarea name="barcodes" rows={3} className="w-full rounded-md border border-slate-300 p-2 font-mono text-xs" placeholder="One per line" /></Field></div>
          : <Field label="Quantity"><Input name="qty" type="number" min={1} defaultValue={1} /></Field>}
        <Field label="Reason code"><Input name="reasonCode" required placeholder="DAMAGED, COUNT_SHORTAGE…" /></Field>
        <div className="md:col-span-2"><Field label="Notes"><Input name="notes" required /></Field></div>
        <div className="flex items-end"><Button type="submit" disabled={busy || !sku}>Submit for approval</Button></div>
      </form>
    </Card>
  );
}

export function AdjustmentDecision({ adjustment }: { adjustment: Adjustment }) {
  const { error, busy, run } = useAction();
  const [cost, setCost] = useState("");
  return (
    <div className="flex flex-col items-end gap-1">
      {adjustment.kind === "Found" && <Input className="w-28" type="number" min={0} step="0.01" placeholder="Unit cost ₹" value={cost} onChange={(e) => setCost(e.target.value)} aria-label="Unit cost" />}
      <div className="flex gap-1">
        <Button disabled={busy || (adjustment.kind === "Found" && cost === "")} onClick={() => run(() => callApi(`admin/inventory/adjustments/${adjustment.id}/approve`, "POST",
          { note: window.prompt("Approval note (optional)") ?? null, unitCost: adjustment.kind === "Found" ? Number(cost) : null }))}>Approve</Button>
        <Button variant="ghost" disabled={busy} onClick={() => { const reason = window.prompt("Reason for rejecting?"); if (reason) void run(() => callApi(`admin/inventory/adjustments/${adjustment.id}/reject`, "POST", { reason })); }}>Reject</Button>
      </div>
      {error && <p className="max-w-xs text-xs text-red-600">{error}</p>}
    </div>
  );
}
