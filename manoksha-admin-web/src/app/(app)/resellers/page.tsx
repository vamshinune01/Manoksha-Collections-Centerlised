import Link from "next/link";
import { Alert, Badge, Button, Forbidden, Input, PageHeader, Select, Table, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { ResellerSummary } from "@/lib/types";
import { CreateResellerForm } from "./create-reseller-form";
import { RESELLER_TONE } from "./reseller-status";

type Search = Record<string, string | string[] | undefined>;

export default async function ResellersPage({ searchParams }: { searchParams: Promise<Search> }) {
  const me = (await getMe())!;
  if (!can(me, P.resellersView)) return <Forbidden what="resellers" />;
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q : "";
  const status = typeof sp.status === "string" ? sp.status : "";
  const list = await backendFetch<ResellerSummary[]>(`admin/resellers?${new URLSearchParams({ ...(q && { q }), ...(status && { status }) })}`);
  if (!list.ok) return <Alert>{list.problem.title}</Alert>;
  return (
    <>
      <PageHeader title="Resellers" description="Only the Owner onboards resellers. New resellers start Pending and become Active after verifying their mobile number by OTP." />
      {can(me, P.resellersManage) && <CreateResellerForm />}
      <form method="get" className="mb-4 flex gap-2">
        <Input name="q" placeholder="Name, shop, RS number or mobile" defaultValue={q} className="max-w-sm" />
        <Select name="status" defaultValue={status} className="max-w-xs">
          <option value="">Any status</option>
          {["Pending", "Active", "Frozen", "Suspended", "Closed"].map((s) => <option key={s}>{s}</option>)}
        </Select>
        <Button type="submit" variant="secondary">Search</Button>
      </form>
      <Table head={["Reseller", "Shop", "Mobile", "City", "Discount", "Wallet", "Status", "Onboarded"]} empty={list.data.length === 0}>
        {list.data.map((r) => (
          <tr key={r.id} className="hover:bg-slate-50">
            <td className="px-4 py-2.5"><Link href={`/resellers/${r.id}`} className="font-medium text-brand-700 hover:underline">{r.contactName}</Link>
              <div className="font-mono text-xs text-slate-400">{r.resellerNumber}</div></td>
            <td className="px-4 py-2.5">{r.businessName ?? "—"}</td>
            <td className="px-4 py-2.5">{r.mobile}</td>
            <td className="px-4 py-2.5">{r.city}</td>
            <td className="px-4 py-2.5">{r.currentDiscountPct}%</td>
            <td className="px-4 py-2.5">{inr(r.walletBalance)}</td>
            <td className="px-4 py-2.5"><Badge tone={RESELLER_TONE[r.status]}>{r.status}</Badge></td>
            <td className="px-4 py-2.5 text-slate-600">{formatDateTime(r.createdAt)}</td>
          </tr>
        ))}
      </Table>
    </>
  );
}
