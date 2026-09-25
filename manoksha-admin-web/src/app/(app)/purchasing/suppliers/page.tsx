import { Alert, Badge, Forbidden, PageHeader, Table } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Supplier } from "@/lib/types";
import { PurchasingNav } from "../purchasing-nav";
import { SupplierForm } from "./supplier-form";

export default async function SuppliersPage() {
  const me = (await getMe())!;
  if (!can(me, P.purchasingView)) return <Forbidden what="suppliers" />;
  const suppliers = await backendFetch<Supplier[]>("admin/purchasing/suppliers");
  if (!suppliers.ok) return <Alert>{suppliers.problem.title}</Alert>;
  const manage = can(me, P.purchasingManage);
  return (
    <>
      <PageHeader title="Purchasing" />
      <PurchasingNav active="suppliers" showSuppliers />
      {manage && <SupplierForm />}
      <Table head={["Code", "Supplier", "Contact", "Mobile", "GSTIN", "Status", ""]} empty={suppliers.data.length === 0}>
        {suppliers.data.map((s) => (
          <tr key={s.id}>
            <td className="px-4 py-2.5 font-mono text-xs">{s.code}</td>
            <td className="px-4 py-2.5 font-medium">{s.name}</td>
            <td className="px-4 py-2.5">{s.contactName ?? "—"}</td>
            <td className="px-4 py-2.5">{s.mobile ?? "—"}</td>
            <td className="px-4 py-2.5 font-mono text-xs">{s.gstin ?? "—"}</td>
            <td className="px-4 py-2.5"><Badge tone={s.isActive ? "green" : "red"}>{s.isActive ? "Active" : "Inactive"}</Badge></td>
            <td className="px-4 py-2.5 text-right">{manage && <SupplierForm existing={s} />}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
