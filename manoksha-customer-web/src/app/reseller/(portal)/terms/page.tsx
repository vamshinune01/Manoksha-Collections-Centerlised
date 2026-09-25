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
          <p className="text-2xl font-semibold">{t.discountPct}% reseller discount</p>
          <p className="text-sm text-slate-500">From {formatDateTime(t.effectiveFrom)}</p>
          {t.notes && <p className="mt-2 text-sm">{t.notes}</p>}
        </Card>
      ))}
    </div>
  );
}
