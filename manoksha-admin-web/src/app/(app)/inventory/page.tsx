import { Alert, Button, Forbidden, PageHeader, Select, Table, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, InventoryItem, Movement, StockRow } from "@/lib/types";
import { InventoryNav, STATUS_LABEL } from "./inventory-nav";

type Search = Record<string, string | string[] | undefined>;
const COLUMNS = ["Available", "Reserved", "TransferPending", "InTransit", "Damaged", "Repair", "Blocked", "Lost"];

export default async function StockPage({ searchParams }: { searchParams: Promise<Search> }) {
  const me = (await getMe())!;
  if (!can(me, P.inventoryView)) return <Forbidden what="inventory" />;
  const sp = await searchParams;
  const branchId = typeof sp.branchId === "string" ? sp.branchId : "";
  const skuId = typeof sp.skuId === "string" ? sp.skuId : "";
  const [stock, branches] = await Promise.all([
    backendFetch<StockRow[]>(`admin/inventory/stock?${new URLSearchParams({ ...(branchId && { branchId }), ...(skuId && { skuId }) })}`),
    backendFetch<Branch[]>("admin/branches"),
  ]);
  if (!stock.ok) return <Alert>{stock.problem.title}</Alert>;
  const [items, movements] = skuId
    ? await Promise.all([backendFetch<InventoryItem[]>(`admin/inventory/items?skuId=${skuId}`), backendFetch<Movement[]>(`admin/inventory/movements?skuId=${skuId}`)])
    : [null, null];
  const names = new Map((branches.ok ? branches.data : []).map((b) => [b.id, b.name]));

  return (
    <>
      <PageHeader title="Inventory" description="One central stock source for store, online and reseller channels. Every change is recorded as a movement." />
      <InventoryNav active="stock" />
      <form method="get" className="mb-4 flex gap-2">
        <Select name="branchId" defaultValue={branchId} className="max-w-xs">
          <option value="">All my branches</option>
          {(branches.ok ? branches.data : []).map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
        </Select>
        {skuId && <input type="hidden" name="skuId" value={skuId} />}
        <Button type="submit" variant="secondary">Filter</Button>
        {skuId && <a href="/inventory" className="self-center text-sm text-brand-700">Clear SKU</a>}
      </form>
      <Table head={["Branch", "SKU", ...COLUMNS.map((c) => STATUS_LABEL[c] ?? c)]} empty={stock.data.length === 0}>
        {stock.data.map((r) => (
          <tr key={`${r.branchId}-${r.skuId}`} className="hover:bg-slate-50">
            <td className="px-4 py-2">{r.branchName}</td>
            <td className="px-4 py-2">
              <a href={`/inventory?skuId=${r.skuId}`} className="text-brand-700 hover:underline">{r.productName} · {r.variantName}</a>
              <span className="ml-1 font-mono text-xs text-slate-400">{r.skuCode}</span>
            </td>
            {COLUMNS.map((c) => <td key={c} className={`px-4 py-2 ${c === "Available" ? "font-semibold" : "text-slate-600"}`}>{r.byStatus[c] ?? 0}</td>)}
          </tr>
        ))}
      </Table>
      {items?.ok && items.data.length > 0 && (
        <div className="mt-8">
          <h2 className="mb-2 text-sm font-semibold text-slate-800">Pieces</h2>
          <Table head={["Barcode", "Branch", "Status", "Received"]}>
            {items.data.map((i) => (
              <tr key={i.id}><td className="px-4 py-2 font-mono text-xs">{i.barcode}</td><td className="px-4 py-2">{i.branchName}</td>
                <td className="px-4 py-2">{i.writtenOff ? "Written off" : STATUS_LABEL[i.status] ?? i.status}</td><td className="px-4 py-2">{formatDateTime(i.receivedAt)}</td></tr>
            ))}
          </Table>
        </div>
      )}
      {movements?.ok && (
        <div className="mt-8">
          <h2 className="mb-2 text-sm font-semibold text-slate-800">Movement history</h2>
          <Table head={["When", "Type", "Qty", "From", "To", "Reference"]} empty={movements.data.length === 0}>
            {movements.data.map((m) => (
              <tr key={m.seq}>
                <td className="px-4 py-2 text-xs">{formatDateTime(m.occurredAt)}</td>
                <td className="px-4 py-2 font-mono text-xs">{m.movementType}</td>
                <td className="px-4 py-2">{m.quantity}</td>
                <td className="px-4 py-2 text-xs">{m.fromBranchId ? names.get(m.fromBranchId) : ""} {m.fromStatus ? `(${STATUS_LABEL[m.fromStatus] ?? m.fromStatus})` : ""}</td>
                <td className="px-4 py-2 text-xs">{m.toBranchId ? names.get(m.toBranchId) : ""} {m.toStatus ? `(${STATUS_LABEL[m.toStatus] ?? m.toStatus})` : ""}</td>
                <td className="px-4 py-2 font-mono text-xs">{m.referenceNumber}</td>
              </tr>
            ))}
          </Table>
        </div>
      )}
    </>
  );
}
