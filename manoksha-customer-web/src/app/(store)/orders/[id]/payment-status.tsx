"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { cs } from "@/lib/client-api";
import type { OrderPaymentStatus } from "@/lib/types";
import { Alert, formatDateTime } from "@/components/ui";

const LIVE = ["INITIATED", "PENDING", "LATE_SUCCESS_RECHECK"];

/**
 * Payment progress. The server asks the payment provider (never trusting the browser) each time this polls (design §13 point 6).
 */
export function PaymentStatus({ orderId, initialStatus }: { orderId: string; initialStatus: string }) {
  const router = useRouter();
  const [status, setStatus] = useState<OrderPaymentStatus | null>(null);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    let stop = false;
    let timer: ReturnType<typeof setTimeout>;
    const tick = async () => {
      try {
        const s = await cs<OrderPaymentStatus>(`orders/${orderId}/payment`);
        if (stop) return;
        setStatus(s);
        if (s.orderStatus !== initialStatus) router.refresh();
        if (s.payment && LIVE.includes(s.payment.status)) timer = setTimeout(tick, 3000);
      } catch {
        if (!stop) timer = setTimeout(tick, 5000);
      }
    };
    void tick();
    return () => {
      stop = true;
      clearTimeout(timer);
    };
  }, [orderId, initialStatus, router]);

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  const p = status?.payment;
  if (!p) return null;
  const secondsLeft = Math.max(0, Math.floor((Date.parse(p.expiresAt) - now) / 1000));
  const tone = p.status === "SUCCESS" || p.status === "ORDER_RECOVERED" ? "success" : LIVE.includes(p.status) ? "info" : "warning";
  return (
    <Alert tone={tone}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <span>{p.message}</span>
        {p.status === "PENDING" && p.redirectUrl && secondsLeft > 0 && (
          <span className="flex items-center gap-3">
            <span className="tabular-nums text-xs">Reserved for {Math.floor(secondsLeft / 60)}:{String(secondsLeft % 60).padStart(2, "0")}</span>
            <a href={p.redirectUrl} className="rounded-md bg-brand-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-brand-700">Pay now</a>
          </span>
        )}
        {p.completedAt && <span className="text-xs opacity-75">{formatDateTime(p.completedAt)}</span>}
      </div>
    </Alert>
  );
}
