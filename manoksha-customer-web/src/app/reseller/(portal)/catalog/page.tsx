import { Alert, Button, Input } from "@/components/ui";
import { resellerFetch } from "@/lib/backend";
import { type CatalogPage, inr } from "@/lib/types";
import { AddToCart } from "./add-to-cart";

type Search = Record<string, string | string[] | undefined>;

export default async function Catalog({ searchParams }: { searchParams: Promise<Search> }) {
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q : "";
  const page = typeof sp.page === "string" ? Number(sp.page) : 1;
  const result = await resellerFetch<CatalogPage>(`catalog?${new URLSearchParams({ ...(q && { q }), page: String(page), pageSize: "24" })}`);
  if (!result.ok) return <Alert>{result.title}</Alert>;
  const pages = Math.max(1, Math.ceil(result.data.total / result.data.pageSize));
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h1 className="text-xl font-semibold">Catalog</h1>
        <form method="get" className="flex gap-2"><Input name="q" defaultValue={q} placeholder="Search products or SKU" /><Button type="submit" variant="secondary">Search</Button></form>
      </div>
      <p className="text-sm text-slate-500">Your price is calculated by Manoksha Collections from your reseller terms. Availability is confirmed at checkout.</p>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {result.data.items.map((i) => (
          <div key={i.skuId} className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
            <p className="text-xs uppercase tracking-wide text-slate-400">{i.categoryName}</p>
            <p className="mt-1 font-medium text-slate-900">{i.productName}</p>
            <p className="text-sm text-slate-600">{i.variantName} <span className="font-mono text-xs text-slate-400">{i.skuCode}</span></p>
            <div className="mt-3 flex items-baseline gap-2">
              <span className="text-lg font-semibold text-brand-700">{inr(i.resellerPrice)}</span>
              <span className="text-sm text-slate-400 line-through">{inr(i.retailPrice)}</span>
              <span className="text-xs text-emerald-700">{i.discountPct}% off{i.discountSource === "PRODUCT_RESELLER" ? " (special)" : ""}</span>
            </div>
            <AddToCart skuId={i.skuId} name={`${i.productName} · ${i.variantName}`} />
          </div>
        ))}
      </div>
      {result.data.items.length === 0 && <p className="text-sm text-slate-500">No products found.</p>}
      {pages > 1 && (
        <div className="flex justify-end gap-3 text-sm">
          {page > 1 && <a className="text-brand-700" href={`?${new URLSearchParams({ q, page: String(page - 1) })}`}>← Previous</a>}
          <span className="text-slate-500">Page {page} of {pages}</span>
          {page < pages && <a className="text-brand-700" href={`?${new URLSearchParams({ q, page: String(page + 1) })}`}>Next →</a>}
        </div>
      )}
    </div>
  );
}
