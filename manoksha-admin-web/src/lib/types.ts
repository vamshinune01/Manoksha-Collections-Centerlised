/** Response shapes used by pages (mirrors the backend contract in @manoksha/api-client). */
export interface UserSummary {
  id: string;
  displayName: string;
  email: string | null;
  mobile: string | null;
  status: string;
  mfaEnabled: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  roles: { assignmentId: string; roleId: string; roleCode: string; roleName: string; branchId: string | null; assignedAt: string }[];
}

export interface Role {
  id: string;
  code: string;
  name: string;
  description: string | null;
  scope: "Global" | "Branch";
  isSystem: boolean;
  isActive: boolean;
  permissions: string[];
}

export interface PermissionInfo {
  code: string;
  module: string;
  ownerOnly: boolean;
}

export interface Setting {
  key: string;
  description: string;
  kind: "Integer" | "Money" | "String";
  value: unknown;
  version: number;
  updatedAt: string;
  updatedBy: string | null;
}

export interface AuditEntry {
  id: string;
  seq: number;
  occurredAt: string;
  actorUserId: string | null;
  actorType: string;
  actorRoles: string[];
  branchId: string | null;
  action: string;
  entityType: string;
  entityId: string;
  before: string | null;
  after: string | null;
  reason: string | null;
  ipAddress: string | null;
  correlationId: string;
}
