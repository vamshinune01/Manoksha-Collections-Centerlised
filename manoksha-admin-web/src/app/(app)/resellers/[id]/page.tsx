import Link from "next/link";
import { Alert, Badge, Card, PageHeader, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { LedgerPage, ResellerDetail } from "@/lib/types";
import { RESELLER_TONE } from "../reseller-status";
import { PricePreview, ResellerStatusActions, TermsForm, WalletAdjustment } from "./reseller-actions";

export default async function ResellerPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  const [reseller, ledger] = await Promise.all([
    backendFetch<ResellerDetail>(`admin/resellers/${id}`),
    can(me, P.walletView) ? backendFetch<LedgerPage>(`admin/wallet/resellers/${id}/ledger?limit=20`) : Promise.resolve(null),
  ]);
  if (!reseller.ok) return <Alert>{reseller.problem.title}</Alert>;
  const r = reseller.data;
  const manage = can(me, P.resellersManage);
  return (
    <>
      <PageHeader title={r.profile.businessName ?? r.profile.contactName} description={`${r.resellerNumber} · ${r.profile.contactName} · ${r.mobile}`}
        actions={<Link href="/resellers" className="text-sm text-brand-700 hover:underline">← Resellers</Link>} />
      <div className="mb-6 flex flex-wrap items-center gap-3">
        <Badge tone={RESELLER_TONE[r.status]}>{r.status}</Badge>
        {r.status === "Pending" && <span className="text-sm text-amber-800">Waiting for the reseller to verify their mobile number by OTP.</span>}
        {manage && <ResellerStatusActions reseller={r} />}
      </div>
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Commercial terms" className="lg:col-span-2">
          <p className="text-3xl font-semibold text-slate-900">{r.currentTerms.discountPct}% <span className="text-sm font-normal text-slate-500">reseller discount · version {r.currentTerms.version}</span></p>
          <ol className="mt-4 space-y-2 text-sm">
            {r.termsHistory.map((t) => (
              <li key={t.id} className={t.isCurrent ? "font-medium" : "text-slate-600"}>
                v{t.version}: {t.discountPct}% from {formatDateTime(t.effectiveFrom)} — {t.reason}{t.notes ? ` (${t.notes})` : ""}
              </li>
            ))}
          </ol>
          {manage && r.status !== "Closed" && <TermsForm reseller={r} />}
        </Card>
        <div className="space-y-6">
          <Card title="Wallet">
            <p className="text-2xl font-semibold">{inr(r.walletBalance)}</p>
            {ledger?.ok && (
              <ul className="mt-3 space-y-1 text-xs">
                {ledger.data.entries.map((e) => (
                  <li key={e.id} className="flex justify-between gap-2">
                    <span>{formatDateTime(e.createdAt)} · {e.type}{e.orderNumber ? ` ${e.orderNumber}` : ""}{e.reason && !e.orderNumber ? ` · ${e.reason}` : ""}</span>
                    <span className={e.direction === "Credit" ? "text-emerald-700" : "text-red-700"}>{e.direction === "Credit" ? "+" : "−"}{inr(e.amount)} → {inr(e.balanceAfter)}</span>
                  </li>
                ))}
                {ledger.data.entries.length === 0 && <li className="text-slate-500">No transactions yet.</li>}
              </ul>
            )}
            {can(me, P.walletAdjust) && <WalletAdjustment resellerId={r.id} />}
          </Card>
          <Card title="Profile">
            <dl className="space-y-1 text-sm">
              <div><dt className="inline text-slate-500">Email: </dt><dd className="inline">{r.profile.email}</dd></div>
              <div><dt className="inline text-slate-500">Address: </dt><dd className="inline">{r.profile.addressLine}, {r.profile.city}, {r.profile.state} {r.profile.pin}</dd></div>
              {r.profile.notes && <div><dt className="inline text-slate-500">Notes: </dt><dd className="inline">{r.profile.notes}</dd></div>}
              <div><dt className="inline text-slate-500">Activated: </dt><dd className="inline">{formatDateTime(r.activatedAt)}</dd></div>
            </dl>
          </Card>
          <Card title="Status history">
            <ul className="space-y-1 text-xs text-slate-600">
              {r.statusHistory.map((s) => <li key={s.occurredAt}>{formatDateTime(s.occurredAt)} · {s.fromStatus ?? "—"} → <strong>{s.toStatus}</strong> · {s.reason}</li>)}
            </ul>
          </Card>
          {r.status !== "Pending" && <Card title="Price preview"><PricePreview resellerId={r.id} /></Card>}
        </div>
      </div>
    </>
  );
}
