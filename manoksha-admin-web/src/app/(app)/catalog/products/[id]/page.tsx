import Link from "next/link";
import { Alert, Badge, Card, PageHeader } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Category, ProductDetail } from "@/lib/types";
import { AddVariantForm, OnlineDiscountEditor, ProductEditor, ProductStatusButtons, ProductStockSwitch, VariantsTable } from "./product-forms";
import { MediaManager } from "./media-manager";

export default async function ProductPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  const [product, categories, onlineDiscount] = await Promise.all([
    backendFetch<ProductDetail>(`admin/catalog/products/${id}`),
    backendFetch<Category[]>("admin/catalog/categories"),
    can(me, P.pricingView) ? backendFetch<{ currentDiscountPct: number | null }>(`admin/pricing/products/${id}/online-discount`) : Promise.resolve(null),
  ]);
  if (!product.ok) return <Alert>{product.problem.title}</Alert>;
  const p = product.data;
  const manage = can(me, P.catalogManage);

  return (
    <>
      <PageHeader
        title={p.name}
        description={p.productCode ? `${p.productCode} · ${p.vendorName} · ${p.categoryName}` : `${p.categoryName} · ${p.trackingMode === "Serialized" ? "tracked per piece" : "tracked by quantity"}`}
        actions={<Link href="/catalog" className="text-sm text-brand-700 hover:underline">← Catalog</Link>}
      />
      <div className="mb-6 flex flex-wrap items-center gap-2">
        <Badge tone={p.status === "Active" ? "green" : p.status === "Draft" ? "amber" : "red"}>{p.status}</Badge>
        {p.availableForRetail ? <Badge>Retail</Badge> : <Badge tone="red">Not for retail</Badge>}
        {p.availableForReseller ? <Badge tone="brand">Reseller</Badge> : <Badge tone="red">Not for resellers</Badge>}
        {can(me, P.pricingView) && <Link href={`/pricing?productId=${p.id}`} className="text-sm text-brand-700 hover:underline">Prices →</Link>}
        {manage && <ProductStatusButtons product={p} />}
        {manage && p.vendorId && <ProductStockSwitch product={p} />}
      </div>
      <div className="grid gap-6 xl:grid-cols-3">
        <div className="space-y-6 xl:col-span-2">
          <Card title="Photos & videos">
            <MediaManager productId={p.id} canManage={manage} />
          </Card>
          <Card title={`Variants & SKUs (${p.variants.length})`}>
            <VariantsTable product={p} canManage={manage} canPrint={can(me, P.barcodesPrint)} />
            {manage && <AddVariantForm product={p} />}
          </Card>
        </div>
        <div className="space-y-6">
          <Card title="Details">
            {manage && categories.ok ? <ProductEditor product={p} categories={categories.data} /> : (
              <p className="whitespace-pre-line text-sm text-slate-700">{p.description ?? "No description."}</p>
            )}
          </Card>
          {onlineDiscount?.ok && (
            <Card title="Online customer discount">
              {can(me, P.pricingManage) ? <OnlineDiscountEditor productId={p.id} current={onlineDiscount.data.currentDiscountPct} />
                : <p className="text-sm text-slate-600">{onlineDiscount.data.currentDiscountPct ? `${onlineDiscount.data.currentDiscountPct}% off for online customers.` : "No online discount."}</p>}
            </Card>
          )}
        </div>
      </div>
    </>
  );
}
