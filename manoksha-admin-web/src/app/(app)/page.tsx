import Link from "next/link";
import { Badge, Card, PageHeader } from "@/components/ui";
import { NAV, canAny, shortId } from "@/lib/access";
import { getMe } from "@/lib/backend";

export default async function DashboardPage() {
  const me = (await getMe())!;
  const sections = NAV.filter((n) => n.href !== "/" && (canAny(me, n.permission)));

  return (
    <>
      <PageHeader
        title={`Welcome, ${me.displayName}`}
        description={me.isOwner ? "You have global Owner access across all branches." : "Your access is limited to your assigned roles and branches."}
      />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Your access" className="lg:col-span-2">
          <ul className="space-y-2 text-sm">
            {me.roles.length === 0 && <li className="text-slate-500">No roles assigned yet. Contact the Owner.</li>}
            {me.roles.map((r) => (
              <li key={`${r.code}-${r.branchId}`} className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2">
                <span className="font-medium text-slate-800">{r.name}</span>
                <Badge tone={r.branchId ? "slate" : "brand"}>{r.branchId ? `Branch ${shortId(r.branchId)}` : "All branches"}</Badge>
              </li>
            ))}
          </ul>
          {!me.mfaEnabled && me.isOwner && (
            <p className="mt-4 text-sm text-amber-800">
              Two-step verification is not enabled on this Owner account.{" "}
              <Link href="/account" className="font-medium underline">
                Set it up
              </Link>
              .
            </p>
          )}
        </Card>
        <Card title="Go to">
          <ul className="space-y-1 text-sm">
            {sections.map((s) => (
              <li key={s.href}>
                <Link className="text-brand-700 hover:underline" href={s.href}>
                  {s.label}
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      </div>
    </>
  );
}
