import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, PurchaseOrder, Supplier } from "@/lib/types";
import { CreatePoForm } from "./create-po-form";
import { PO_TONE, PurchasingNav } from "./purchasing-nav";

export default async function PurchasingPage() {
  const me = (await getMe())!;
  if (!can(me, P.purchasingView) && !can(me, P.goodsReceipt)) return <Forbidden what="purchasing" />;
  const manage = can(me, P.purchasingManage);
  const [orders, suppliers, branches] = await Promise.all([
    backendFetch<PurchaseOrder[]>("admin/purchasing/purchase-orders"),
    manage ? backendFetch<Supplier[]>("admin/purchasing/suppliers") : Promise.resolve(null),
    backendFetch<Branch[]>("admin/branches"),
  ]);
  if (!orders.ok) return <Alert>{orders.problem.title}</Alert>;

  return (
    <>
      <PageHeader title="Purchasing" description={can(me, P.purchasingView)
        ? "Supplier → purchase order → goods receipt at the receiving branch. Receiving more than ordered requires amending the PO first."
        : "Purchase orders waiting to be received at your branch."} />
      <PurchasingNav active="orders" showSuppliers={can(me, P.purchasingView)} />
      {manage && suppliers?.ok && branches.ok && (
        <CreatePoForm suppliers={suppliers.data.filter((s) => s.isActive)} branches={branches.data.filter((b) => b.isActive)} />
      )}
      <Table head={["PO", "Supplier", "Receiving branch", "Lines", "Value", "Status", "Created"]} empty={orders.data.length === 0}>
        {orders.data.map((po) => (
          <tr key={po.id} className="hover:bg-slate-50">
            <td className="px-4 py-2.5"><Link href={`/purchasing/po/${po.id}`} className="font-mono text-sm font-medium text-brand-700 hover:underline">{po.number}</Link></td>
            <td className="px-4 py-2.5">{po.supplierName}</td>
            <td className="px-4 py-2.5">{po.receivingBranchName}</td>
            <td className="px-4 py-2.5">{po.lines.length}</td>
            <td className="px-4 py-2.5">{inr(po.expectedTotal)}</td>
            <td className="px-4 py-2.5"><Badge tone={PO_TONE[po.status]}>{po.status}</Badge></td>
            <td className="px-4 py-2.5 text-slate-600">{formatDateTime(po.createdAt)}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
