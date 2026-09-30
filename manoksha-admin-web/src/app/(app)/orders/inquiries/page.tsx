import Link from "next/link";
import { Alert, Badge, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { FulfillmentInquiry } from "@/lib/types";

export default async function InquiriesPage() {
  const me = (await getMe())!;
  if (!can(me, P.exceptionsView)) return <Forbidden what="unfulfilled checkouts" />;
  const list = await backendFetch<FulfillmentInquiry[]>("admin/fulfillment-inquiries");
  if (!list.ok) return <Alert>{list.problem.title}</Alert>;
  return (
    <>
      <PageHeader title="Unfulfilled checkouts" description="No single branch could fulfil the complete basket, so no order was created and nothing was charged. The buyer was given this reference and the WhatsApp help link." />
      <Table head={["Reference", "Channel", "Contact", "Cart", "Branch evaluation", "When", "Follow-up"]} empty={list.data.length === 0}>
        {list.data.map((i) => (
          <tr key={i.id} className="align-top">
            <td className="px-4 py-2.5 font-mono text-xs">{i.reference}</td>
            <td className="px-4 py-2.5">{i.channel}</td>
            <td className="px-4 py-2.5 text-xs">{i.contactName}<div>{i.contactMobile}</div></td>
            <td className="max-w-xs px-4 py-2.5"><pre className="overflow-x-auto text-[11px]">{JSON.stringify(JSON.parse(i.cart), null, 1)}</pre></td>
            <td className="max-w-md px-4 py-2.5"><pre className="overflow-x-auto text-[11px]">{JSON.stringify(JSON.parse(i.evaluations), null, 1)}</pre></td>
            <td className="px-4 py-2.5 text-xs text-slate-600">{formatDateTime(i.createdAt)}</td>
            <td className="max-w-xs px-4 py-2.5 text-xs">
              <Badge tone={i.status === "Open" ? "amber" : "green"}>{i.status}</Badge>
              {i.followUpNote && <div className="mt-1 text-slate-600">{i.followUpNote}</div>}
              {i.status === "Open" && <div className="mt-1"><Link className="text-brand-700 hover:underline" href="/exceptions?type=UNFULFILLED_CHECKOUT">Mark followed up</Link></div>}
            </td>
          </tr>
        ))}
      </Table>
    </>
  );
}
