import Link from "next/link";

/** Storefront arrives in Phase 6; the reseller area is live. */
export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center bg-gradient-to-br from-brand-50 via-white to-slate-100 px-4 text-center">
      <p className="text-xs font-semibold uppercase tracking-[0.3em] text-brand-600">Manoksha Collections</p>
      <h1 className="mt-3 text-3xl font-semibold text-slate-900">Jewellery · Sarees · Kids wear</h1>
      <p className="mt-2 max-w-md text-slate-600">Our online store is opening soon.</p>
      <Link href="/reseller/login" className="mt-8 rounded-md bg-brand-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-brand-700">Reseller sign in</Link>
    </main>
  );
}
