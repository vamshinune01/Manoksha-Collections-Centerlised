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
  kind: "Integer" | "Money" | "String" | "Boolean";
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
  vendorId?: string | null;
  vendorName?: string | null;
  productCode?: string | null;
}

/** A vendor that ships its products directly (ADR-001 §41). ownerMarginPct is only sent to the Owner. */
export interface Vendor {
  id: string;
  code: string;
  name: string;
  shippingFee: number;
  ownerMarginPct: number | null;
  isActive: boolean;
  productCount: number;
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
  outOfStock?: boolean;
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
  vendorId?: string | null;
  vendorCode?: string | null;
  vendorName?: string | null;
  productCode?: string | null;
}

/** One vendor's parcel of an order (ADR-001 §46). */
export interface Parcel {
  id: string;
  vendorId: string;
  vendorCode: string;
  vendorName: string;
  shippingFee: number;
  status: "Pending" | "OrderedFromVendor" | "Shipped" | "Delivered" | "Cancelled";
  vendorReference: string | null;
  courier: string | null;
  courierLabel: string | null;
  trackingNumber: string | null;
  shippedAt: string | null;
  deliveredOn: string | null;
  note: string | null;
}

export interface VendorDiscount { vendorId: string; vendorCode: string; vendorName: string; discountPct: number }

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
  vendorDiscounts?: VendorDiscount[];
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

export interface LedgerEntry {
  id: string;
  seq: number;
  createdAt: string;
  type: "Deposit" | "Debit" | "Reversal" | "Adjustment";
  direction: "Credit" | "Debit";
  amount: number;
  balanceBefore: number;
  balanceAfter: number;
  orderId: string | null;
  orderNumber: string | null;
  depositRequestId: string | null;
  reason: string | null;
}

export interface LedgerPage {
  balance: number;
  entries: LedgerEntry[];
  nextBeforeSeq: number | null;
}

export interface Deposit {
  id: string;
  number: string;
  resellerId: string;
  resellerNumber: string | null;
  resellerName: string | null;
  amount: number;
  method: string;
  reference: string;
  resellerNote: string | null;
  status: "Pending" | "Approved" | "Credited" | "Rejected";
  submittedAt: string;
  reviewedAt: string | null;
  reviewNote: string | null;
}

export interface OrderLine {
  id: string;
  skuId: string;
  skuCode: string;
  productName: string;
  variantName: string;
  quantity: number;
  retailUnitPrice: number;
  discountSource: string;
  discountPct: number;
  discountAmountPerUnit: number;
  finalUnitPrice: number;
  lineTotal: number;
  commercialTermVersion: number | null;
  parcelId?: string | null;
  productCode?: string | null;
}

export interface Order {
  id: string;
  number: string;
  channel: string;
  status: string;
  resellerId: string | null;
  fulfillmentBranchId: string | null;
  fulfillmentBranchName: string;
  delivery: { name: string; mobile: string; email: string | null; addressLine: string; city: string; state: string; pin: string };
  merchandiseTotal: number;
  shippingFee: number;
  grandTotal: number;
  createdAt: string;
  confirmedAt: string | null;
  lines: OrderLine[];
  history: { fromStatus: string | null; toStatus: string; note: string | null; occurredAt: string }[];
  helpWhatsAppUrl: string;
  costOfGoods: number | null;
  shipment: Shipment | null;
  openException: FulfillmentExceptionInfo | null;
  posSale?: PosSaleDetail | null;
  fulfillmentMode?: "Branch" | "Vendor";
  parcels?: Parcel[] | null;
}

export interface Shipment {
  courier: string;
  courierLabel: string;
  trackingNumber: string | null;
  shippedAt: string;
  deliveredOn: string | null;
}

export interface FulfillmentExceptionInfo {
  id: string;
  orderId: string;
  orderNumber: string;
  channel: string;
  branchId: string;
  branchName: string;
  reason: string;
  notes: string;
  status: string;
  raisedAt: string;
  resolvedAt: string | null;
  resolution: string | null;
  lines: { skuId: string; skuCode: string; item: string; missingQty: number; damagedQty: number }[];
}

