import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, shortId } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { UserSummary } from "@/lib/types";
import { CreateUserForm } from "./create-user-form";

export default async function UsersPage() {
  const me = (await getMe())!;
  if (!can(me, P.usersView)) return <Forbidden what="users" />;
  const result = await backendFetch<UserSummary[]>("admin/users");

  return (
    <>
      <PageHeader title="Users" description="Every employee has an individual account. Roles and permissions can only be changed by the Owner." />
      {can(me, P.usersManage) && <CreateUserForm />}
      {!result.ok ? (
        <Alert>{result.problem.title}</Alert>
      ) : (
        <Table head={["Name", "Email", "Roles", "Status", "Last sign-in"]} empty={result.data.length === 0}>
          {result.data.map((u) => (
            <tr key={u.id} className="hover:bg-slate-50">
              <td className="px-4 py-2.5">
                <Link href={`/users/${u.id}`} className="font-medium text-brand-700 hover:underline">
                  {u.displayName}
                </Link>
              </td>
              <td className="px-4 py-2.5 text-slate-600">{u.email}</td>
              <td className="px-4 py-2.5">
                <div className="flex flex-wrap gap-1">
                  {u.roles.map((r) => (
                    <Badge key={r.assignmentId} tone={r.roleCode === "OWNER" ? "brand" : "slate"}>
                      {r.roleName}
                      {r.branchId ? ` · ${shortId(r.branchId)}` : ""}
                    </Badge>
                  ))}
                  {u.roles.length === 0 && <span className="text-xs text-slate-400">None</span>}
                </div>
              </td>
              <td className="px-4 py-2.5">
                <Badge tone={u.status === "Active" ? "green" : "red"}>{u.status}</Badge>
              </td>
              <td className="px-4 py-2.5 text-slate-600">{formatDateTime(u.lastLoginAt)}</td>
            </tr>
          ))}
        </Table>
      )}
    </>
  );
}
