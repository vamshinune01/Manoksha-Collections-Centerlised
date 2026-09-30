import { redirect } from "next/navigation";
import { Shell } from "@/components/shell";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { ExceptionCenter } from "@/lib/types";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const me = await getMe();
  if (!me) redirect("/login");
  if (me.accountType !== "Internal") redirect("/login");
  // CRITICAL alerts (SPEC §14.2, §28) stay visible on every page until handled; the bell shows unread notifications.
  const [center, unread] = await Promise.all([
    can(me, P.exceptionsView) ? backendFetch<ExceptionCenter>("admin/exceptions") : Promise.resolve(null),
    can(me, P.notificationsView) ? backendFetch<{ unread: number }>("admin/notifications/unread-count") : Promise.resolve(null),
  ]);
  const count = (type: string) => (center?.ok ? center.data.counts.find((c) => c.type === type)?.open ?? 0 : 0);
  return (
    <Shell
      me={me}
      openReconciliations={count("PAYMENT_RECONCILIATION")}
      openExceptions={count("FULFILLMENT_EXCEPTION")}
      openAlerts={count("SENSITIVE_ALERT")}
      openTotal={center?.ok ? center.data.counts.reduce((n, c) => n + c.open, 0) : 0}
      unread={unread?.ok ? unread.data.unread : 0}
    >
      {children}
    </Shell>
  );
}
