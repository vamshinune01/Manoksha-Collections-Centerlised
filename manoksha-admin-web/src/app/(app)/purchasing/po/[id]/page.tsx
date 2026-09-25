import Link from "next/link";
import { Alert, Badge, Card, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, canAt, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { GoodsReceipt, PurchaseOrder } from "@/lib/types";
import { PO_TONE } from "../../purchasing-nav";
import { AmendLineForm, PoActions, ReceiveForm } from "./po-actions";

export default async function PoPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  const [po, receipts] = await Promise.all([
    backendFetch<PurchaseOrder>(`admin/purchasing/purchase-orders/${id}`),
    backendFetch<GoodsReceipt[]>(`admin/purchasing/goods-receipts?purchaseOrderId=${id}`),
  ]);
  if (!po.ok) return <Alert>{po.problem.title}</Alert>;
  const o = po.data;
  const manage = can(me, P.purchasingManage);
  const canReceive = canAt(me, P.goodsReceipt, o.receivingBranchId) && (o.status === "Issued" || o.status === "PartiallyReceived");

  return (
    <>
      <PageHeader title={o.number} description={`${o.supplierName} → ${o.receivingBranchName}`}
        actions={<Link href="/purchasing" className="text-sm text-brand-700 hover:underline">← Purchasing</Link>} />
      <div className="mb-6 flex flex-wrap items-center gap-3">
        <Badge tone={PO_TONE[o.status]}>{o.status}</Badge>
        {o.supplierReference && <span className="text-sm text-slate-600">Ref: {o.supplierReference}</span>}
        {o.expectedTotal != null && <span className="text-sm text-slate-600">Ordered value: {inr(o.expectedTotal)}</span>}
        {o.closeReason && <span className="text-sm text-slate-600">Closed: {o.closeReason}</span>}
        {manage && <PoActions po={o} />}
      </div>
      <div className="space-y-6">
        <Card title="Lines">
          <Table head={["SKU", "Ordered", "Received", "Damaged", "Remaining", ...(o.expectedTotal != null ? ["Expected cost"] : [])]}>
            {o.lines.map((l) => (
              <tr key={l.id}>
                <td className="px-4 py-2">{l.productName} · {l.variantName} <span className="font-mono text-xs text-slate-400">{l.skuCode}</span>
                  {l.trackingMode === "Serialized" && <Badge tone="brand">per piece</Badge>}</td>
                <td className="px-4 py-2">{l.orderedQty}</td>
                <td className="px-4 py-2">{l.receivedQty}</td>
                <td className="px-4 py-2">{l.damagedQty}</td>
                <td className="px-4 py-2 font-medium">{l.remainingQty}</td>
                {o.expectedTotal != null && <td className="px-4 py-2">{inr(l.expectedUnitCost)}</td>}
              </tr>
            ))}
          </Table>
          {manage && (o.status === "Draft" || o.status === "Issued" || o.status === "PartiallyReceived") && <AmendLineForm po={o} />}
        </Card>
        {canReceive && <Card title="Record goods receipt"><ReceiveForm po={o} canPrint={can(me, P.barcodesPrint)} /></Card>}
        {receipts.ok && receipts.data.length > 0 && (
          <Card title="Goods receipts">
            <ul className="divide-y divide-slate-100 text-sm">
              {receipts.data.map((g) => (
                <li key={g.id} className="py-2">
                  <span className="font-mono font-medium">{g.number}</span> · invoice {g.supplierInvoiceRef} · {formatDateTime(g.receivedAt)}
                  {g.totalCost != null && <> · {inr(g.totalCost)}</>}
                  <p className="text-xs text-slate-500">{g.lines.map((l) => `${l.skuCode}: ${l.receivedQty} received (${l.damagedQty} damaged)`).join(" · ")}</p>
                </li>
              ))}
            </ul>
          </Card>
        )}
      </div>
    </>
  );
}
