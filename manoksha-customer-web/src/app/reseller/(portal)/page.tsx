import Link from "next/link";
import { Card } from "@/components/ui";
import { getResellerMe, resellerFetch } from "@/lib/backend";
import { type Order, type ResellerDashboard, inr } from "@/lib/types";

export default async function Dashboard() {
  const me = (await getResellerMe())!;
  const [orders, stats] = await Promise.all([resellerFetch<Order[]>("orders"), resellerFetch<ResellerDashboard>("dashboard")]);
  const recent = orders.ok ? orders.data.slice(0, 5) : [];
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-900">Welcome, {me.businessName ?? me.contactName}</h1>
        <p className="text-sm text-slate-500">{me.resellerNumber} · {me.mobile}</p>
      </div>
      <div className="grid gap-4 md:grid-cols-3">
        <Card title="Wallet balance"><p className="text-2xl font-semibold">{inr(me.walletBalance)}</p><Link className="text-sm text-brand-700" href="/reseller/wallet">Add money →</Link></Card>
        <Card title="Your discounts">
          {(me.vendorDiscounts ?? []).length === 0 ? <p className="text-sm text-slate-600">None yet — retail prices apply.</p> : (
            <ul className="space-y-1 text-sm">{(me.vendorDiscounts ?? []).map((v) => <li key={v.vendorId} className="flex justify-between"><span>{v.vendorName}</span><strong>{v.discountPct}%</strong></li>)}</ul>
          )}
          <Link className="mt-2 inline-block text-xs text-brand-700" href="/reseller/terms">Terms version {me.termsVersion} →</Link>
        </Card>
        <Card title="Shipping"><p className="text-sm text-slate-700">Each brand ships its own parcel: shipping is charged once per brand in an order and included in the wallet debit.</p></Card>
      </div>
      {stats.ok && (
        <div className="grid gap-4 md:grid-cols-3">
          <Card title="This month"><p className="text-2xl font-semibold">{inr(stats.data.spentThisMonth)}</p><p className="text-xs text-slate-500">{stats.data.ordersThisMonth} order{stats.data.ordersThisMonth === 1 ? "" : "s"} · {stats.data.deliveredThisMonth} delivered</p></Card>
          <Card title="On the way"><p className="text-2xl font-semibold">{stats.data.openOrders}</p><p className="text-xs text-slate-500">orders confirmed but not yet delivered</p></Card>
          <Card title="All time"><p className="text-2xl font-semibold">{inr(stats.data.spentAllTime)}</p><p className="text-xs text-slate-500">{stats.data.ordersAllTime} orders with Manoksha</p></Card>
        </div>
      )}
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
