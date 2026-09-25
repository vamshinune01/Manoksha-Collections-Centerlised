import type { Metadata } from "next";
import { Suspense } from "react";
import { LoginFlow } from "./login-flow";

export const metadata: Metadata = { title: "Sign in" };

export default function LoginPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-gradient-to-br from-brand-50 via-white to-slate-100 px-4">
      <div className="w-full max-w-sm">
        <div className="mb-8 text-center">
          <p className="text-xs font-semibold uppercase tracking-[0.25em] text-brand-600">Manoksha Collections</p>
          <h1 className="mt-2 text-2xl font-semibold text-slate-900">Staff sign in</h1>
          <p className="mt-1 text-sm text-slate-600">Use your individual work account.</p>
        </div>
        <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
          <Suspense>
            <LoginFlow />
          </Suspense>
        </div>
      </div>
    </main>
  );
}
