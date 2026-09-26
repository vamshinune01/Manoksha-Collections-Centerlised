import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import { orderStatusTone, type Order } from "@/lib/types";

export default async function OrdersPage() {
  const me = (await getMe())!;
  if (!can(me, P.ordersView)) return <Forbidden what="orders" />;
  const orders = await backendFetch<Order[]>("admin/orders");
  if (!orders.ok) return <Alert>{orders.problem.title}</Alert>;
  return (
    <>
      <PageHeader title="Orders" description="Orders are assigned to one branch by the Owner's priority and are never split."
        actions={can(me, P.exceptionsView) ? <Link href="/orders/inquiries" className="text-sm text-brand-700 hover:underline">Unfulfilled checkouts →</Link> : undefined} />
      <Table head={["Order", "Channel", "Deliver to", "Branch", "Total", "Status", "Placed"]} empty={orders.data.length === 0}>
        {orders.data.map((o) => (
          <tr key={o.id} className="hover:bg-slate-50">
            <td className="px-4 py-2.5"><Link href={`/orders/${o.id}`} className="font-mono font-medium text-brand-700 hover:underline">{o.number}</Link></td>
            <td className="px-4 py-2.5"><Badge tone={o.channel === "Reseller" ? "brand" : o.channel === "Online" ? "amber" : "slate"}>{o.channel}</Badge></td>
            <td className="px-4 py-2.5">{o.delivery.name}<div className="text-xs text-slate-500">{o.delivery.city}</div></td>
            <td className="px-4 py-2.5">{o.fulfillmentBranchName}</td>
            <td className="px-4 py-2.5 font-semibold">{inr(o.grandTotal)}</td>
            <td className="px-4 py-2.5"><Badge tone={orderStatusTone(o.status)}>{o.status}</Badge></td>
            <td className="px-4 py-2.5 text-slate-600">{formatDateTime(o.createdAt)}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
