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

export interface SkuInfo {
  skuId: string;
  skuCode: string;
  variantId: string;
  variantName: string;
  variantActive: boolean;
  productId: string;
  productName: string;
  trackingMode: "Serialized" | "Quantity";
  productStatus: string;
  availableForRetail: boolean;
  availableForReseller: boolean;
}

export interface Supplier {
  id: string;
  code: string;
  name: string;
  contactName: string | null;
  mobile: string | null;
  email: string | null;
  gstin: string | null;
  address: string | null;
  isActive: boolean;
}

export interface PoLine {
  id: string;
  skuId: string;
  skuCode: string;
  productName: string;
  variantName: string;
  trackingMode: string;
  orderedQty: number;
  expectedUnitCost: number | null;
  receivedQty: number;
  damagedQty: number;
  remainingQty: number;
}

export interface PurchaseOrder {
  id: string;
  number: string;
  status: "Draft" | "Issued" | "PartiallyReceived" | "Received" | "Closed" | "Cancelled";
  supplierId: string;
  supplierName: string;
  receivingBranchId: string;
  receivingBranchName: string;
  supplierReference: string | null;
  expectedDate: string | null;
  notes: string | null;
  createdAt: string;
  issuedAt: string | null;
  closedAt: string | null;
  closeReason: string | null;
  expectedTotal: number | null;
  lines: PoLine[];
}

export interface GoodsReceipt {
  id: string;
  number: string;
  purchaseOrderId: string;
  purchaseOrderNumber: string;
  supplierName: string;
  branchId: string;
  branchName: string;
  supplierInvoiceRef: string;
  receivedAt: string;
  notes: string | null;
  totalCost: number | null;
  lines: { id: string; skuCode: string; productName: string; variantName: string; receivedQty: number; damagedQty: number; acceptedQty: number; unitCost: number | null }[];
  items: { itemId: string; barcodeId: string; barcode: string; skuId: string; status: string }[];
}

export interface StockRow {
  branchId: string;
  branchName: string;
  skuId: string;
  skuCode: string;
  productName: string;
  variantName: string;
  trackingMode: string;
  byStatus: Record<string, number>;
  available: number;
  onHand: number;
}

export interface InventoryItem {
  id: string;
  skuId: string;
  branchId: string;
  branchName: string;
  status: string;
  barcode: string;
  receivedAt: string;
  writtenOff: boolean;
}

export interface Movement {
  seq: number;
  occurredAt: string;
  skuId: string;
  itemId: string | null;
  quantity: number;
  fromBranchId: string | null;
  toBranchId: string | null;
  fromStatus: string | null;
  toStatus: string | null;
  movementType: string;
  referenceType: string;
  referenceNumber: string | null;
  reason: string | null;
}

export interface TransferLine {
  id: string;
  skuId: string;
  skuCode: string;
  productName: string;
  variantName: string;
  serialized: boolean;
  requestedQty: number;
  preparedQty: number;
  dispatchedQty: number;
  receivedQty: number;
  resolvedQty: number;
  outstandingQty: number;
  items: { itemId: string; barcode: string; outcome: string | null }[];
}

export interface Transfer {
  id: string;
  number: string;
  status: "Requested" | "Approved" | "Prepared" | "InTransit" | "Received" | "Discrepancy" | "Rejected" | "Cancelled";
  sourceBranchId: string;
  sourceBranchName: string;
  destinationBranchId: string;
  destinationBranchName: string;
  reason: string;
  requestedBy: string;
  requestedAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  ownerSelfAuthorized: boolean;
  decisionNote: string | null;
  preparedAt: string | null;
  dispatchedAt: string | null;
  receivedAt: string | null;
  lines: TransferLine[];
}

export interface Discrepancy {
  id: string;
  number: string;
  sourceType: "TRANSFER" | "COUNT";
  sourceId: string;
  sourceNumber: string;
  branchId: string;
  branchName: string;
  skuId: string;
  skuCode: string;
  productName: string;
  expectedQty: number;
  actualQty: number;
  variance: number;
  outstandingQty: number;
  missingItemIds: string[];
  status: "Open" | "Resolved";
  resolution: string | null;
  resolutionNotes: string | null;
  createdAt: string;
  resolvedAt: string | null;
}

export interface StockCount {
  id: string;
  number: string;
  branchId: string;
  branchName: string;
  status: "Open" | "Submitted";
  notes: string | null;
  createdAt: string;
  submittedAt: string | null;
  lines: { skuId: string; skuCode: string; productName: string; variantName: string; countedQty: number | null; systemQty: number | null; variance: number | null }[];
  discrepancyIds: string[];
}

export interface Adjustment {
  id: string;
  number: string;
  branchId: string;
  branchName: string;
  skuId: string;
  skuCode: string;
  productName: string;
  kind: "StatusChange" | "WriteOff" | "Found";
  fromStatus: string | null;
  toStatus: string | null;
  quantity: number;
  itemIds: string[];
  reasonCode: string;
  notes: string;
  discrepancyId: string | null;
  status: "Pending" | "Applied" | "Rejected";
  requestedBy: string;
  requestedAt: string;
  decidedBy: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
  unitCost: number | null;
  valueAtCost: number | null;
  estimatedValue: number | null;
  requiresOwner: boolean | null;
}

export interface ResellerSummary {
  id: string;
  resellerNumber: string;
  contactName: string;
  businessName: string | null;
  mobile: string;
  city: string;
  status: "Pending" | "Active" | "Frozen" | "Suspended" | "Closed";
  currentDiscountPct: number;
  walletBalance: number;
  createdAt: string;
  activatedAt: string | null;
}

export interface CommercialTerm {
  id: string;
  version: number;
  discountPct: number;
  notes: string | null;
  reason: string;
  effectiveFrom: string;
  isCurrent: boolean;
}

export interface ResellerDetail {
  id: string;
  resellerNumber: string;
  userId: string;
  mobile: string;
  status: ResellerSummary["status"];
  profile: { contactName: string; businessName: string | null; email: string; addressLine: string; city: string; state: string; pin: string; notes: string | null };
  walletBalance: number;
  currentTerms: CommercialTerm;
  termsHistory: CommercialTerm[];
  statusHistory: { fromStatus: string | null; toStatus: string; reason: string; actorUserId: string | null; occurredAt: string }[];
  createdAt: string;
  activatedAt: string | null;
}

export interface SkuPrice {
  skuId: string;
  skuCode: string;
  productId: string;
  productName: string;
  variantName: string;
  productStatus: string;
  availableForRetail: boolean;
  availableForReseller: boolean;
  retailPrice: number | null;
  priceSince: string | null;
  productResellerDiscountPct: number | null;
}

export interface PriceHistory {
  id: string;
  price: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  reason: string;
}

export interface ProductDiscount {
  productId: string;
  currentDiscountPct: number | null;
  history: { id: string; discountPct: number; effectiveFrom: string; effectiveTo: string | null; reason: string; endReason: string | null }[];
}

export interface PricePreview {
  termsVersion: number;
  resellerDiscountPct: number;
  productDiscountPct: number | null;
  retailPrice: number;
  appliedSource: string;
  appliedPct: number;
  finalUnitPrice: number;
}
