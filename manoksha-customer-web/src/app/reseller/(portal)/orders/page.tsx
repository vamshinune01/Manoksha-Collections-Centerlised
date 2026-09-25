import Link from "next/link";
import { Alert, Table, formatDateTime } from "@/components/ui";
import { resellerFetch } from "@/lib/backend";
import { type Order, inr } from "@/lib/types";

export default async function Orders() {
  const orders = await resellerFetch<Order[]>("orders");
  if (!orders.ok) return <Alert>{orders.title}</Alert>;
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">Orders</h1>
      <Table head={["Order", "Deliver to", "Total", "Status", "Placed"]} empty={orders.data.length === 0}>
        {orders.data.map((o) => (
          <tr key={o.id}>
            <td className="px-4 py-2.5"><Link className="font-mono text-brand-700" href={`/reseller/orders/${o.id}`}>{o.number}</Link></td>
            <td className="px-4 py-2.5">{o.delivery.name}<div className="text-xs text-slate-500">{o.delivery.city}</div></td>
            <td className="px-4 py-2.5 font-medium">{inr(o.grandTotal)}</td>
            <td className="px-4 py-2.5">{o.status}</td>
            <td className="px-4 py-2.5 text-slate-600">{formatDateTime(o.createdAt)}</td>
          </tr>
        ))}
      </Table>
    </div>
  );
}
