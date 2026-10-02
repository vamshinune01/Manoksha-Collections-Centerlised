import Link from "next/link";
import { Alert, Badge, Card, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { EmailDelivery, Inbox, OperationalAlert, Severity } from "@/lib/types";
import { MarkAllRead, NotificationLink, RetryEmail, SendTestEmail } from "./inbox-actions";

const TONE: Record<Severity, "red" | "amber" | "slate"> = { CRITICAL: "red", WARNING: "amber", INFO: "slate" };

/** In-app notifications (SPEC §29) and, for the Owner, every email the system sent or tried to send. */
export default async function NotificationsPage({ searchParams }: { searchParams: Promise<{ tab?: string; status?: string }> }) {
  const me = (await getMe())!;
  if (!can(me, P.notificationsView)) return <Forbidden what="notifications" />;
  const { tab = "inbox", status } = await searchParams;
  const owner = can(me, P.exceptionsManage);
  const tabs = [["inbox", "Inbox"], ...(owner ? [["emails", "Email log"], ["alerts", "Sensitive alerts"]] : [])];

  return (
    <>
      <PageHeader title="Notifications" description="Alerts and updates from orders, payments, stock and approvals. Critical items are also emailed to the Owner." />
      <div className="mb-4 flex gap-2 text-xs">
        {tabs.map(([key, label]) => (
          <Link key={key} href={`/notifications?tab=${key}`} className={`rounded-full border px-3 py-1 ${tab === key ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white"}`}>{label}</Link>
        ))}
      </div>
      {tab === "emails" && owner ? <EmailLog status={status} /> : tab === "alerts" && owner ? <Alerts /> : <InboxList />}
    </>
  );
}

async function InboxList() {
  const inbox = await backendFetch<Inbox>("admin/notifications");
  if (!inbox.ok) return <Alert>{inbox.problem.title}</Alert>;
  return (
    <Card title={`${inbox.data.unread} unread`} actions={inbox.data.unread > 0 ? <MarkAllRead /> : undefined}>
      {inbox.data.items.length === 0 ? <p className="text-sm text-slate-500">No notifications in the last 30 days.</p> : (
        <ul className="divide-y divide-slate-100">
          {inbox.data.items.map((n) => (
            <li key={n.id} className={`flex items-start gap-3 py-3 ${n.read ? "" : "bg-brand-50/40"}`}>
              <Badge tone={TONE[n.category]}>{n.category}</Badge>
              <div className="min-w-0 flex-1 text-sm">
                <NotificationLink item={n} />
                <p className="text-xs text-slate-600">{n.body}</p>
              </div>
              <span className="whitespace-nowrap text-xs text-slate-500">{formatDateTime(n.createdAt)}</span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

async function EmailLog({ status }: { status?: string }) {
  const emails = await backendFetch<EmailDelivery[]>(`admin/notifications/emails${status ? `?status=${status}` : ""}`);
  if (!emails.ok) return <Alert>{emails.problem.title}</Alert>;
  return (
    <>
      <Alert tone="info">
        Every email is recorded here. While no email provider is connected (staging), they are only recorded, not delivered. Open one to see exactly what the recipient gets.
      </Alert>
      <div className="my-3 flex flex-wrap items-center gap-2 text-xs">
        <SendTestEmail />
        {["", "Pending", "Sent", "Failed"].map((s) => (
          <Link key={s || "all"} href={`/notifications?tab=emails${s ? `&status=${s}` : ""}`}
            className={`rounded-full border px-3 py-1 ${(status ?? "") === s ? "border-brand-600 bg-brand-600 text-white" : "border-slate-300 bg-white"}`}>{s || "All"}</Link>
        ))}
      </div>
      <Table head={["Created", "To", "Subject", "Status", "Attempts", ""]} empty={emails.data.length === 0}>
        {emails.data.map((e) => (
          <tr key={e.id} className="align-top">
            <td className="whitespace-nowrap px-4 py-2 text-xs text-slate-500">{formatDateTime(e.createdAt)}</td>
            <td className="px-4 py-2 text-xs">{e.toName}<div className="text-slate-500">{e.toAddress} · {e.recipientKind.toLowerCase()}</div></td>
            <td className="px-4 py-2 text-sm"><Link className="text-brand-700 hover:underline" href={`/notifications/emails/${e.id}`}>{e.subject}</Link></td>
            <td className="px-4 py-2">
              <Badge tone={e.status === "Sent" ? "green" : e.status === "Failed" ? "red" : "amber"}>{e.status}</Badge>
              {e.lastError && <div className="mt-1 max-w-xs text-xs text-red-700">{e.lastError}</div>}
            </td>
            <td className="px-4 py-2 text-xs">{e.attempts}{e.sentAt && <div className="text-slate-500">sent {formatDateTime(e.sentAt)}</div>}</td>
            <td className="px-4 py-2 text-right">{e.status === "Failed" && <RetryEmail id={e.id} />}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}

async function Alerts() {
  const alerts = await backendFetch<OperationalAlert[]>("admin/alerts");
  if (!alerts.ok) return <Alert>{alerts.problem.title}</Alert>;
  return (
    <Table head={["Raised", "Alert", "Status", "Follow-up"]} empty={alerts.data.length === 0}>
      {alerts.data.map((a) => (
        <tr key={a.id} className="align-top">
          <td className="whitespace-nowrap px-4 py-2 text-xs text-slate-500">{formatDateTime(a.createdAt)}</td>
          <td className="px-4 py-2 text-sm"><strong>{a.title}</strong><div className="text-xs text-slate-600">{a.detail}</div></td>
          <td className="px-4 py-2"><Badge tone={a.status === "Open" ? "red" : "green"}>{a.status}</Badge></td>
          <td className="px-4 py-2 text-xs text-slate-600">
            {a.status === "Open" ? <Link className="text-brand-700 hover:underline" href="/exceptions?type=SENSITIVE_ALERT">Resolve in the Exception Center</Link>
              : <>{a.resolutionNote}<div className="text-slate-500">{formatDateTime(a.resolvedAt)}</div></>}
          </td>
        </tr>
      ))}
    </Table>
  );
}
