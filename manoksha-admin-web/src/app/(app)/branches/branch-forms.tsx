"use client";

import { useState } from "react";
import { Alert, Badge, Button, Card, Field, Input } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Branch, Priority } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function CreateBranchForm() {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  if (!open) return <div className="flex justify-end"><Button onClick={() => setOpen(true)}>Add branch</Button></div>;
  return (
    <Card title="New branch" actions={<Button variant="ghost" onClick={() => setOpen(false)}>Close</Button>}>
      <form
        className="grid gap-4 md:grid-cols-2"
        onSubmit={async (e) => {
          e.preventDefault();
          const f = new FormData(e.currentTarget);
          const ok = await run(() =>
            callApi("admin/branches", "POST", {
              code: f.get("code"),
              name: f.get("name"),
              address: { line1: f.get("line1") || null, city: f.get("city") || null, state: f.get("state") || null, pin: f.get("pin") || null, phone: f.get("phone") || null },
              reason: f.get("reason"),
            }),
          );
          if (ok) setOpen(false);
        }}
      >
        {error && <div className="md:col-span-2"><Alert>{error}</Alert></div>}
        <Field label="Code" hint="2–10 letters/digits, e.g. KNR"><Input name="code" required maxLength={10} /></Field>
        <Field label="Name"><Input name="name" required /></Field>
        <Field label="Address"><Input name="line1" /></Field>
        <Field label="City"><Input name="city" /></Field>
        <Field label="State"><Input name="state" defaultValue="Telangana" /></Field>
        <Field label="PIN"><Input name="pin" inputMode="numeric" maxLength={6} /></Field>
        <Field label="Phone"><Input name="phone" inputMode="tel" /></Field>
        <Field label="Reason" hint="New branches join the priority list at the lowest position."><Input name="reason" required /></Field>
        <div className="md:col-span-2"><Button type="submit" disabled={busy}>Create branch</Button></div>
      </form>
    </Card>
  );
}

export function BranchStatusButton({ branch }: { branch: Branch }) {
  const { error, busy, run } = useAction();
  return (
    <>
      <Button
        variant={branch.isActive ? "ghost" : "secondary"}
        disabled={busy}
        onClick={() => {
          const reason = window.prompt(`Reason for ${branch.isActive ? "deactivating" : "reactivating"} ${branch.name}?`);
          if (reason) void run(() => callApi(`admin/branches/${branch.id}/status`, "POST", { isActive: !branch.isActive, reason }));
        }}
      >
        {branch.isActive ? "Deactivate" : "Reactivate"}
      </Button>
      {error && <p className="text-xs text-red-600">{error}</p>}
    </>
  );
}

export function PriorityEditor({ priority, editable }: { priority: Priority; editable: boolean }) {
  const { error, busy, run } = useAction();
  const [order, setOrder] = useState(priority.entries);
  const [reason, setReason] = useState("");
  const dirty = order.some((e, i) => e.branchId !== priority.entries[i]?.branchId);

  function move(index: number, delta: number) {
    const next = [...order];
    const [item] = next.splice(index, 1);
    next.splice(index + delta, 0, item!);
    setOrder(next);
  }

  return (
    <div className="space-y-3">
      {error && <Alert>{error}</Alert>}
      <ol className="space-y-2">
        {order.map((e, i) => (
          <li key={e.branchId} className="flex items-center gap-3 rounded-md border border-slate-200 px-3 py-2">
            <span className="flex h-7 w-7 items-center justify-center rounded-full bg-brand-600 text-sm font-semibold text-white">{i + 1}</span>
            <span className="flex-1 text-sm font-medium text-slate-800">
              {e.branchName} <span className="font-mono text-xs text-slate-400">{e.branchCode}</span>
            </span>
            {!e.isActive && <Badge tone="red">Inactive · skipped</Badge>}
            {editable && (
              <span className="flex gap-1">
                <Button variant="ghost" aria-label={`Move ${e.branchName} up`} disabled={i === 0} onClick={() => move(i, -1)}>↑</Button>
                <Button variant="ghost" aria-label={`Move ${e.branchName} down`} disabled={i === order.length - 1} onClick={() => move(i, 1)}>↓</Button>
              </span>
            )}
          </li>
        ))}
      </ol>
      {editable && dirty && (
        <div className="space-y-2 border-t border-slate-100 pt-3">
          <Field label="Reason for change" hint="Audited with the old and new order."><Input value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
          <div className="flex gap-2">
            <Button
              disabled={busy || !reason.trim()}
              onClick={async () => {
                const ok = await run(() =>
                  callApi("admin/fulfillment-priority", "PUT", { branchIds: order.map((e) => e.branchId), expectedVersion: priority.version, reason }),
                );
                if (ok) setReason("");
              }}
            >
              Save priority
            </Button>
            <Button variant="ghost" onClick={() => setOrder(priority.entries)}>Reset</Button>
          </div>
        </div>
      )}
    </div>
  );
}
