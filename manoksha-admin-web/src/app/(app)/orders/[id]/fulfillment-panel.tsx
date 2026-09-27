"use client";

import { useState } from "react";
import { Alert, Button, Field, Input, Select, Textarea } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { OrderLine, RerouteOption } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export interface PanelRights {
  fulfill: boolean;
  raise: boolean;
  reroute: boolean;
  cancel: boolean;
}

type IssueLine = { skuId: string; missingQty: number; damagedQty: number; missingBarcodes: string; damagedBarcodes: string };

const toRequest = (lines: IssueLine[]) =>
  lines
    .filter((l) => l.missingQty > 0 || l.damagedQty > 0 || l.missingBarcodes.trim() || l.damagedBarcodes.trim())
    .map((l) => ({
      skuId: l.skuId,
      missingQty: l.missingQty,
      damagedQty: l.damagedQty,
      missingBarcodes: l.missingBarcodes.split(/[\s,]+/).filter(Boolean),
      damagedBarcodes: l.damagedBarcodes.split(/[\s,]+/).filter(Boolean),
    }));

/** Per-line missing/damaged units. Quantity items: counts. Individually tracked pieces: scan their barcodes. */
function IssueEditor({ lines, value, onChange }: { lines: OrderLine[]; value: IssueLine[]; onChange: (v: IssueLine[]) => void }) {
  const set = (i: number, patch: Partial<IssueLine>) => onChange(value.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  return (
    <div className="space-y-3">
      {lines.map((line, i) => {
        const v = value[i] ?? { skuId: line.skuId, missingQty: 0, damagedQty: 0, missingBarcodes: "", damagedBarcodes: "" };
        return (
        <div key={line.id} className="rounded-md border border-slate-200 p-3">
          <p className="text-sm font-medium">{line.productName} · {line.variantName} <span className="text-xs text-slate-500">× {line.quantity}</span></p>
          <div className="mt-2 grid gap-2 sm:grid-cols-2">
            <Field label="Missing (qty)"><Input type="number" min={0} max={line.quantity} value={v.missingQty} onChange={(e) => set(i, { missingQty: Number(e.target.value) })} /></Field>
            <Field label="Damaged (qty)"><Input type="number" min={0} max={line.quantity} value={v.damagedQty} onChange={(e) => set(i, { damagedQty: Number(e.target.value) })} /></Field>
            <Field label="Missing pieces (scan barcodes)" hint="Only for individually tracked items."><Input value={v.missingBarcodes} onChange={(e) => set(i, { missingBarcodes: e.target.value })} /></Field>
            <Field label="Damaged pieces (scan barcodes)"><Input value={v.damagedBarcodes} onChange={(e) => set(i, { damagedBarcodes: e.target.value })} /></Field>
          </div>
        </div>
        );
      })}
    </div>
  );
}

/**
 * Fulfillment actions for one order. Buttons follow the order status and the user's permissions for the order's branch;
 * the backend enforces both (SPEC §19, §22; ADR-001 §8).
 */
export function FulfillmentPanel({ orderId, status, lines, rights }: { orderId: string; status: string; lines: OrderLine[]; rights: PanelRights }) {
  const { error, busy, run } = useAction();
  const [mode, setMode] = useState<"none" | "ship" | "deliver" | "problem" | "cancel" | "reroute" | "resolve">("none");
  const blank = () => lines.map((l) => ({ skuId: l.skuId, missingQty: 0, damagedQty: 0, missingBarcodes: "", damagedBarcodes: "" }));
  const [issues, setIssues] = useState<IssueLine[]>(blank);
  const [text, setText] = useState("");
  const [reason, setReason] = useState("ITEM_NOT_FOUND");
  const [courier, setCourier] = useState("XPRESSBEES");
  const [courierName, setCourierName] = useState("");
  const [tracking, setTracking] = useState("");
  const [deliveredOn, setDeliveredOn] = useState("");
  const [options, setOptions] = useState<RerouteOption[] | null>(null);
  const [target, setTarget] = useState("");

  const act = async (path: string, body: unknown) => {
    const ok = await run(() => callApi(`admin/orders/${orderId}/${path}`, "POST", body));
    if (ok) {
      setMode("none");
      setText("");
      setIssues(blank());
    }
  };

  const open = (m: typeof mode) => {
    setMode(m);
    setText("");
    if (m === "reroute") {
      setOptions(null);
      callApi<RerouteOption[]>(`admin/orders/${orderId}/reroute-options`).then(setOptions).catch(() => setOptions([]));
    }
  };

  const canProblem = rights.raise && ["Confirmed", "Processing", "Packed"].includes(status);
  const canCancel = rights.cancel && ["Confirmed", "Processing", "Packed", "FulfillmentException"].includes(status);

  return (
    <div className="space-y-3">
      {error && <Alert>{error}</Alert>}
      <div className="flex flex-wrap gap-2">
        {rights.fulfill && status === "Confirmed" && <Button disabled={busy} onClick={() => act("processing", {})}>Start processing</Button>}
        {rights.fulfill && status === "Processing" && <Button disabled={busy} onClick={() => act("packed", {})}>Mark packed</Button>}
        {rights.fulfill && status === "Packed" && <Button disabled={busy} onClick={() => open("ship")}>Mark shipped…</Button>}
        {rights.fulfill && status === "Shipped" && <Button disabled={busy} onClick={() => open("deliver")}>Mark delivered…</Button>}
        {rights.reroute && status === "FulfillmentException" && <Button disabled={busy} onClick={() => open("reroute")}>Reroute whole order…</Button>}
        {rights.reroute && status === "FulfillmentException" && <Button variant="secondary" disabled={busy} onClick={() => open("resolve")}>Resolved at this branch…</Button>}
        {canProblem && <Button variant="secondary" disabled={busy} onClick={() => open("problem")}>Report a problem…</Button>}
        {canCancel && <Button variant="danger" disabled={busy} onClick={() => open("cancel")}>Cancel order…</Button>}
      </div>
      {!rights.fulfill && !rights.raise && !rights.reroute && !rights.cancel && <p className="text-sm text-slate-500">You can view this order only.</p>}

      {mode === "ship" && (
        <form className="space-y-3 rounded-md border border-slate-200 p-3" onSubmit={(e) => { e.preventDefault(); void act("shipped", { courier, courierName: courierName || null, trackingNumber: tracking || null }); }}>
          <Field label="Courier"><Select value={courier} onChange={(e) => setCourier(e.target.value)}><option value="XPRESSBEES">Xpressbees</option><option value="DELHIVERY">Delhivery</option><option value="OTHER">Other</option></Select></Field>
          {courier === "OTHER" && <Field label="Courier name"><Input value={courierName} onChange={(e) => setCourierName(e.target.value)} required /></Field>}
          <Field label="Tracking number (optional)"><Input value={tracking} onChange={(e) => setTracking(e.target.value)} /></Field>
          <Button type="submit" disabled={busy}>Confirm shipped</Button>
        </form>
      )}

      {mode === "deliver" && (
        <form className="space-y-3 rounded-md border border-slate-200 p-3" onSubmit={(e) => { e.preventDefault(); void act("delivered", { deliveredOn: deliveredOn || null, note: text || null }); }}>
          <Field label="Delivered on" hint="Leave empty for today."><Input type="date" value={deliveredOn} onChange={(e) => setDeliveredOn(e.target.value)} /></Field>
          <Field label="Note (optional)"><Input value={text} onChange={(e) => setText(e.target.value)} placeholder="e.g. courier confirmation" /></Field>
          <Button type="submit" disabled={busy}>Confirm delivered</Button>
        </form>
      )}

      {mode === "problem" && (
        <form className="space-y-3 rounded-md border border-amber-200 bg-amber-50/50 p-3" onSubmit={(e) => { e.preventDefault(); void act("fulfillment-exceptions", { reason, notes: text, lines: toRequest(issues) }); }}>
          <Field label="What happened"><Select value={reason} onChange={(e) => setReason(e.target.value)}>
            <option value="ITEM_NOT_FOUND">Item not found</option><option value="DAMAGED">Damaged</option><option value="INVENTORY_MISMATCH">Inventory mismatch</option><option value="OTHER">Other</option>
          </Select></Field>
          <Field label="Details"><Textarea value={text} onChange={(e) => setText(e.target.value)} rows={2} required minLength={3} /></Field>
          <IssueEditor lines={lines} value={issues} onChange={setIssues} />
          <Button type="submit" disabled={busy}>Report problem</Button>
        </form>
      )}

      {mode === "resolve" && (
        <form className="space-y-3 rounded-md border border-slate-200 p-3" onSubmit={(e) => { e.preventDefault(); void act("fulfillment-exceptions/resolve", { note: text }); }}>
          <Field label="How was it resolved?"><Textarea value={text} onChange={(e) => setText(e.target.value)} rows={2} required minLength={3} placeholder="e.g. the piece was found" /></Field>
          <Button type="submit" disabled={busy}>Resume processing here</Button>
        </form>
      )}

      {mode === "reroute" && (
        <form className="space-y-3 rounded-md border border-slate-200 p-3" onSubmit={(e) => { e.preventDefault(); void act("reroute", { targetBranchId: target, reason: text }); }}>
          {options === null ? <p className="text-sm text-slate-500">Checking branches…</p> : (
            <ul className="space-y-1 text-sm">
              {options.map((o) => (
                <li key={o.branchId}>
                  <label className="flex items-center gap-2">
                    <input type="radio" name="target" value={o.branchId} disabled={!o.canFulfil} checked={target === o.branchId} onChange={() => setTarget(o.branchId)} />
                    <span className={o.canFulfil ? "" : "text-slate-400"}>P{o.priority} {o.branchName} — {o.canFulfil ? "can fulfil the complete order" : o.isActive ? "not enough stock" : "inactive"}</span>
                  </label>
                </li>
              ))}
            </ul>
          )}
          {options?.length !== undefined && options.every((o) => !o.canFulfil) && (
            <Alert tone="warning">No other branch can fulfil the complete order. Resolve it here, wait for stock, or cancel the order.</Alert>
          )}
          <Field label="Reason"><Textarea value={text} onChange={(e) => setText(e.target.value)} rows={2} required minLength={3} /></Field>
          <Button type="submit" disabled={busy || !target}>Reroute</Button>
        </form>
      )}

      {mode === "cancel" && (
        <form className="space-y-3 rounded-md border border-red-200 bg-red-50/40 p-3" onSubmit={(e) => { e.preventDefault(); void act("cancel", { reason: text, lines: toRequest(issues) }); }}>
          <p className="text-sm text-slate-700">
            Stock returns to the shelf automatically except units you mark damaged or missing (a reported problem&apos;s units are used if you leave these empty).
            A reseller&apos;s wallet is credited back; a paid online order goes to payment reconciliation.
          </p>
          <Field label="Reason"><Textarea value={text} onChange={(e) => setText(e.target.value)} rows={2} required minLength={3} /></Field>
          <IssueEditor lines={lines} value={issues} onChange={setIssues} />
          <Button type="submit" variant="danger" disabled={busy}>Cancel order</Button>
        </form>
      )}
    </div>
  );
}
