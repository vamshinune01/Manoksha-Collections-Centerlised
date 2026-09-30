import type { Metadata } from "next";
import Link from "next/link";
import { publicGet, type SetupStatus } from "@/lib/public-api";
import { SetupForm } from "./setup-form";

export const metadata: Metadata = { title: "Set up the Owner account" };

/** First-run setup: creates the Owner once; closed afterwards (and unless a setup code is configured on the server). */
export default async function SetupPage() {
  const status = await publicGet<SetupStatus>("setup/status");
  return (
    <main className="flex min-h-screen items-center justify-center bg-gradient-to-br from-brand-50 via-white to-slate-100 px-4 py-10">
      <div className="w-full max-w-md">
        <div className="mb-6 text-center">
          <p className="text-xs font-semibold uppercase tracking-[0.25em] text-brand-600">Manoksha Collections</p>
          <h1 className="mt-2 text-2xl font-semibold text-slate-900">Set up the Owner account</h1>
        </div>
        <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
          {!status.ok ? (
            <p className="text-sm text-red-700">{status.title}</p>
          ) : status.data.ownerExists ? (
            <p className="text-sm text-slate-700">The Owner account is already set up. <Link href="/login" className="font-medium text-brand-700 hover:underline">Sign in</Link></p>
          ) : !status.data.setupEnabled ? (
            <p className="text-sm text-slate-700">Setup is not enabled on this server. Ask whoever deployed it for the setup code configuration.</p>
          ) : (
            <SetupForm />
          )}
        </div>
      </div>
    </main>
  );
}
