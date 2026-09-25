import { Alert, Badge, Forbidden, PageHeader, Table } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Category } from "@/lib/types";
import { CatalogNav } from "../catalog-nav";
import { CategoryForm } from "./category-form";

export default async function CategoriesPage() {
  const me = (await getMe())!;
  if (!can(me, P.catalogView)) return <Forbidden what="the catalog" />;
  const categories = await backendFetch<Category[]>("admin/catalog/categories");
  if (!categories.ok) return <Alert>{categories.problem.title}</Alert>;
  const names = new Map(categories.data.map((c) => [c.id, c.name]));

  return (
    <>
      <PageHeader title="Catalog" />
      <CatalogNav active="categories" />
      {can(me, P.catalogManage) && <CategoryForm categories={categories.data} />}
      <Table head={["Category", "Parent", "Order", "Status", ""]} empty={categories.data.length === 0}>
        {categories.data.map((c) => (
          <tr key={c.id}>
            <td className="px-4 py-2.5 font-medium text-slate-800">{c.name}</td>
            <td className="px-4 py-2.5 text-slate-600">{c.parentId ? names.get(c.parentId) : "—"}</td>
            <td className="px-4 py-2.5">{c.sortOrder}</td>
            <td className="px-4 py-2.5"><Badge tone={c.isActive ? "green" : "red"}>{c.isActive ? "Active" : "Inactive"}</Badge></td>
            <td className="px-4 py-2.5 text-right">{can(me, P.catalogManage) && <CategoryForm categories={categories.data} existing={c} />}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
