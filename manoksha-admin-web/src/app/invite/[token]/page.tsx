import type { Metadata } from "next";
import { publicGet } from "@/lib/public-api";
import { AcceptForm } from "./accept-form";

export const metadata: Metadata = { title: "Set your password" };

interface Invitation { displayName: string; emailMasked: string; expiresAt: string }

/** Staff invitation: the person chooses their own password (no open staff sign-up). */
export default async function InvitePage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  const invite = await publicGet<Invitation>(`invitations/${encodeURIComponent(token)}`);
  return (
    <main className="flex min-h-screen items-center justify-center bg-gradient-to-br from-brand-50 via-white to-slate-100 px-4 py-10">
      <div className="w-full max-w-sm">
        <div className="mb-6 text-center">
          <p className="text-xs font-semibold uppercase tracking-[0.25em] text-brand-600">Manoksha Collections</p>
          <h1 className="mt-2 text-2xl font-semibold text-slate-900">Welcome{invite.ok ? `, ${invite.data.displayName.split(" ")[0]}` : ""}</h1>
        </div>
        <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
          {invite.ok ? <AcceptForm token={token} email={invite.data.emailMasked} /> : <p className="text-sm text-slate-700">{invite.title}</p>}
        </div>
      </div>
    </main>
  );
}
