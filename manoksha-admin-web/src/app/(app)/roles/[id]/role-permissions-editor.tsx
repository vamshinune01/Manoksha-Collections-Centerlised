"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Alert, Badge, Button, Card, Field, Input } from "@/components/ui";
import { ApiError, callApi } from "@/lib/client-api";
import type { PermissionInfo, Role } from "@/lib/types";

export function RolePermissionsEditor({ role, catalog, editable }: { role: Role; catalog: PermissionInfo[]; editable: boolean }) {
  const router = useRouter();
  const [selected, setSelected] = useState(() => new Set(role.permissions));
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string>();
  const [saved, setSaved] = useState(false);
  const groups = useMemo(() => {
    const map = new Map<string, PermissionInfo[]>();
    for (const p of catalog) map.set(p.module, [...(map.get(p.module) ?? []), p]);
    return [...map.entries()];
  }, [catalog]);
  const dirty = selected.size !== role.permissions.length || role.permissions.some((p) => !selected.has(p));

  return (
    <div className="space-y-4">
      {error && <Alert>{error}</Alert>}
      {saved && <Alert tone="success">Permissions saved. Holders of this role get the change on their next request.</Alert>}
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {groups.map(([module, perms]) => (
          <Card key={module} title={module}>
            <ul className="space-y-1.5">
              {perms.map((p) => {
                const locked = p.ownerOnly && role.code !== "OWNER";
                return (
                  <li key={p.code}>
                    <label className="flex items-start gap-2 text-sm">
                      <input
                        type="checkbox"
                        className="mt-0.5 accent-brand-600"
                        checked={selected.has(p.code)}
                        disabled={!editable || locked}
                        onChange={(e) => {
                          const next = new Set(selected);
                          if (e.target.checked) next.add(p.code);
                          else next.delete(p.code);
                          setSelected(next);
                          setSaved(false);
                        }}
                      />
                      <span className="font-mono text-xs text-slate-700">{p.code}</span>
                      {p.ownerOnly && <Badge tone="brand">Owner only</Badge>}
                    </label>
                  </li>
                );
              })}
            </ul>
          </Card>
        ))}
      </div>
      {editable && (
        <Card>
          <div className="flex flex-wrap items-end gap-3">
            <div className="min-w-72 flex-1">
              <Field label="Reason for change" hint="Recorded with the before/after permission list in the audit log.">
                <Input value={reason} onChange={(e) => setReason(e.target.value)} />
              </Field>
            </div>
            <Button
              disabled={!dirty || !reason.trim()}
              onClick={async () => {
                setError(undefined);
                try {
                  await callApi(`admin/roles/${role.id}/permissions`, "PUT", { permissions: [...selected], reason });
                  setSaved(true);
                  setReason("");
                  router.refresh();
                } catch (e) {
                  setError(e instanceof ApiError ? e.message : "Could not save permissions.");
                }
              }}
            >
              Save permissions
            </Button>
          </div>
        </Card>
      )}
    </div>
  );
}
