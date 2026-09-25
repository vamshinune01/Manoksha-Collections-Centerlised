"use client";

import { useState } from "react";
import { Alert, Button, Card, Field, Input } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import { useAction } from "@/lib/use-action";

export function CreateAttributeForm() {
  const { error, busy, run } = useAction();
  const [open, setOpen] = useState(false);
  if (!open) return <div className="mb-4 flex justify-end"><Button onClick={() => setOpen(true)}>Add attribute</Button></div>;
  return (
    <Card title="New variant attribute" className="mb-6" actions={<Button variant="ghost" onClick={() => setOpen(false)}>Close</Button>}>
      <form className="grid gap-3 md:grid-cols-4" onSubmit={async (e) => {
        e.preventDefault();
        const f = new FormData(e.currentTarget);
        const options = String(f.get("options") ?? "").split(",").map((s) => s.trim()).filter(Boolean);
        const ok = await run(() => callApi("admin/catalog/attributes", "POST", { code: f.get("code"), name: f.get("name"), options, reason: f.get("reason") }));
        if (ok) setOpen(false);
      }}>
        {error && <div className="md:col-span-4"><Alert>{error}</Alert></div>}
        <Field label="Name"><Input name="name" required placeholder="Design" /></Field>
        <Field label="Code"><Input name="code" required placeholder="DESIGN" /></Field>
        <Field label="Options" hint="Comma separated"><Input name="options" placeholder="Kanchi border, Plain" /></Field>
        <Field label="Reason"><Input name="reason" required /></Field>
        <div><Button type="submit" disabled={busy}>Create</Button></div>
      </form>
    </Card>
  );
}

export function AddOptionForm({ attributeId }: { attributeId: string }) {
  const { error, busy, run } = useAction();
  const [value, setValue] = useState("");
  return (
    <form className="mt-4 flex gap-2" onSubmit={async (e) => {
      e.preventDefault();
      const ok = await run(() => callApi(`admin/catalog/attributes/${attributeId}/options`, "POST", { value, reason: "New option" }));
      if (ok) setValue("");
    }}>
      <Input value={value} onChange={(e) => setValue(e.target.value)} placeholder="New option" aria-label="New option" />
      <Button type="submit" variant="secondary" disabled={busy || !value.trim()}>Add</Button>
      {error && <span className="text-xs text-red-600">{error}</span>}
    </form>
  );
}
