"use client";

import { useState } from "react";
import { SkuPicker } from "@/components/sku-picker";
import { Alert, Button, Field, Input } from "@/components/ui";
import { inr } from "@/lib/access";
import { ApiError, callApi } from "@/lib/client-api";
import type { PricePreview as Preview, ResellerDetail, SkuInfo } from "@/lib/types";
import { useAction } from "@/lib/use-action";

const NEXT: Record<string, { to: string; label: string; tone: "primary" | "secondary" | "danger" }[]> = {
  Pending: [{ to: "Closed", label: "Close", tone: "danger" }],
  Active: [{ to: "Frozen", label: "Freeze", tone: "secondary" }, { to: "Suspended", label: "Suspend", tone: "danger" }, { to: "Closed", label: "Close", tone: "danger" }],
  Frozen: [{ to: "Active", label: "Reactivate", tone: "primary" }, { to: "Closed", label: "Close", tone: "danger" }],
  Suspended: [{ to: "Active", label: "Reactivate", tone: "primary" }, { to: "Closed", label: "Close", tone: "danger" }],
  Closed: [],
};

export function ResellerStatusActions({ reseller }: { reseller: ResellerDetail }) {
  const { error, busy, run } = useAction();
  return (
    <span className="ml-auto flex items-center gap-2">
      {error && <span className="text-sm text-red-600">{error}</span>}
      {(NEXT[reseller.status] ?? []).map((n) => (
        <Button key={n.to} variant={n.tone} disabled={busy} onClick={() => {
          const reason = window.prompt(`Reason to ${n.label.toLowerCase()} this reseller?`);
          if (reason) void run(() => callApi(`admin/resellers/${reseller.id}/status`, "POST", { status: n.to, reason }));
        }}>{n.label}</Button>
      ))}
    </span>
  );
}

export function TermsForm({ reseller }: { reseller: ResellerDetail }) {
  const { error, busy, run } = useAction();
  return (
    <form className="mt-5 grid gap-3 border-t border-slate-100 pt-4 md:grid-cols-4" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      const ok = await run(() => callApi(`admin/resellers/${reseller.id}/commercial-terms`, "POST", {
        resellerDiscountPct: Number(f.get("pct")), notes: f.get("notes") || null, reason: f.get("reason"),
      }));
      if (ok) (e.target as HTMLFormElement).reset();
    }}>
      <p className="text-sm font-medium text-slate-700 md:col-span-4">New terms version (applies to future orders only)</p>
      {error && <div className="md:col-span-4"><Alert>{error}</Alert></div>}
      <Field label="Discount %"><Input name="pct" type="number" min={0} max={100} step="0.01" required defaultValue={reseller.currentTerms.discountPct} /></Field>
      <Field label="Notes"><Input name="notes" /></Field>
      <Field label="Reason"><Input name="reason" required /></Field>
      <div className="flex items-end"><Button type="submit" disabled={busy}>Save new version</Button></div>
    </form>
  );
}

export function PricePreview({ resellerId }: { resellerId: string }) {
  const [sku, setSku] = useState<SkuInfo | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState<string>();
  return (
    <div className="space-y-2 text-sm">
      <SkuPicker placeholder="Check this reseller's price for…" onPick={async (s) => {
        setSku(s); setPreview(null); setError(undefined);
        try { setPreview(await callApi<Preview>(`admin/pricing/preview?resellerId=${resellerId}&skuId=${s.skuId}`)); }
        catch (e) { setError(e instanceof ApiError ? e.message : "Unavailable"); }
      }} />
      {sku && <p className="font-medium">{sku.productName} · {sku.variantName}</p>}
      {error && <p className="text-red-600">{error}</p>}
      {preview && (
        <dl className="space-y-0.5">
          <div>Retail: {inr(preview.retailPrice)}</div>
          <div>Reseller discount (v{preview.termsVersion}): {preview.resellerDiscountPct}%</div>
          <div>Product reseller discount: {preview.productDiscountPct == null ? "—" : `${preview.productDiscountPct}%`}</div>
          <div className="font-semibold">Applied {preview.appliedPct}% ({preview.appliedSource === "PRODUCT_RESELLER" ? "product override" : "reseller"}) → {inr(preview.finalUnitPrice)}</div>
        </dl>
      )}
    </div>
  );
}