export interface RerouteOption {
  branchId: string;
  branchName: string;
  priority: number;
  isActive: boolean;
  canFulfil: boolean;
  shortfalls: { skuId: string; requested: number; available: number }[];
}

export interface FulfillmentInquiry {
  id: string;
  reference: string;
  channel: string;
  resellerId: string | null;
  contactName: string | null;
  contactMobile: string | null;
  cart: string;
  evaluations: string;
  failureReason: string;
  status: string;
  createdAt: string;
  followUpNote: string | null;
  closedAt: string | null;
}

export interface ResellerCustomer {
  id: string;
  details: { name: string; mobile: string; email: string | null; addressLine: string; city: string; state: string; pin: string };
  createdAt: string;
  updatedAt: string;
}

// ---- Online payments (Phase 6) ----

export interface PaymentAttempt {
  id: string;
  purpose: "ORDER" | "WALLET_DEPOSIT";
  referenceId: string;
  referenceNumber: string;
  payerUserId: string;
  provider: string;
  amount: number;
  status: string;
  providerOrderRef: string | null;
  providerPaymentRef: string | null;
  initiatedAt: string;
  expiresAt: string;
  completedAt: string | null;
  failureReason: string | null;
  history: { fromStatus: string | null; toStatus: string; source: string; note: string | null; occurredAt: string }[];
}

export interface PaymentReconciliation {
  id: string;
  caseNumber: string;
  paymentAttemptId: string;
  provider: string;
  providerOrderRef: string | null;
  providerPaymentRef: string | null;
  expectedAmount: number;
  paidAmount: number | null;
  payerUserId: string;
  purpose: string;
  referenceId: string;
  referenceNumber: string;
  reasonCode: string;
  detail: string | null;
  status: string;
  ownerAction: string | null;
  externalRefundRef: string | null;
  notes: string | null;
  createdAt: string;
}

export const PAYMENT_TONE: Record<string, "green" | "amber" | "red" | "slate" | "brand"> = {
  SUCCESS: "green",
  ORDER_RECOVERED: "green",
  PENDING: "amber",
  INITIATED: "amber",
  LATE_SUCCESS_RECHECK: "amber",
  FAILED: "slate",
  EXPIRED: "slate",
  PAYMENT_RECONCILIATION_REQUIRED: "red",
};

export function orderStatusTone(status: string): "green" | "amber" | "red" | "slate" | "brand" {
  if (["Confirmed", "Processing", "Packed", "Shipped", "Delivered", "Completed"].includes(status)) return "green";
  if (status === "PaymentPending" || status === "FulfillmentException") return "amber";
  if (["PaymentFailed", "PaymentExpired", "Cancelled"].includes(status)) return "red";
  return "slate";
}

export interface PosSaleDetail {
  cashier: string;
  customerName: string | null;
  customerMobile: string | null;
  payments: { method: string; amount: number; reference: string | null }[];
  priceOverrides: {
    skuCode: string; quantity: number; originalUnitPrice: number; finalUnitPrice: number; discountPct: number; reason: string;
    approvalLevel: string; seller: string; approver: string | null; occurredAt: string;
  }[];
}

// ---- Phase 9: notifications, Exception Center, dashboards and reports ----

export type Severity = "CRITICAL" | "WARNING" | "INFO";

export interface NotificationItem { id: string; category: Severity; title: string; body: string; link: string | null; createdAt: string; read: boolean }
export interface Inbox { unread: number; items: NotificationItem[] }

export interface EmailDelivery {
  id: string; eventType: string; category: Severity; reference: string | null; toAddress: string; toName: string | null; recipientKind: string;
  subject: string; status: "Pending" | "Sent" | "Failed"; attempts: number; nextAttemptAt: string; lastError: string | null; sentAt: string | null; createdAt: string;
}
export interface EmailPreview { id: string; subject: string; toAddress: string; htmlBody: string; textBody: string }
export interface OperationalAlert {
  id: string; kind: string; severity: Severity; title: string; detail: string; reference: string | null; status: "Open" | "Resolved";
  createdAt: string; resolvedAt: string | null; resolutionNote: string | null;
}

