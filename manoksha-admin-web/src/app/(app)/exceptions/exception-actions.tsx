"use client";

import Link from "next/link";
import { Button } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { ExceptionItem } from "@/lib/types";
import { useAction } from "@/lib/use-action";

/** Where each exception is acted on (design §19): inline for follow-ups, retries and alerts; the domain page for the rest. */
export function ExceptionAction({ item, canManage }: { item: ExceptionItem; canManage: boolean }) {
  const { error, busy, run } = useAction();
  const note = (question: string) => window.prompt(question)?.trim();
  let action: React.ReactNode;
  switch (item.type) {
    case "PAYMENT_RECONCILIATION":
      action = <Link className="text-brand-700 hover:underline" href={`/payments/${item.id}`}>Open case</Link>;
      break;
    case "FULFILLMENT_EXCEPTION":
      action = <Link className="text-brand-700 hover:underline" href={`/orders/${item.relatedId}`}>Reroute or resolve</Link>;
      break;
    case "TRANSFER_DISCREPANCY":
    case "INVENTORY_DISCREPANCY":
      action = <Link className="text-brand-700 hover:underline" href="/inventory/discrepancies">Resolve</Link>;
      break;
    case "WALLET_DEPOSIT_PENDING":
      action = <Link className="text-brand-700 hover:underline" href="/wallet-deposits">Approve or reject</Link>;
      break;
    case "UNFULFILLED_CHECKOUT":
      action = canManage ? (
        <Button variant="secondary" disabled={busy} onClick={() => {
          const text = note("How did you follow up with the customer? (saved with the inquiry)");
          if (text) void run(() => callApi(`admin/fulfillment-inquiries/${item.id}/close`, "POST", { note: text }));
        }}>Mark followed up</Button>
      ) : <Link className="text-brand-700 hover:underline" href="/orders/inquiries">View</Link>;
      break;
    case "FAILED_NOTIFICATION":
      action = canManage ? (
        <span className="flex gap-2">
          <Link className="text-brand-700 hover:underline" href={`/notifications/emails/${item.id}`}>View email</Link>
          <Button variant="secondary" disabled={busy} onClick={() => void run(() => callApi(`admin/notifications/emails/${item.id}/retry`, "POST"))}>Retry</Button>
        </span>
      ) : null;
      break;
    case "SENSITIVE_ALERT":
      action = canManage ? (
        <Button variant="secondary" disabled={busy} onClick={() => {
          const text = note("What follow-up was done? (recorded in the audit log)");
          if (text) void run(() => callApi(`admin/alerts/${item.id}/resolve`, "POST", { note: text }));
        }}>Resolve</Button>
      ) : null;
      break;
  }
  return (
    <span className="inline-flex flex-col items-end gap-1 text-sm">
      {action}
      {error && <span className="text-xs text-red-600">{error}</span>}
    </span>
  );
}
