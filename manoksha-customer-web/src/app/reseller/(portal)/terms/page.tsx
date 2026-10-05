import { Alert, Card, formatDateTime } from "@/components/ui";
import { resellerFetch } from "@/lib/backend";
import type { Term } from "@/lib/types";

export default async function Terms() {
  const terms = await resellerFetch<Term[]>("commercial-terms");
  if (!terms.ok) return <Alert>{terms.title}</Alert>;
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">My commercial terms</h1>
      <p className="text-sm text-slate-500">Changes apply to new orders only. Past orders keep the prices you paid.</p>
      {terms.data.map((t) => (
        <Card key={t.version} title={`Version ${t.version}${t.isCurrent ? " · current" : ""}`}>
          <p className="text-sm text-slate-500">From {formatDateTime(t.effectiveFrom)}</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {(t.vendorDiscounts ?? []).map((v) => (
              <span key={v.vendorId} className="rounded-full bg-brand-50 px-3 py-1 text-sm text-brand-900">{v.vendorName}: <strong>{v.discountPct}% off</strong></span>
            ))}
            {(t.vendorDiscounts ?? []).length === 0 && <span className="text-sm text-slate-600">No brand discounts yet — you pay the retail price.</span>}
          </div>
          {t.discountPct > 0 && <p className="mt-2 text-sm text-slate-600">Other products: {t.discountPct}% off</p>}
          <p className="mt-2 text-xs text-slate-500">Brands not listed: full retail price.</p>
          {t.notes && <p className="mt-2 text-sm">{t.notes}</p>}
        </Card>
      ))}
    </div>
  );
}
