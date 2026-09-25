import { Alert, Badge, Card, Forbidden, PageHeader } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Attribute } from "@/lib/types";
import { CatalogNav } from "../catalog-nav";
import { AddOptionForm, CreateAttributeForm } from "./attribute-forms";

export default async function AttributesPage() {
  const me = (await getMe())!;
  if (!can(me, P.catalogView)) return <Forbidden what="the catalog" />;
  const attributes = await backendFetch<Attribute[]>("admin/catalog/attributes");
  if (!attributes.ok) return <Alert>{attributes.problem.title}</Alert>;
  const manage = can(me, P.catalogManage);

  return (
    <>
      <PageHeader title="Catalog" description="Variant attributes are configurable — e.g. Colour, Size, Design, Fabric, Length." />
      <CatalogNav active="attributes" />
      {manage && <CreateAttributeForm />}
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {attributes.data.map((a) => (
          <Card key={a.id} title={<>{a.name} <span className="font-mono text-xs text-slate-400">{a.code}</span></>}>
            <div className="flex flex-wrap gap-1.5">
              {a.options.map((o) => <Badge key={o.id} tone={o.isActive ? "slate" : "red"}>{o.value}</Badge>)}
              {a.options.length === 0 && <span className="text-sm text-slate-500">No options.</span>}
            </div>
            {manage && <AddOptionForm attributeId={a.id} />}
          </Card>
        ))}
      </div>
    </>
  );
}
