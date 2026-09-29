import Link from "next/link";
import { notFound } from "next/navigation";
import { storeFetch } from "@/lib/backend";
import { inr, type StoreProduct } from "@/lib/types";
import { AddToCart } from "@/components/store-client";
import { Gallery } from "./gallery";

export default async function ProductPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const product = await storeFetch<StoreProduct>(`products/${id}`);
  if (!product.ok) notFound();
  const p = product.data;
  return (
    <div className="space-y-6">
      <Link href="/" className="text-sm text-brand-700 hover:underline">← Back to shop</Link>
      <div className="grid gap-8 md:grid-cols-2">
        <Gallery media={p.media} name={p.productName} />
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">{p.productName}</h1>
          <p className="mt-1 text-sm text-slate-500">Shipping is added once per order at checkout.</p>
          <ul className="mt-6 space-y-3">
            {p.variants.map((v) => (
              <li key={v.skuId} className="flex items-center justify-between gap-4 rounded-lg border border-slate-200 bg-white p-4">
                <div>
                  <p className="font-medium text-slate-900">{v.variantName}</p>
                  <p className="text-xs text-slate-500">{v.skuCode}</p>
                  <p className="mt-1 font-semibold">{inr(v.price)}</p>
                </div>
                <div className="w-36">
                  <AddToCart skuId={v.skuId} name={`${p.productName} — ${v.variantName}`} disabled={!v.inStock} />
                </div>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}
