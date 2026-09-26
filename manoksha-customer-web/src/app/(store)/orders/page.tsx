import Link from "next/link";
import { customerFetch } from "@/lib/backend";
import { inr, ORDER_STATUS_LABEL, orderStatusTone as statusTone, type Order } from "@/lib/types";
import { Alert, Badge, formatDateTime, PageHeader } from "@/components/ui";

export const metadata = { title: "My orders" };

export default async function OrdersPage() {
  const orders = await customerFetch<Order[]>("orders");
  return (
    <div>
      <PageHeader title="My orders" />
      {!orders.ok ? (
        <Alert>Your orders could not be loaded.</Alert>
      ) : orders.data.length === 0 ? (
        <p className="rounded-lg border border-slate-200 bg-white p-10 text-center text-sm text-slate-500">No orders yet.</p>
      ) : (
        <ul className="space-y-3">
          {orders.data.map((o) => (
            <li key={o.id}>
              <Link href={`/orders/${o.id}`} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-slate-200 bg-white p-4 hover:border-brand-500">
                <div>
                  <p className="font-medium text-slate-900">{o.number}</p>
                  <p className="text-xs text-slate-500">{formatDateTime(o.createdAt)} · {o.lines.length} item(s)</p>
                </div>
                <div className="flex items-center gap-3">
                  <Badge tone={statusTone(o.status)}>{ORDER_STATUS_LABEL[o.status] ?? o.status}</Badge>
                  <span className="font-semibold">{inr(o.grandTotal)}</span>
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
