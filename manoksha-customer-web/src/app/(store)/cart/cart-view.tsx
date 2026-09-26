"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { storeCart } from "@/lib/cart";
import { useStoreCart } from "@/components/store-client";
import { store } from "@/lib/client-api";
import { inr, type CartQuoteLine } from "@/lib/types";
import { Alert, Button, Input, PageHeader } from "@/components/ui";

/** Cart with current display prices from the backend. Checkout re-prices and snapshots the authoritative price (SPEC §33). */
export function CartView() {
  const lines = useStoreCart();
  const [quotes, setQuotes] = useState<Record<string, CartQuoteLine>>({});
  const [error, setError] = useState<string | null>(null);

  const skuKey = lines.map((l) => l.skuId).join(",");
  useEffect(() => {
    if (!skuKey) return;
    store<CartQuoteLine[]>("cart-quote", "POST", { skuIds: skuKey.split(",") })
      .then((q) => { setQuotes(Object.fromEntries(q.map((x) => [x.skuId, x]))); setError(null); })
      .catch(() => setError("Current prices could not be loaded. Please try again."));
  }, [skuKey]);

  const update = (skuId: string, quantity: number) =>
    storeCart.write(quantity <= 0 ? lines.filter((l) => l.skuId !== skuId) : lines.map((l) => (l.skuId === skuId ? { ...l, quantity: Math.min(1000, quantity) } : l)));

  const subtotal = lines.reduce((sum, l) => sum + (quotes[l.skuId]?.price ?? 0) * l.quantity, 0);
  const blocked = lines.some((l) => quotes[l.skuId]?.sellable === false);

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
                    <p className="font-medium text-slate-900">{q?.productName ? `${q.productName} — ${q.variantName}` : l.name}</p>
                    {q && !q.sellable && <p className="text-sm text-red-700">{q.message}</p>}
                    {q?.sellable && !q.inStock && <p className="text-sm text-amber-700">Currently unavailable — checkout may not be possible.</p>}
                    <p className="text-sm text-slate-600">{q?.price != null ? inr(q.price) : "—"} each</p>
                  </div>
                  <Input type="number" min={1} max={1000} value={l.quantity} onChange={(e) => update(l.skuId, Number(e.target.value))} className="w-20" aria-label="Quantity" />
                  <p className="w-24 text-right font-semibold">{q?.price != null ? inr(q.price * l.quantity) : "—"}</p>
                  <Button variant="ghost" onClick={() => update(l.skuId, 0)}>Remove</Button>
                </li>
              );
            })}
          </ul>
          <aside className="h-fit space-y-3 rounded-lg border border-slate-200 bg-white p-5">
            <div className="flex justify-between text-sm"><span>Items</span><span>{inr(subtotal)}</span></div>
            <p className="text-xs text-slate-500">Shipping is added once per order. The final total is shown before you pay.</p>
            {blocked && <Alert tone="warning">Remove unavailable items to continue.</Alert>}
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
