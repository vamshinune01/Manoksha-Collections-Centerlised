import Link from "next/link";
import { Alert, Badge, Card, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, canAt, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import { PAYMENT_TONE, orderStatusTone, type Order, type PaymentAttempt } from "@/lib/types";
import { FulfillmentPanel } from "./fulfillment-panel";

export default async function OrderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const order = await backendFetch<Order>(`admin/orders/${id}`);
  if (!order.ok) return <Alert>{order.problem.title}</Alert>;
  const o = order.data;
  const me = (await getMe())!;
  const payments = o.channel === "Online" && can(me, P.exceptionsView) ? await backendFetch<PaymentAttempt[]>(`admin/payments?referenceId=${o.id}`) : null;
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
          <Card title="Fulfillment">
            <FulfillmentPanel orderId={o.id} status={o.status} lines={o.lines} rights={{
              fulfill: canAt(me, P.ordersFulfill, o.fulfillmentBranchId),
              raise: canAt(me, P.ordersExceptionRaise, o.fulfillmentBranchId),
              reroute: canAt(me, P.ordersReroute, o.fulfillmentBranchId),
              cancel: canAt(me, P.ordersCancel, o.fulfillmentBranchId),
            }} />
            {o.openException && (
              <div className="mt-3 rounded-md border border-amber-200 bg-amber-50 p-3 text-sm">
                <p className="font-medium text-amber-900">{o.openException.reason.replaceAll("_", " ")} · {o.openException.branchName}</p>
                <p className="text-amber-900">{o.openException.notes}</p>
                <ul className="mt-1 text-xs text-amber-800">{o.openException.lines.map((l) => <li key={l.skuId}>{l.item}: {l.missingQty} missing, {l.damagedQty} damaged</li>)}</ul>
              </div>
            )}
            {o.shipment && (
              <p className="mt-3 text-sm text-slate-700">
                Shipped via <strong>{o.shipment.courierLabel}</strong>{o.shipment.trackingNumber ? ` · ${o.shipment.trackingNumber}` : ""} · {formatDateTime(o.shipment.shippedAt)}
                {o.shipment.deliveredOn && <> · delivered {o.shipment.deliveredOn}</>}
              </p>
            )}
          </Card>
          {o.posSale ? (
            <Card title="Store sale">
              <div className="space-y-2 text-sm">
                <p>Sold by <strong>{o.posSale.cashier}</strong>{o.posSale.customerName || o.posSale.customerMobile ? ` · customer ${o.posSale.customerName ?? ""} ${o.posSale.customerMobile ?? ""}` : ""}</p>
                <ul className="space-y-0.5">
                  {o.posSale.payments.map((p, i) => <li key={i}>{p.method} {inr(p.amount)}{p.reference ? <span className="font-mono text-xs text-slate-500"> · {p.reference}</span> : null}</li>)}
                </ul>
                {o.posSale.priceOverrides.length > 0 && (
                  <div className="rounded-md border border-slate-200 p-2">
                    <p className="text-xs font-medium text-slate-700">Price changes</p>
                    <ul className="mt-1 space-y-1 text-xs text-slate-600">
                      {o.posSale.priceOverrides.map((x, i) => (
                        <li key={i}>
                          <span className="font-mono">{x.skuCode}</span> × {x.quantity}: {inr(x.originalUnitPrice)} → {inr(x.finalUnitPrice)} ({x.discountPct}%) · “{x.reason}” ·{" "}
                          {x.approver ? `approved by ${x.approver} (${x.approvalLevel.toLowerCase()})` : `within ${x.seller}'s limit`}
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            </Card>
          ) : (
            <Card title="Deliver to">
              <p className="text-sm">{o.delivery.name} · {o.delivery.mobile}<br />{o.delivery.addressLine}, {o.delivery.city}, {o.delivery.state} {o.delivery.pin}</p>
            </Card>
          )}
          <Card title="Status">
            <Badge tone={orderStatusTone(o.status)}>{o.status}</Badge>
            <ul className="mt-2 space-y-1 text-xs text-slate-600">{o.history.map((h) => <li key={h.occurredAt}>{formatDateTime(h.occurredAt)} · {h.toStatus}{h.note ? ` · ${h.note}` : ""}</li>)}</ul>
          </Card>
          {payments?.ok && payments.data.length > 0 && (
            <Card title="UPI payment">
              {payments.data.map((p) => (
                <div key={p.id} className="space-y-1 text-sm">
                  <Badge tone={PAYMENT_TONE[p.status] ?? "slate"}>{p.status}</Badge>
                  <div className="font-mono text-xs text-slate-500">{p.provider} · {p.providerOrderRef ?? "no session"}{p.providerPaymentRef ? ` · ${p.providerPaymentRef}` : ""}</div>
                  <ul className="space-y-0.5 text-xs text-slate-600">
                    {p.history.map((h) => <li key={h.occurredAt + h.toStatus}>{formatDateTime(h.occurredAt)} · {h.toStatus} ({h.source}){h.note ? ` · ${h.note}` : ""}</li>)}
                  </ul>
                </div>
              ))}
            </Card>
          )}
        </div>
      </div>
    </>
  );
}
