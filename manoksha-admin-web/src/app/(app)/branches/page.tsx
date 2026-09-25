import { Alert, Badge, Card, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, Priority } from "@/lib/types";
import { BranchStatusButton, CreateBranchForm, PriorityEditor } from "./branch-forms";

export default async function BranchesPage() {
  const me = (await getMe())!;
  if (!can(me, P.branchesView)) return <Forbidden what="branches" />;
  const [branches, priority, history] = await Promise.all([
    backendFetch<Branch[]>("admin/branches"),
    backendFetch<Priority>("admin/fulfillment-priority"),
    backendFetch<Priority[]>("admin/fulfillment-priority/history"),
  ]);
  if (!branches.ok) return <Alert>{branches.problem.title}</Alert>;
  if (!priority.ok) return <Alert>{priority.problem.title}</Alert>;

  return (
    <>
      <PageHeader
        title="Branches & fulfillment priority"
        description="Online and reseller orders go to the first branch, in this order, that can fulfil the complete order. Orders are never split. Changes apply to new checkouts only."
      />
      <div className="grid gap-6 xl:grid-cols-5">
        <div className="space-y-6 xl:col-span-3">
          {can(me, P.branchesManage) && <CreateBranchForm />}
          <Table head={["Code", "Branch", "City", "Status", ""]}>
            {branches.data.map((b) => (
              <tr key={b.id}>
                <td className="px-4 py-2.5 font-mono text-xs">{b.code}</td>
                <td className="px-4 py-2.5 font-medium text-slate-800">{b.name}</td>
                <td className="px-4 py-2.5 text-slate-600">{b.address.city ?? "—"}</td>
                <td className="px-4 py-2.5"><Badge tone={b.isActive ? "green" : "red"}>{b.isActive ? "Active" : "Inactive"}</Badge></td>
                <td className="px-4 py-2.5 text-right">{can(me, P.branchesManage) && <BranchStatusButton branch={b} />}</td>
              </tr>
            ))}
          </Table>
        </div>
        <div className="space-y-6 xl:col-span-2">
          <Card title={`Fulfillment priority · version ${priority.data.version}`}>
            <PriorityEditor priority={priority.data} editable={can(me, P.priorityManage)} />
          </Card>
          {history.ok && (
            <Card title="Priority history">
              <ol className="space-y-3 text-sm">
                {history.data.slice(0, 10).map((v) => (
                  <li key={v.version}>
                    <p className="font-medium text-slate-800">
                      v{v.version} · {v.entries.map((e) => e.branchCode).join(" → ")}
                    </p>
                    <p className="text-xs text-slate-500">{formatDateTime(v.changedAt)} · {v.reason}</p>
                  </li>
                ))}
              </ol>
            </Card>
          )}
        </div>
      </div>
    </>
  );
}
