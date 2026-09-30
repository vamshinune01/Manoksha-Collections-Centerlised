import Link from "next/link";
import type { ReactNode } from "react";
import { Alert, Badge, Button, Card, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, InventoryValuation, LowStockReport, ProductSalesReport, ResellerReport, SalesReport } from "@/lib/types";
import { CsvButton } from "./csv-button";

type Search = { tab?: string; from?: string; to?: string; branchId?: string; groupBy?: string };

const pct = (v: number | null) => (v === null ? "—" : `${v.toFixed(1)}%`);

/** Sales, product, stock and reseller reports (SPEC §31). Cost, gross profit and stock value are Owner-only (ADR-001 §40). */
export default async function ReportsPage({ searchParams }: { searchParams: Promise<Search> }) {
  const me = (await getMe())!;
  if (!can(me, P.reportsView)) return <Forbidden what="reports" />;
  const sp = await searchParams;
  const tab = sp.tab ?? "sales";
  const owner = can(me, P.reportsGlobal);
  const tabs: [string, string][] = [["sales", "Sales"], ["products", "Products"], ["low-stock", "Low stock"],
    ...(owner ? [["valuation", "Stock value"], ["resellers", "Resellers"]] as [string, string][] : [])];
  const branches = await backendFetch<Branch[]>("admin/branches");
  const q = (extra: Record<string, string | undefined>) =>
    new URLSearchParams(Object.entries({ from: sp.from, to: sp.to, branchId: sp.branchId, groupBy: sp.groupBy, ...extra }).filter(([, v]) => v) as [string, string][]).toString();

  return (
    <>
      <PageHeader title="Reports" description="Sales are counted on the day an order is confirmed (India time); cancelled orders are excluded. Revenue excludes shipping." />
      <div className="mb-4 flex flex-wrap gap-2 text-xs">
        {tabs.map(([key, label]) => (
          <Link key={key} href={`/reports?${q({ tab: key })}`} className={`rounded-full border px-3 py-1 ${tab === key ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white"}`}>{label}</Link>
        ))}
      </div>
      <form className="mb-4 flex flex-wrap items-end gap-3 rounded-lg border border-slate-200 bg-white p-3 text-sm" action="/reports">
        <input type="hidden" name="tab" value={tab} />
        {["sales", "products", "resellers"].includes(tab) && (
          <>
            <label className="flex flex-col text-xs text-slate-600">From<input type="date" name="from" defaultValue={sp.from} className="mt-1 rounded border border-slate-300 px-2 py-1 text-sm" /></label>
            <label className="flex flex-col text-xs text-slate-600">To<input type="date" name="to" defaultValue={sp.to} className="mt-1 rounded border border-slate-300 px-2 py-1 text-sm" /></label>
          </>
        )}
        {tab !== "resellers" && branches.ok && (
          <label className="flex flex-col text-xs text-slate-600">Branch
            <select name="branchId" defaultValue={sp.branchId ?? ""} className="mt-1 rounded border border-slate-300 px-2 py-1 text-sm">
              <option value="">All my branches</option>
              {branches.data.filter((b) => b.isActive).map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </select>
          </label>
        )}
        {tab === "sales" && (
          <label className="flex flex-col text-xs text-slate-600">Group by
            <select name="groupBy" defaultValue={sp.groupBy ?? "day"} className="mt-1 rounded border border-slate-300 px-2 py-1 text-sm">
              <option value="day">Day</option><option value="channel">Channel</option><option value="branch">Branch</option>
              {owner && <option value="reseller">Reseller</option>}
            </select>
          </label>
        )}
        <Button type="submit">Show</Button>
        {!sp.from && ["sales", "products", "resellers"].includes(tab) && <span className="text-xs text-slate-500">Default: this month to date.</span>}
      </form>
      {tab === "products" ? <Products query={q({})} /> : tab === "low-stock" ? <LowStock query={q({})} />
        : tab === "valuation" && owner ? <Valuation query={q({})} /> : tab === "resellers" && owner ? <Resellers query={q({})} /> : <Sales query={q({})} />}
    </>
  );
}

function Money({ children }: { children: ReactNode }) {
  return <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums">{children}</td>;
}

async function Sales({ query }: { query: string }) {
  const res = await backendFetch<SalesReport>(`admin/reports/sales?${query}`);
  if (!res.ok) return <Alert>{res.problem.title}</Alert>;
  const r = res.data;
  const head = ["", "Orders", "Items", "Sales", "Shipping", ...(r.costVisible ? ["FIFO cost", "Gross profit", "Margin"] : [])];
  const cells = (x: SalesReport["total"]) => [x.label, x.orders, x.units, x.revenue, x.shippingFees, ...(r.costVisible ? [x.cost, x.grossProfit, x.marginPct] : [])];
  const label = (k: string, l: string) => (r.groupBy === "day" ? new Date(k).toLocaleDateString("en-IN", { weekday: "short", day: "numeric", month: "short" }) : l || "—");
  return (
    <Card title={`${r.from} to ${r.to}`} actions={<CsvButton filename={`sales-${r.groupBy}-${r.from}-${r.to}.csv`} head={head.map((h, i) => (i === 0 ? r.groupBy : h))} rows={[...r.rows, r.total].map(cells)} />}>
      <Table head={head} empty={r.rows.length === 0}>
        {r.rows.map((x) => (
          <tr key={x.key}>
            <td className="px-4 py-2">{label(x.key, x.label)}</td>
            <td className="px-4 py-2 text-right">{x.orders}</td>
            <td className="px-4 py-2 text-right">{x.units}</td>
            <Money>{inr(x.revenue)}</Money>
            <Money>{inr(x.shippingFees)}</Money>
            {r.costVisible && <><Money>{inr(x.cost)}</Money><Money>{inr(x.grossProfit)}</Money><Money>{pct(x.marginPct)}</Money></>}
          </tr>
        ))}
        {r.rows.length > 0 && (
          <tr className="bg-slate-50 font-semibold">
            <td className="px-4 py-2">Total</td>
            <td className="px-4 py-2 text-right">{r.total.orders}</td>
            <td className="px-4 py-2 text-right">{r.total.units}</td>
            <Money>{inr(r.total.revenue)}</Money>
            <Money>{inr(r.total.shippingFees)}</Money>
            {r.costVisible && <><Money>{inr(r.total.cost)}</Money><Money>{inr(r.total.grossProfit)}</Money><Money>{pct(r.total.marginPct)}</Money></>}
          </tr>
        )}
      </Table>
      {r.costVisible && <p className="mt-2 text-xs text-slate-500">Gross profit is sales minus FIFO product cost — not net profit. &ldquo;—&rdquo; means some sale has no recorded cost.</p>}
    </Card>
  );
}

async function Products({ query }: { query: string }) {
  const res = await backendFetch<ProductSalesReport>(`admin/reports/products?${query}`);
  if (!res.ok) return <Alert>{res.problem.title}</Alert>;
  const r = res.data;
  const head = ["SKU", "Product", "Items sold", "Sales", ...(r.costVisible ? ["FIFO cost", "Gross profit", "Margin"] : [])];
  return (
    <Card title={`Best sellers · ${r.from} to ${r.to}`} actions={<CsvButton filename={`products-${r.from}-${r.to}.csv`} head={head}
      rows={r.rows.map((x) => [x.skuCode, `${x.productName} · ${x.variantName}`, x.units, x.revenue, ...(r.costVisible ? [x.cost, x.grossProfit, x.marginPct] : [])])} />}>
      <Table head={head} empty={r.rows.length === 0}>
        {r.rows.map((x) => (
          <tr key={x.skuId}>
            <td className="px-4 py-2 font-mono text-xs">{x.skuCode}</td>
            <td className="px-4 py-2">{x.productName} · {x.variantName}</td>
            <td className="px-4 py-2 text-right">{x.units}</td>
            <Money>{inr(x.revenue)}</Money>
            {r.costVisible && <><Money>{inr(x.cost)}</Money><Money>{inr(x.grossProfit)}</Money><Money>{pct(x.marginPct)}</Money></>}
          </tr>
        ))}
      </Table>
    </Card>
  );
}

async function LowStock({ query }: { query: string }) {
  const res = await backendFetch<LowStockReport>(`admin/reports/low-stock?${query}`);
  if (!res.ok) return <Alert>{res.problem.title}</Alert>;
  const r = res.data;
  return (
    <Card title={`${r.rows.length} SKU(s) at or below ${r.threshold} available`}
      actions={<CsvButton filename="low-stock.csv" head={["Branch", "SKU", "Product", "Available"]} rows={r.rows.map((x) => [x.branchName, x.skuCode, `${x.productName} · ${x.variantName}`, x.available])} />}>
      <p className="mb-3 text-xs text-slate-500">The threshold is the business setting <span className="font-mono">inventory.low_stock_threshold</span>. Branch managers get a daily notification.</p>
      <Table head={["Branch", "SKU", "Product", "Available"]} empty={r.rows.length === 0}>
        {r.rows.map((x) => (
          <tr key={`${x.branchId}-${x.skuId}`}>
            <td className="px-4 py-2">{x.branchName}</td>
            <td className="px-4 py-2 font-mono text-xs">{x.skuCode}</td>
            <td className="px-4 py-2">{x.productName} · {x.variantName}</td>
            <td className="px-4 py-2"><Badge tone={x.available === 0 ? "red" : "amber"}>{x.available}</Badge></td>
          </tr>
        ))}
      </Table>
    </Card>
  );
}

async function Valuation({ query }: { query: string }) {
  const res = await backendFetch<InventoryValuation>(`admin/reports/inventory-valuation?${query}`);
  if (!res.ok) return <Alert>{res.problem.title}</Alert>;
  const r = res.data;
  return (
    <>
      <div className="mb-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-lg border border-slate-200 bg-white p-4"><p className="text-xs uppercase text-slate-500">Total stock value (FIFO)</p><p className="text-2xl font-semibold">{inr(r.totalValue)}</p></div>
        {r.branches.map((b) => (
          <div key={b.branchId} className="rounded-lg border border-slate-200 bg-white p-4"><p className="text-xs uppercase text-slate-500">{b.branchName}</p>
            <p className="text-xl font-semibold">{inr(b.value)}</p><p className="text-xs text-slate-500">{b.costedUnits} units</p></div>
        ))}
      </div>
      <Card title={`As of ${formatDateTime(r.asOf)}`} actions={<CsvButton filename="stock-value.csv" head={["Branch", "SKU", "Product", "Available", "On hand", "Costed units", "Value"]}
        rows={r.rows.map((x) => [x.branchName, x.skuCode, `${x.productName} · ${x.variantName}`, x.availableUnits, x.onHandUnits, x.costedUnits, x.value])} />}>
        <p className="mb-3 text-xs text-slate-500">Value = remaining units of each purchase layer × its unit cost (FIFO). Units sold but not yet delivered are no longer counted.</p>
        <Table head={["Branch", "SKU", "Product", "Available", "On hand", "Costed units", "Value"]} empty={r.rows.length === 0}>
          {r.rows.map((x) => (
            <tr key={`${x.branchId}-${x.skuId}`}>
              <td className="px-4 py-2">{x.branchName}</td>
              <td className="px-4 py-2 font-mono text-xs">{x.skuCode}</td>
              <td className="px-4 py-2">{x.productName} · {x.variantName}</td>
              <td className="px-4 py-2 text-right">{x.availableUnits}</td>
              <td className="px-4 py-2 text-right">{x.onHandUnits}</td>
              <td className="px-4 py-2 text-right">{x.costedUnits}</td>
              <Money>{inr(x.value)}</Money>
            </tr>
          ))}
        </Table>
      </Card>
    </>
  );
}

async function Resellers({ query }: { query: string }) {
  const res = await backendFetch<ResellerReport>(`admin/reports/resellers?${query}`);
  if (!res.ok) return <Alert>{res.problem.title}</Alert>;
  const r = res.data;
  const head = ["Reseller", "Status", "Discount", "Orders", "Sales", "Wallet balance", "Last order"];
  return (
    <Card title={`${r.from} to ${r.to}`} actions={<CsvButton filename={`resellers-${r.from}-${r.to}.csv`} head={head}
      rows={r.rows.map((x) => [`${x.resellerNumber} ${x.name}`, x.status, x.discountPct, x.orders, x.revenue, x.walletBalance, x.lastOrderAt])} />}>
      <Table head={head} empty={r.rows.length === 0}>
        {r.rows.map((x) => (
          <tr key={x.resellerId}>
            <td className="px-4 py-2"><Link href={`/resellers/${x.resellerId}`} className="text-brand-700 hover:underline">{x.name}</Link><div className="font-mono text-xs text-slate-500">{x.resellerNumber}</div></td>
            <td className="px-4 py-2"><Badge tone={x.status === "Active" ? "green" : x.status === "Pending" ? "slate" : "amber"}>{x.status}</Badge></td>
            <td className="px-4 py-2 text-right">{x.discountPct}%</td>
            <td className="px-4 py-2 text-right">{x.orders}</td>
            <Money>{inr(x.revenue)}</Money>
            <Money>{inr(x.walletBalance)}</Money>
            <td className="px-4 py-2 text-xs text-slate-500">{formatDateTime(x.lastOrderAt)}</td>
          </tr>
        ))}
      </Table>
    </Card>
  );
}
