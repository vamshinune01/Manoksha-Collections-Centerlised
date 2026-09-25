import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Deposit } from "@/lib/types";
import { DepositDecision } from "./deposit-decision";

type Search = Record<string, string | string[] | undefined>;

export default async function DepositsPage({ searchParams }: { searchParams: Promise<Search> }) {
  const me = (await getMe())!;
  if (!can(me, P.walletView)) return <Forbidden what="wallet deposits" />;
  const status = typeof (await searchParams).status === "string" ? String((await searchParams).status) : "Pending";
  const deposits = await backendFetch<Deposit[]>(`admin/wallet/deposits${status === "All" ? "" : `?status=${status}`}`);
  if (!deposits.ok) return <Alert>{deposits.problem.title}</Alert>;
  return (
    <>
      <PageHeader title="Wallet deposits" description="Direct/PhonePe deposits are credited only after the Owner checks the proof. Each request can be credited at most once." />
      <nav className="mb-4 flex gap-3 text-sm">
        {["Pending", "Credited", "Rejected", "All"].map((s) => (
          <Link key={s} href={`/wallet-deposits?status=${s}`} className={s === status ? "font-semibold text-brand-700" : "text-slate-600"}>{s}</Link>
        ))}
      </nav>
      <Table head={["Request", "Reseller", "Amount", "Method / reference", "Submitted", "Status", ""]} empty={deposits.data.length === 0}>
        {deposits.data.map((d) => (
          <tr key={d.id} className="align-top">
            <td className="px-4 py-2.5 font-mono text-xs">{d.number}</td>
            <td className="px-4 py-2.5"><Link className="text-brand-700 hover:underline" href={`/resellers/${d.resellerId}`}>{d.resellerName}</Link>
              <div className="font-mono text-xs text-slate-400">{d.resellerNumber}</div></td>
            <td className="px-4 py-2.5 font-semibold">{inr(d.amount)}</td>
            <td className="px-4 py-2.5 text-xs">{d.method}<div className="font-mono">{d.reference}</div>{d.resellerNote && <div className="text-slate-500">{d.resellerNote}</div>}</td>
            <td className="px-4 py-2.5 text-xs text-slate-600">{formatDateTime(d.submittedAt)}</td>
            <td className="px-4 py-2.5"><Badge tone={d.status === "Pending" ? "amber" : d.status === "Rejected" ? "red" : "green"}>{d.status}</Badge>
              {d.reviewNote && <div className="text-xs text-slate-500">{d.reviewNote}</div>}</td>
            <td className="px-4 py-2.5 text-right">
              <a href={`/api/backend/admin/wallet/deposits/${d.id}/proof`} target="_blank" rel="noreferrer" className="mr-2 text-sm text-brand-700 underline">Proof</a>
              {d.status === "Pending" && can(me, P.depositApprove) && <DepositDecision deposit={d} />}
            </td>
          </tr>
        ))}
      </Table>
    </>
  );
}
