export interface ResellerMe {
  resellerNumber: string;
  contactName: string;
  businessName: string | null;
  mobile: string;
  email: string;
  status: "Active" | "Frozen" | "Closed" | "Pending" | "Suspended";
  canPlaceOrders: boolean;
  resellerDiscountPct: number;
  termsVersion: number;
  walletBalance: number;
  vendorDiscounts?: VendorDiscount[];
}

/** The reseller's % for one vendor's products (ADR-001 §45). */
export interface VendorDiscount { vendorId: string; vendorCode: string; vendorName: string; discountPct: number }

export interface StoreVendor { id: string; code: string; name: string }

export interface CatalogItem {
  skuId: string;
  skuCode: string;
  productId: string;
  productName: string;
  variantName: string;
  categoryName: string;
  retailPrice: number;
  discountSource: "RESELLER" | "PRODUCT_RESELLER" | "RESELLER_VENDOR" | "NONE";
  discountPct: number;
  resellerPrice: number;
  image?: ImageUrls | null;
  vendorId?: string | null;
  vendorName?: string | null;
  productCode?: string | null;
  outOfStock?: boolean;
}

export interface CatalogPage {
  items: CatalogItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Quote {
  skuId: string;
  retailPrice: number;
  discountSource: string;
  discountPct: number;
  finalUnitPrice: number;
}

export interface Delivery {
  name: string;
  mobile: string;
  email: string | null;
  addressLine: string;
  city: string;
  state: string;
  pin: string;
}

export interface OrderLine {
  id: string;
  skuCode: string;
  productName: string;
  variantName: string;
  quantity: number;
  retailUnitPrice: number;
  discountSource: string;
  discountPct: number;
  finalUnitPrice: number;
  lineTotal: number;
  parcelId?: string | null;
  productCode?: string | null;
}

/** One vendor's parcel of an order: shipped separately with its own courier and tracking (ADR-001 §46). */
export interface Parcel {
  id: string;
  vendorName: string;
  shippingFee: number;
  status: "Pending" | "OrderedFromVendor" | "Shipped" | "Delivered" | "Cancelled";
  courierLabel: string | null;
  trackingNumber: string | null;
  shippedAt: string | null;
  deliveredOn: string | null;
}

export interface Order {
  id: string;
  number: string;
  channel?: string;
  status: string;
  fulfillmentBranchName: string;
  delivery: Delivery;
  merchandiseTotal: number;
  shippingFee: number;
  grandTotal: number;
  createdAt: string;
  lines: OrderLine[];
  history: { toStatus: string; note: string | null; occurredAt: string }[];
  helpWhatsAppUrl: string;
  shipment?: Shipment | null;
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

export interface CheckoutResult {
  outcome: "CONFIRMED" | "UNFULFILLABLE";
  order: Order | null;
  walletBalance: number | null;
  inquiry: { reference: string; message: string; whatsAppUrl: string } | null;
}

export interface LedgerEntry {
  id: string;
  createdAt: string;
  type: string;
  direction: "Credit" | "Debit";
  amount: number;
  balanceAfter: number;
  orderNumber: string | null;
  reason: string | null;
}

export interface Deposit {
  id: string;
  number: string;
  amount: number;
  method: string;
  reference: string;
  status: string;
  submittedAt: string;
  reviewNote: string | null;
}

export interface SavedCustomer {
  id: string;
  details: Delivery;
}

export interface Term {
  version: number;
  discountPct: number;
  notes: string | null;
  effectiveFrom: string;
  isCurrent: boolean;
  vendorDiscounts?: VendorDiscount[];
}

// ---- Storefront & online orders (Phase 6) ----

export interface CustomerMe {
  userId: string;
  accountType: string;
  displayName: string;
  email: string | null;
  mobile: string | null;
}

export interface CustomerProfile {
  fullName: string;
  email: string | null;
  mobile: string | null;
  memberSince: string;
}

export interface OrderCharges {
  shippingFeePerOrder: number;
}

/** Optimized WebP renditions (never the original upload). */
export interface ImageUrls {
  thumb: string;
  medium: string;
  large: string;
  altText: string | null;
  width: number;
  height: number;
}

export interface PublicMedia {
  id: string;
  kind: "Image" | "Video";
  image: ImageUrls | null;
  videoUrl: string | null;
  posterUrl: string | null;
  durationSeconds: number | null;
  altText: string | null;
}

export interface StoreItem {
  skuId: string;
  skuCode: string;
  productId: string;
  productName: string;
  variantName: string;
  categoryName: string;
  price: number;
  inStock: boolean;
  image: ImageUrls | null;
  retailPrice?: number | null;
  discountPct?: number;
  vendorId?: string | null;
  vendorName?: string | null;
  productCode?: string | null;
}

export interface StorePage {
  items: StoreItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface StoreCategory {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
}

export interface StoreProduct {
  productId: string;
  productName: string;
  variants: { skuId: string; skuCode: string; variantName: string; price: number; inStock: boolean; retailPrice?: number | null; discountPct?: number }[];
  media: PublicMedia[];
  vendorName?: string | null;
  productCode?: string | null;
}

/** Display totals for a cart: lines grouped by vendor, each vendor's shipping (ADR-001 §46). Checkout re-prices. */
export interface CartSummary {
  lines: {
    skuId: string; quantity: number; sellable: boolean; inStock: boolean; productName: string | null; variantName: string | null; productCode: string | null;
    vendorId: string | null; vendorName: string | null; retailPrice: number | null; discountPct: number; unitPrice: number | null; lineTotal: number; message: string | null;
  }[];
  groups: { vendorId: string | null; vendorName: string; itemsTotal: number; shippingFee: number }[];
  merchandise: number;
  shipping: number;
  total: number;
}

export interface CartQuoteLine {
  skuId: string;
  sellable: boolean;
  productName: string | null;
  variantName: string | null;
  price: number | null;
  inStock: boolean;
  message: string | null;
}

export interface OnlinePayment {
  attemptId: string;
  status: string;
  amount: number;
  redirectUrl: string | null;
  expiresAt: string;
  completedAt: string | null;
  message: string;
}

export interface Inquiry {
  reference: string;
  message: string;
  whatsAppUrl: string;
}

export interface CustomerCheckoutResult {
  outcome: "PAYMENT_PENDING" | "PAYMENT_NOT_COMPLETED" | "ORDER_PLACED" | "UNFULFILLABLE";
  order: Order | null;
  payment: OnlinePayment | null;
  inquiry: Inquiry | null;
}

export interface OrderPaymentStatus {
  orderId: string;
  orderNumber: string;
  orderStatus: string;
  payment: OnlinePayment | null;
}

export interface OnlineDeposit {
  id: string;
  number: string;
  amount: number;
  status: "Pending" | "Credited" | "Failed" | "Expired";
  createdAt: string;
  completedAt: string | null;
  providerPaymentRef: string | null;
  payment: { attemptId: string; status: string; redirectUrl: string | null; expiresAt: string } | null;
}

/** Customer-facing wording for order statuses (SPEC §27.1); customers never get a cancel action (SPEC §21). */
export const ORDER_STATUS_LABEL: Record<string, string> = {
  PaymentPending: "Awaiting payment",
  PaymentFailed: "Payment not completed",
  PaymentExpired: "Payment window ended",
  Confirmed: "Confirmed",
  Processing: "Processing",
  Packed: "Packed",
  Shipped: "Shipped",
  Delivered: "Delivered",
  FulfillmentException: "Being resolved",
  Cancelled: "Cancelled",
};

export function orderStatusTone(status: string): "green" | "amber" | "red" | "slate" | "brand" {
  if (["Confirmed", "Processing", "Packed", "Shipped", "Delivered"].includes(status)) return "green";
  if (status === "PaymentPending") return "amber";
  if (["PaymentFailed", "PaymentExpired", "Cancelled"].includes(status)) return "red";
  return "slate";
}

export const inr = (v: number | null | undefined) =>
  v == null ? "—" : new Intl.NumberFormat("en-IN", { style: "currency", currency: "INR", minimumFractionDigits: 2 }).format(v);

/** The signed-in reseller's own figures (GET /reseller/dashboard). */
export interface ResellerDashboard {
  ordersThisMonth: number;
  spentThisMonth: number;
  openOrders: number;
  deliveredThisMonth: number;
  walletBalance: number;
  spentAllTime: number;
  ordersAllTime: number;
}
