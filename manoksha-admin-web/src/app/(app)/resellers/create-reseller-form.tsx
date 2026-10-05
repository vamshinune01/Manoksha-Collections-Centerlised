"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Card, Field, Input, Textarea } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import { useAction } from "@/lib/use-action";
import type { Vendor } from "@/lib/types";
import { VendorPercentages, readVendorPercentages } from "./vendor-percentages";

export function CreateResellerForm({ vendors }: { vendors: Vendor[] }) {
  const router = useRouter();
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  if (!open) return <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)}>Add new reseller</Button></div>;
  return (
    <Card title="Add new reseller" className="mb-6" actions={<Button variant="ghost" onClick={() => setOpen(false)}>Close</Button>}>
      <form className="grid gap-4 md:grid-cols-3" onSubmit={async (e) => {
        e.preventDefault();
        const f = new FormData(e.currentTarget);
        const r = await run(() => callApi<{ id: string }>("admin/resellers", "POST", {
          contactName: f.get("contactName"), businessName: f.get("businessName") || null, mobile: f.get("mobile"), email: f.get("email"),
          addressLine: f.get("addressLine"), city: f.get("city"), state: f.get("state"), pin: f.get("pin"),
          resellerDiscountPct: Number(f.get("discount") || 0), notes: f.get("notes") || null, reason: f.get("reason"),
          vendorDiscounts: readVendorPercentages(f),
        }));
        if (r) router.push(`/resellers/${r.id}`);
      }}>
        {error && <div className="md:col-span-3"><Alert>{error}</Alert></div>}
        <Field label="Reseller name"><Input name="contactName" required /></Field>
        <Field label="Business / shop name"><Input name="businessName" /></Field>
        <Field label="Mobile number" hint="Login identity; verified by OTP; cannot be changed later."><Input name="mobile" inputMode="tel" required /></Field>
        <Field label="Email"><Input name="email" type="email" required /></Field>
        <Field label="Address"><Input name="addressLine" required /></Field>
        <Field label="City"><Input name="city" required /></Field>
        <Field label="State"><Input name="state" required defaultValue="Telangana" /></Field>
        <Field label="PIN"><Input name="pin" inputMode="numeric" maxLength={6} required /></Field>
        <div className="md:col-span-3">
          <Field label="Discount per vendor" hint="What this reseller gets off retail for each vendor's products. Leave empty for no discount on that vendor.">
            <VendorPercentages vendors={vendors} />
          </Field>
        </div>
        <Field label="Discount on own-stock products %" hint="Only for products without a vendor (store stock)."><Input name="discount" type="number" min={0} max={100} step="0.01" defaultValue={0} /></Field>
        <div className="md:col-span-2"><Field label="Notes"><Textarea name="notes" rows={2} /></Field></div>
        <Field label="Reason"><Input name="reason" required defaultValue="New reseller" /></Field>
        <div className="md:col-span-3"><p className="mb-3 text-xs text-slate-500">A prepaid wallet is created with ₹0 — opening balances cannot be typed in.</p>
          <Button type="submit" disabled={busy}>Create reseller (Pending)</Button></div>
      </form>
    </Card>
  );
}
