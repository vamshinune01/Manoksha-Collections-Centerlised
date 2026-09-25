import Link from "next/link";
import { redirect } from "next/navigation";
import { getResellerMe } from "@/lib/backend";
import { inr } from "@/lib/types";
import { CartBadge, SignOut } from "./portal-client";

const NAV = [
  { href: "/reseller", label: "Dashboard" },
  { href: "/reseller/catalog", label: "Catalog" },
  { href: "/reseller/orders", label: "Orders" },
  { href: "/reseller/wallet", label: "Wallet" },
  { href: "/reseller/customers", label: "Customers" },
  { href: "/reseller/terms", label: "My terms" },
];

export default async function PortalLayout({ children }: { children: React.ReactNode }) {
  const me = await getResellerMe();
  if (!me) redirect("/reseller/login");
  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-4 px-4 py-3">
          <Link href="/reseller" className="mr-4">
            <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-brand-600">Manoksha</p>
            <p className="text-sm font-semibold text-slate-900">Reseller</p>
          </Link>
          <nav className="flex flex-1 flex-wrap gap-3 text-sm">
            {NAV.map((n) => <Link key={n.href} href={n.href} className="text-slate-700 hover:text-brand-700">{n.label}</Link>)}
          </nav>
          <Link href="/reseller/wallet" className="text-sm text-slate-700">Wallet <strong>{inr(me.walletBalance)}</strong></Link>
          <CartBadge />
          <SignOut />
        </div>
        {!me.canPlaceOrders && (
          <div className="bg-amber-50 px-4 py-2 text-center text-sm text-amber-900">
            Your account is <strong>{me.status}</strong>. You can view your history, but new orders and deposits are paused. Please contact Manoksha Collections.
          </div>
        )}
      </header>
      <main className="mx-auto max-w-6xl px-4 py-6">{children}</main>
    </div>
  );
}
