import Link from "next/link";
import type { ReactNode } from "react";
import { Alert, Badge, Card, PageHeader, formatDateTime } from "@/components/ui";
import { NAV, P, can, canAny, inr, shortId } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import { EXCEPTION_LABELS } from "@/lib/exceptions";
import type { Dashboard, SalesBlock } from "@/lib/types";

/** Owner / branch dashboard (SPEC §31). Branch users see only their branches and never cost, profit or stock value. */
export default async function DashboardPage({ searchParams }: { searchParams: Promise<{ branchId?: string }> }) {
  const me = (await getMe())!;
  if (!can(me, P.reportsView)) return <Welcome />;
  const { branchId } = await searchParams;
  const res = await backendFetch<Dashboard>(`admin/dashboard${branchId ? `?branchId=${branchId}` : ""}`);
  if (!res.ok) return <Alert>{res.problem.title}</Alert>;
  const d = res.data;
  const scope = branchId ? d.branches.find((b) => b.id === branchId)?.name : d.branches.length === 1 ? d.branches[0]?.name : "All branches";

  return (
    <>
      <PageHeader title="Dashboard" description={`${scope} · ${new Date(d.today).toLocaleDateString("en-IN", { dateStyle: "full" })}`} />
      {d.branches.length > 1 && (
        <div className="mb-4 flex flex-wrap gap-2 text-xs">
          <Pill href="/" active={!branchId}>All branches</Pill>
          {d.branches.map((b) => <Pill key={b.id} href={`/?branchId=${b.id}`} active={branchId === b.id}>{b.name}</Pill>)}
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Kpi label="Sales today" value={inr(d.salesToday.revenue)} note={`${d.salesToday.orders} orders · ${d.salesToday.units} items`} />
        {d.costVisible
          ? <Kpi label="Gross profit today (FIFO)" value={inr(d.salesToday.grossProfit)} note={margin(d.salesToday)} />
          : <Kpi label="Shipping collected today" value={inr(d.salesToday.shippingFees)} note="charged on online and reseller orders" />}
        <Kpi label="Sales this month" value={inr(d.salesMonth.revenue)}
          note={d.costVisible ? `gross profit ${inr(d.salesMonth.grossProfit)} · ${margin(d.salesMonth)}` : `${d.salesMonth.orders} orders`} />
        <Kpi label="Stock available" value={d.inventory.availableUnits.toLocaleString("en-IN")}
          note={d.costVisible ? `stock value ${inr(d.inventory.stockValue)} (FIFO)` : `${d.inventory.onHandUnits.toLocaleString("en-IN")} on hand · ${d.inventory.inTransitUnits} in transit`} />
      </div>
      {d.costVisible && (
        <p className="mt-2 text-xs text-slate-500">Gross profit = sales (excluding shipping) minus FIFO product cost. It is not net profit: business expenses are not recorded.</p>
      )}

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        <Card title="Last 14 days" className="lg:col-span-2"><Trend points={d.last14Days} /></Card>
        <Card title="Today by channel">
          <ul className="space-y-2 text-sm">
            {["Online", "Store", "Reseller"].map((ch) => {
              const c = d.salesToday.byChannel.find((x) => x.channel === ch);
              return (
                <li key={ch} className="flex justify-between">
                  <span className="text-slate-600">{ch}</span>
                  <span className="font-medium">{inr(c?.revenue ?? 0)} <span className="text-xs text-slate-500">· {c?.orders ?? 0}</span></span>
                </li>
              );
            })}
          </ul>
          <Link href="/reports" className="mt-4 inline-block text-sm text-brand-700 hover:underline">Sales reports →</Link>
        </Card>
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        {d.openExceptions && (
          <Card title="Needs attention" actions={<Link href="/exceptions" className="text-sm text-brand-700 hover:underline">Exception Center</Link>}>
            {d.openExceptions.every((c) => c.open === 0) ? <p className="text-sm text-slate-500">Nothing is waiting.</p> : (
              <ul className="space-y-1.5 text-sm">
                {d.openExceptions.filter((c) => c.open > 0).map((c) => (
                  <li key={c.type} className="flex justify-between">
                    <Link href={`/exceptions?type=${c.type}`} className="text-slate-700 hover:underline">{EXCEPTION_LABELS[c.type]}</Link>
                    <Badge tone={c.type === "PAYMENT_RECONCILIATION" || c.type === "FULFILLMENT_EXCEPTION" || c.type === "SENSITIVE_ALERT" ? "red" : "amber"}>{c.open}</Badge>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        )}
        <Card title="Approvals & stock movement">
          <ul className="space-y-1.5 text-sm">
            <Row href="/inventory/transfers" label="Transfers waiting for approval" value={d.operations.transfersAwaitingApproval} />
            <Row href="/inventory/transfers" label="Transfers in progress" value={d.operations.transfersInProgress} />
            <Row href="/inventory/adjustments" label="Stock adjustments to approve" value={d.operations.adjustmentsPending} />
            <Row href="/inventory/discrepancies" label="Open stock discrepancies" value={d.operations.discrepanciesOpen} />
            {d.operations.depositsPending !== null && <Row href="/wallet-deposits" label="Wallet deposits to approve" value={d.operations.depositsPending} />}
          </ul>
        </Card>
        <Card title="Attendance today">
          <ul className="space-y-1.5 text-sm">
            <Row href="/attendance" label="Clocked in now" value={d.attendance.clockedInNow} />
            <Row href="/attendance" label="Came in today" value={d.attendance.clockedInToday} />
            <Row href="/employees" label="Active employees" value={d.attendance.activeEmployees} />
          </ul>
        </Card>
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-2">
        <Card title={`Low stock (${d.lowStockCount})`} actions={<Link href={`/reports?tab=low-stock${branchId ? `&branchId=${branchId}` : ""}`} className="text-sm text-brand-700 hover:underline">All</Link>}>
          <p className="mb-2 text-xs text-slate-500">Available quantity at or below {d.lowStockThreshold} (Business settings → inventory.low_stock_threshold).</p>
          {d.lowStock.length === 0 ? <p className="text-sm text-slate-500">No SKU is low on stock.</p> : (
            <ul className="divide-y divide-slate-100 text-sm">
              {d.lowStock.map((r) => (
                <li key={`${r.branchId}-${r.skuId}`} className="flex justify-between gap-3 py-1.5">
                  <span className="min-w-0 truncate">{r.productName} · {r.variantName} <span className="font-mono text-xs text-slate-400">{r.skuCode}</span></span>
                  <span className="whitespace-nowrap text-xs"><span className="text-slate-500">{r.branchName}</span> <Badge tone={r.available === 0 ? "red" : "amber"}>{r.available}</Badge></span>
                </li>
              ))}
            </ul>
          )}
        </Card>
        {d.branchSales.length > 1 && (
          <Card title="Branches">
            <table className="w-full text-sm">
              <thead><tr className="text-left text-xs text-slate-500"><th className="py-1">Branch</th><th>Today</th><th>This month</th>{d.costVisible && <th>Gross profit (month)</th>}</tr></thead>
              <tbody className="divide-y divide-slate-100">
                {d.branchSales.map((b) => (
                  <tr key={b.branchId}>
                    <td className="py-1.5"><Link href={`/?branchId=${b.branchId}`} className="hover:underline">{b.branchName}</Link></td>
                    <td>{inr(b.revenueToday)} <span className="text-xs text-slate-500">· {b.ordersToday}</span></td>
                    <td>{inr(b.revenueMonth)} <span className="text-xs text-slate-500">· {b.ordersMonth}</span></td>
                    {d.costVisible && <td>{inr(b.grossProfitMonth)}</td>}
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        )}
        {d.resellers && (
          <Card title="Resellers" actions={<Link href="/reports?tab=resellers" className="text-sm text-brand-700 hover:underline">Report</Link>}>
            <p className="text-sm text-slate-600">{d.resellers.active} active · {d.resellers.frozen} frozen · wallets hold {inr(d.resellers.walletBalances)}</p>
            <p className="mt-3 text-xs font-medium uppercase tracking-wide text-slate-500">Top this month</p>
            {d.resellers.topThisMonth.length === 0 ? <p className="text-sm text-slate-500">No reseller orders this month.</p> : (
              <ul className="mt-1 divide-y divide-slate-100 text-sm">
                {d.resellers.topThisMonth.map((r) => (
                  <li key={r.resellerId} className="flex justify-between py-1.5">
                    <Link href={`/resellers/${r.resellerId}`} className="hover:underline">{r.name} <span className="font-mono text-xs text-slate-400">{r.resellerNumber}</span></Link>
                    <span>{inr(r.revenue)} <span className="text-xs text-slate-500">· {r.orders}</span></span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        )}
        {d.priceChanges && (
          <Card title="Retail price changes (7 days)" actions={<Link href="/pricing" className="text-sm text-brand-700 hover:underline">Pricing</Link>}>
            {d.priceChanges.length === 0 ? <p className="text-sm text-slate-500">No price changes this week.</p> : (
              <ul className="divide-y divide-slate-100 text-sm">
                {d.priceChanges.map((p, i) => (
                  <li key={i} className="py-1.5">
                    <div className="flex justify-between gap-3"><span className="truncate">{p.productName} <span className="font-mono text-xs text-slate-400">{p.skuCode}</span></span>
                      <span className="whitespace-nowrap">{p.oldPrice !== null && <span className="text-slate-400 line-through">{inr(p.oldPrice)}</span>} {inr(p.newPrice)}</span></div>
                    <div className="text-xs text-slate-500">{formatDateTime(p.at)} · {p.reason}</div>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        )}
      </div>
      {can(me, P.auditView) && <p className="mt-6 text-sm"><Link href="/audit" className="text-brand-700 hover:underline">Search the audit log →</Link></p>}
    </>
  );
}

function margin(s: SalesBlock) {
  return s.marginPct === null ? (s.revenue > 0 ? "margin unavailable (cost missing)" : "no sales yet") : `${s.marginPct.toFixed(1)}% margin`;
}

function Kpi({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</p>
      <p className="mt-1 text-2xl font-semibold text-slate-900">{value}</p>
      {note && <p className="mt-1 text-xs text-slate-500">{note}</p>}
    </div>
  );
}

function Pill({ href, active, children }: { href: string; active: boolean; children: ReactNode }) {
  return <Link href={href} className={`rounded-full border px-3 py-1 ${active ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white"}`}>{children}</Link>;
}

function Row({ href, label, value }: { href: string; label: string; value: number }) {
  return (
    <li className="flex justify-between">
      <Link href={href} className="text-slate-700 hover:underline">{label}</Link>
      <span className={value > 0 ? "font-semibold" : "text-slate-400"}>{value}</span>
    </li>
  );
}

/** Daily sales bars (revenue; profit shown in the tooltip for the Owner). */
function Trend({ points }: { points: Dashboard["last14Days"] }) {
  const max = Math.max(1, ...points.map((p) => p.revenue));
  return (
    <div>
      <div className="flex h-40 items-end gap-1.5" role="img" aria-label="Sales for the last 14 days">
        {points.map((p) => (
          <div key={p.date} className="group flex h-full flex-1 flex-col justify-end"
            title={`${new Date(p.date).toLocaleDateString("en-IN", { day: "numeric", month: "short" })}: ${inr(p.revenue)} · ${p.orders} orders${p.grossProfit !== null ? ` · gross profit ${inr(p.grossProfit)}` : ""}`}>
            <div className="rounded-t bg-brand-500 group-hover:bg-brand-700" style={{ height: `${Math.max(p.revenue > 0 ? 3 : 1, (p.revenue / max) * 100)}%` }} />
          </div>
        ))}
      </div>
      <div className="mt-1 flex justify-between text-[11px] text-slate-500">
        <span>{points[0] && new Date(points[0].date).toLocaleDateString("en-IN", { day: "numeric", month: "short" })}</span>
        <span>Today · {inr(points.at(-1)?.revenue ?? 0)}</span>
      </div>
    </div>
  );
}

/** Staff without reports keep the simple welcome page. */
async function Welcome() {
  const me = (await getMe())!;
  const sections = NAV.filter((n) => n.href !== "/" && canAny(me, n.permission));
  return (
    <>
      <PageHeader title={`Welcome, ${me.displayName}`} description="Your access is limited to your assigned roles and branches." />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Your access" className="lg:col-span-2">
          <ul className="space-y-2 text-sm">
            {me.roles.length === 0 && <li className="text-slate-500">No roles assigned yet. Contact the Owner.</li>}
            {me.roles.map((r) => (
              <li key={`${r.code}-${r.branchId}`} className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2">
                <span className="font-medium text-slate-800">{r.name}</span>
                <Badge tone={r.branchId ? "slate" : "brand"}>{r.branchId ? `Branch ${shortId(r.branchId)}` : "All branches"}</Badge>
              </li>
            ))}
          </ul>
        </Card>
        <Card title="Go to">
          <ul className="space-y-1 text-sm">
            {sections.map((s) => <li key={s.href}><Link className="text-brand-700 hover:underline" href={s.href}>{s.label}</Link></li>)}
          </ul>
        </Card>
      </div>
    </>
  );
}
