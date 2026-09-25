import Link from "next/link";
import { Alert, Badge, Card, PageHeader } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Category, ProductDetail } from "@/lib/types";
import { AddVariantForm, ProductEditor, ProductStatusButtons, VariantsTable } from "./product-forms";

export default async function ProductPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  const [product, categories] = await Promise.all([backendFetch<ProductDetail>(`admin/catalog/products/${id}`), backendFetch<Category[]>("admin/catalog/categories")]);
  if (!product.ok) return <Alert>{product.problem.title}</Alert>;
  const p = product.data;
  const manage = can(me, P.catalogManage);

  return (
    <>
      <PageHeader
        title={p.name}
        description={`${p.categoryName} · ${p.trackingMode === "Serialized" ? "tracked per piece" : "tracked by quantity"}`}
        actions={<Link href="/catalog" className="text-sm text-brand-700 hover:underline">← Catalog</Link>}
      />
      <div className="mb-6 flex flex-wrap items-center gap-2">
        <Badge tone={p.status === "Active" ? "green" : p.status === "Draft" ? "amber" : "red"}>{p.status}</Badge>
        {p.availableForRetail ? <Badge>Retail</Badge> : <Badge tone="red">Not for retail</Badge>}
        {p.availableForReseller ? <Badge tone="brand">Reseller</Badge> : <Badge tone="red">Not for resellers</Badge>}
        {can(me, P.pricingView) && <Link href={`/pricing?productId=${p.id}`} className="text-sm text-brand-700 hover:underline">Prices →</Link>}
        {manage && <ProductStatusButtons product={p} />}
      </div>
      <div className="grid gap-6 xl:grid-cols-3">
        <div className="space-y-6 xl:col-span-2">
          <Card title={`Variants & SKUs (${p.variants.length})`}>
            <VariantsTable product={p} canManage={manage} canPrint={can(me, P.barcodesPrint)} />
            {manage && <AddVariantForm product={p} />}
          </Card>
        </div>
        <Card title="Details">
          {manage && categories.ok ? <ProductEditor product={p} categories={categories.data} /> : (
            <p className="whitespace-pre-line text-sm text-slate-700">{p.description ?? "No description."}</p>
          )}
        </Card>
      </div>
    </>
  );
}
