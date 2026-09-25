import { Alert, Table, formatDateTime } from "@/components/ui";
import { resellerFetch } from "@/lib/backend";
import type { SavedCustomer } from "@/lib/types";

export default async function Customers() {
  const customers = await resellerFetch<(SavedCustomer & { updatedAt: string })[]>("customers");
  if (!customers.ok) return <Alert>{customers.title}</Alert>;
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">My customers</h1>
      <p className="text-sm text-slate-500">Saved when you place orders. Only you can see this list.</p>
      <Table head={["Name", "Mobile", "Address", "Last updated"]} empty={customers.data.length === 0}>
        {customers.data.map((c) => (
          <tr key={c.id}>
            <td className="px-4 py-2.5 font-medium">{c.details.name}</td>
            <td className="px-4 py-2.5">{c.details.mobile}</td>
            <td className="px-4 py-2.5 text-sm text-slate-600">{c.details.addressLine}, {c.details.city}, {c.details.state} {c.details.pin}</td>
            <td className="px-4 py-2.5 text-xs text-slate-500">{formatDateTime(c.updatedAt)}</td>
          </tr>
        ))}
      </Table>
    </div>
  );
}
