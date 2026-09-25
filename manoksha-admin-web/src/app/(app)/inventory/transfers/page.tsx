import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, Transfer } from "@/lib/types";
import { InventoryNav, TRANSFER_TONE } from "../inventory-nav";
import { CreateTransferForm } from "./create-transfer-form";

export default async function TransfersPage() {
  const me = (await getMe())!;
  if (!can(me, P.inventoryView)) return <Forbidden what="transfers" />;
  const [transfers, branches] = await Promise.all([backendFetch<Transfer[]>("admin/inventory/transfers"), backendFetch<Branch[]>("admin/branches")]);
  if (!transfers.ok) return <Alert>{transfers.problem.title}</Alert>;
  return (
    <>
      <PageHeader title="Inventory" description="Requested → approved by the source branch → prepared → in transit → received. Shortfalls become discrepancies." />
      <InventoryNav active="transfers" />
      {can(me, P.transfersCreate) && branches.ok && <CreateTransferForm branches={branches.data.filter((b) => b.isActive)} />}
      <Table head={["Transfer", "From", "To", "Lines", "Status", "Requested"]} empty={transfers.data.length === 0}>
        {transfers.data.map((t) => (
          <tr key={t.id} className="hover:bg-slate-50">
            <td className="px-4 py-2.5"><Link href={`/inventory/transfers/${t.id}`} className="font-mono font-medium text-brand-700 hover:underline">{t.number}</Link></td>
            <td className="px-4 py-2.5">{t.sourceBranchName}</td>
            <td className="px-4 py-2.5">{t.destinationBranchName}</td>
            <td className="px-4 py-2.5">{t.lines.length}</td>
            <td className="px-4 py-2.5"><Badge tone={TRANSFER_TONE[t.status]}>{t.status}</Badge></td>
            <td className="px-4 py-2.5 text-slate-600">{formatDateTime(t.requestedAt)}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
