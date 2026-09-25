import Link from "next/link";
import { Alert, Badge, Card, PageHeader, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, BranchAssignment, Employee } from "@/lib/types";
import { EmployeeActions } from "./employee-actions";

export default async function EmployeePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  const [employee, history, branches] = await Promise.all([
    backendFetch<Employee>(`admin/employees/${id}`),
    backendFetch<BranchAssignment[]>(`admin/employees/${id}/branch-history`),
    backendFetch<Branch[]>("admin/branches"),
  ]);
  if (!employee.ok) return <Alert>{employee.problem.title}</Alert>;
  const e = employee.data;

  return (
    <>
      <PageHeader
        title={e.fullName}
        description={`${e.employeeCode} · ${e.assignedBranchName}`}
        actions={<Link href="/employees" className="text-sm text-brand-700 hover:underline">← All employees</Link>}
      />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Profile">
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt className="text-slate-500">Status</dt><dd><Badge tone={e.status === "Active" ? "green" : "red"}>{e.status}</Badge></dd></div>
            <div className="flex justify-between"><dt className="text-slate-500">Email</dt><dd>{e.email}</dd></div>
            <div className="flex justify-between"><dt className="text-slate-500">Mobile</dt><dd>{e.mobile ?? "—"}</dd></div>
            <div className="flex justify-between"><dt className="text-slate-500">Joined</dt><dd>{e.joinedOn}</dd></div>
          </dl>
          {can(me, P.usersView) && <Link href={`/users/${e.userId}`} className="mt-4 block text-sm text-brand-700 hover:underline">Login & roles →</Link>}
        </Card>
        <Card title="Branch history" className="lg:col-span-2">
          <ul className="divide-y divide-slate-100 text-sm">
            {(history.ok ? history.data : []).map((h) => (
              <li key={h.fromAt} className="py-2">
                <span className="font-medium text-slate-800">{h.branchName}</span>{" "}
                <span className="text-slate-500">{formatDateTime(h.fromAt)} → {h.toAt ? formatDateTime(h.toAt) : "now"}</span>
                <p className="text-xs text-slate-500">{h.reason}</p>
              </li>
            ))}
          </ul>
          {can(me, P.employeesManage) && branches.ok && (
            <EmployeeActions employee={e} branches={branches.data.filter((b) => b.isActive && b.id !== e.assignedBranchId)} />
          )}
        </Card>
      </div>
    </>
  );
}
