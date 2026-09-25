import { Alert, Card, Table, formatDateTime } from "@/components/ui";
import { getResellerMe, resellerFetch } from "@/lib/backend";
import { type Deposit, type LedgerEntry, inr } from "@/lib/types";
import { DepositForm } from "./deposit-form";

export default async function Wallet() {
  const me = (await getResellerMe())!;
  const [ledger, deposits] = await Promise.all([
    resellerFetch<{ balance: number; entries: LedgerEntry[] }>("wallet?limit=100"),
    resellerFetch<Deposit[]>("wallet/deposits"),
  ]);
  if (!ledger.ok) return <Alert>{ledger.title}</Alert>;
  return (
    <div className="space-y-6">
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Wallet balance"><p className="text-3xl font-semibold">{inr(ledger.data.balance)}</p><p className="mt-1 text-xs text-slate-500">Prepaid. Orders are paid from this balance, including ₹100 shipping per order.</p></Card>
        <Card title="Add money" className="lg:col-span-2">{me.canPlaceOrders ? <DepositForm /> : <p className="text-sm text-slate-600">Deposits are paused while your account is {me.status}.</p>}</Card>
      </div>
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
