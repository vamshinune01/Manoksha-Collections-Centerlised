"use client";

import { useEffect, useState } from "react";
import { BarcodeSvg } from "@/components/barcode-svg";
import type { Label } from "@/lib/types";

/** Printable label sheet. The print was already recorded by the backend (reprints keep the same barcode identity). */
export default function LabelsPage() {
  const [labels, setLabels] = useState<Label[] | null>(null);

  useEffect(() => {
    let parsed: Label[] = [];
    try { parsed = JSON.parse(sessionStorage.getItem("manoksha.labels") ?? "[]") as Label[]; } catch { /* storage unavailable */ }
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reads browser-only storage once after mount
    setLabels(parsed);
  }, []);

  if (labels === null) return null;
  if (labels.length === 0) return <p className="p-8 text-sm text-slate-600">No labels to print. Select barcodes on a product page first.</p>;

  const sheet = labels.flatMap((l) => Array.from({ length: l.copies }, (_, i) => ({ ...l, key: `${l.barcodeId}-${i}` })));
  return (
    <main className="bg-white p-6">
      <div className="mb-4 flex items-center gap-3 print:hidden">
        <button className="rounded-md bg-brand-600 px-4 py-2 text-sm font-medium text-white" onClick={() => window.print()}>Print</button>
        <button className="text-sm text-slate-600" onClick={() => history.back()}>Back</button>
        {labels.some((l) => l.isReprint) && <span className="text-sm text-amber-800">Includes reprints (same barcode, no new identity).</span>}
      </div>
      <div className="grid grid-cols-3 gap-3 print:gap-2">
        {sheet.map((l) => (
          <div key={l.key} className="break-inside-avoid rounded border border-slate-300 p-2 text-center">
            <p className="truncate text-xs font-semibold">{l.productName}</p>
            <p className="truncate text-[11px] text-slate-600">{l.variantName} · {l.skuCode}</p>
            <div className="mt-1 flex justify-center"><BarcodeSvg code={l.code} /></div>
          </div>
        ))}
      </div>
    </main>
  );
}
