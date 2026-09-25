import Link from "next/link";
import { Alert, Badge, Button, Forbidden, Input, PageHeader, Select, Table } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Attribute, Category, ProductSummary } from "@/lib/types";
import { CatalogNav } from "./catalog-nav";
import { CreateProductForm } from "./create-product-form";

type Search = Record<string, string | string[] | undefined>;

export default async function ProductsPage({ searchParams }: { searchParams: Promise<Search> }) {
  const me = (await getMe())!;
  if (!can(me, P.catalogView)) return <Forbidden what="the catalog" />;
  const sp = await searchParams;
  const pick = (k: string) => (typeof sp[k] === "string" && sp[k] ? (sp[k] as string) : "");
  const query = new URLSearchParams();
  for (const k of ["q", "categoryId", "status", "page"]) if (pick(k)) query.set(k, pick(k));

  const [page, categories, attributes] = await Promise.all([
    backendFetch<{ items: ProductSummary[]; total: number; page: number; pageSize: number }>(`admin/catalog/products?${query}`),
    backendFetch<Category[]>("admin/catalog/categories"),
    backendFetch<Attribute[]>("admin/catalog/attributes"),
  ]);
  if (!page.ok) return <Alert>{page.problem.title}</Alert>;
  const pages = Math.max(1, Math.ceil(page.data.total / page.data.pageSize));
  const link = (p: number) => { const q = new URLSearchParams(query); q.set("page", String(p)); return `/catalog?${q}`; };

  return (
    <>
      <PageHeader title="Catalog" description="Products, variants, SKUs and barcodes. Prices arrive with the pricing module." />
      <CatalogNav active="products" />
      {can(me, P.catalogManage) && categories.ok && attributes.ok && (
        <CreateProductForm categories={categories.data.filter((c) => c.isActive)} attributes={attributes.data.filter((a) => a.isActive)} />
      )}
      <form method="get" className="mb-4 grid gap-2 md:grid-cols-4">
        <Input name="q" placeholder="Name, SKU code or barcode" defaultValue={pick("q")} />
        <Select name="categoryId" defaultValue={pick("categoryId")}>
          <option value="">All categories</option>
          {(categories.ok ? categories.data : []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </Select>
        <Select name="status" defaultValue={pick("status")}>
          <option value="">Any status</option>
          <option>Draft</option><option>Active</option><option>Inactive</option>
        </Select>
        <Button type="submit" variant="secondary">Search</Button>
      </form>
      <Table head={["Product", "Category", "Tracking", "Variants", "Sold to", "Status"]} empty={page.data.items.length === 0}>
        {page.data.items.map((p) => (
          <tr key={p.id} className="hover:bg-slate-50">
            <td className="px-4 py-2.5"><Link href={`/catalog/products/${p.id}`} className="font-medium text-brand-700 hover:underline">{p.name}</Link></td>
            <td className="px-4 py-2.5 text-slate-600">{p.categoryName}</td>
            <td className="px-4 py-2.5 text-slate-600">{p.trackingMode === "Serialized" ? "Per piece" : "By quantity"}</td>
            <td className="px-4 py-2.5">{p.variantCount}</td>
            <td className="px-4 py-2.5 space-x-1">
              {p.availableForRetail && <Badge>Retail</Badge>}
              {p.availableForReseller && <Badge tone="brand">Reseller</Badge>}
            </td>
            <td className="px-4 py-2.5"><Badge tone={p.status === "Active" ? "green" : p.status === "Draft" ? "amber" : "red"}>{p.status}</Badge></td>
          </tr>
        ))}
      </Table>
      {pages > 1 && (
        <div className="mt-4 flex items-center justify-end gap-3 text-sm">
          {page.data.page > 1 && <Link className="text-brand-700" href={link(page.data.page - 1)}>← Previous</Link>}
          <span className="text-slate-500">Page {page.data.page} of {pages}</span>
          {page.data.page < pages && <Link className="text-brand-700" href={link(page.data.page + 1)}>Next →</Link>}
        </div>
      )}
    </>
  );
}
