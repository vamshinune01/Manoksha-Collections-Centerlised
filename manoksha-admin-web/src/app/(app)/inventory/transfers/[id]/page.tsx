import Link from "next/link";
import { Alert, Badge, Card, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, canAt } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Transfer } from "@/lib/types";
import { TRANSFER_TONE } from "../../inventory-nav";
import { TransferActions } from "./transfer-actions";

export default async function TransferPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  const transfer = await backendFetch<Transfer>(`admin/inventory/transfers/${id}`);
  if (!transfer.ok) return <Alert>{transfer.problem.title}</Alert>;
  const t = transfer.data;
  const perms = {
    approve: canAt(me, P.transfersApprove, t.sourceBranchId) && (t.requestedBy !== me.userId || me.isOwner),
    dispatch: canAt(me, P.transfersDispatch, t.sourceBranchId),
    receive: canAt(me, P.transfersReceive, t.destinationBranchId),
    cancel: t.requestedBy === me.userId || canAt(me, P.transfersApprove, t.sourceBranchId),
  };

  return (
    <>
      <PageHeader title={t.number} description={`${t.sourceBranchName} → ${t.destinationBranchName} · ${t.reason}`}
        actions={<Link href="/inventory/transfers" className="text-sm text-brand-700 hover:underline">← Transfers</Link>} />
      <div className="mb-6 flex flex-wrap items-center gap-3 text-sm text-slate-600">
        <Badge tone={TRANSFER_TONE[t.status]}>{t.status}</Badge>
        <span>Requested {formatDateTime(t.requestedAt)}</span>
        {t.approvedAt && <span>· {t.status === "Rejected" ? "Rejected" : "Approved"} {formatDateTime(t.approvedAt)}</span>}
        {t.ownerSelfAuthorized && <Badge tone="brand">Owner initiated &amp; authorized</Badge>}
        {t.dispatchedAt && <span>· Dispatched {formatDateTime(t.dispatchedAt)}</span>}
        {t.receivedAt && <span>· Received {formatDateTime(t.receivedAt)}</span>}
        {t.decisionNote && <span>· Note: {t.decisionNote}</span>}
      </div>
      <Card title="Lines">
        <Table head={["SKU", "Requested", "Prepared", "Dispatched", "Received", "Outstanding"]}>
          {t.lines.map((l) => (
            <tr key={l.id}>
              <td className="px-4 py-2">{l.productName} · {l.variantName} <span className="font-mono text-xs text-slate-400">{l.skuCode}</span></td>
              <td className="px-4 py-2">{l.requestedQty}</td><td className="px-4 py-2">{l.preparedQty}</td><td className="px-4 py-2">{l.dispatchedQty}</td>
              <td className="px-4 py-2">{l.receivedQty}</td><td className="px-4 py-2">{l.outstandingQty > 0 ? <Badge tone="red">{l.outstandingQty}</Badge> : 0}</td>
            </tr>
          ))}
        </Table>
        <TransferActions transfer={t} can={perms} />
        {t.status === "Discrepancy" && <p className="mt-4 text-sm text-red-700">Received with a shortfall. Resolve it under <Link className="underline" href="/inventory/discrepancies">Discrepancies</Link>.</p>}
      </Card>
    </>
  );
}
