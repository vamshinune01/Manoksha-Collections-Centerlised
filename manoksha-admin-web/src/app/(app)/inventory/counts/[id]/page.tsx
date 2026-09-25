import Link from "next/link";
import { Alert, Badge, Card, PageHeader, Table } from "@/components/ui";
import { backendFetch } from "@/lib/backend";
import type { StockCount } from "@/lib/types";
import { CountEditor } from "../count-forms";

export default async function CountPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const count = await backendFetch<StockCount>(`admin/inventory/counts/${id}`);
  if (!count.ok) return <Alert>{count.problem.title}</Alert>;
  const c = count.data;
  return (
    <>
      <PageHeader title={c.number} description={`${c.branchName}${c.notes ? ` · ${c.notes}` : ""}`}
        actions={<Link href="/inventory/counts" className="text-sm text-brand-700 hover:underline">← Counts</Link>} />
      <Card title={c.status === "Open" ? "Enter counted quantities" : "Result"}>
        {c.status === "Open" ? <CountEditor count={c} /> : (
          <>
            <Table head={["SKU", "Counted", "System", "Difference"]}>
              {c.lines.map((l) => (
                <tr key={l.skuId}>
                  <td className="px-4 py-2">{l.productName} · {l.variantName} <span className="font-mono text-xs text-slate-400">{l.skuCode}</span></td>
                  <td className="px-4 py-2">{l.countedQty}</td><td className="px-4 py-2">{l.systemQty}</td>
                  <td className="px-4 py-2">{l.variance ? <Badge tone="red">{l.variance > 0 ? `+${l.variance}` : l.variance}</Badge> : "0"}</td>
                </tr>
              ))}
            </Table>
            {c.discrepancyIds.length > 0 && <p className="mt-4 text-sm">Differences were raised as <Link className="text-brand-700 underline" href="/inventory/discrepancies">discrepancies</Link>.</p>}
          </>
        )}
      </Card>
    </>
  );
}
