"use client";

import { useEffect, useState } from "react";
import { rs, store } from "@/lib/client-api";
import { inr, type CartSummary } from "@/lib/types";

/**
 * Display totals for a cart from the backend: each line's price for this buyer (online price, or the reseller's own vendor %),
 * grouped by brand with each brand's shipping fee (ADR-001 §46). Checkout re-prices; this is never authoritative.
 */
export function useCartSummary(lines: { skuId: string; quantity: number }[], as: "shopper" | "reseller") {
  const [summary, setSummary] = useState<CartSummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const key = lines.map((l) => `${l.skuId}:${l.quantity}`).join(",");
  useEffect(() => {
    if (!key) return;
    const body = { lines: key.split(",").map((x) => ({ skuId: x.split(":")[0], quantity: Number(x.split(":")[1]) })) };
    (as === "reseller" ? rs<CartSummary>("cart-summary", "POST", body) : store<CartSummary>("cart-summary", "POST", body))
      .then((s) => { setSummary(s); setError(null); })
      .catch(() => setError("Current prices could not be loaded. Please try again."));
  }, [key, as]);
  return { summary: key ? summary : null, error };
}

export function CartTotals({ summary, totalLabel = "Total" }: { summary: CartSummary | null; totalLabel?: string }) {
  if (!summary) return <p className="text-sm text-slate-500">Calculating…</p>;
  return (
    <div className="space-y-2 text-sm">
      {summary.groups.map((g) => (
        <div key={g.vendorId ?? "own"} className="flex justify-between text-slate-700">
          <span>{g.vendorName} <span className="text-xs text-slate-500">· items {inr(g.itemsTotal)}</span></span>
          <span>shipping {inr(g.shippingFee)}</span>
        </div>
      ))}
      <div className="flex justify-between border-t border-slate-100 pt-2"><span>Items</span><span>{inr(summary.merchandise)}</span></div>
      <div className="flex justify-between"><span>Shipping{summary.groups.length > 1 ? ` (${summary.groups.length} brands)` : ""}</span><span>{inr(summary.shipping)}</span></div>
      <div className="flex justify-between border-t border-slate-100 pt-2 text-base font-semibold"><span>{totalLabel}</span><span>{inr(summary.total)}</span></div>
      {summary.groups.length > 1 && <p className="text-xs text-slate-500">Each brand ships its items separately, so shipping is charged once per brand.</p>}
    </div>
  );
}
