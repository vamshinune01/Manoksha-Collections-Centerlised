"use client";

import { useEffect, useState } from "react";
import { callApi } from "@/lib/client-api";
import type { SkuInfo } from "@/lib/types";
import { Input } from "./ui";

/** Search SKUs by product name, SKU code or barcode (scanner input works too). */
export function SkuPicker({ onPick, placeholder = "Search product, SKU or scan barcode" }: { onPick: (sku: SkuInfo) => void; placeholder?: string }) {
  const [q, setQ] = useState("");
  const [results, setResults] = useState<SkuInfo[]>([]);

  useEffect(() => {
    if (q.trim().length < 2) return;
    const handle = setTimeout(async () => {
      try {
        setResults(await callApi<SkuInfo[]>(`admin/catalog/skus?q=${encodeURIComponent(q.trim())}&limit=10`));
      } catch {
        setResults([]);
      }
    }, 250);
    return () => clearTimeout(handle);
  }, [q]);

  const visible = q.trim().length < 2 ? [] : results;
  return (
    <div className="relative">
      <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder={placeholder} aria-label="Find SKU" />
      {visible.length > 0 && (
        <ul className="absolute z-10 mt-1 max-h-64 w-full overflow-auto rounded-md border border-slate-200 bg-white shadow-lg">
          {visible.map((s) => (
            <li key={s.skuId}>
              <button type="button" className="block w-full px-3 py-2 text-left text-sm hover:bg-brand-50"
                onClick={() => { onPick(s); setQ(""); setResults([]); }}>
                <span className="font-medium">{s.productName}</span> <span className="text-slate-500">· {s.variantName}</span>
                <span className="ml-2 font-mono text-xs text-slate-400">{s.skuCode}</span>
                {s.trackingMode === "Serialized" && <span className="ml-2 text-xs text-brand-700">per piece</span>}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