export type ExceptionType =
  | "PAYMENT_RECONCILIATION" | "FULFILLMENT_EXCEPTION" | "UNFULFILLED_CHECKOUT" | "TRANSFER_DISCREPANCY" | "INVENTORY_DISCREPANCY"
  | "WALLET_DEPOSIT_PENDING" | "FAILED_NOTIFICATION" | "SENSITIVE_ALERT";
export interface ExceptionCount { type: ExceptionType; open: number }
export interface ExceptionItem {
  type: ExceptionType; id: string; reference: string; severity: Severity; branchId: string | null; branchName: string | null; createdAt: string; detail: string;
  relatedId: string | null;
}
export interface ExceptionCenter { counts: ExceptionCount[]; items: ExceptionItem[] }

export interface ChannelSales { channel: string; orders: number; units: number; revenue: number; shippingFees: number; cost: number | null }
export interface SalesBlock {
  orders: number; units: number; revenue: number; shippingFees: number; cost: number | null; grossProfit: number | null; marginPct: number | null; byChannel: ChannelSales[];
}
export interface LowStockRow { branchId: string; branchName: string; skuId: string; skuCode: string; productName: string; variantName: string; available: number }
export interface Dashboard {
  today: string;
  branches: { id: string; name: string }[];
  costVisible: boolean;
  salesToday: SalesBlock;
  salesMonth: SalesBlock;
  last14Days: { date: string; orders: number; revenue: number; grossProfit: number | null }[];
  branchSales: { branchId: string; branchName: string; ordersToday: number; revenueToday: number; ordersMonth: number; revenueMonth: number; grossProfitMonth: number | null }[];
  inventory: { availableUnits: number; reservedUnits: number; inTransitUnits: number; onHandUnits: number; stockValue: number | null };
  lowStockThreshold: number;
  lowStockCount: number;
  lowStock: LowStockRow[];
  operations: { transfersAwaitingApproval: number; transfersInProgress: number; adjustmentsPending: number; discrepanciesOpen: number; depositsPending: number | null };
  attendance: { clockedInNow: number; clockedInToday: number; activeEmployees: number };
  priceChanges: { at: string; skuCode: string; productName: string; oldPrice: number | null; newPrice: number; reason: string }[] | null;
  resellers: { active: number; frozen: number; walletBalances: number; topThisMonth: { resellerId: string; resellerNumber: string; name: string; orders: number; revenue: number }[] } | null;
  openExceptions: ExceptionCount[] | null;
}

export interface SalesReportRow {
  key: string; label: string; orders: number; units: number; revenue: number; shippingFees: number; cost: number | null; grossProfit: number | null; marginPct: number | null;
}
export interface SalesReport { from: string; to: string; groupBy: string; costVisible: boolean; rows: SalesReportRow[]; total: SalesReportRow }
export interface ProductSalesReport {
  from: string; to: string; costVisible: boolean;
  rows: { skuId: string; skuCode: string; productName: string; variantName: string; units: number; revenue: number; cost: number | null; grossProfit: number | null; marginPct: number | null }[];
}
export interface InventoryValuation {
  asOf: string; totalValue: number;
  branches: { branchId: string; branchName: string; costedUnits: number; value: number }[];
  rows: { branchId: string; branchName: string; skuId: string; skuCode: string; productName: string; variantName: string; availableUnits: number; onHandUnits: number; costedUnits: number; value: number }[];
}
export interface LowStockReport { threshold: number; rows: LowStockRow[] }
export interface ResellerReport {
  from: string; to: string;
  rows: { resellerId: string; resellerNumber: string; name: string; status: string; discountPct: number; orders: number; revenue: number; walletBalance: number; lastOrderAt: string | null }[];
}
