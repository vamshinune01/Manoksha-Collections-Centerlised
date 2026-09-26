import Link from "next/link";
import { getCustomerMe } from "@/lib/backend";
import { CartLink, SignOut } from "@/components/store-client";

/** Public storefront shell (SPEC §19.1). Browsing is anonymous; ordering requires customer sign-in (ADR-001 §10). */
export default async function StoreLayout({ children }: { children: React.ReactNode }) {
  const me = await getCustomerMe();
  return (
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-10 border-b border-slate-200 bg-white/95 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center gap-4 px-4 py-3">
          <Link href="/" className="shrink-0">
            <span className="block text-[10px] font-semibold uppercase tracking-[0.3em] text-brand-600">Manoksha</span>
            <span className="block text-sm font-semibold text-slate-900">Collections</span>
          </Link>
          <form action="/" className="min-w-0 flex-1">
            <input
              name="q"
              placeholder="Search sarees, jewellery, kids wear…"
              className="w-full rounded-full border border-slate-300 bg-slate-50 px-4 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
            />
          </form>
          <nav className="flex shrink-0 items-center">
            <CartLink />
            {me ? (
              <>
                <Link href="/orders" className="rounded-md px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100">My orders</Link>
                <Link href="/account" className="hidden rounded-md px-3 py-2 text-sm text-slate-600 hover:bg-slate-100 md:inline">Hi, {me.displayName.split(" ")[0]}</Link>
                <SignOut />
              </>
            ) : (
              <Link href="/login" className="rounded-md px-3 py-2 text-sm font-medium text-brand-700 hover:bg-brand-50">Sign in</Link>
            )}
          </nav>
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6">{children}</main>
      <footer className="border-t border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-2 px-4 py-4 text-xs text-slate-500">
          <span>Manoksha Collections · Karimnagar · Hyderabad · Mulugu</span>
          <Link href="/reseller/login" className="hover:text-brand-700">Reseller sign in</Link>
        </div>
      </footer>
    </div>
  );
}
