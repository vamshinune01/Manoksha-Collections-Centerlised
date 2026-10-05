"use client";

import { useState } from "react";
import { Alert, Badge, Button, Field, Input, Select, formatDateTime } from "@/components/ui";
import { inr } from "@/lib/access";
import { callApi } from "@/lib/client-api";
import type { OrderLine, Parcel } from "@/lib/types";
import { useAction } from "@/lib/use-action";

const STATUS: Record<Parcel["status"], { label: string; tone: "slate" | "amber" | "brand" | "green" | "red" }> = {
  Pending: { label: "To place with vendor", tone: "amber" },
  OrderedFromVendor: { label: "Placed with vendor", tone: "brand" },
  Shipped: { label: "Shipped", tone: "brand" },
  Delivered: { label: "Delivered", tone: "green" },
  Cancelled: { label: "Cancelled", tone: "red" },
};

/** Vendor orders (ADR-001 §46): each vendor ships its own parcel; the order status follows the parcels. */
export function ParcelsPanel({ orderId, status, parcels, lines, canFulfill, canCancel }: {
  orderId: string; status: string; parcels: Parcel[]; lines: OrderLine[]; canFulfill: boolean; canCancel: boolean;
}) {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState<{ parcel: string; action: "ordered" | "shipped" | "delivered" } | "cancel" | null>(null);
  const live = ["Confirmed", "Processing", "Shipped"].includes(status);
  const post = (parcelId: string, action: string, body: object) => run(() => callApi(`admin/orders/${orderId}/parcels/${parcelId}/${action}`, "POST", body)).then((r) => { if (r) setOpen(null); });

  return (
    <div className="space-y-4">
      {error && <Alert>{error}</Alert>}
      {parcels.map((p) => {
        const items = lines.filter((l) => l.parcelId === p.id);
        const form = open !== null && open !== "cancel" && open.parcel === p.id ? open.action : null;
        return (
          <div key={p.id} className="rounded-md border border-slate-200 p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="font-medium text-slate-900">{p.vendorName} <span className="text-xs text-slate-500">· shipping {inr(p.shippingFee)}</span></p>
              <Badge tone={STATUS[p.status].tone}>{STATUS[p.status].label}</Badge>
            </div>
            <ul className="mt-1 text-xs text-slate-600">
              {items.map((l) => <li key={l.id}>{l.productCode ?? l.skuCode} · {l.productName} ({l.variantName}) × {l.quantity}</li>)}
            </ul>
            <div className="mt-1 space-y-0.5 text-xs text-slate-500">
              {p.vendorReference && <p>Vendor ref: {p.vendorReference}</p>}
              {p.courierLabel && <p>Courier: {p.courierLabel}{p.trackingNumber ? ` · ${p.trackingNumber}` : ""} · {formatDateTime(p.shippedAt)}</p>}
              {p.deliveredOn && <p>Delivered {p.deliveredOn}</p>}
              {p.note && <p>Note: {p.note}</p>}
            </div>
            {canFulfill && live && (
              <div className="mt-2 flex flex-wrap gap-2">
                {p.status === "Pending" && <Button variant="secondary" disabled={busy} onClick={() => setOpen({ parcel: p.id, action: "ordered" })}>Placed with vendor…</Button>}
                {(p.status === "Pending" || p.status === "OrderedFromVendor") && <Button variant="secondary" disabled={busy} onClick={() => setOpen({ parcel: p.id, action: "shipped" })}>Shipped…</Button>}
                {p.status === "Shipped" && <Button variant="secondary" disabled={busy} onClick={() => setOpen({ parcel: p.id, action: "delivered" })}>Delivered…</Button>}
              </div>
            )}
            {form === "ordered" && (
              <form className="mt-3 grid gap-2 md:grid-cols-3" onSubmit={(e) => { e.preventDefault(); const f = new FormData(e.currentTarget); void post(p.id, "ordered", { vendorReference: f.get("ref") || null, note: f.get("note") || null }); }}>
                <Field label="Vendor's order / invoice no. (optional)"><Input name="ref" maxLength={100} /></Field>
                <Field label="Note (optional)"><Input name="note" maxLength={1000} /></Field>
                <div className="flex items-end gap-2"><Button type="submit" disabled={busy}>Save</Button><Button type="button" variant="ghost" onClick={() => setOpen(null)}>Cancel</Button></div>
              </form>
            )}
            {form === "shipped" && (
              <ShipForm busy={busy} onCancel={() => setOpen(null)} onSubmit={(body) => void post(p.id, "shipped", body)} />
            )}
            {form === "delivered" && (
              <form className="mt-3 grid gap-2 md:grid-cols-3" onSubmit={(e) => { e.preventDefault(); const f = new FormData(e.currentTarget); void post(p.id, "delivered", { deliveredOn: f.get("on") || null, note: f.get("note") || null }); }}>
                <Field label="Delivered on"><Input name="on" type="date" /></Field>
                <Field label="Note (optional)"><Input name="note" maxLength={1000} /></Field>
                <div className="flex items-end gap-2"><Button type="submit" disabled={busy}>Save</Button><Button type="button" variant="ghost" onClick={() => setOpen(null)}>Cancel</Button></div>
              </form>
            )}
          </div>
        );
      })}
      {canCancel && ["Confirmed", "Processing"].includes(status) && parcels.every((p) => p.status === "Pending" || p.status === "OrderedFromVendor") && (
        open === "cancel" ? (
          <form className="space-y-2 rounded-md border border-red-200 bg-red-50/40 p-3" onSubmit={(e) => {
            e.preventDefault();
            const f = new FormData(e.currentTarget);
            void run(() => callApi(`admin/orders/${orderId}/cancel`, "POST", { reason: f.get("reason"), lines: null })).then((r) => { if (r) setOpen(null); });
          }}>
            <p className="text-sm text-red-800">Cancels every parcel. A reseller&apos;s wallet is refunded automatically; an online payment opens a reconciliation case for the refund.</p>
            <Field label="Reason"><Input name="reason" required /></Field>
            <div className="flex gap-2"><Button type="submit" variant="danger" disabled={busy}>Cancel order</Button><Button type="button" variant="ghost" onClick={() => setOpen(null)}>Keep order</Button></div>
          </form>
        ) : <Button variant="danger" disabled={busy} onClick={() => setOpen("cancel")}>Cancel order…</Button>
      )}
      {!canFulfill && <p className="text-xs text-slate-500">Only the Owner updates vendor parcels.</p>}
    </div>
  );
}

function ShipForm({ busy, onSubmit, onCancel }: { busy: boolean; onSubmit: (body: object) => void; onCancel: () => void }) {
  const [courier, setCourier] = useState("XPRESSBEES");
  return (
    <form className="mt-3 grid gap-2 md:grid-cols-4" onSubmit={(e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      onSubmit({ courier, courierName: f.get("courierName") || null, trackingNumber: f.get("tracking") || null, note: null });
    }}>
      <Field label="Courier"><Select value={courier} onChange={(e) => setCourier(e.target.value)}><option value="XPRESSBEES">Xpressbees</option><option value="DELHIVERY">Delhivery</option><option value="OTHER">Other</option></Select></Field>
      {courier === "OTHER" && <Field label="Courier name"><Input name="courierName" required maxLength={100} /></Field>}
      <Field label="Tracking number (optional)"><Input name="tracking" maxLength={100} /></Field>
      <div className="flex items-end gap-2"><Button type="submit" disabled={busy}>Save</Button><Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button></div>
    </form>
  );
}
