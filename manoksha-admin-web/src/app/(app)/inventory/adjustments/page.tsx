import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, canAt, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Adjustment, Branch } from "@/lib/types";
import { InventoryNav, STATUS_LABEL } from "../inventory-nav";
import { AdjustmentDecision, RequestAdjustmentForm } from "./adjustment-forms";

export default async function AdjustmentsPage() {
  const me = (await getMe())!;
  if (!can(me, P.adjustRequest) && !can(me, P.adjustApprove) && !me.isOwner) return <Forbidden what="adjustments" />;
  const [adjustments, branches] = await Promise.all([backendFetch<Adjustment[]>("admin/inventory/adjustments"), backendFetch<Branch[]>("admin/branches")]);
  if (!adjustments.ok) return <Alert>{adjustments.problem.title}</Alert>;
  const requestable = (branches.ok ? branches.data : []).filter((b) => b.isActive && canAt(me, P.adjustRequest, b.id));
  return (
    <>
      <PageHeader title="Inventory" description="Every correction needs approval by a different person. Above the Owner-configured value limit, only the Owner can approve." />
      <InventoryNav active="adjustments" />
      {requestable.length > 0 && <RequestAdjustmentForm branches={requestable} />}
      <Table head={["No.", "Branch", "SKU", "Change", "Qty", "Value", "Reason", "Status", ""]} empty={adjustments.data.length === 0}>
        {adjustments.data.map((a) => (
          <tr key={a.id} className="align-top">
            <td className="px-4 py-2 font-mono text-xs">{a.number}</td>
            <td className="px-4 py-2">{a.branchName}</td>
            <td className="px-4 py-2">{a.productName} <span className="font-mono text-xs text-slate-400">{a.skuCode}</span></td>
            <td className="px-4 py-2 text-xs">{a.kind === "Found" ? "Found stock" : a.kind === "WriteOff" ? `Write off ${STATUS_LABEL[a.fromStatus!] ?? a.fromStatus}` : `${STATUS_LABEL[a.fromStatus!] ?? a.fromStatus} → ${STATUS_LABEL[a.toStatus!] ?? a.toStatus}`}</td>
            <td className="px-4 py-2">{a.quantity}</td>
            <td className="px-4 py-2 text-xs">{a.valueAtCost != null ? inr(a.valueAtCost) : a.estimatedValue != null ? `≈ ${inr(a.estimatedValue)}` : "—"}
              {a.requiresOwner && <div><Badge tone="brand">Owner approval</Badge></div>}</td>
            <td className="px-4 py-2 text-xs">{a.reasonCode}: {a.notes}<div className="text-slate-400">{formatDateTime(a.requestedAt)}</div></td>
            <td className="px-4 py-2"><Badge tone={a.status === "Applied" ? "green" : a.status === "Pending" ? "amber" : "slate"}>{a.status}</Badge></td>
            <td className="px-4 py-2 text-right">{a.status === "Pending" && a.requestedBy !== me.userId && (me.isOwner || canAt(me, P.adjustApprove, a.branchId)) && <AdjustmentDecision adjustment={a} />}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
