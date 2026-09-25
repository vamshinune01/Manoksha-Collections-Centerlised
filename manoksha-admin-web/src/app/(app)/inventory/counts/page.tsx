import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, canAt } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, StockCount } from "@/lib/types";
import { InventoryNav } from "../inventory-nav";
import { StartCountForm } from "./count-forms";

export default async function CountsPage() {
  const me = (await getMe())!;
  if (!can(me, P.inventoryCount)) return <Forbidden what="stock counts" />;
  const [counts, branches] = await Promise.all([backendFetch<StockCount[]>("admin/inventory/counts"), backendFetch<Branch[]>("admin/branches")]);
  if (!counts.ok) return <Alert>{counts.problem.title}</Alert>;
  const countable = (branches.ok ? branches.data : []).filter((b) => b.isActive && canAt(me, P.inventoryCount, b.id));
  return (
    <>
      <PageHeader title="Inventory" description="Blind counts: record what is physically on the shelf. Differences become discrepancies after submission." />
      <InventoryNav active="counts" />
      <StartCountForm branches={countable} />
      <Table head={["Count", "Branch", "SKUs", "Status", "Started", "Differences"]} empty={counts.data.length === 0}>
        {counts.data.map((c) => (
          <tr key={c.id} className="hover:bg-slate-50">
            <td className="px-4 py-2.5"><Link href={`/inventory/counts/${c.id}`} className="font-mono font-medium text-brand-700 hover:underline">{c.number}</Link></td>
            <td className="px-4 py-2.5">{c.branchName}</td>
            <td className="px-4 py-2.5">{c.lines.length}</td>
            <td className="px-4 py-2.5"><Badge tone={c.status === "Open" ? "amber" : "green"}>{c.status}</Badge></td>
            <td className="px-4 py-2.5 text-slate-600">{formatDateTime(c.createdAt)}</td>
            <td className="px-4 py-2.5">{c.status === "Submitted" ? c.discrepancyIds.length : "—"}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
