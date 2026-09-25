"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { Alert, Button, Card, Field, Input, Select } from "@/components/ui";
import { type CartLine, readCart, writeCart } from "@/lib/cart";
import { ApiError, rs } from "@/lib/client-api";
import { type CheckoutResult, type Quote, type SavedCustomer, inr } from "@/lib/types";

const SHIPPING = 100; // display only — the backend applies the authoritative fee

function subscribe(cb: () => void) {
  window.addEventListener("manoksha-cart", cb);
  return () => window.removeEventListener("manoksha-cart", cb);
}

let cachedJson = "";
let cachedCart: CartLine[] = [];
function snapshot() {
  const json = JSON.stringify(readCart());
  if (json !== cachedJson) { cachedJson = json; cachedCart = JSON.parse(json) as CartLine[]; }
  return cachedCart;
}

export function CartCheckout({ canOrder, walletBalance, customers }: { canOrder: boolean; walletBalance: number; customers: SavedCustomer[] }) {
  const router = useRouter();
  const cart = useSyncExternalStore(subscribe, snapshot, () => [] as CartLine[]);
  const [quotes, setQuotes] = useState<Record<string, Quote>>({});
  const [quoteError, setQuoteError] = useState<string>();
  const [customerId, setCustomerId] = useState<string>("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<CheckoutResult>();
  // One key per checkout attempt: double clicks and retries create one order and one wallet debit.
  const idempotencyKey = useMemo(() => crypto.randomUUID(), []);

  const skuKey = cart.map((l) => l.skuId).join(",");
  useEffect(() => {
    if (!skuKey) return;
    rs<Quote[]>("price-quote", "POST", { skuIds: skuKey.split(",") })
      .then((q) => { setQuotes(Object.fromEntries(q.map((x) => [x.skuId, x]))); setQuoteError(undefined); })
      .catch((e: unknown) => setQuoteError(e instanceof ApiError ? e.message : "Could not price the cart."));
  }, [skuKey]);

  const merchandise = cart.reduce((sum, l) => sum + (quotes[l.skuId]?.finalUnitPrice ?? 0) * l.quantity, 0);
  const total = merchandise + (cart.length ? SHIPPING : 0);

  if (result?.outcome === "CONFIRMED" && result.order) {
    return (
      <Card title="Order placed">
        <Alert tone="success">Order <strong>{result.order.number}</strong> is confirmed. {inr(result.order.grandTotal)} was debited from your wallet; balance {inr(result.walletBalance)}.</Alert>
        <Link className="mt-4 inline-block text-brand-700" href={`/reseller/orders/${result.order.id}`}>View order →</Link>
      </Card>
    );
  }
  if (result?.outcome === "UNFULFILLABLE" && result.inquiry) {
    return (
      <Card title="We could not complete this order">
        <Alert tone="warning">{result.inquiry.message}</Alert>
        <p className="mt-3 text-sm text-slate-600">Nothing was charged. Reference: <strong className="font-mono">{result.inquiry.reference}</strong></p>
        <a className="mt-4 inline-block rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white" href={result.inquiry.whatsAppUrl} target="_blank" rel="noreferrer">Contact us on WhatsApp</a>
      </Card>
    );
  }
  if (cart.length === 0) return <p className="text-sm text-slate-600">Your cart is empty. <Link className="text-brand-700" href="/reseller/catalog">Browse the catalog</Link>.</p>;

  function setQty(skuId: string, quantity: number) {
    writeCart(quantity <= 0 ? cart.filter((l) => l.skuId !== skuId) : cart.map((l) => (l.skuId === skuId ? { ...l, quantity: Math.min(1000, quantity) } : l)));
  }

  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <Card title="Cart" className="lg:col-span-2">
        {quoteError && <Alert>{quoteError}</Alert>}
        <ul className="divide-y divide-slate-100">
          {cart.map((l) => (
            <li key={l.skuId} className="flex flex-wrap items-center gap-3 py-3 text-sm">
              <span className="flex-1">{l.name}</span>
              <Input type="number" min={0} max={1000} value={l.quantity} onChange={(e) => setQty(l.skuId, Number(e.target.value))} className="w-20" aria-label={`Quantity of ${l.name}`} />
              <span className="w-28 text-right">{inr(quotes[l.skuId]?.finalUnitPrice)}</span>
              <span className="w-28 text-right font-medium">{quotes[l.skuId] ? inr(quotes[l.skuId]!.finalUnitPrice * l.quantity) : "—"}</span>
              <button className="text-slate-400 hover:text-red-600" onClick={() => setQty(l.skuId, 0)} aria-label={`Remove ${l.name}`}>✕</button>
            </li>
          ))}
        </ul>
        <dl className="mt-4 space-y-1 text-right text-sm">
          <div>Items: {inr(merchandise)}</div>
          <div>Shipping (per order): {inr(SHIPPING)}</div>
          <div className="text-base font-semibold">Total to be debited: {inr(total)}</div>
          <div className={walletBalance < total ? "text-red-600" : "text-slate-500"}>Wallet balance: {inr(walletBalance)}</div>
        </dl>
      </Card>
      <Card title="Delivery details">
        <form className="space-y-3" onSubmit={async (e) => {
          e.preventDefault();
          const f = new FormData(e.currentTarget);
          const delivery = customerId ? null : {
            name: f.get("name"), mobile: f.get("mobile"), email: f.get("email") || null,
            addressLine: f.get("addressLine"), city: f.get("city"), state: f.get("state"), pin: f.get("pin"),
          };
          setBusy(true);
          setError(undefined);
          try {
            const r = await rs<CheckoutResult>("checkout", "POST", {
              lines: cart.map((l) => ({ skuId: l.skuId, quantity: l.quantity })),
              resellerCustomerId: customerId || null, delivery, saveCustomer: f.get("save") === "on",
            }, { "Idempotency-Key": idempotencyKey });
            if (r.outcome === "CONFIRMED") writeCart([]);
            setResult(r);
            router.refresh();
          } catch (err) {
            setError(err instanceof ApiError ? err.message : "Checkout failed. Please try again.");
          } finally {
            setBusy(false);
          }
        }}>
          {error && <Alert>{error}</Alert>}
          {customers.length > 0 && (
            <Field label="Saved customer">
              <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
                <option value="">Enter new details</option>
                {customers.map((c) => <option key={c.id} value={c.id}>{c.details.name} · {c.details.mobile}</option>)}
              </Select>
            </Field>
          )}
          {!customerId && (
            <>
              <Field label="Name"><Input name="name" required /></Field>
              <Field label="Mobile"><Input name="mobile" inputMode="tel" required /></Field>
              <Field label="Email (optional)"><Input name="email" type="email" /></Field>
              <Field label="Address"><Input name="addressLine" required /></Field>
              <div className="grid grid-cols-2 gap-2">
                <Field label="City"><Input name="city" required /></Field>
                <Field label="PIN"><Input name="pin" inputMode="numeric" maxLength={6} required /></Field>
              </div>
              <Field label="State"><Input name="state" defaultValue="Telangana" required /></Field>
              <label className="flex items-center gap-2 text-sm"><input type="checkbox" name="save" defaultChecked className="accent-brand-600" /> Save to my customers</label>
            </>
          )}
          <Button type="submit" className="w-full" disabled={busy || !canOrder || !!quoteError}>{busy ? "Placing order…" : `Place order · ${inr(total)}`}</Button>
          {!canOrder && <p className="text-xs text-amber-800">Ordering is paused for your account.</p>}
        </form>
      </Card>
    </div>
  );
}
