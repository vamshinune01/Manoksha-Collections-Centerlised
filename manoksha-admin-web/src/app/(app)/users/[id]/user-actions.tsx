"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Field, Input, Select } from "@/components/ui";
import { ApiError, callApi } from "@/lib/client-api";

function useAction() {
  const router = useRouter();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  async function run(fn: () => Promise<unknown>) {
    setBusy(true);
    setError(undefined);
    try {
      await fn();
      router.refresh();
      return true;
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "The action failed.");
      return false;
    } finally {
      setBusy(false);
    }
  }
  return { error, busy, run };
}

export function AssignRoleForm({ userId, roles }: { userId: string; roles: { id: string; name: string; scope: "Global" | "Branch" }[] }) {
  const { error, busy, run } = useAction();
  const [roleId, setRoleId] = useState(roles[0]?.id ?? "");
  const scope = roles.find((r) => r.id === roleId)?.scope;

  return (
    <form
      className="mt-5 grid gap-3 border-t border-slate-100 pt-4 md:grid-cols-2"
      onSubmit={async (e) => {
        e.preventDefault();
        const form = new FormData(e.currentTarget);
        const ok = await run(() =>
          callApi(`admin/users/${userId}/role-assignments`, "POST", {
            roleId,
            branchId: scope === "Branch" ? String(form.get("branchId")).trim() : null,
            reason: form.get("reason"),
          }),
        );
        if (ok) (e.target as HTMLFormElement).reset();
      }}
    >
      <p className="text-sm font-medium text-slate-700 md:col-span-2">Assign a role</p>
      {error && <div className="md:col-span-2"><Alert>{error}</Alert></div>}
      <Field label="Role">
        <Select value={roleId} onChange={(e) => setRoleId(e.target.value)}>
          {roles.map((r) => (
            <option key={r.id} value={r.id}>{r.name} ({r.scope === "Global" ? "all branches" : "one branch"})</option>
          ))}
        </Select>
      </Field>
      {scope === "Branch" && (
        <Field label="Branch ID" hint="Branch selection from the branch master arrives with Phase 2.">
          <Input name="branchId" required pattern="[0-9a-fA-F-]{36}" />
        </Field>
      )}
      <Field label="Reason"><Input name="reason" required /></Field>
      <div className="md:col-span-2"><Button type="submit" disabled={busy || !roleId}>Assign role</Button></div>
    </form>
  );
}

export function RevokeRoleButton({ userId, assignmentId, roleName }: { userId: string; assignmentId: string; roleName: string }) {
  const { error, busy, run } = useAction();
  return (
    <div className="text-right">
      <Button
        variant="ghost"
        disabled={busy}
        onClick={() => {
          const reason = window.prompt(`Reason for removing "${roleName}"?`);
          if (reason) void run(() => callApi(`admin/users/${userId}/role-assignments/${assignmentId}/revoke`, "POST", { reason }));
        }}
      >
        Remove
      </Button>
      {error && <p className="text-xs text-red-600">{error}</p>}
    </div>
  );
}

export function UserStatusForm({ userId, status, canRevokeSessions }: { userId: string; status: string; canRevokeSessions: boolean }) {
  const { error, busy, run } = useAction();
  const [message, setMessage] = useState<string>();
  return (
    <div className="mt-5 space-y-3 border-t border-slate-100 pt-4">
      {error && <Alert>{error}</Alert>}
      {message && <Alert tone="success">{message}</Alert>}
      <div className="flex flex-wrap gap-2">
        {["Active", "Locked", "Disabled"].filter((s) => s !== status).map((s) => (
          <Button
            key={s}
            variant={s === "Active" ? "secondary" : "danger"}
            disabled={busy}
            onClick={() => {
              const reason = window.prompt(`Reason for setting this account to ${s}?`);
              if (reason) void run(() => callApi(`admin/users/${userId}/status`, "POST", { status: s, reason }));
            }}
          >
            {s === "Active" ? "Reactivate" : s === "Locked" ? "Lock" : "Disable"}
          </Button>
        ))}
        {canRevokeSessions && (
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => {
              const reason = window.prompt("Reason for signing this user out everywhere?");
              if (reason)
                void run(async () => {
                  const r = await callApi<{ revokedSessions: number }>(`admin/users/${userId}/sessions/revoke`, "POST", { reason });
                  setMessage(`Signed out of ${r.revokedSessions} session(s).`);
                });
            }}
          >
            Sign out everywhere
          </Button>
        )}
      </div>
    </div>
  );
}
