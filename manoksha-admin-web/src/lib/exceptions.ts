import type { ExceptionType } from "./types";

export const EXCEPTION_LABELS: Record<ExceptionType, string> = {
  PAYMENT_RECONCILIATION: "Late payment / inventory gone",
  FULFILLMENT_EXCEPTION: "Fulfillment exception",
  UNFULFILLED_CHECKOUT: "Unfulfilled checkout",
  TRANSFER_DISCREPANCY: "Transfer discrepancy",
  INVENTORY_DISCREPANCY: "Inventory discrepancy",
  WALLET_DEPOSIT_PENDING: "Wallet deposit pending",
  FAILED_NOTIFICATION: "Failed notification",
  SENSITIVE_ALERT: "Sensitive alert",
};

