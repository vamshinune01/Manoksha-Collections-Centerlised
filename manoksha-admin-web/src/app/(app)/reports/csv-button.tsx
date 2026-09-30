"use client";

import { Button } from "@/components/ui";

type Cell = string | number | null;

/** Downloads the table as CSV (opens in Excel / Google Sheets). Values are quoted; formulas are neutralised. */
export function CsvButton({ filename, head, rows }: { filename: string; head: string[]; rows: Cell[][] }) {
  const quote = (v: Cell) => {
    let s = v === null ? "" : String(v);
    if (/^[=+\-@]/.test(s) && typeof v === "string") s = "'" + s;
    return `"${s.replaceAll('"', '""')}"`;
  };
  return (
    <Button variant="secondary" onClick={() => {
      const csv = [head, ...rows].map((r) => r.map(quote).join(",")).join("\r\n");
      const url = URL.createObjectURL(new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8" }));
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      a.click();
      URL.revokeObjectURL(url);
    }}>Download CSV</Button>
  );
}
