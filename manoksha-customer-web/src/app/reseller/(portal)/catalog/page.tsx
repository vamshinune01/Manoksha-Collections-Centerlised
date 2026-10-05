import Link from "next/link";
import { Alert, Button, Input, Select } from "@/components/ui";
import { resellerFetch, storeFetch } from "@/lib/backend";
import { type CatalogPage, type StoreVendor, inr } from "@/lib/types";
import { AddToCart } from "./add-to-cart";
import { ProductImage } from "@/components/product-image";

type Search = Record<string, string | string[] | undefined>;

export default async function Catalog({ searchParams }: { searchParams: Promise<Search> }) {
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q : "";
  const vendor = typeof sp.vendor === "string" ? sp.vendor : "";
  const page = typeof sp.page === "string" ? Number(sp.page) : 1;
  const [result, vendors] = await Promise.all([
    resellerFetch<CatalogPage>(`catalog?${new URLSearchParams({ ...(q && { q }), ...(vendor && { vendorId: vendor }), page: String(page), pageSize: "24" })}`),
    storeFetch<StoreVendor[]>("vendors"),
  ]);
  if (!result.ok) return <Alert>{result.title}</Alert>;
  const pages = Math.max(1, Math.ceil(result.data.total / result.data.pageSize));
  const link = (p: number) => `?${new URLSearchParams({ ...(q && { q }), ...(vendor && { vendor }), page: String(p) })}`;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h1 className="text-xl font-semibold">Catalog</h1>
        <form method="get" className="flex flex-wrap gap-2">
          {vendors.ok && vendors.data.length > 0 && (
            <Select name="vendor" defaultValue={vendor} aria-label="Brand">
              <option value="">All brands</option>
              {vendors.data.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
            </Select>
          )}
          <Input name="q" defaultValue={q} placeholder="Product, ID (ZR-000123) or SKU" />
          <Button type="submit" variant="secondary">Search</Button>
        </form>
      </div>
      <p className="text-sm text-slate-500">Your price is your own discount for each brand, set in your reseller terms. <Link href="/reseller/terms" className="text-brand-700 hover:underline">See your discounts</Link>.</p>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {result.data.items.map((i) => (
          <div key={i.skuId} className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
            <ProductImage image={i.image} alt={i.productName} sizes="(min-width: 1024px) 33vw, (min-width: 640px) 50vw, 100vw" className="mb-3 aspect-[4/3] w-full rounded-md" />
            <p className="text-xs uppercase tracking-wide text-slate-400">{i.vendorName ?? i.categoryName}</p>
            <p className="mt-1 font-medium text-slate-900">{i.productName}</p>
            <p className="text-sm text-slate-600">{i.variantName} <span className="font-mono text-xs text-slate-400">{i.productCode ?? i.skuCode}</span></p>
            <div className="mt-3 flex items-baseline gap-2">
              <span className="text-lg font-semibold text-brand-700">{inr(i.resellerPrice)}</span>
              {i.discountPct > 0 && <span className="text-sm text-slate-400 line-through">{inr(i.retailPrice)}</span>}
              {i.discountPct > 0 && <span className="text-xs text-emerald-700">{i.discountPct}% off{i.discountSource === "PRODUCT_RESELLER" ? " (special)" : ""}</span>}
            </div>
            {i.outOfStock ? <p className="mt-3 text-sm font-medium text-amber-700">Out of stock</p> : <AddToCart skuId={i.skuId} name={`${i.productName} · ${i.variantName}`} />}
          </div>
        ))}
      </div>
      {result.data.items.length === 0 && <p className="text-sm text-slate-500">No products found.</p>}
      {pages > 1 && (
        <div className="flex justify-end gap-3 text-sm">
          {page > 1 && <a className="text-brand-700" href={link(page - 1)}>← Previous</a>}
          <span className="text-slate-500">Page {page} of {pages}</span>
          {page < pages && <a className="text-brand-700" href={link(page + 1)}>Next →</a>}
        </div>
      )}
    </div>
  );
}
