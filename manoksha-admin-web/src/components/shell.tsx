import Link from "next/link";
import type { ReactNode } from "react";
import { type Me, NAV, canAny, shortId } from "@/lib/access";
import { LogoutButton } from "./logout-button";
import { NavLink } from "./nav-link";

/** Navigation is filtered by the user's permissions for convenience only — the backend authorizes every call. */
export function Shell({ me, children }: { me: Me; children: ReactNode }) {
  const items = NAV.filter((item) => canAny(me, item.permission));
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
        <div className="border-t border-slate-100 p-4 text-xs text-slate-500">V1 · Phase 5</div>
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
            <div className="text-right">
              <p className="text-sm font-medium text-slate-900">{me.displayName}</p>
              <p className="text-xs text-slate-500">{primaryRole}</p>
            </div>
            <LogoutButton />
          </div>
        </header>
        <main className="flex-1 px-6 py-6">{children}</main>
      </div>
    </div>
  );
}
