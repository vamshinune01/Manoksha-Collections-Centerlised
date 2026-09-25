import Link from "next/link";
import { Alert, Forbidden, PageHeader } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { PermissionInfo, Role } from "@/lib/types";
import { RolePermissionsEditor } from "./role-permissions-editor";

export default async function RoleDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = (await getMe())!;
  if (!can(me, P.rolesView)) return <Forbidden what="roles" />;
  const [role, permissions] = await Promise.all([backendFetch<Role>(`admin/roles/${id}`), backendFetch<PermissionInfo[]>("admin/permissions")]);
  if (!role.ok) return <Alert>{role.problem.title}</Alert>;
  if (!permissions.ok) return <Alert>{permissions.problem.title}</Alert>;

  const editable = can(me, P.rolesManage) && role.data.code !== "OWNER";
  return (
    <>
      <PageHeader
        title={role.data.name}
        description={role.data.code === "OWNER" ? "The Owner role always holds every permission and cannot be edited." : role.data.description ?? undefined}
        actions={<Link href="/roles" className="text-sm text-brand-700 hover:underline">← All roles</Link>}
      />
      <RolePermissionsEditor role={role.data} catalog={permissions.data} editable={editable} />
    </>
  );
}
