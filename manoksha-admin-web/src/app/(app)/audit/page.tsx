import Link from "next/link";
import { Alert, Button, Forbidden, Input, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, shortId } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { AuditEntry } from "@/lib/types";

type Search = Record<string, string | string[] | undefined>;

export default async function AuditPage({ searchParams }: { searchParams: Promise<Search> }) {
  const me = (await getMe())!;
  if (!can(me, P.auditView)) return <Forbidden what="the audit log" />;
  const sp = await searchParams;
  const pick = (k: string) => (typeof sp[k] === "string" && sp[k] ? (sp[k] as string) : undefined);

  const query = new URLSearchParams();
  for (const key of ["action", "entityType", "entityId", "actorUserId", "beforeSeq"]) {
    const v = pick(key);
    if (v) query.set(key, v);
  }
  query.set("limit", "50");
  const page = await backendFetch<{ items: AuditEntry[]; nextBeforeSeq: number | null }>(`admin/audit?${query}`);

  const nextQuery = new URLSearchParams(query);
  if (page.ok && page.data.nextBeforeSeq) nextQuery.set("beforeSeq", String(page.data.nextBeforeSeq));

  return (
    <>
      <PageHeader title="Audit log" description="Immutable history of sensitive business actions. Entries cannot be edited or deleted." />
      <form className="mb-4 grid gap-2 md:grid-cols-5" method="get">
        <Input name="action" placeholder="Action prefix, e.g. identity." defaultValue={pick("action")} />
        <Input name="entityType" placeholder="Entity type, e.g. User" defaultValue={pick("entityType")} />
        <Input name="entityId" placeholder="Entity id" defaultValue={pick("entityId")} />
        <Input name="actorUserId" placeholder="Actor user id" defaultValue={pick("actorUserId")} />
        <Button type="submit" variant="secondary">Filter</Button>
      </form>
      {!page.ok ? (
        <Alert>{page.problem.title}</Alert>
      ) : (
        <>
          <Table head={["When (IST)", "Action", "Entity", "Actor", "Reason", "Change"]} empty={page.data.items.length === 0}>
            {page.data.items.map((e) => (
              <tr key={e.id} className="align-top">
                <td className="whitespace-nowrap px-4 py-2.5 text-slate-600">{formatDateTime(e.occurredAt)}</td>
                <td className="px-4 py-2.5 font-mono text-xs">{e.action}</td>
                <td className="px-4 py-2.5 text-xs">
                  {e.entityType} <span className="text-slate-400">{shortId(e.entityId)}</span>
                </td>
                <td className="px-4 py-2.5 text-xs">
                  {e.actorType}
                  {e.actorUserId && <span className="text-slate-400"> {shortId(e.actorUserId)}</span>}
                  {e.actorRoles.length > 0 && <div className="text-slate-400">{e.actorRoles.join(", ")}</div>}
                </td>
                <td className="px-4 py-2.5 text-slate-700">{e.reason ?? "—"}</td>
                <td className="max-w-md px-4 py-2.5">
                  {(e.before || e.after) && (
                    <details>
                      <summary className="cursor-pointer text-xs text-brand-700">View</summary>
                      {e.before && <pre className="mt-1 overflow-x-auto rounded bg-red-50 p-2 text-[11px]">{e.before}</pre>}
                      {e.after && <pre className="mt-1 overflow-x-auto rounded bg-emerald-50 p-2 text-[11px]">{e.after}</pre>}
                    </details>
                  )}
                </td>
              </tr>
            ))}
          </Table>
          {page.data.nextBeforeSeq && (
            <div className="mt-4 text-right">
              <Link className="text-sm text-brand-700 hover:underline" href={`/audit?${nextQuery}`}>Older entries →</Link>
            </div>
          )}
        </>
      )}
    </>
  );
}
