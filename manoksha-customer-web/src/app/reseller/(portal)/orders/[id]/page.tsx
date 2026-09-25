import Link from "next/link";
import { Alert, Card, Table, formatDateTime } from "@/components/ui";
import { resellerFetch } from "@/lib/backend";
import { type Order, inr } from "@/lib/types";

export default async function OrderDetail({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const order = await resellerFetch<Order>(`orders/${id}`);
  if (!order.ok) return <Alert>{order.status === 404 ? "Order not found." : order.title}</Alert>;
  const o = order.data;
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <Link href="/reseller/orders" className="text-sm text-brand-700">← Orders</Link>
          <h1 className="mt-1 font-mono text-xl font-semibold">{o.number}</h1>
          <p className="text-sm text-slate-500">{o.status} · placed {formatDateTime(o.createdAt)}</p>
        </div>
        {/* No self-service cancellation: order problems go through WhatsApp support (SPEC §21). */}
        <a href={o.helpWhatsAppUrl} target="_blank" rel="noreferrer" className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white">Need help with this order?</a>
      </div>
      <Card title="Items">
        <Table head={["Item", "Qty", "Your price", "Line total"]}>
          {o.lines.map((l) => (
            <tr key={l.id}>
              <td className="px-4 py-2">{l.productName} · {l.variantName}</td>
              <td className="px-4 py-2">{l.quantity}</td>
              <td className="px-4 py-2">{inr(l.finalUnitPrice)} <span className="text-xs text-slate-400 line-through">{inr(l.retailUnitPrice)}</span></td>
              <td className="px-4 py-2 font-medium">{inr(l.lineTotal)}</td>
            </tr>
          ))}
        </Table>
        <dl className="mt-4 space-y-1 text-right text-sm">
          <div>Items {inr(o.merchandiseTotal)}</div><div>Shipping {inr(o.shippingFee)}</div><div className="font-semibold">Paid from wallet {inr(o.grandTotal)}</div>
        </dl>
      </Card>
      <Card title="Deliver to">
        <p className="text-sm">{o.delivery.name} · {o.delivery.mobile}<br />{o.delivery.addressLine}, {o.delivery.city}, {o.delivery.state} {o.delivery.pin}</p>
      </Card>
    </div>
  );
}
