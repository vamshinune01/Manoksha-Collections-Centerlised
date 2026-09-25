"use client";

import { useState } from "react";
import { Button, Input, Select } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Discrepancy } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function ResolveDiscrepancy({ discrepancy: d }: { discrepancy: Discrepancy }) {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  const [action, setAction] = useState(d.sourceType === "COUNT" ? "DISMISSED" : "RECEIVED_LATE");
  const [qty, setQty] = useState(String(d.outstandingQty));
  const [notes, setNotes] = useState("");
  if (!open) return <Button variant="secondary" onClick={() => setOpen(true)}>Resolve</Button>;
  const serialized = d.missingItemIds.length > 0;
  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex flex-wrap justify-end gap-2">
        <Select value={action} onChange={(e) => setAction(e.target.value)} className="w-52">
          {d.sourceType === "COUNT" ? <option value="DISMISSED">Dismiss (explain why)</option> : <>
            <option value="RECEIVED_LATE">Received late at destination</option>
            <option value="RETURNED_TO_SOURCE">Returned to source</option>
            <option value="WRITTEN_OFF">Write off (lost in transit)</option>
          </>}
        </Select>
        {d.sourceType === "TRANSFER" && !serialized && <Input type="number" min={1} max={d.outstandingQty} className="w-20" value={qty} onChange={(e) => setQty(e.target.value)} aria-label="Quantity" />}
        <Input className="w-56" placeholder="Notes (required)" value={notes} onChange={(e) => setNotes(e.target.value)} />
        <Button disabled={busy || !notes.trim()} onClick={() => run(() => callApi(`admin/inventory/discrepancies/${d.id}/resolve`, "POST", {
          action, quantity: serialized ? null : Number(qty), itemIds: serialized ? d.missingItemIds : null, notes,
        }))}>Save</Button>
      </div>
      {error && <p className="text-xs text-red-600">{error}</p>}
    </div>
  );
}
