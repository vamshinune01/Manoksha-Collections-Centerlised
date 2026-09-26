"use client";

import { useState } from "react";
import { Alert, Button, Field, Input, Select, Textarea } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import { useAction } from "@/lib/use-action";

const NEXT: Record<string, { value: string; label: string }[]> = {
  Open: [
    { value: "NOTE", label: "Add a note" },
    { value: "REFUND_INITIATED", label: "Refund initiated" },
    { value: "REFUND_COMPLETED", label: "Refund completed" },
    { value: "RESOLVED", label: "Resolved (no refund needed)" },
  ],
  RefundInitiated: [
    { value: "NOTE", label: "Add a note" },
    { value: "REFUND_COMPLETED", label: "Refund completed" },
    { value: "RESOLVED", label: "Resolved" },
  ],
  RefundCompleted: [
    { value: "NOTE", label: "Add a note" },
    { value: "RESOLVED", label: "Resolved" },
  ],
};
const NOTE_ONLY = [{ value: "NOTE", label: "Add a note" }];

export function ReconciliationActions({ id, status }: { id: string; status: string }) {
  const options = NEXT[status] ?? NOTE_ONLY;
  const [action, setAction] = useState("NOTE");
  const [note, setNote] = useState("");
  const [refundRef, setRefundRef] = useState("");
  const { error, busy, run } = useAction();
  return (
    <form className="space-y-3" onSubmit={async (e) => {
      e.preventDefault();
      const ok = await run(() => callApi(`admin/payment-reconciliations/${id}/actions`, "POST", { action, note, externalRefundRef: refundRef || null }));
      if (ok) { setNote(""); setRefundRef(""); }
    }}>
      {error && <Alert>{error}</Alert>}
      <Field label="Action"><Select value={action} onChange={(e) => setAction(e.target.value)}>{options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</Select></Field>
      {action === "REFUND_COMPLETED" && (
        <Field label="External refund reference" hint="From the bank or payment gateway."><Input value={refundRef} onChange={(e) => setRefundRef(e.target.value)} required /></Field>
      )}
      <Field label="What was done / decided"><Textarea value={note} onChange={(e) => setNote(e.target.value)} rows={3} required minLength={3} /></Field>
      <Button type="submit" disabled={busy}>{busy ? "Saving…" : "Record"}</Button>
      <p className="text-xs text-slate-500">No money is moved by this app — refunds are made outside it and recorded here.</p>
    </form>
  );
}
