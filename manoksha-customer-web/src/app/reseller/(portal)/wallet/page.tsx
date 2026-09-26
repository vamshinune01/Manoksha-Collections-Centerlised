import { Alert, Card, Table, formatDateTime } from "@/components/ui";
import { getResellerMe, resellerFetch } from "@/lib/backend";
import { type Deposit, type LedgerEntry, type OnlineDeposit, inr } from "@/lib/types";
import { DepositForm } from "./deposit-form";
import { OnlineDepositForm, PendingDepositWatcher } from "./online-deposit";

const ONLINE_STATUS: Record<OnlineDeposit["status"], string> = {
  Pending: "Waiting for payment",
  Credited: "Credited",
  Failed: "Not completed",
  Expired: "Not completed (time ran out)",
};

export default async function Wallet() {
  const me = (await getResellerMe())!;
  const [ledger, deposits, online] = await Promise.all([
    resellerFetch<{ balance: number; entries: LedgerEntry[] }>("wallet?limit=100"),
    resellerFetch<Deposit[]>("wallet/deposits"),
    resellerFetch<OnlineDeposit[]>("wallet/deposits/online"),
  ]);
  const pending = online.ok ? online.data.filter((d) => d.status === "Pending").map((d) => d.id) : [];
  if (!ledger.ok) return <Alert>{ledger.title}</Alert>;
  return (
    <div className="space-y-6">
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Wallet balance"><p className="text-3xl font-semibold">{inr(ledger.data.balance)}</p><p className="mt-1 text-xs text-slate-500">Prepaid. Orders are paid from this balance, including ₹100 shipping per order.</p></Card>
        <Card title="Add money" className="lg:col-span-2">
          {me.canPlaceOrders ? (
            <div className="space-y-6">
              <div className="space-y-2">
                <h3 className="text-sm font-semibold text-slate-800">Pay online with UPI</h3>
                <p className="text-sm text-slate-600">Credited automatically as soon as the payment is confirmed.</p>
                <OnlineDepositForm />
              </div>
              <details className="border-t border-slate-100 pt-4">
                <summary className="cursor-pointer text-sm font-semibold text-slate-800">Already paid by PhonePe or bank transfer? Submit the proof</summary>
                <div className="mt-3"><DepositForm /></div>
              </details>
            </div>
          ) : <p className="text-sm text-slate-600">Deposits are paused while your account is {me.status}.</p>}
        </Card>
      </div>
      <PendingDepositWatcher ids={pending} />
      {online.ok && online.data.length > 0 && (
        <Card title="Online UPI deposits">
          <Table head={["Deposit", "Amount", "Started", "Status", "UPI reference"]}>
            {online.data.map((d) => (
              <tr key={d.id}>
                <td className="px-4 py-2 font-mono text-xs">{d.number}</td>
                <td className="px-4 py-2">{inr(d.amount)}</td>
                <td className="px-4 py-2 text-xs">{formatDateTime(d.createdAt)}</td>
                <td className="px-4 py-2">
                  {ONLINE_STATUS[d.status]}
                  {d.status === "Pending" && d.payment?.redirectUrl && <a className="ml-2 text-xs text-brand-700 underline" href={d.payment.redirectUrl}>Pay now</a>}
                </td>
                <td className="px-4 py-2 font-mono text-xs">{d.providerPaymentRef ?? "—"}</td>
              </tr>
            ))}
          </Table>
        </Card>
      )}
      {deposits.ok && deposits.data.length > 0 && (
        <Card title="Deposit requests">
          <Table head={["Request", "Amount", "Reference", "Submitted", "Status", "Proof"]}>
            {deposits.data.map((d) => (
              <tr key={d.id}>
                <td className="px-4 py-2 font-mono text-xs">{d.number}</td>
                <td className="px-4 py-2">{inr(d.amount)}</td>
                <td className="px-4 py-2 font-mono text-xs">{d.method} {d.reference}</td>
                <td className="px-4 py-2 text-xs">{formatDateTime(d.submittedAt)}</td>
                <td className="px-4 py-2">{d.status === "Credited" ? "Credited" : d.status}{d.reviewNote && <div className="text-xs text-slate-500">{d.reviewNote}</div>}</td>
                <td className="px-4 py-2"><a className="text-sm text-brand-700 underline" target="_blank" rel="noreferrer" href={`/api/rs/wallet/deposits/${d.id}/proof`}>View</a></td>
              </tr>
            ))}
          </Table>
        </Card>
      )}
      <Card title="Transactions">
        <Table head={["When", "Type", "Details", "Amount", "Balance"]} empty={ledger.data.entries.length === 0}>
          {ledger.data.entries.map((e) => (
            <tr key={e.id}>
              <td className="px-4 py-2 text-xs">{formatDateTime(e.createdAt)}</td>
              <td className="px-4 py-2">{e.type}</td>
              <td className="px-4 py-2 text-xs">{e.orderNumber ?? e.reason}</td>
              <td className={`px-4 py-2 ${e.direction === "Credit" ? "text-emerald-700" : "text-red-700"}`}>{e.direction === "Credit" ? "+" : "−"}{inr(e.amount)}</td>
              <td className="px-4 py-2">{inr(e.balanceAfter)}</td>
            </tr>
          ))}
        </Table>
      </Card>
    </div>
  );
}
