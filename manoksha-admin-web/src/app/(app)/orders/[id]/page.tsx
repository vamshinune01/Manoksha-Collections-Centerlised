import Link from "next/link";
import { Alert, Badge, Card, PageHeader, Table, formatDateTime } from "@/components/ui";
import { inr } from "@/lib/access";
import { backendFetch } from "@/lib/backend";
import type { Order } from "@/lib/types";

export default async function OrderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const order = await backendFetch<Order>(`admin/orders/${id}`);
  if (!order.ok) return <Alert>{order.problem.title}</Alert>;
  const o = order.data;
  return (
    <>
      <PageHeader title={o.number} description={`${o.channel} order · fulfilled by ${o.fulfillmentBranchName}`}
        actions={<Link href="/orders" className="text-sm text-brand-700 hover:underline">← Orders</Link>} />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Items (prices as charged — never recalculated)" className="lg:col-span-2">
          <Table head={["Item", "Qty", "Retail", "Discount", "Unit price", "Line total"]}>
            {o.lines.map((l) => (
              <tr key={l.id}>
                <td className="px-4 py-2">{l.productName} · {l.variantName}<div className="font-mono text-xs text-slate-400">{l.skuCode}</div></td>
                <td className="px-4 py-2">{l.quantity}</td>
                <td className="px-4 py-2">{inr(l.retailUnitPrice)}</td>
                <td className="px-4 py-2 text-xs">{l.discountPct}% {l.discountSource === "PRODUCT_RESELLER" ? "(product)" : l.discountSource === "RESELLER" ? `(terms v${l.commercialTermVersion})` : ""}</td>
                <td className="px-4 py-2">{inr(l.finalUnitPrice)}</td>
                <td className="px-4 py-2 font-medium">{inr(l.lineTotal)}</td>
              </tr>
            ))}
          </Table>
          <dl className="mt-4 space-y-1 text-right text-sm">
            <div>Merchandise: {inr(o.merchandiseTotal)}</div>
            <div>Shipping: {inr(o.shippingFee)}</div>
            <div className="text-base font-semibold">Total: {inr(o.grandTotal)}</div>
            {o.costOfGoods != null && <div className="text-xs text-slate-500">FIFO cost {inr(o.costOfGoods)} · gross profit {inr(o.merchandiseTotal - o.costOfGoods)}</div>}
          </dl>
        </Card>
        <div className="space-y-6">
          <Card title="Deliver to">
            <p className="text-sm">{o.delivery.name} · {o.delivery.mobile}<br />{o.delivery.addressLine}, {o.delivery.city}, {o.delivery.state} {o.delivery.pin}</p>
          </Card>
          <Card title="Status">
            <Badge tone="green">{o.status}</Badge>
            <ul className="mt-2 space-y-1 text-xs text-slate-600">{o.history.map((h) => <li key={h.occurredAt}>{formatDateTime(h.occurredAt)} · {h.toStatus}{h.note ? ` · ${h.note}` : ""}</li>)}</ul>
          </Card>
        </div>
      </div>
    </>
  );
}
