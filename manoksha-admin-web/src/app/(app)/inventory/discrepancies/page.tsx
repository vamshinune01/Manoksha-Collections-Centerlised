import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, canAt } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Discrepancy } from "@/lib/types";
import { InventoryNav } from "../inventory-nav";
import { ResolveDiscrepancy } from "./resolve-discrepancy";

export default async function DiscrepanciesPage() {
  const me = (await getMe())!;
  if (!can(me, P.inventoryView)) return <Forbidden what="discrepancies" />;
  const list = await backendFetch<Discrepancy[]>("admin/inventory/discrepancies");
  if (!list.ok) return <Alert>{list.problem.title}</Alert>;
  return (
    <>
      <PageHeader title="Inventory" description="Differences between expected and actual stock never complete silently. Each needs a recorded decision." />
      <InventoryNav active="discrepancies" />
      <Table head={["No.", "Source", "Branch", "SKU", "Expected", "Actual", "Outstanding", "Status", ""]} empty={list.data.length === 0}>
        {list.data.map((d) => (
          <tr key={d.id} className="align-top">
            <td className="px-4 py-2 font-mono text-xs">{d.number}<div className="text-slate-400">{formatDateTime(d.createdAt)}</div></td>
            <td className="px-4 py-2 text-xs">
              {d.sourceType === "TRANSFER" ? <Link className="text-brand-700 underline" href={`/inventory/transfers/${d.sourceId}`}>{d.sourceNumber}</Link>
                : <Link className="text-brand-700 underline" href={`/inventory/counts/${d.sourceId}`}>{d.sourceNumber}</Link>}
            </td>
            <td className="px-4 py-2">{d.branchName}</td>
            <td className="px-4 py-2">{d.productName} <span className="font-mono text-xs text-slate-400">{d.skuCode}</span></td>
            <td className="px-4 py-2">{d.expectedQty}</td><td className="px-4 py-2">{d.actualQty}</td><td className="px-4 py-2">{d.outstandingQty}</td>
            <td className="px-4 py-2"><Badge tone={d.status === "Open" ? "red" : "green"}>{d.status}</Badge>{d.resolution && <div className="text-xs text-slate-500">{d.resolution}</div>}</td>
            <td className="px-4 py-2 text-right">{d.status === "Open" && canAt(me, P.discrepancyResolve, d.branchId) && <ResolveDiscrepancy discrepancy={d} />}
              {d.status === "Open" && d.sourceType === "COUNT" && <p className="text-xs text-slate-500">Or request an adjustment linked to it.</p>}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
