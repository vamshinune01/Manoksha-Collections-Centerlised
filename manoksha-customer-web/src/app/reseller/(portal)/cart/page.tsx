import { resellerFetch, getResellerMe } from "@/lib/backend";
import type { SavedCustomer } from "@/lib/types";
import { CartCheckout } from "./cart-checkout";

export default async function CartPage() {
  const me = (await getResellerMe())!;
  const customers = await resellerFetch<SavedCustomer[]>("customers");
  return <CartCheckout canOrder={me.canPlaceOrders} walletBalance={me.walletBalance} customers={customers.ok ? customers.data : []} />;
}
