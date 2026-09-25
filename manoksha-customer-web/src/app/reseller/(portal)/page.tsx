import Link from "next/link";
import { Card } from "@/components/ui";
import { getResellerMe, resellerFetch } from "@/lib/backend";
import { type Order, inr } from "@/lib/types";

export default async function Dashboard() {
  const me = (await getResellerMe())!;
  const orders = await resellerFetch<Order[]>("orders");
  const recent = orders.ok ? orders.data.slice(0, 5) : [];
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-900">Welcome, {me.businessName ?? me.contactName}</h1>
        <p className="text-sm text-slate-500">{me.resellerNumber} · {me.mobile}</p>
      </div>
      <div className="grid gap-4 md:grid-cols-3">
        <Card title="Wallet balance"><p className="text-2xl font-semibold">{inr(me.walletBalance)}</p><Link className="text-sm text-brand-700" href="/reseller/wallet">Add money →</Link></Card>
        <Card title="Your reseller discount"><p className="text-2xl font-semibold">{me.resellerDiscountPct}%</p><p className="text-xs text-slate-500">Terms version {me.termsVersion}. Some products carry their own reseller discount instead.</p></Card>
        <Card title="Shipping"><p className="text-2xl font-semibold">₹100</p><p className="text-xs text-slate-500">per order, included in the wallet debit.</p></Card>
      </div>
      <Card title="Recent orders" actions={<Link className="text-sm text-brand-700" href="/reseller/orders">All orders</Link>}>
        {recent.length === 0 ? <p className="text-sm text-slate-500">No orders yet. <Link className="text-brand-700" href="/reseller/catalog">Browse the catalog</Link>.</p> : (
          <ul className="divide-y divide-slate-100 text-sm">
            {recent.map((o) => (
              <li key={o.id} className="flex justify-between py-2">
                <Link href={`/reseller/orders/${o.id}`} className="font-mono text-brand-700">{o.number}</Link>
                <span>{o.delivery.name}</span><span>{inr(o.grandTotal)}</span><span className="text-slate-500">{o.status}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}
