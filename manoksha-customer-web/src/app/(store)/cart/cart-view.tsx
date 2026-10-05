"use client";

import Link from "next/link";
import { storeCart } from "@/lib/cart";
import { useStoreCart } from "@/components/store-client";
import { CartTotals, useCartSummary } from "@/components/cart-summary";
import { inr } from "@/lib/types";
import { Alert, Button, Input, PageHeader } from "@/components/ui";

/** Cart with current display prices and per-brand shipping from the backend. Checkout re-prices and snapshots (SPEC §33). */
export function CartView() {
  const lines = useStoreCart();
  const { summary, error } = useCartSummary(lines, "shopper");
  const quotes = Object.fromEntries((summary?.lines ?? []).map((l) => [l.skuId, l]));

  const update = (skuId: string, quantity: number) =>
    storeCart.write(quantity <= 0 ? lines.filter((l) => l.skuId !== skuId) : lines.map((l) => (l.skuId === skuId ? { ...l, quantity: Math.min(1000, quantity) } : l)));

  const blocked = lines.some((l) => quotes[l.skuId] && (!quotes[l.skuId]!.sellable || !quotes[l.skuId]!.inStock));

  return (
    <div className="space-y-6">
      <PageHeader title="Your cart" description="Prices shown are current; your order is priced when you place it." />
      {error && <Alert>{error}</Alert>}
      {lines.length === 0 ? (
        <div className="rounded-lg border border-slate-200 bg-white p-10 text-center">
          <p className="text-sm text-slate-600">Your cart is empty.</p>
          <Link href="/" className="mt-4 inline-block text-sm font-medium text-brand-700 hover:underline">Continue shopping</Link>
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-3">
          <ul className="space-y-3 lg:col-span-2">
            {lines.map((l) => {
              const q = quotes[l.skuId];
              return (
                <li key={l.skuId} className="flex flex-wrap items-center gap-4 rounded-lg border border-slate-200 bg-white p-4">
                  <div className="min-w-0 flex-1">
                    {q?.vendorName && <p className="text-xs font-semibold uppercase tracking-wide text-brand-700">{q.vendorName}</p>}
                    <p className="font-medium text-slate-900">{q?.productName ? `${q.productName} — ${q.variantName}` : l.name}</p>
                    {q?.productCode && <p className="font-mono text-[11px] text-slate-400">{q.productCode}</p>}
                    {q?.message && <p className="text-sm text-red-700">{q.message}</p>}
                    <p className="text-sm text-slate-600">
                      {q?.unitPrice != null ? inr(q.unitPrice) : "—"} each
                      {q && q.discountPct > 0 && q.retailPrice ? <span className="ml-2 text-xs text-slate-400 line-through">{inr(q.retailPrice)}</span> : null}
                    </p>
                  </div>
                  <Input type="number" min={1} max={1000} value={l.quantity} onChange={(e) => update(l.skuId, Number(e.target.value))} className="w-20" aria-label="Quantity" />
                  <p className="w-24 text-right font-semibold">{q?.unitPrice != null ? inr(q.unitPrice * l.quantity) : "—"}</p>
                  <Button variant="ghost" onClick={() => update(l.skuId, 0)}>Remove</Button>
                </li>
              );
            })}
          </ul>
          <aside className="h-fit space-y-3 rounded-lg border border-slate-200 bg-white p-5">
            <CartTotals summary={summary} />
            {blocked && <Alert tone="warning">Remove unavailable or out-of-stock items to continue.</Alert>}
            <Link href="/checkout" aria-disabled={blocked}
              className={`block rounded-md px-4 py-2.5 text-center text-sm font-medium text-white ${blocked ? "pointer-events-none bg-slate-400" : "bg-brand-600 hover:bg-brand-700"}`}>
              Proceed to checkout
            </Link>
          </aside>
        </div>
      )}
    </div>
  );
}
