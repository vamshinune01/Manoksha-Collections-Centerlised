"use client";

import { useState } from "react";
import { Button } from "./ui";

/** One-time set-password link for an invited staff member (valid 72 hours). */
export function InviteLink({ token, expiresAt }: { token: string; expiresAt: string }) {
  const url = typeof window === "undefined" ? `/invite/${token}` : `${window.location.origin}/invite/${token}`;
  const [copied, setCopied] = useState(false);
  return (
    <div className="space-y-2">
      <div className="flex gap-2">
        <input readOnly value={url} className="w-full rounded-md border border-slate-300 bg-white px-2 py-1.5 font-mono text-xs" onFocus={(e) => e.target.select()} />
        <Button type="button" variant="secondary" onClick={async () => { await navigator.clipboard.writeText(url); setCopied(true); }}>{copied ? "Copied" : "Copy"}</Button>
      </div>
      <p className="text-xs text-slate-600">
        Send this link to the person privately (WhatsApp or email). They open it and choose their own password. It works once and expires{" "}
        {new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone: "Asia/Kolkata" }).format(new Date(expiresAt))}.
      </p>
    </div>
  );
}
