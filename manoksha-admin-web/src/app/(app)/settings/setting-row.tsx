"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Card, Input, formatDateTime } from "@/components/ui";
import { ApiError, callApi } from "@/lib/client-api";
import type { Setting } from "@/lib/types";

export function SettingRow({ setting, editable }: { setting: Setting; editable: boolean }) {
  const router = useRouter();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(String(setting.value));
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string>();

  const display = setting.kind === "Money" ? `₹${Number(setting.value).toFixed(2)}` : String(setting.value);

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="max-w-2xl">
          <p className="font-mono text-xs text-slate-500">{setting.key}</p>
          <p className="mt-0.5 text-sm text-slate-700">{setting.description}</p>
          <p className="mt-1 text-xs text-slate-400">Version {setting.version} · updated {formatDateTime(setting.updatedAt)}</p>
        </div>
        {!editing ? (
          <div className="flex items-center gap-3">
            <span className="text-lg font-semibold text-slate-900">{display}</span>
            {editable && <Button variant="secondary" onClick={() => setEditing(true)}>Change</Button>}
          </div>
        ) : (
          <form
            className="flex flex-wrap items-end gap-2"
            onSubmit={async (e) => {
              e.preventDefault();
              setError(undefined);
              try {
                const parsed = setting.kind === "String" ? value.trim() : Number(value);
                await callApi(`admin/settings/${setting.key}`, "PUT", { value: parsed, expectedVersion: setting.version, reason });
                setEditing(false);
                setReason("");
                router.refresh();
              } catch (err) {
                setError(err instanceof ApiError ? err.message : "Could not save.");
              }
            }}
          >
            <Input className="w-40" value={value} onChange={(e) => setValue(e.target.value)} inputMode={setting.kind === "String" ? "text" : "decimal"} required />
            <Input className="w-64" placeholder="Reason (required)" value={reason} onChange={(e) => setReason(e.target.value)} required />
            <Button type="submit">Save</Button>
            <Button type="button" variant="ghost" onClick={() => setEditing(false)}>Cancel</Button>
          </form>
        )}
      </div>
      {error && <div className="mt-3"><Alert>{error}</Alert></div>}
    </Card>
  );
}
