import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, Employee } from "@/lib/types";
import { CreateEmployeeForm } from "./create-employee-form";

export default async function EmployeesPage() {
  const me = (await getMe())!;
  if (!can(me, P.employeesView)) return <Forbidden what="employees" />;
  const [employees, branches] = await Promise.all([backendFetch<Employee[]>("admin/employees"), backendFetch<Branch[]>("admin/branches")]);
  if (!employees.ok) return <Alert>{employees.problem.title}</Alert>;

  // Branches where this user may add employees (the backend re-checks).
  const manageable = (branches.ok ? branches.data : []).filter(
    (b) => b.isActive && (me.globalPermissions.includes(P.employeesManage) || (me.branchPermissions[b.id] ?? []).includes(P.employeesManage)),
  );

  return (
    <>
      <PageHeader title="Employees" description="Each employee has an individual login. Creating an employee never grants a role — the Owner assigns roles on the Users page." />
      {manageable.length > 0 && <CreateEmployeeForm branches={manageable} />}
      <Table head={["Code", "Name", "Branch", "Email", "Status"]} empty={employees.data.length === 0}>
        {employees.data.map((e) => (
          <tr key={e.id} className="hover:bg-slate-50">
            <td className="px-4 py-2.5 font-mono text-xs">{e.employeeCode}</td>
            <td className="px-4 py-2.5"><Link className="font-medium text-brand-700 hover:underline" href={`/employees/${e.id}`}>{e.fullName}</Link></td>
            <td className="px-4 py-2.5">{e.assignedBranchName}</td>
            <td className="px-4 py-2.5 text-slate-600">{e.email}</td>
            <td className="px-4 py-2.5"><Badge tone={e.status === "Active" ? "green" : "red"}>{e.status}</Badge></td>
          </tr>
        ))}
      </Table>
    </>
  );
}
