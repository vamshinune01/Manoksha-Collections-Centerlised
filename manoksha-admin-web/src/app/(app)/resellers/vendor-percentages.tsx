"use client";

import { Input } from "@/components/ui";
import type { Vendor, VendorDiscount } from "@/lib/types";

/** The reseller's % for each vendor (ADR-001 §45). Empty = no discount on that vendor's products (full retail). */
export function VendorPercentages({ vendors, current }: { vendors: Vendor[]; current?: VendorDiscount[] }) {
  if (vendors.length === 0) return <p className="text-xs text-slate-500">No vendors yet (Catalog → Vendors).</p>;
  return (
    <div className="overflow-hidden rounded-md border border-slate-200">
      <table className="w-full text-sm">
        <thead className="bg-slate-50 text-left text-xs text-slate-500"><tr><th className="px-3 py-1.5">Vendor</th><th className="px-3 py-1.5">Reseller gets (% off retail)</th></tr></thead>
        <tbody className="divide-y divide-slate-100">
          {vendors.map((v) => (
            <tr key={v.id}>
              <td className="px-3 py-1.5">{v.name} <span className="font-mono text-xs text-slate-400">{v.code}</span></td>
              <td className="px-3 py-1.5">
                <Input name={`vendor:${v.id}`} type="number" min={0} max={100} step="0.01" className="w-28" placeholder="0"
                  defaultValue={current?.find((c) => c.vendorId === v.id)?.discountPct ?? ""} aria-label={`${v.name} percentage`} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Reads the vendor % inputs of a form: only vendors with a percentage above 0 are sent. */
export function readVendorPercentages(f: FormData) {
  return [...f.entries()]
    .filter(([k, v]) => k.startsWith("vendor:") && String(v).trim() !== "" && Number(v) > 0)
    .map(([k, v]) => ({ vendorId: k.slice("vendor:".length), discountPct: Number(v) }));
}
