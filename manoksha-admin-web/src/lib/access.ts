/**
 * UI-side permission helpers. These ONLY decide what to show; every action is authorized again by the backend.
 */
export interface Me {
  userId: string;
  accountType: string;
  audience: string;
  displayName: string;
  email: string | null;
  mobile: string | null;
  mfaEnabled: boolean;
  isOwner: boolean;
  roles: { code: string; name: string; branchId: string | null }[];
  globalPermissions: string[];
  branchPermissions: Record<string, string[]>;
}

export function can(me: Me, permission: string): boolean {
  return me.globalPermissions.includes(permission) || Object.values(me.branchPermissions).some((p) => p.includes(permission));
}

export const P = {
  usersView: "identity.users.view",
  usersManage: "identity.users.manage",
  rolesView: "identity.roles.view",
  rolesManage: "identity.roles.manage",
  userRolesAssign: "identity.user_roles.assign",
  sessionsRevoke: "identity.sessions.revoke",
  settingsView: "settings.view",
  settingsManage: "settings.manage",
  auditView: "audit.view",
  branchesView: "branches.view",
  branchesManage: "branches.manage",
  priorityManage: "fulfillment.priority.manage",
  employeesView: "employees.view",
  employeesManage: "employees.manage",
  attendanceSelf: "attendance.self",
  attendanceView: "attendance.view",
  attendanceCorrect: "attendance.correct",
  catalogView: "catalog.view",
  catalogManage: "catalog.manage",
  barcodesPrint: "barcodes.print",
} as const;

export interface NavItem {
  href: string;
  label: string;
  /** Shown when the user holds ANY of these permissions (or always when omitted). */
  permission?: string | string[];
}

/** Sections delivered so far. Later phases add Branches, Catalog, Inventory, … here. */
export const NAV: NavItem[] = [
  { href: "/", label: "Dashboard" },
  { href: "/branches", label: "Branches & priority", permission: P.branchesView },
  { href: "/employees", label: "Employees", permission: P.employeesView },
  { href: "/attendance", label: "Attendance", permission: [P.attendanceSelf, P.attendanceView] },
  { href: "/catalog", label: "Catalog", permission: P.catalogView },
  { href: "/users", label: "Users", permission: P.usersView },
  { href: "/roles", label: "Roles & permissions", permission: P.rolesView },
  { href: "/settings", label: "Business settings", permission: P.settingsView },
  { href: "/audit", label: "Audit log", permission: P.auditView },
  { href: "/account", label: "My account" },
];

export function canAny(me: Me, permission: string | string[] | undefined): boolean {
  if (!permission) return true;
  return (Array.isArray(permission) ? permission : [permission]).some((p) => can(me, p));
}

export function shortId(id: string | null | undefined): string {
  return id ? id.slice(-8) : "—";
}
