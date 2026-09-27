import { redirect } from "next/navigation";
import { Shell } from "@/components/shell";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { FulfillmentExceptionInfo, PaymentReconciliation } from "@/lib/types";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const me = await getMe();
  if (!me) redirect("/login");
  if (me.accountType !== "Internal") redirect("/login");
  // CRITICAL alert (SPEC §14.2): payments received that could not be applied stay visible on every page until handled.
  const [open, exceptions] = can(me, P.exceptionsView)
    ? await Promise.all([
        backendFetch<PaymentReconciliation[]>("admin/payment-reconciliations?status=Open"),
        backendFetch<FulfillmentExceptionInfo[]>("admin/fulfillment-exceptions?status=Open"),
      ])
    : [null, null];
  return (
    <Shell me={me} openReconciliations={open?.ok ? open.data.length : 0} openExceptions={exceptions?.ok ? exceptions.data.length : 0}>
      {children}
    </Shell>
  );
}
