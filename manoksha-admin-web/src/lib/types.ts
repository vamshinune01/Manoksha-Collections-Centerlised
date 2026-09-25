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

export interface Branch {
  id: string;
  code: string;
  name: string;
  address: { line1: string | null; city: string | null; state: string | null; pin: string | null; phone: string | null };
  isActive: boolean;
  createdAt: string;
}

export interface PriorityEntry {
  priority: number;
  branchId: string;
  branchCode: string;
  branchName: string;
  isActive: boolean;
}

export interface Priority {
  version: number;
  changedAt: string | null;
  changedBy: string | null;
  reason: string | null;
  entries: PriorityEntry[];
}

export interface Employee {
  id: string;
  userId: string;
  employeeCode: string;
  fullName: string;
  mobile: string | null;
  email: string | null;
  assignedBranchId: string;
  assignedBranchName: string;
  status: string;
  joinedOn: string;
  createdAt: string;
}

export interface BranchAssignment {
  branchId: string;
  branchName: string;
  fromAt: string;
  toAt: string | null;
  assignedBy: string | null;
  reason: string;
}

export interface Attendance {
  id: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  branchId: string;
  branchName: string;
  clockInAt: string;
  clockOutAt: string | null;
  hoursWorked: number | null;
  clockInSource: string;
  clockOutSource: string | null;
  correctedBy: string | null;
  correctionReason: string | null;
}

export interface MyAttendance {
  isEmployee: boolean;
  isClockedIn: boolean;
  open: Attendance | null;
  recent: Attendance[];
  assignedBranchId: string | null;
  assignedBranchName: string | null;
}

export interface Category {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
  sortOrder: number;
  isActive: boolean;
}

export interface AttributeOption {
  id: string;
  value: string;
  sortOrder: number;
  isActive: boolean;
}

export interface Attribute {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
  options: AttributeOption[];
}

export interface ProductSummary {
  id: string;
  name: string;
  slug: string;
  categoryId: string;
  categoryName: string;
  trackingMode: "Serialized" | "Quantity";
  status: "Draft" | "Active" | "Inactive";
  availableForRetail: boolean;
  availableForReseller: boolean;
  variantCount: number;
  createdAt: string;
}

export interface BarcodeInfo {
  id: string;
  code: string;
  kind: "InternalEan13" | "External";
  status: "Active" | "Inactive";
  inventoryItemId: string | null;
  createdAt: string;
  printCount: number;
  lastPrintedAt: string | null;
  retireReason: string | null;
}

export interface VariantInfo {
  id: string;
  name: string;
  status: "Active" | "Inactive";
  values: { attributeId: string; attributeName: string; optionId: string; value: string }[];
  skuId: string;
  skuCode: string;
  barcodes: BarcodeInfo[];
}

export interface ProductDetail {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  categoryId: string;
  categoryName: string;
  trackingMode: "Serialized" | "Quantity";
  status: "Draft" | "Active" | "Inactive";
  availableForRetail: boolean;
  availableForReseller: boolean;
  variantAttributes: Attribute[];
  variants: VariantInfo[];
  createdAt: string;
}

export interface Label {
  barcodeId: string;
  code: string;
  kind: string;
  productName: string;
  variantName: string;
  skuCode: string;
  copies: number;
  isReprint: boolean;
}
