import Link from "next/link";
import { storeFetch } from "@/lib/backend";
import { inr, type StoreCategory, type StorePage, type StoreVendor } from "@/lib/types";
import { AddToCart } from "@/components/store-client";
import { ProductImage } from "@/components/product-image";
import { Alert, cx } from "@/components/ui";

export const metadata = { title: "Shop" };

export default async function StoreHome({ searchParams }: { searchParams: Promise<{ q?: string; category?: string; vendor?: string; page?: string }> }) {
  const { q, category, vendor, page } = await searchParams;
  const current = Math.max(1, Number(page) || 1);
  const params = new URLSearchParams({ page: String(current), pageSize: "24" });
  if (q) params.set("q", q);
  if (category) params.set("categoryId", category);
  if (vendor) params.set("vendorId", vendor);
  const [items, categories, vendors] = await Promise.all([
    storeFetch<StorePage>(`products?${params}`), storeFetch<StoreCategory[]>("categories"), storeFetch<StoreVendor[]>("vendors"),
  ]);
  const link = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams({ ...(q ? { q } : {}), ...(category ? { category } : {}), ...(vendor ? { vendor } : {}), ...patch } as Record<string, string>);
    for (const [k, v] of [...next.entries()]) if (!v) next.delete(k);
    return `/?${next}`;
  };

  return (
    <div className="space-y-6">
      {!q && !category && !vendor && (
        <section className="rounded-2xl bg-gradient-to-br from-brand-900 via-brand-700 to-brand-500 px-6 py-10 text-white">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-brand-100">Manoksha Collections</p>
          <h1 className="mt-2 text-3xl font-semibold">Jewellery · Sarees · Kids wear</h1>
          <p className="mt-2 max-w-lg text-sm text-brand-100">Handpicked from our partner brands and delivered to your door. Pay securely with UPI.</p>
        </section>
      )}

      {categories.ok && categories.data.length > 0 && (
        <div className="flex flex-wrap gap-2">
          <Link href={link({ category: undefined, page: undefined })} className={cx("rounded-full border px-3 py-1 text-sm", !category ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white text-slate-700 hover:border-brand-500")}>All</Link>
          {categories.data.map((c) => (
            <Link key={c.id} href={link({ category: c.id, page: undefined })}
              className={cx("rounded-full border px-3 py-1 text-sm", category === c.id ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white text-slate-700 hover:border-brand-500")}>
              {c.name}
            </Link>
          ))}
        </div>
      )}

      {vendors.ok && vendors.data.length > 1 && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-slate-500">Brand:</span>
          <Link href={link({ vendor: undefined, page: undefined })} className={cx("rounded-full border px-3 py-1", !vendor ? "border-brand-600 bg-brand-50 text-brand-800" : "border-slate-300 bg-white text-slate-700")}>All</Link>
          {vendors.data.map((v) => (
            <Link key={v.id} href={link({ vendor: v.id, page: undefined })}
              className={cx("rounded-full border px-3 py-1", vendor === v.id ? "border-brand-600 bg-brand-50 text-brand-800" : "border-slate-300 bg-white text-slate-700")}>{v.name}</Link>
          ))}
        </div>
      )}

      {q && <p className="text-sm text-slate-600">Results for “{q}”</p>}
      {!items.ok ? (
        <Alert>The catalogue could not be loaded. Please try again.</Alert>
      ) : items.data.items.length === 0 ? (
        <p className="rounded-lg border border-slate-200 bg-white p-10 text-center text-sm text-slate-500">No products found.</p>
      ) : (
        <>
          <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
            {items.data.items.map((i) => (
              <li key={i.skuId} className="flex flex-col rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
                <Link href={`/products/${i.productId}`} className="group flex-1">
                  <ProductImage image={i.image} alt={i.productName} sizes="(min-width: 1024px) 25vw, (min-width: 640px) 33vw, 50vw" className="mb-3 aspect-square w-full rounded-lg" />
                  <p className="text-xs uppercase tracking-wide text-slate-500">{i.vendorName ?? i.categoryName}</p>
                  <h2 className="font-medium text-slate-900 group-hover:text-brand-700">{i.productName}</h2>
                  <p className="text-sm text-slate-600">{i.variantName}</p>
                  {i.productCode && <p className="font-mono text-[11px] text-slate-400">{i.productCode}</p>}
                  <p className="mt-2 text-base font-semibold text-slate-900">
                    {inr(i.price)}
                    {i.discountPct && i.retailPrice && i.retailPrice > i.price ? (
                      <><span className="ml-2 text-sm font-normal text-slate-400 line-through">{inr(i.retailPrice)}</span><span className="ml-1 text-xs font-medium text-emerald-700">{i.discountPct}% off</span></>
                    ) : null}
                  </p>
                  {!i.inStock && <p className="text-xs text-amber-700">Out of stock</p>}
                </Link>
                <div className="mt-3">
                  <AddToCart skuId={i.skuId} name={`${i.productName} — ${i.variantName}`} disabled={!i.inStock} />
                </div>
              </li>
            ))}
          </ul>
          <Pager page={current} total={items.data.total} size={items.data.pageSize} link={link} />
        </>
      )}
    </div>
  );
}

function Pager({ page, total, size, link }: { page: number; total: number; size: number; link: (p: Record<string, string | undefined>) => string }) {
  const pages = Math.ceil(total / size);
  if (pages <= 1) return null;
  return (
    <div className="flex items-center justify-center gap-3 text-sm">
      {page > 1 && <Link href={link({ page: String(page - 1) })} className="rounded-md border border-slate-300 bg-white px-3 py-1.5 hover:bg-slate-50">Previous</Link>}
      <span className="text-slate-500">Page {page} of {pages}</span>
      {page < pages && <Link href={link({ page: String(page + 1) })} className="rounded-md border border-slate-300 bg-white px-3 py-1.5 hover:bg-slate-50">Next</Link>}
    </div>
  );
}
