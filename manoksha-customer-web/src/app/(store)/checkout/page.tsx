import { redirect } from "next/navigation";
import { customerFetch, getCustomerMe } from "@/lib/backend";
import type { Delivery } from "@/lib/types";
import { CheckoutForm } from "./checkout-form";

export const metadata = { title: "Checkout" };

/** Login is required before an order is placed (ADR-001 §10). */
export default async function CheckoutPage() {
  const me = await getCustomerMe();
  if (!me) redirect("/login?next=/checkout");
  const last = await customerFetch<Delivery | null>("delivery-defaults");
  const defaults: Delivery = (last.ok && last.data) || {
    name: me.displayName, mobile: me.mobile ?? "", email: me.email, addressLine: "", city: "", state: "Telangana", pin: "",
  };
  return <CheckoutForm defaults={defaults} />;
}
