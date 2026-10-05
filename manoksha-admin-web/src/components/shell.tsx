import Link from "next/link";
import type { ReactNode } from "react";
import { type Me, NAV, canAny, shortId } from "@/lib/access";
import { LogoutButton } from "./logout-button";
import { NavLink } from "./nav-link";

/** Navigation is filtered by the user's permissions for convenience only — the backend authorizes every call. */
export function Shell({ me, openReconciliations = 0, openExceptions = 0, openAlerts = 0, openTotal = 0, unread = 0, children }: {
  me: Me; openReconciliations?: number; openExceptions?: number; openAlerts?: number; openTotal?: number; unread?: number; children: ReactNode;
}) {
  const items = NAV.filter((item) => canAny(me, item.permission) && (!item.storeOnly || me.storeSellingEnabled));
  const primaryRole = me.isOwner ? "Owner" : me.roles.map((r) => (r.branchId ? `${r.name} · ${shortId(r.branchId)}` : r.name)).join(", ") || "No role assigned";

  return (
    <div className="flex min-h-screen">
      <aside className="hidden w-60 shrink-0 flex-col border-r border-slate-200 bg-white md:flex">
        <Link href="/" className="border-b border-slate-100 px-5 py-4">
          <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-brand-600">Manoksha</p>
          <p className="text-sm font-semibold text-slate-900">Operations</p>
        </Link>
        <nav className="flex-1 space-y-0.5 p-3">
          {items.map((item) => (
            <NavLink key={item.href} href={item.href} label={item.label} />
          ))}
        </nav>
        <div className="border-t border-slate-100 p-4 text-xs text-slate-500">V1 · Phase 9</div>
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center justify-between gap-4 border-b border-slate-200 bg-white px-6 py-3">
          <nav className="flex gap-3 overflow-x-auto text-sm md:hidden">
            {items.map((item) => (
              <Link key={item.href} href={item.href} className="whitespace-nowrap text-slate-700">
                {item.label}
              </Link>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-4">
            {openTotal > 0 && (
              <Link href="/exceptions" className="hidden rounded-full border border-amber-300 bg-amber-50 px-3 py-1 text-xs font-medium text-amber-900 sm:inline">
                {openTotal} to handle
              </Link>
            )}
            <Link href="/notifications" aria-label={`Notifications${unread > 0 ? `, ${unread} unread` : ""}`} className="relative rounded-full p-2 text-slate-600 hover:bg-slate-100">
              <svg aria-hidden viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                <path d="M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9" />
                <path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" />
              </svg>
              {unread > 0 && (
                <span className="absolute -right-0.5 -top-0.5 min-w-[1.1rem] rounded-full bg-red-600 px-1 text-center text-[10px] font-semibold leading-[1.1rem] text-white">
                  {unread > 99 ? "99+" : unread}
                </span>
              )}
            </Link>
            <div className="text-right">
              <p className="text-sm font-medium text-slate-900">{me.displayName}</p>
              <p className="text-xs text-slate-500">{primaryRole}</p>
            </div>
            <LogoutButton />
          </div>
        </header>
        {openReconciliations > 0 && (
          <div role="alert" className="flex flex-wrap items-center justify-between gap-3 border-b border-red-200 bg-red-50 px-6 py-2.5 text-sm text-red-800">
            <span>
              <strong>Critical:</strong> {openReconciliations} payment{openReconciliations === 1 ? " was" : "s were"} received but could not be applied to an order or deposit.
            </span>
            <Link href="/exceptions?type=PAYMENT_RECONCILIATION" className="font-medium underline">Review reconciliation</Link>
          </div>
        )}
        {openAlerts > 0 && (
          <div role="alert" className="flex flex-wrap items-center justify-between gap-3 border-b border-red-200 bg-red-50 px-6 py-2.5 text-sm text-red-800">
            <span>
              <strong>Critical:</strong> {openAlerts} sensitive alert{openAlerts === 1 ? " needs" : "s need"} your follow-up (security lockouts, wallet integrity).
            </span>
            <Link href="/exceptions?type=SENSITIVE_ALERT" className="font-medium underline">Review alerts</Link>
          </div>
        )}
        {openExceptions > 0 && (
          <div role="status" className="flex flex-wrap items-center justify-between gap-3 border-b border-amber-200 bg-amber-50 px-6 py-2.5 text-sm text-amber-900">
            <span>
              {openExceptions} confirmed order{openExceptions === 1 ? " has" : "s have"} a fulfillment exception (item missing, damaged or mismatched).
            </span>
            <Link href="/orders/exceptions" className="font-medium underline">Resolve</Link>
          </div>
        )}
        <main className="flex-1 px-6 py-6">{children}</main>
      </div>
    </div>
  );
}
