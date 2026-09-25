"use client";

import { Alert, Button, Field, Input, Select } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Branch, Employee } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function EmployeeActions({ employee, branches }: { employee: Employee; branches: Branch[] }) {
  const { error, busy, run } = useAction();
  return (
    <div className="mt-5 space-y-4 border-t border-slate-100 pt-4">
      {error && <Alert>{error}</Alert>}
      {branches.length > 0 && (
        <form
          className="grid gap-3 md:grid-cols-3"
          onSubmit={(e) => {
            e.preventDefault();
            const f = new FormData(e.currentTarget);
            void run(() => callApi(`admin/employees/${employee.id}/reassign`, "POST", { branchId: f.get("branchId"), reason: f.get("reason") }));
          }}
        >
          <Field label="Move to branch">
            <Select name="branchId">{branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}</Select>
          </Field>
          <Field label="Reason"><Input name="reason" required /></Field>
          <div className="flex items-end"><Button type="submit" variant="secondary" disabled={busy}>Reassign</Button></div>
          <p className="text-xs text-slate-500 md:col-span-3">Role assignments are separate; ask the Owner to update the employee&apos;s branch role.</p>
        </form>
      )}
      <Button
        variant={employee.status === "Active" ? "danger" : "secondary"}
        disabled={busy}
        onClick={() => {
          const next = employee.status === "Active" ? "Inactive" : "Active";
          const reason = window.prompt(`Reason for marking ${employee.fullName} ${next}?`);
          if (reason) void run(() => callApi(`admin/employees/${employee.id}/status`, "POST", { status: next, reason }));
        }}
      >
        {employee.status === "Active" ? "Mark inactive" : "Reactivate"}
      </Button>
    </div>
  );
}
