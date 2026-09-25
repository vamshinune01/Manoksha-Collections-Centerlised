import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Role } from "@/lib/types";
import { CreateRoleForm } from "./create-role-form";

export default async function RolesPage() {
  const me = (await getMe())!;
  if (!can(me, P.rolesView)) return <Forbidden what="roles" />;
  const roles = await backendFetch<Role[]>("admin/roles");

  return (
    <>
      <PageHeader
        title="Roles & permissions"
        description="Roles bundle permissions and are assigned to users for all branches or one branch. Owner-only permissions can never be granted to other roles."
      />
      {can(me, P.rolesManage) && <CreateRoleForm />}
      {!roles.ok ? (
        <Alert>{roles.problem.title}</Alert>
      ) : (
        <Table head={["Role", "Code", "Scope", "Permissions", "Status"]}>
          {roles.data.map((r) => (
            <tr key={r.id} className="hover:bg-slate-50">
              <td className="px-4 py-2.5">
                <Link href={`/roles/${r.id}`} className="font-medium text-brand-700 hover:underline">{r.name}</Link>
                {r.isSystem && <span className="ml-2 text-xs text-slate-400">system</span>}
              </td>
              <td className="px-4 py-2.5 font-mono text-xs text-slate-600">{r.code}</td>
              <td className="px-4 py-2.5">{r.scope === "Global" ? "All branches" : "One branch"}</td>
              <td className="px-4 py-2.5 text-slate-600">{r.permissions.length}</td>
              <td className="px-4 py-2.5"><Badge tone={r.isActive ? "green" : "red"}>{r.isActive ? "Active" : "Inactive"}</Badge></td>
            </tr>
          ))}
        </Table>
      )}
    </>
  );
}
