import Link from "next/link";
import { Alert, Badge, Card, Forbidden, PageHeader, formatDateTime } from "@/components/ui";
import { P, can, shortId } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Branch, Role, UserSummary } from "@/lib/types";
import { AssignRoleForm, RevokeRoleButton, UserStatusForm } from "./user-actions";

export default async function UserDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  if (!can(me, P.usersView)) return <Forbidden what="users" />;

  const [user, roles, branches] = await Promise.all([
    backendFetch<UserSummary>(`admin/users/${id}`),
    can(me, P.rolesView) ? backendFetch<Role[]>("admin/roles") : Promise.resolve(null),
    backendFetch<Branch[]>("admin/branches"),
  ]);
  const branchNames = new Map((branches.ok ? branches.data : []).map((b) => [b.id, b.name]));
  if (!user.ok) return <Alert>{user.problem.title}</Alert>;
  const u = user.data;
  const canAssign = can(me, P.userRolesAssign);

  return (
    <>
      <PageHeader
        title={u.displayName}
        description={u.email ?? undefined}
        actions={<Link href="/users" className="text-sm text-brand-700 hover:underline">← All users</Link>}
      />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Roles" className="lg:col-span-2">
          <ul className="divide-y divide-slate-100">
            {u.roles.length === 0 && <li className="py-2 text-sm text-slate-500">No roles assigned.</li>}
            {u.roles.map((r) => (
              <li key={r.assignmentId} className="flex items-center justify-between gap-4 py-2.5 text-sm">
                <div>
                  <span className="font-medium text-slate-800">{r.roleName}</span>{" "}
                  <Badge tone={r.branchId ? "slate" : "brand"}>{r.branchId ? branchNames.get(r.branchId) ?? shortId(r.branchId) : "All branches"}</Badge>
                  <p className="text-xs text-slate-500">Since {formatDateTime(r.assignedAt)}</p>
                </div>
                {canAssign && <RevokeRoleButton userId={u.id} assignmentId={r.assignmentId} roleName={r.roleName} />}
              </li>
            ))}
          </ul>
          {canAssign && roles?.ok && <AssignRoleForm userId={u.id} roles={roles.data.filter((r) => r.isActive)} branches={branches.ok ? branches.data.filter((b) => b.isActive) : []} />}
          {!canAssign && <p className="mt-4 text-xs text-slate-500">Only the Owner can change roles and permissions.</p>}
        </Card>
        <Card title="Account">
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt className="text-slate-500">Status</dt><dd><Badge tone={u.status === "Active" ? "green" : "red"}>{u.status}</Badge></dd></div>
            <div className="flex justify-between"><dt className="text-slate-500">Two-step verification</dt><dd>{u.mfaEnabled ? "On" : "Off"}</dd></div>
            <div className="flex justify-between"><dt className="text-slate-500">Mobile</dt><dd>{u.mobile ?? "—"}</dd></div>
            <div className="flex justify-between"><dt className="text-slate-500">Created</dt><dd>{formatDateTime(u.createdAt)}</dd></div>
            <div className="flex justify-between"><dt className="text-slate-500">Last sign-in</dt><dd>{formatDateTime(u.lastLoginAt)}</dd></div>
          </dl>
          {can(me, P.usersManage) && u.id !== me.userId && <UserStatusForm userId={u.id} status={u.status} canRevokeSessions={can(me, P.sessionsRevoke)} />}
        </Card>
      </div>
    </>
  );
}
