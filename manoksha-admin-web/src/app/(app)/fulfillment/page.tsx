import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import { orderStatusTone, type Branch, type Order } from "@/lib/types";

const COLUMNS: { status: string; title: string; hint: string }[] = [
  { status: "Confirmed", title: "New", hint: "Paid / confirmed — start picking" },
  { status: "Processing", title: "Processing", hint: "Being picked" },
  { status: "Packed", title: "Packed", hint: "Ready to hand to the courier" },
  { status: "Shipped", title: "Shipped", hint: "With the courier — mark delivered" },
  { status: "FulfillmentException", title: "Problem", hint: "Item missing/damaged — reroute or resolve" },
];

/** Branch work queue (SPEC §19.1): only confirmed orders reach a branch; unpaid online orders never appear here. */
export default async function FulfillmentPage({ searchParams }: { searchParams: Promise<{ branchId?: string }> }) {
  const me = (await getMe())!;
  if (!can(me, P.ordersView)) return <Forbidden what="the fulfillment queue" />;
  const { branchId } = await searchParams;
  const [queue, branches] = await Promise.all([
    backendFetch<Order[]>(`admin/fulfillment/queue${branchId ? `?branchId=${branchId}` : ""}`),
    backendFetch<Branch[]>("admin/branches"),
  ]);
  if (!queue.ok) return <Alert>{queue.problem.title}</Alert>;
  const visibleBranches = branches.ok ? branches.data.filter((b) => me.isOwner || me.branchPermissions[b.id]?.includes(P.ordersView) || me.globalPermissions.includes(P.ordersView)) : [];

  return (
    <>
      <PageHeader title="Fulfillment" description="Orders assigned to your branch, oldest first. Every order is fulfilled completely by one branch." />
      {visibleBranches.length > 1 && (
        <div className="mb-4 flex flex-wrap gap-2 text-xs">
          <Link href="/fulfillment" className={`rounded-full border px-3 py-1 ${!branchId ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white"}`}>All branches</Link>
          {visibleBranches.map((b) => (
            <Link key={b.id} href={`/fulfillment?branchId=${b.id}`}
              className={`rounded-full border px-3 py-1 ${branchId === b.id ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white"}`}>{b.name}</Link>
          ))}
        </div>
      )}
      <div className="grid gap-4 lg:grid-cols-5">
        {COLUMNS.map((c) => {
          const items = queue.data.filter((o) => o.status === c.status);
          return (
            <section key={c.status} className="rounded-lg border border-slate-200 bg-slate-50">
              <header className="border-b border-slate-200 px-3 py-2">
                <h2 className="text-sm font-semibold text-slate-800">{c.title} <span className="text-slate-500">({items.length})</span></h2>
                <p className="text-xs text-slate-500">{c.hint}</p>
              </header>
              <ul className="space-y-2 p-2">
                {items.map((o) => (
                  <li key={o.id}>
                    <Link href={`/orders/${o.id}`} className="block rounded-md border border-slate-200 bg-white p-3 text-sm hover:border-brand-500">
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-mono font-medium text-brand-700">{o.number}</span>
                        <Badge tone={o.channel === "Reseller" ? "brand" : "amber"}>{o.channel}</Badge>
                      </div>
                      <div className="mt-1 text-xs text-slate-600">{o.lines.reduce((n, l) => n + l.quantity, 0)} item(s) · {inr(o.grandTotal)}</div>
                      <div className="text-xs text-slate-500">{o.delivery.city} · {o.fulfillmentBranchName}</div>
                      {o.status === "FulfillmentException" && <div className="mt-1"><Badge tone={orderStatusTone(o.status)}>{o.openException?.reason ?? "Problem"}</Badge></div>}
                    </Link>
                  </li>
                ))}
                {items.length === 0 && <li className="p-2 text-center text-xs text-slate-400">None</li>}
              </ul>
            </section>
          );
        })}
      </div>
    </>
  );
}
