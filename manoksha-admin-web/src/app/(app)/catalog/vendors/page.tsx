import { Alert, Card, Forbidden, PageHeader, Table } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Vendor } from "@/lib/types";
import { CatalogNav } from "../catalog-nav";
import { CreateVendorForm, EditVendorRow } from "./vendor-forms";

/** Vendors who ship their products directly (ADR-001 §41–47). Shipping fee and margin are the Owner's decisions. */
export default async function VendorsPage() {
  const me = (await getMe())!;
  if (!can(me, P.catalogView)) return <Forbidden what="vendors" />;
  const vendors = await backendFetch<Vendor[]>("admin/catalog/vendors");
  if (!vendors.ok) return <Alert>{vendors.problem.title}</Alert>;
  const manage = can(me, P.pricingManage);
  return (
    <>
      <PageHeader title="Catalog" description="Every product belongs to a vendor, who ships it directly to the customer or reseller." />
      <CatalogNav active="vendors" />
      {manage && <Card title="Add a vendor" className="mb-6"><CreateVendorForm /></Card>}
      <Table head={["Code", "Vendor", "Shipping per order", "Your margin", "Products", ""]} empty={vendors.data.length === 0}>
        {vendors.data.map((v) => <EditVendorRow key={v.id} vendor={v} canManage={manage} />)}
      </Table>
      <p className="mt-3 text-xs text-slate-500">
        An order with items from three vendors pays three shipping fees. Each reseller&apos;s percentage per vendor is set on the reseller&apos;s page.
      </p>
    </>
  );
}
