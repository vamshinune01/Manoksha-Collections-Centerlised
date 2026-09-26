import Link from "next/link";
import { storeFetch } from "@/lib/backend";
import { inr, type StoreCategory, type StorePage } from "@/lib/types";
import { AddToCart } from "@/components/store-client";
import { Alert, cx } from "@/components/ui";

export const metadata = { title: "Shop" };

export default async function StoreHome({ searchParams }: { searchParams: Promise<{ q?: string; category?: string; page?: string }> }) {
  const { q, category, page } = await searchParams;
  const current = Math.max(1, Number(page) || 1);
  const params = new URLSearchParams({ page: String(current), pageSize: "24" });
  if (q) params.set("q", q);
  if (category) params.set("categoryId", category);
  const [items, categories] = await Promise.all([storeFetch<StorePage>(`products?${params}`), storeFetch<StoreCategory[]>("categories")]);
  const link = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams({ ...(q ? { q } : {}), ...(category ? { category } : {}), ...patch } as Record<string, string>);
    for (const [k, v] of [...next.entries()]) if (!v) next.delete(k);
    return `/?${next}`;
  };

  return (
    <div className="space-y-6">
      {!q && !category && (
        <section className="rounded-2xl bg-gradient-to-br from-brand-900 via-brand-700 to-brand-500 px-6 py-10 text-white">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-brand-100">Manoksha Collections</p>
          <h1 className="mt-2 text-3xl font-semibold">Jewellery · Sarees · Kids wear</h1>
          <p className="mt-2 max-w-lg text-sm text-brand-100">Shop online from our Karimnagar, Hyderabad and Mulugu stores. Pay securely with UPI.</p>
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
                  <div className="mb-3 flex aspect-square items-center justify-center rounded-lg bg-gradient-to-br from-brand-50 to-slate-100 text-3xl font-semibold text-brand-500/70">
                    {i.productName.slice(0, 1)}
                  </div>
                  <p className="text-xs uppercase tracking-wide text-slate-500">{i.categoryName}</p>
                  <h2 className="font-medium text-slate-900 group-hover:text-brand-700">{i.productName}</h2>
                  <p className="text-sm text-slate-600">{i.variantName}</p>
                  <p className="mt-2 text-base font-semibold text-slate-900">{inr(i.price)}</p>
                  {!i.inStock && <p className="text-xs text-amber-700">Currently unavailable</p>}
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
