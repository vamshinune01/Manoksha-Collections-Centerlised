"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Alert, Button, Card, Field, Input, Select } from "@/components/ui";
import { ApiError, callApi } from "@/lib/client-api";

export function CreateRoleForm() {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string>();

  if (!open) {
    return (
      <div className="mb-4 flex justify-end">
        <Button onClick={() => setOpen(true)}>New role</Button>
      </div>
    );
  }
  return (
    <Card title="New custom role" className="mb-6" actions={<Button variant="ghost" onClick={() => setOpen(false)}>Close</Button>}>
      <form
        className="grid gap-4 md:grid-cols-2"
        onSubmit={async (e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          setError(undefined);
          try {
            const role = await callApi<{ id: string }>("admin/roles", "POST", {
              code: form.get("code"),
              name: form.get("name"),
              description: form.get("description") || null,
              scope: form.get("scope"),
              permissions: [],
              reason: form.get("reason"),
            });
            router.push(`/roles/${role.id}`);
          } catch (err) {
            setError(err instanceof ApiError ? err.message : "Could not create the role.");
          }
        }}
      >
        {error && <div className="md:col-span-2"><Alert>{error}</Alert></div>}
        <Field label="Name"><Input name="name" required /></Field>
        <Field label="Code" hint="Uppercase letters, digits and underscore, e.g. STORE_SUPERVISOR"><Input name="code" required pattern="[A-Za-z][A-Za-z0-9_]{2,49}" /></Field>
        <Field label="Scope">
          <Select name="scope" defaultValue="Branch">
            <option value="Branch">One branch (assigned per branch)</option>
            <option value="Global">All branches</option>
          </Select>
        </Field>
        <Field label="Description"><Input name="description" /></Field>
        <Field label="Reason"><Input name="reason" required /></Field>
        <div className="md:col-span-2"><Button type="submit">Create role, then choose permissions</Button></div>
      </form>
    </Card>
  );
}
