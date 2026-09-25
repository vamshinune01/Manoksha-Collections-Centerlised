import { Alert } from "@/components/ui";
import { resellerFetch } from "@/lib/backend";
import type { SavedCustomer } from "@/lib/types";
import { CustomerManager } from "./customer-manager";

export default async function Customers() {
  const customers = await resellerFetch<(SavedCustomer & { updatedAt: string })[]>("customers");
  if (!customers.ok) return <Alert>{customers.title}</Alert>;
  return (
    <div className="space-y-4">
      <h1 className="text-xl font-semibold">My customers</h1>
      <p className="text-sm text-slate-500">Only you can see this list. Saved customers can be picked at checkout.</p>
      <CustomerManager customers={customers.data} />
    </div>
  );
}
