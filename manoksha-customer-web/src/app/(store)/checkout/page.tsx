import { redirect } from "next/navigation";
import { customerFetch, getCustomerMe, storeFetch } from "@/lib/backend";
import type { Delivery, OrderCharges } from "@/lib/types";
import { CheckoutForm } from "./checkout-form";

export const metadata = { title: "Checkout" };

/** Login is required before an order is placed (ADR-001 §10). */
export default async function CheckoutPage() {
  const me = await getCustomerMe();
  if (!me) redirect("/login?next=/checkout");
  const [last, charges] = await Promise.all([customerFetch<Delivery | null>("delivery-defaults"), storeFetch<OrderCharges>("order-charges")]);
  const defaults: Delivery = (last.ok && last.data) || {
    name: me.displayName, mobile: me.mobile ?? "", email: me.email, addressLine: "", city: "", state: "Telangana", pin: "",
  };
  return <CheckoutForm defaults={defaults} shippingFee={charges.ok ? charges.data.shippingFeePerOrder : null} />;
}
