"use client";

import { Button } from "@/components/ui";
import { inr } from "@/lib/access";
import { callApi } from "@/lib/client-api";
import type { Deposit } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function DepositDecision({ deposit }: { deposit: Deposit }) {
  const { error, busy, run } = useAction();
  return (
    <span className="inline-flex flex-col items-end gap-1">
      <span className="flex gap-1">
        <Button disabled={busy} onClick={() => {
          if (window.confirm(`Credit ${inr(deposit.amount)} to ${deposit.resellerName}'s wallet? Confirm you have seen this money arrive (ref ${deposit.reference}).`)) {
            void run(() => callApi(`admin/wallet/deposits/${deposit.id}/approve`, "POST", { note: null }));
          }
        }}>Approve &amp; credit</Button>
        <Button variant="ghost" disabled={busy} onClick={() => {
          const reason = window.prompt("Reason for rejecting (shown to the reseller)?");
          if (reason) void run(() => callApi(`admin/wallet/deposits/${deposit.id}/reject`, "POST", { reason }));
        }}>Reject</Button>
      </span>
      {error && <span className="text-xs text-red-600">{error}</span>}
    </span>
  );
}
