"use client";

import { useState } from "react";
import { Alert, Button, Input } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Attendance } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function ClockButtons({ clockedIn }: { clockedIn: boolean }) {
  const { error, busy, run } = useAction();
  return (
    <span className="flex items-center gap-2">
      <Button disabled={busy} variant={clockedIn ? "secondary" : "primary"}
        onClick={() => run(() => callApi(`admin/me/attendance/${clockedIn ? "clock-out" : "clock-in"}`, "POST"))}>
        {clockedIn ? "Clock out" : "Clock in"}
      </Button>
      {error && <span className="text-sm text-red-600">{error}</span>}
    </span>
  );
}

/** datetime-local value in IST for an ISO instant. */
function toLocal(iso: string | null) {
  if (!iso) return "";
  const d = new Date(new Date(iso).getTime() + 5.5 * 3600_000);
  return d.toISOString().slice(0, 16);
}

function fromLocal(value: string) {
  return value ? new Date(`${value}:00+05:30`).toISOString() : null;
}

export function CorrectButton({ record }: { record: Attendance }) {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  const [clockIn, setClockIn] = useState(toLocal(record.clockInAt));
  const [clockOut, setClockOut] = useState(toLocal(record.clockOutAt));
  const [reason, setReason] = useState("");
  if (!open) return <Button variant="ghost" onClick={() => setOpen(true)}>Correct</Button>;
  return (
    <div className="flex flex-col items-end gap-2">
      {error && <Alert>{error}</Alert>}
      <div className="flex flex-wrap justify-end gap-2">
        <Input type="datetime-local" className="w-48" value={clockIn} onChange={(e) => setClockIn(e.target.value)} aria-label="Clock in (IST)" />
        <Input type="datetime-local" className="w-48" value={clockOut} onChange={(e) => setClockOut(e.target.value)} aria-label="Clock out (IST)" />
        <Input className="w-56" placeholder="Reason (required)" value={reason} onChange={(e) => setReason(e.target.value)} />
        <Button disabled={busy || !reason.trim()} onClick={async () => {
          const ok = await run(() => callApi(`admin/attendance/${record.id}/correct`, "POST", { clockInAt: fromLocal(clockIn), clockOutAt: fromLocal(clockOut), reason }));
          if (ok) setOpen(false);
        }}>Save</Button>
        <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
      </div>
    </div>
  );
}
