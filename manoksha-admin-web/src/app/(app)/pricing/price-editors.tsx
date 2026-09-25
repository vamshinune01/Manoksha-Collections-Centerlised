"use client";

import { useState } from "react";
import { Button, Card, Input, formatDateTime } from "@/components/ui";
import { inr } from "@/lib/access";
import { callApi } from "@/lib/client-api";
import type { PriceHistory, ProductDiscount, SkuPrice } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function PriceEditor({ sku }: { sku: SkuPrice }) {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  const [price, setPrice] = useState(sku.retailPrice?.toString() ?? "");
  const [reason, setReason] = useState("");
  const [history, setHistory] = useState<PriceHistory[] | null>(null);
  if (!open) return (
    <span className="flex justify-end gap-1">
      <Button variant="ghost" onClick={async () => setHistory(history ? null : await callApi<PriceHistory[]>(`admin/pricing/skus/${sku.skuId}/history`))}>History</Button>
      <Button variant="secondary" onClick={() => setOpen(true)}>Set price</Button>
      {history && (
        <ul className="mt-2 w-72 text-left text-xs text-slate-600">
          {history.map((h) => <li key={h.id}>{inr(h.price)} · {formatDateTime(h.effectiveFrom)}{h.effectiveTo ? ` → ${formatDateTime(h.effectiveTo)}` : " (current)"} · {h.reason}</li>)}
        </ul>
      )}
    </span>
  );
  return (
    <div className="flex flex-col items-end gap-1">
      <div className="flex gap-1">
        <Input type="number" min={0.01} step="0.01" className="w-28" value={price} onChange={(e) => setPrice(e.target.value)} aria-label="New retail price" />
        <Input className="w-44" placeholder="Reason (required)" value={reason} onChange={(e) => setReason(e.target.value)} />
        <Button disabled={busy || !reason.trim() || !price} onClick={async () => {
          const ok = await run(() => callApi(`admin/pricing/skus/${sku.skuId}/retail-price`, "PUT", { price: Number(price), reason }));
          if (ok) { setOpen(false); setReason(""); }
        }}>Save</Button>
        <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
      </div>
      {error && <p className="text-xs text-red-600">{error}</p>}
    </div>
  );
}

export function ProductDiscountEditor({ productId, productName, current, editable }: { productId: string; productName: string; current: number | null; editable: boolean }) {
  const { error, busy, run } = useAction();
  const [pct, setPct] = useState(current?.toString() ?? "");
  const [reason, setReason] = useState("");
  const [history, setHistory] = useState<ProductDiscount | null>(null);
  return (
    <Card title={`Product reseller discount · ${productName}`} actions={
      <Button variant="ghost" onClick={async () => setHistory(history ? null : await callApi<ProductDiscount>(`admin/pricing/products/${productId}/reseller-discount`))}>History</Button>}>
      <p className="text-sm text-slate-700">
        {current == null ? "Not set — each reseller's own discount applies." : <>Current override: <strong>{current}%</strong> for every reseller (their own discount is ignored for this product).</>}
      </p>
      {editable && (
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <Input type="number" min={0} max={100} step="0.01" className="w-28" value={pct} onChange={(e) => setPct(e.target.value)} aria-label="Discount %" />
          <Input className="w-56" placeholder="Reason (required)" value={reason} onChange={(e) => setReason(e.target.value)} />
          <Button disabled={busy || !reason.trim() || pct === ""} onClick={() => run(() => callApi(`admin/pricing/products/${productId}/reseller-discount`, "PUT", { discountPct: Number(pct), reason }))}>Set override</Button>
          {current != null && <Button variant="secondary" disabled={busy || !reason.trim()} onClick={() => run(() => callApi(`admin/pricing/products/${productId}/reseller-discount/clear`, "POST", { reason }))}>Remove override</Button>}
          {error && <span className="text-xs text-red-600">{error}</span>}
        </div>
      )}
      {history && (
        <ul className="mt-3 space-y-1 text-xs text-slate-600">
          {history.history.map((h) => <li key={h.id}>{h.discountPct}% · {formatDateTime(h.effectiveFrom)}{h.effectiveTo ? ` → ${formatDateTime(h.effectiveTo)} (${h.endReason})` : " (current)"} · {h.reason}</li>)}
          {history.history.length === 0 && <li>No history.</li>}
        </ul>
      )}
    </Card>
  );
}
