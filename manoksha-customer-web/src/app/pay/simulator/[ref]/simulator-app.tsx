"use client";

import { useEffect, useState } from "react";
import { ApiError } from "@/lib/client-api";
import { inr } from "@/lib/types";
import { Alert, Button, Input } from "@/components/ui";

interface SimPayment {
  providerOrderRef: string;
  amount: number;
  description: string;
  status: "Created" | "Succeeded" | "Failed";
  expiresAt: string;
  windowEnded: boolean;
  paidAmount: number | null;
  providerPaymentRef: string | null;
  returnPath: string;
}

async function sim<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api/payment-simulator/${path}`, {
    method: body === undefined ? "GET" : "POST",
    headers: { "x-manoksha-client": "customer-web", ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const json = await response.json().catch(() => ({}));
  if (!response.ok) throw new ApiError(response.status, json?.code, json?.title ?? "Request failed", json);
  return json as T;
}

const safeReturn = (path: string) => (path.startsWith("/") && !path.startsWith("//") ? path : "/");

/**
 * Simulated UPI app. "Pay" approves the payment and sends the provider's signed webhook; the other options exercise the
 * failure paths the backend must handle (declined, lost callback, wrong amount, paying after the window).
 */
export function SimulatorApp({ providerRef }: { providerRef: string }) {
  const [payment, setPayment] = useState<SimPayment | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const [custom, setCustom] = useState("");

  useEffect(() => {
    sim<SimPayment>(providerRef).then(setPayment).catch((e: unknown) => setError(e instanceof ApiError ? e.message : "Payment not found."));
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, [providerRef]);

  const act = async (action: "approve" | "decline", body: Record<string, unknown>) => {
    setBusy(true);
    setError(null);
    try {
      const p = await sim<SimPayment>(`${providerRef}/${action}`, body);
      setPayment(p);
      setTimeout(() => window.location.assign(safeReturn(p.returnPath)), 1200);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  };

  const secondsLeft = payment ? Math.max(0, Math.floor((Date.parse(payment.expiresAt) - now) / 1000)) : 0;

  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-900 px-4 py-10">
      <div className="w-full max-w-sm overflow-hidden rounded-3xl bg-white shadow-2xl">
        <div className="bg-indigo-700 px-6 py-5 text-white">
          <p className="text-xs uppercase tracking-widest text-indigo-200">UPI · Simulator</p>
          <p className="mt-1 text-sm">Paying <strong>Manoksha Collections</strong></p>
        </div>
        <div className="space-y-5 p-6">
          <p className="rounded-md bg-amber-50 px-3 py-2 text-xs text-amber-900">Development only — no real money moves. Real UPI replaces this page once a gateway is chosen.</p>
          {error && <Alert>{error}</Alert>}
          {payment && (
            <>
              <div className="text-center">
                <p className="text-sm text-slate-500">{payment.description}</p>
                <p className="mt-1 text-4xl font-semibold text-slate-900">{inr(payment.amount)}</p>
                {payment.status === "Created" && (
                  <p className={`mt-2 text-xs ${payment.windowEnded || secondsLeft === 0 ? "text-red-600" : "text-slate-500"}`}>
                    {payment.windowEnded || secondsLeft === 0
                      ? "The merchant's payment window has ended — paying now simulates a delayed bank confirmation."
                      : `Complete within ${Math.floor(secondsLeft / 60)}:${String(secondsLeft % 60).padStart(2, "0")}`}
                  </p>
                )}
              </div>

              {payment.status === "Created" ? (
                <div className="space-y-3">
                  <Button className="w-full bg-indigo-700 py-3 text-base hover:bg-indigo-800" disabled={busy} onClick={() => act("approve", { sendWebhook: true })}>
                    Pay {inr(payment.amount)}
                  </Button>
                  <Button variant="secondary" className="w-full" disabled={busy} onClick={() => act("decline", { sendWebhook: true })}>Decline</Button>
                  <details className="rounded-md border border-slate-200 p-3 text-sm">
                    <summary className="cursor-pointer text-slate-600">Test scenarios</summary>
                    <div className="mt-3 space-y-2">
                      <Button variant="secondary" className="w-full" disabled={busy} onClick={() => act("approve", { sendWebhook: false })}>
                        Pay, but lose the callback
                      </Button>
                      <div className="flex gap-2">
                        <Input placeholder="Other amount" value={custom} onChange={(e) => setCustom(e.target.value)} inputMode="decimal" />
                        <Button variant="secondary" disabled={busy || !Number(custom)} onClick={() => act("approve", { paidAmount: Number(custom), sendWebhook: true })}>Pay</Button>
                      </div>
                    </div>
                  </details>
                </div>
              ) : (
                <div className="space-y-3 text-center">
                  <Alert tone={payment.status === "Succeeded" ? "success" : "warning"}>
                    {payment.status === "Succeeded" ? `Paid ${inr(payment.paidAmount)} · Ref ${payment.providerPaymentRef}` : "Payment declined."}
                  </Alert>
                  <a href={safeReturn(payment.returnPath)} className="text-sm font-medium text-indigo-700 hover:underline">Return to Manoksha Collections →</a>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </main>
  );
}
