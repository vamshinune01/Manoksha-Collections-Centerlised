import { formatDateTime } from "./ui";
import { ORDER_STATUS_LABEL, type Order } from "@/lib/types";

const STEPS = ["Confirmed", "Processing", "Packed", "Shipped", "Delivered"];

/** Order progress and courier details for customers and resellers (no internal notes are ever shown). */
export function OrderTracking({ order }: { order: Order }) {
  if (order.status === "Cancelled") {
    return (
      <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800">
        This order was cancelled by Manoksha Collections. If you paid online, our team will contact you about your payment; reseller wallet
        orders are credited back to your wallet. Use “Need help with this order?” for any question.
      </div>
    );
  }
  const reached = (step: string) => order.history.some((h) => h.toStatus === step);
  if (!reached("Confirmed")) return null;
  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4">
      <ol className="grid grid-cols-5 gap-2 text-center text-xs">
        {STEPS.map((step) => {
          const at = order.history.filter((h) => h.toStatus === step).at(-1)?.occurredAt;
          return (
            <li key={step} className="space-y-1">
              <div className={`mx-auto h-2.5 w-2.5 rounded-full ${at ? "bg-emerald-600" : "bg-slate-300"}`} />
              <div className={at ? "font-medium text-slate-900" : "text-slate-400"}>{ORDER_STATUS_LABEL[step] ?? step}</div>
              {at && <div className="text-[10px] text-slate-500">{formatDateTime(at)}</div>}
            </li>
          );
        })}
      </ol>
      {order.status === "FulfillmentException" && (
        <p className="mt-3 text-sm text-amber-800">We are sorting out an item in your order; it may be sent from another of our stores. No action is needed from you.</p>
      )}
      {order.shipment && (
        <p className="mt-3 text-sm text-slate-700">
          Shipped with <strong>{order.shipment.courierLabel}</strong>
          {order.shipment.trackingNumber ? <> · tracking number <span className="font-mono">{order.shipment.trackingNumber}</span></> : null}
          {order.shipment.deliveredOn ? <> · delivered on {order.shipment.deliveredOn}</> : null}
        </p>
      )}
    </div>
  );
}
