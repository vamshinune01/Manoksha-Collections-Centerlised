import Link from "next/link";
import { Alert, Badge, Card, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import { PAYMENT_TONE, type PaymentAttempt, type PaymentReconciliation } from "@/lib/types";

/**
 * Online UPI payments and the minimum reconciliation safeguard (SPEC §14.2, §36): money received that could not be applied is
 * never silently kept — each case is listed here, and the Owner records actions and refund markers on the case page.
 */
export default async function PaymentsPage({ searchParams }: { searchParams: Promise<{ status?: string }> }) {
  const me = (await getMe())!;
  if (!can(me, P.exceptionsView)) return <Forbidden what="online payments" />;
  const { status } = await searchParams;
  const [cases, attempts] = await Promise.all([
    backendFetch<PaymentReconciliation[]>("admin/payment-reconciliations"),
    backendFetch<PaymentAttempt[]>(`admin/payments${status ? `?status=${encodeURIComponent(status)}` : ""}`),
  ]);
  if (!cases.ok) return <Alert>{cases.problem.title}</Alert>;
  if (!attempts.ok) return <Alert>{attempts.problem.title}</Alert>;
  const filters = ["", "PENDING", "SUCCESS", "ORDER_RECOVERED", "FAILED", "EXPIRED", "PAYMENT_RECONCILIATION_REQUIRED"];

  return (
    <>
      <PageHeader title="Online payments" description="UPI payments for online orders and reseller wallet deposits. Provider webhooks and polling keep these up to date." />
      <Card title={`Payment reconciliation (${cases.data.filter((c) => c.status === "Open").length} open)`} className="mb-6">
        {cases.data.length === 0 ? (
          <p className="text-sm text-slate-500">No payments need reconciliation.</p>
        ) : (
          <Table head={["Case", "Reason", "For", "Expected", "Paid", "Provider ref", "Status", "Opened"]}>
            {cases.data.map((c) => (
              <tr key={c.id}>
                <td className="px-4 py-2 font-mono text-xs"><Link className="text-brand-700 hover:underline" href={`/payments/${c.id}`}>{c.caseNumber}</Link></td>
                <td className="px-4 py-2 text-xs">{c.reasonCode}{c.detail && <div className="text-slate-500">{c.detail}</div>}</td>
                <td className="px-4 py-2 text-xs">
                  {c.purpose === "ORDER" ? <Link className="text-brand-700 hover:underline" href={`/orders/${c.referenceId}`}>{c.referenceNumber}</Link> : c.referenceNumber}
                </td>
                <td className="px-4 py-2">{inr(c.expectedAmount)}</td>
                <td className="px-4 py-2">{inr(c.paidAmount)}</td>
                <td className="px-4 py-2 font-mono text-xs">{c.providerPaymentRef ?? c.providerOrderRef}</td>
                <td className="px-4 py-2"><Badge tone={c.status === "Open" ? "red" : "green"}>{c.status}</Badge></td>
                <td className="px-4 py-2 text-xs">{formatDateTime(c.createdAt)}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      <div className="mb-3 flex flex-wrap gap-2 text-xs">
        {filters.map((f) => (
          <Link key={f || "all"} href={f ? `/payments?status=${f}` : "/payments"}
            className={`rounded-full border px-3 py-1 ${(status ?? "") === f ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white text-slate-700"}`}>
            {f || "All"}
          </Link>
        ))}
      </div>
      <Table head={["For", "Purpose", "Amount", "Status", "Provider refs", "Started", "Window ends", "Completed"]} empty={attempts.data.length === 0}>
        {attempts.data.map((a) => (
          <tr key={a.id}>
            <td className="px-4 py-2 text-xs">
              {a.purpose === "ORDER" ? <Link className="font-mono text-brand-700 hover:underline" href={`/orders/${a.referenceId}`}>{a.referenceNumber}</Link> : <span className="font-mono">{a.referenceNumber}</span>}
            </td>
            <td className="px-4 py-2 text-xs">{a.purpose === "ORDER" ? "Online order" : "Wallet deposit"}</td>
            <td className="px-4 py-2">{inr(a.amount)}</td>
            <td className="px-4 py-2"><Badge tone={PAYMENT_TONE[a.status] ?? "slate"}>{a.status}</Badge>{a.failureReason && <div className="text-xs text-slate-500">{a.failureReason}</div>}</td>
            <td className="px-4 py-2 font-mono text-xs">{a.providerOrderRef ?? "—"}{a.providerPaymentRef && <div>{a.providerPaymentRef}</div>}</td>
            <td className="px-4 py-2 text-xs">{formatDateTime(a.initiatedAt)}</td>
            <td className="px-4 py-2 text-xs">{formatDateTime(a.expiresAt)}</td>
            <td className="px-4 py-2 text-xs">{formatDateTime(a.completedAt)}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
