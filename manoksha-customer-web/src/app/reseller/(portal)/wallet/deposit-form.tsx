"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Field, Input, Select } from "@/components/ui";
import { ApiError, rs } from "@/lib/client-api";

export function DepositForm() {
  const router = useRouter();
  const [error, setError] = useState<string>();
  const [done, setDone] = useState<string>();
  const [busy, setBusy] = useState(false);
  return (
    <form className="grid gap-3 md:grid-cols-2" onSubmit={async (e) => {
      e.preventDefault();
      const form = e.currentTarget;
      setBusy(true);
      setError(undefined);
      try {
        const r = await rs<{ number: string }>("wallet/deposits", "POST", new FormData(form));
        setDone(`Deposit request ${r.number} sent. It will be credited after Manoksha Collections verifies the payment.`);
        form.reset();
        router.refresh();
      } catch (err) {
        setError(err instanceof ApiError ? err.message : "Could not submit the deposit.");
      } finally {
        setBusy(false);
      }
    }}>
      <p className="text-sm text-slate-600 md:col-span-2">Pay Manoksha Collections by PhonePe/UPI or bank transfer, then submit the payment reference and a screenshot. Both are required.</p>
      {error && <div className="md:col-span-2"><Alert>{error}</Alert></div>}
      {done && <div className="md:col-span-2"><Alert tone="success">{done}</Alert></div>}
      <Field label="Amount paid (₹)"><Input name="amount" type="number" min={1} step="0.01" required /></Field>
      <Field label="Paid via"><Select name="method"><option value="PHONEPE">PhonePe</option><option value="UPI">Other UPI app</option><option value="BANK_TRANSFER">Bank transfer</option></Select></Field>
      <Field label="Transaction reference (UTR)"><Input name="reference" required minLength={6} /></Field>
      <Field label="Payment screenshot" hint="JPEG, PNG, WEBP or PDF, up to 5 MB."><Input name="proof" type="file" accept="image/jpeg,image/png,image/webp,application/pdf" required /></Field>
      <div className="md:col-span-2"><Field label="Note (optional)"><Input name="note" /></Field></div>
      <div><Button type="submit" disabled={busy}>{busy ? "Sending…" : "Submit deposit request"}</Button></div>
    </form>
  );
}
