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
}

export interface CatalogItem {
  skuId: string;
  skuCode: string;
  productId: string;
  productName: string;
  variantName: string;
  categoryName: string;
  retailPrice: number;
  discountSource: "RESELLER" | "PRODUCT_RESELLER";
  discountPct: number;
  resellerPrice: number;
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
}

export interface Order {
  id: string;
  number: string;
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
}

export const inr = (v: number | null | undefined) =>
  v == null ? "—" : new Intl.NumberFormat("en-IN", { style: "currency", currency: "INR", minimumFractionDigits: 2 }).format(v);
