"use client";

import { useState } from "react";
import { Alert, Button, Input } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Transfer } from "@/lib/types";
import { useAction } from "@/lib/use-action";

type Perms = { approve: boolean; dispatch: boolean; receive: boolean; cancel: boolean };

/** Quantity inputs per line; per-piece lines take scanned barcodes (one per line of text). */
function LineInputs({ transfer, mode, value, onChange }: {
  transfer: Transfer; mode: "prepare" | "receive"; value: Record<string, string>; onChange: (v: Record<string, string>) => void;
}) {
  return (
    <div className="space-y-2">
      {transfer.lines.filter((l) => mode === "prepare" || l.dispatchedQty > 0).map((l) => (
        <div key={l.id} className="flex flex-wrap items-center gap-3 text-sm">
          <span className="w-72">{l.productName} · {l.variantName} ({mode === "prepare" ? `requested ${l.requestedQty}` : `dispatched ${l.dispatchedQty}`})</span>
          {l.serialized ? (
            <textarea className="w-80 rounded-md border border-slate-300 p-2 font-mono text-xs" rows={3} placeholder="Scan piece barcodes, one per line"
              value={value[l.id] ?? ""} onChange={(e) => onChange({ ...value, [l.id]: e.target.value })} aria-label={`Barcodes for ${l.skuCode}`} />
          ) : (
            <Input type="number" min={0} className="w-28" value={value[l.id] ?? String(mode === "prepare" ? l.requestedQty : l.dispatchedQty)}
              onChange={(e) => onChange({ ...value, [l.id]: e.target.value })} aria-label={`Quantity for ${l.skuCode}`} />
          )}
        </div>
      ))}
    </div>
  );
}

export function TransferActions({ transfer: t, can }: { transfer: Transfer; can: Perms }) {
  const { error, busy, run, setError } = useAction();
  const [values, setValues] = useState<Record<string, string>>({});

  async function linesPayload(mode: "prepare" | "receive") {
    const result = [];
    for (const l of t.lines.filter((x) => mode === "prepare" || x.dispatchedQty > 0)) {
      if (l.serialized) {
        const codes = (values[l.id] ?? "").split(/\s+/).map((s) => s.trim()).filter(Boolean);
        const byBarcode = mode === "receive" ? new Map(l.items.map((i) => [i.barcode, i.itemId])) : null;
        const ids: string[] = [];
        for (const code of codes) {
          if (byBarcode) {
            const id = byBarcode.get(code);
            if (!id) throw new Error(`Barcode ${code} is not part of this transfer.`);
            ids.push(id);
          } else {
            const scan = await callApi<{ inventoryItemId: string | null }>(`admin/catalog/barcodes/lookup/${encodeURIComponent(code)}`);
            if (!scan.inventoryItemId) throw new Error(`Barcode ${code} is not a piece barcode.`);
            ids.push(scan.inventoryItemId);
          }
        }
        result.push({ lineId: l.id, itemIds: ids });
      } else {
        result.push({ lineId: l.id, quantity: Number(values[l.id] ?? (mode === "prepare" ? l.requestedQty : l.dispatchedQty)) });
      }
    }
    return result;
  }

  async function step(path: string, body?: unknown) {
    try {
      await run(() => callApi(`admin/inventory/transfers/${t.id}/${path}`, "POST", body));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed");
    }
  }

  const reasonStep = (path: string, label: string) => {
    const reason = window.prompt(`Reason to ${label}?`);
    if (reason) void step(path, { reason });
  };

  return (
    <div className="mt-5 space-y-4 border-t border-slate-100 pt-4">
      {error && <Alert>{error}</Alert>}
      {t.status === "Requested" && (
        <div className="flex gap-2">
          {can.approve && <Button disabled={busy} onClick={() => step("approve", { note: window.prompt("Approval note (optional)") ?? null })}>Approve release</Button>}
          {can.approve && <Button variant="secondary" disabled={busy} onClick={() => reasonStep("reject", "reject")}>Reject</Button>}
          {!can.approve && <p className="text-sm text-slate-600">Waiting for approval by the source branch manager.</p>}
        </div>
      )}
      {t.status === "Approved" && can.dispatch && (
        <div className="space-y-3">
          <p className="text-sm font-medium">Prepare (pick) the stock</p>
          <LineInputs transfer={t} mode="prepare" value={values} onChange={setValues} />
          <Button disabled={busy} onClick={async () => { try { await step("prepare", { lines: await linesPayload("prepare") }); } catch (e) { setError((e as Error).message); } }}>Mark prepared</Button>
        </div>
      )}
      {t.status === "Prepared" && can.dispatch && <Button disabled={busy} onClick={() => step("dispatch")}>Dispatch (in transit)</Button>}
      {t.status === "InTransit" && can.receive && (
        <div className="space-y-3">
          <p className="text-sm font-medium">Verify what arrived</p>
          <LineInputs transfer={t} mode="receive" value={values} onChange={setValues} />
          <Button disabled={busy} onClick={async () => { try { await step("receive", { lines: await linesPayload("receive") }); } catch (e) { setError((e as Error).message); } }}>Confirm receipt</Button>
        </div>
      )}
      {(t.status === "Requested" || t.status === "Approved" || t.status === "Prepared") && can.cancel && (
        <Button variant="ghost" disabled={busy} onClick={() => reasonStep("cancel", "cancel this transfer")}>Cancel transfer</Button>
      )}
    </div>
  );
}
