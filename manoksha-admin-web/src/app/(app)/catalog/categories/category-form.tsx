"use client";

import { useState } from "react";
import { Alert, Button, Card, Field, Input, Select } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { Category } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function CategoryForm({ categories, existing }: { categories: Category[]; existing?: Category }) {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  if (!open) return existing
    ? <Button variant="ghost" onClick={() => setOpen(true)}>Edit</Button>
    : <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)}>Add category</Button></div>;

  const form = (
    <form className="grid gap-3 text-left md:grid-cols-5" onSubmit={async (e) => {
      e.preventDefault();
      const f = new FormData(e.currentTarget);
      const body = { name: f.get("name"), parentId: f.get("parentId") || null, sortOrder: Number(f.get("sortOrder") || 0), isActive: f.get("isActive") === "on", reason: f.get("reason") };
      const ok = await run(() => existing ? callApi(`admin/catalog/categories/${existing.id}`, "PUT", body) : callApi("admin/catalog/categories", "POST", body));
      if (ok) setOpen(false);
    }}>
      {error && <div className="md:col-span-5"><Alert>{error}</Alert></div>}
      <Field label="Name"><Input name="name" required defaultValue={existing?.name} /></Field>
      <Field label="Parent">
        <Select name="parentId" defaultValue={existing?.parentId ?? ""}>
          <option value="">(top level)</option>
          {categories.filter((c) => c.id !== existing?.id).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </Select>
      </Field>
      <Field label="Sort order"><Input name="sortOrder" type="number" defaultValue={existing?.sortOrder ?? 0} /></Field>
      <Field label="Reason"><Input name="reason" required /></Field>
      <div className="flex items-end gap-3">
        <label className="flex items-center gap-1 text-sm"><input type="checkbox" name="isActive" defaultChecked={existing?.isActive ?? true} className="accent-brand-600" /> Active</label>
        <Button type="submit" disabled={busy}>Save</Button>
        <Button type="button" variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
      </div>
    </form>
  );
  return existing ? form : <Card title="New category" className="mb-6">{form}</Card>;
}
