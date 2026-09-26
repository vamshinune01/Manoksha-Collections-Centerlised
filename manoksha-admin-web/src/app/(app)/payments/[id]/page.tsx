import Link from "next/link";
import { Alert, Badge, Card, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { PaymentReconciliation } from "@/lib/types";
import { ReconciliationActions } from "./reconciliation-actions";

interface HistoryRow { action: string; fromStatus: string; toStatus: string; note: string; externalRefundRef: string | null; occurredAt: string }

/** One payment reconciliation case (SPEC §36). Refunds are made outside the application; this records what was done. */
export default async function ReconciliationPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  const [rec, history] = await Promise.all([
    backendFetch<PaymentReconciliation>(`admin/payment-reconciliations/${id}`),
    backendFetch<HistoryRow[]>(`admin/payment-reconciliations/${id}/history`),
  ]);
  if (!rec.ok) return <Alert>{rec.problem.title}</Alert>;
  const c = rec.data;
  return (
    <>
      <PageHeader title={c.caseNumber} description={c.reasonCode.replaceAll("_", " ").toLowerCase()}
        actions={<Link href="/payments" className="text-sm text-brand-700 hover:underline">← Online payments</Link>} />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Payment" className="lg:col-span-2">
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div><dt className="text-slate-500">Status</dt><dd><Badge tone={c.status === "Open" ? "red" : c.status === "Resolved" ? "green" : "amber"}>{c.status}</Badge></dd></div>
            <div><dt className="text-slate-500">For</dt><dd>{c.purpose === "ORDER" ? <Link className="text-brand-700 hover:underline" href={`/orders/${c.referenceId}`}>{c.referenceNumber}</Link> : c.referenceNumber}</dd></div>
            <div><dt className="text-slate-500">Amount due</dt><dd>{inr(c.expectedAmount)}</dd></div>
            <div><dt className="text-slate-500">Amount received</dt><dd className="font-semibold">{inr(c.paidAmount)}</dd></div>
            <div><dt className="text-slate-500">Provider order</dt><dd className="font-mono text-xs">{c.provider} · {c.providerOrderRef ?? "—"}</dd></div>
            <div><dt className="text-slate-500">Provider payment ref</dt><dd className="font-mono text-xs">{c.providerPaymentRef ?? "—"}</dd></div>
            <div><dt className="text-slate-500">External refund ref</dt><dd className="font-mono text-xs">{c.externalRefundRef ?? "—"}</dd></div>
            <div><dt className="text-slate-500">Opened</dt><dd>{formatDateTime(c.createdAt)}</dd></div>
            <div className="sm:col-span-2"><dt className="text-slate-500">Why</dt><dd>{c.detail ?? c.reasonCode}</dd></div>
            {c.ownerAction && <div className="sm:col-span-2"><dt className="text-slate-500">Latest Owner action</dt><dd>{c.ownerAction}</dd></div>}
            {c.notes && <div className="sm:col-span-2"><dt className="text-slate-500">Latest note</dt><dd>{c.notes}</dd></div>}
          </dl>
        </Card>
        <Card title="Record an action">
          {can(me, P.reconciliationManage)
            ? <ReconciliationActions id={c.id} status={c.status} />
            : <p className="text-sm text-slate-600">Only the Owner records reconciliation actions.</p>}
        </Card>
      </div>
      <div className="mt-6">
        <Card title="History">
          {history.ok && (
            <Table head={["When", "Action", "Status", "Note", "Refund ref"]} empty={history.data.length === 0}>
              {history.data.map((h) => (
                <tr key={h.occurredAt + h.action}>
                  <td className="px-4 py-2 text-xs">{formatDateTime(h.occurredAt)}</td>
                  <td className="px-4 py-2 text-xs">{h.action.replaceAll("_", " ")}</td>
                  <td className="px-4 py-2 text-xs">{h.fromStatus === h.toStatus ? h.toStatus : `${h.fromStatus} → ${h.toStatus}`}</td>
                  <td className="px-4 py-2 text-sm">{h.note}</td>
                  <td className="px-4 py-2 font-mono text-xs">{h.externalRefundRef ?? ""}</td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      </div>
    </>
  );
}
