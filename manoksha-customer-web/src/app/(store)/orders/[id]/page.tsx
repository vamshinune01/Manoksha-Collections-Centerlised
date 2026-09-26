import Link from "next/link";
import { notFound } from "next/navigation";
import { customerFetch } from "@/lib/backend";
import { inr, ORDER_STATUS_LABEL, type Order } from "@/lib/types";
import { Card, formatDateTime } from "@/components/ui";
import { PaymentStatus } from "./payment-status";

export const metadata = { title: "Order" };

/** Customer order view. No cancel button exists; help goes through WhatsApp (SPEC §21). */
export default async function OrderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const result = await customerFetch<Order>(`orders/${id}`);
  if (!result.ok) notFound();
  const o = result.data;
  return (
    <div className="space-y-6">
      <Link href="/orders" className="text-sm text-brand-700 hover:underline">← My orders</Link>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-slate-900">Order {o.number}</h1>
          <p className="text-sm text-slate-500">Placed {formatDateTime(o.createdAt)} · {ORDER_STATUS_LABEL[o.status] ?? o.status}</p>
        </div>
        <a href={o.helpWhatsAppUrl} target="_blank" rel="noreferrer" className="rounded-md border border-emerald-600 px-4 py-2 text-sm font-medium text-emerald-700 hover:bg-emerald-50">
          Need help with this order?
        </a>
      </div>

      <PaymentStatus orderId={o.id} initialStatus={o.status} />

      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Items" className="lg:col-span-2">
          <ul className="divide-y divide-slate-100 text-sm">
            {o.lines.map((l) => (
              <li key={l.id} className="flex justify-between gap-4 py-2">
                <span>{l.productName} — {l.variantName} × {l.quantity}</span>
                <span className="font-medium">{inr(l.lineTotal)}</span>
              </li>
            ))}
          </ul>
          <dl className="mt-4 space-y-1 border-t border-slate-100 pt-3 text-sm">
            <div className="flex justify-between"><dt>Items</dt><dd>{inr(o.merchandiseTotal)}</dd></div>
            <div className="flex justify-between"><dt>Shipping</dt><dd>{inr(o.shippingFee)}</dd></div>
            <div className="flex justify-between font-semibold"><dt>Total</dt><dd>{inr(o.grandTotal)}</dd></div>
          </dl>
        </Card>
        <Card title="Delivery to">
          <address className="text-sm not-italic text-slate-700">
            {o.delivery.name}<br />{o.delivery.mobile}<br />{o.delivery.addressLine}<br />{o.delivery.city}, {o.delivery.state} {o.delivery.pin}
          </address>
        </Card>
      </div>
    </div>
  );
}
