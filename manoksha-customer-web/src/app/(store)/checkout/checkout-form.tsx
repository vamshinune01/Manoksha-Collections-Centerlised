"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { storeCart } from "@/lib/cart";
import { ApiError, cs, store } from "@/lib/client-api";
import { inr, type CartQuoteLine, type CustomerCheckoutResult, type Delivery } from "@/lib/types";
import { useStoreCart } from "@/components/store-client";
import { Alert, Button, Card, Field, Input, PageHeader } from "@/components/ui";

/**
 * Online checkout (SPEC §19.1). The backend prices the order, finds one branch that can fulfil the complete basket, reserves it for
 * the payment window and starts the UPI payment. The Idempotency-Key makes repeated clicks one order (SPEC §32).
 */
export function CheckoutForm({ defaults }: { defaults: Delivery }) {
  const lines = useStoreCart();
  const [delivery, setDelivery] = useState<Delivery>(defaults);
  const [quotes, setQuotes] = useState<Record<string, CartQuoteLine>>({});
  const [key, setKey] = useState(() => crypto.randomUUID());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<CustomerCheckoutResult | null>(null);

  const skuKey = lines.map((l) => l.skuId).join(",");
  useEffect(() => {
    if (!skuKey) return;
    store<CartQuoteLine[]>("cart-quote", "POST", { skuIds: skuKey.split(",") })
      .then((q) => setQuotes(Object.fromEntries(q.map((x) => [x.skuId, x]))))
      .catch(() => undefined);
  }, [skuKey]);

  const set = (field: keyof Delivery) => (e: React.ChangeEvent<HTMLInputElement>) => setDelivery({ ...delivery, [field]: e.target.value });
  const items = lines.reduce((sum, l) => sum + (quotes[l.skuId]?.price ?? 0) * l.quantity, 0);

  const placeOrder = async () => {
    setBusy(true);
    setError(null);
    try {
      const r = await cs<CustomerCheckoutResult>("checkout", "POST", {
        lines: lines.map((l) => ({ skuId: l.skuId, quantity: l.quantity })),
        delivery: { ...delivery, email: delivery.email || null },
      }, { "Idempotency-Key": key });
      setKey(crypto.randomUUID()); // a definite answer: the next attempt is a new one
      if (r.outcome === "PAYMENT_PENDING" && r.payment?.redirectUrl && /^https?:\/\//.test(r.payment.redirectUrl)) {
        storeCart.write([]);
        window.location.assign(r.payment.redirectUrl);
        return;
      }
      setResult(r);
    } catch (e) {
      if (e instanceof ApiError) {
        setKey(crypto.randomUUID());
        setError(e.message);
      } else {
        setError("Network problem — please try again. Your order will not be placed twice.");
      }
    } finally {
      setBusy(false);
    }
  };

  if (result?.outcome === "UNFULFILLABLE" && result.inquiry) {
    return (
      <Card title="We could not complete this order online">
        <div className="space-y-4">
          <Alert tone="warning">{result.inquiry.message}</Alert>
          <p className="text-sm text-slate-600">No order was placed and no payment was taken. Reference: <strong>{result.inquiry.reference}</strong></p>
          <a href={result.inquiry.whatsAppUrl} target="_blank" rel="noreferrer" className="inline-flex rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700">
            Contact us on WhatsApp
          </a>
          <div><Link href="/cart" className="text-sm text-brand-700 hover:underline">Back to cart</Link></div>
        </div>
      </Card>
    );
  }
  if (result?.order) {
    return (
      <Card title={`Order ${result.order.number}`}>
        <div className="space-y-4">
          <Alert tone={result.outcome === "ORDER_PLACED" ? "success" : "warning"}>{result.payment?.message}</Alert>
          <Link href={`/orders/${result.order.id}`} className="text-sm font-medium text-brand-700 hover:underline">View order →</Link>
        </div>
      </Card>
    );
  }

  if (lines.length === 0) {
    return (
      <div className="rounded-lg border border-slate-200 bg-white p-10 text-center text-sm text-slate-600">
        Your cart is empty. <Link href="/" className="font-medium text-brand-700 hover:underline">Continue shopping</Link>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader title="Checkout" description="Your items are held for a few minutes while you pay with UPI." />
      {error && <Alert>{error}</Alert>}
      <div className="grid gap-6 lg:grid-cols-3">
        <form className="space-y-4 lg:col-span-2" onSubmit={(e) => { e.preventDefault(); void placeOrder(); }}>
          <Card title="Delivery details">
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Full name"><Input value={delivery.name} onChange={set("name")} required autoComplete="name" /></Field>
              <Field label="Mobile"><Input value={delivery.mobile} onChange={set("mobile")} required inputMode="tel" autoComplete="tel" /></Field>
              <div className="sm:col-span-2"><Field label="Address"><Input value={delivery.addressLine} onChange={set("addressLine")} required autoComplete="street-address" /></Field></div>
              <Field label="City"><Input value={delivery.city} onChange={set("city")} required autoComplete="address-level2" /></Field>
              <Field label="State"><Input value={delivery.state} onChange={set("state")} required autoComplete="address-level1" /></Field>
              <Field label="PIN code"><Input value={delivery.pin} onChange={set("pin")} required inputMode="numeric" maxLength={6} autoComplete="postal-code" /></Field>
              <Field label="Email (optional)"><Input type="email" value={delivery.email ?? ""} onChange={set("email")} autoComplete="email" /></Field>
            </div>
          </Card>
          <Button type="submit" disabled={busy} className="w-full py-3 text-base">{busy ? "Placing order…" : "Place order & pay with UPI"}</Button>
          <p className="text-xs text-slate-500">
            After an order is placed it cannot be cancelled online. For any change, use “Need help with this order?” on the order page.
          </p>
        </form>
        <aside className="h-fit space-y-3 rounded-lg border border-slate-200 bg-white p-5">
          <h2 className="text-sm font-semibold text-slate-800">Order summary</h2>
          <ul className="space-y-2 text-sm">
            {lines.map((l) => (
              <li key={l.skuId} className="flex justify-between gap-2">
                <span className="text-slate-700">{l.name} × {l.quantity}</span>
                <span>{quotes[l.skuId]?.price != null ? inr(quotes[l.skuId]!.price! * l.quantity) : "—"}</span>
              </li>
            ))}
          </ul>
          <div className="flex justify-between border-t border-slate-100 pt-2 text-sm"><span>Items</span><span>{inr(items)}</span></div>
          <p className="text-xs text-slate-500">Shipping is added once per order; the exact total to pay is shown on the payment page.</p>
        </aside>
      </div>
    </div>
  );
}
