import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { FulfillmentExceptionInfo } from "@/lib/types";

/** SPEC §22/§28: confirmed orders a branch could not fulfil — reroute the whole order, resolve in place, or cancel. */
export default async function ExceptionsPage({ searchParams }: { searchParams: Promise<{ status?: string }> }) {
  const me = (await getMe())!;
  if (!can(me, P.exceptionsView)) return <Forbidden what="fulfillment exceptions" />;
  const { status = "Open" } = await searchParams;
  const list = await backendFetch<FulfillmentExceptionInfo[]>(`admin/fulfillment-exceptions${status === "All" ? "" : `?status=${status}`}`);
  if (!list.ok) return <Alert>{list.problem.title}</Alert>;
  return (
    <>
      <PageHeader title="Fulfillment exceptions" description="Items a branch could not find or found damaged after the order was confirmed." />
      <div className="mb-3 flex gap-2 text-xs">
        {["Open", "Rerouted", "ResolvedInPlace", "Cancelled", "All"].map((s) => (
          <Link key={s} href={`/orders/exceptions?status=${s}`} className={`rounded-full border px-3 py-1 ${status === s ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white"}`}>{s}</Link>
        ))}
      </div>
      <Table head={["Order", "Branch", "Reason", "Items", "Raised", "Status"]} empty={list.data.length === 0}>
        {list.data.map((e) => (
          <tr key={e.id}>
            <td className="px-4 py-2"><Link className="font-mono text-brand-700 hover:underline" href={`/orders/${e.orderId}`}>{e.orderNumber}</Link><div className="text-xs text-slate-500">{e.channel}</div></td>
            <td className="px-4 py-2">{e.branchName}</td>
            <td className="px-4 py-2 text-xs"><strong>{e.reason}</strong><div className="text-slate-600">{e.notes}</div></td>
            <td className="px-4 py-2 text-xs">{e.lines.map((l) => <div key={l.skuId}>{l.item}: {l.missingQty > 0 && `${l.missingQty} missing `}{l.damagedQty > 0 && `${l.damagedQty} damaged`}</div>)}</td>
            <td className="px-4 py-2 text-xs">{formatDateTime(e.raisedAt)}</td>
            <td className="px-4 py-2"><Badge tone={e.status === "Open" ? "amber" : "green"}>{e.status}</Badge>{e.resolution && <div className="text-xs text-slate-500">{e.resolution}</div>}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
