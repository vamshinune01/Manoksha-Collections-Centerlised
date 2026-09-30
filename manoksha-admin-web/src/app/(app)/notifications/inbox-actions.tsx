"use client";

import Link from "next/link";
import { Button } from "@/components/ui";
import { callApi } from "@/lib/client-api";
import type { NotificationItem } from "@/lib/types";
import { useAction } from "@/lib/use-action";

export function MarkAllRead() {
  const { busy, run } = useAction();
  return <Button variant="secondary" disabled={busy} onClick={() => void run(() => callApi("admin/notifications/read-all", "POST"))}>Mark all as read</Button>;
}

/** Opening a notification marks it read, then follows its link. */
export function NotificationLink({ item }: { item: NotificationItem }) {
  const { run } = useAction();
  const title = <span className={item.read ? "text-slate-700" : "font-semibold text-slate-900"}>{item.title}</span>;
  if (!item.link) {
    return item.read ? title : <button type="button" className="text-left" onClick={() => void run(() => callApi(`admin/notifications/${item.id}/read`, "POST"))}>{title}</button>;
  }
  return (
    <Link href={item.link} onClick={() => { if (!item.read) void callApi(`admin/notifications/${item.id}/read`, "POST"); }} className="hover:underline">
      {title}
    </Link>
  );
}

export function RetryEmail({ id }: { id: string }) {
  const { error, busy, run } = useAction();
  return (
    <span className="inline-flex flex-col items-end">
      <Button variant="secondary" disabled={busy} onClick={() => void run(() => callApi(`admin/notifications/emails/${id}/retry`, "POST"))}>Retry</Button>
      {error && <span className="text-xs text-red-600">{error}</span>}
    </span>
  );
}
