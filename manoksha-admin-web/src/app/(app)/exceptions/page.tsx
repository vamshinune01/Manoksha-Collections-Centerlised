import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import { EXCEPTION_LABELS as LABELS } from "@/lib/exceptions";
import type { ExceptionCenter, Severity } from "@/lib/types";
import { ExceptionAction } from "./exception-actions";

const TONE: Record<Severity, "red" | "amber" | "slate"> = { CRITICAL: "red", WARNING: "amber", INFO: "slate" };

/** SPEC §28: one queue of everything that needs a person, most severe first. */
export default async function ExceptionCenterPage({ searchParams }: { searchParams: Promise<{ type?: string }> }) {
  const me = (await getMe())!;
  if (!can(me, P.exceptionsView)) return <Forbidden what="the Exception Center" />;
  const { type } = await searchParams;
  const center = await backendFetch<ExceptionCenter>(`admin/exceptions${type ? `?type=${encodeURIComponent(type)}` : ""}`);
  if (!center.ok) return <Alert>{center.problem.title}</Alert>;
  const { counts, items } = center.data;
  const total = counts.reduce((n, c) => n + c.open, 0);
  const canManage = can(me, P.exceptionsManage);
  return (
    <>
      <PageHeader title="Exception Center" description="Everything that needs a decision or follow-up. Items leave this list when they are handled on their page." />
      <div className="mb-4 flex flex-wrap gap-2 text-xs">
        <Link href="/exceptions" className={`rounded-full border px-3 py-1 ${!type ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white"}`}>All ({total})</Link>
        {counts.map((c) => (
          <Link key={c.type} href={`/exceptions?type=${c.type}`}
            className={`rounded-full border px-3 py-1 ${type === c.type ? "border-brand-600 bg-brand-600 text-white" : c.open > 0 ? "border-amber-300 bg-amber-50 text-amber-900" : "border-slate-200 bg-white text-slate-500"}`}>
            {LABELS[c.type]} ({c.open})
          </Link>
        ))}
      </div>
      {items.length === 0 ? (
        <Alert tone="success">Nothing is waiting here. Well done.</Alert>
      ) : (
        <Table head={["Severity", "Type", "Reference", "Details", "Branch", "Since", ""]}>
          {items.map((i) => (
            <tr key={`${i.type}-${i.id}`} className="align-top">
              <td className="px-4 py-2.5"><Badge tone={TONE[i.severity]}>{i.severity}</Badge></td>
              <td className="px-4 py-2.5 text-xs font-medium text-slate-700">{LABELS[i.type]}</td>
              <td className="px-4 py-2.5 font-mono text-xs">{i.reference}</td>
              <td className="max-w-md px-4 py-2.5 text-xs text-slate-600">{i.detail}</td>
              <td className="px-4 py-2.5 text-xs">{i.branchName ?? "—"}</td>
              <td className="whitespace-nowrap px-4 py-2.5 text-xs text-slate-500">{formatDateTime(i.createdAt)}</td>
              <td className="px-4 py-2.5 text-right"><ExceptionAction item={i} canManage={canManage} /></td>
            </tr>
          ))}
        </Table>
      )}
    </>
  );
}
