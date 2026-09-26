"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Alert, Button, Field, Input } from "@/components/ui";
import { ApiError, rs } from "@/lib/client-api";
import type { OnlineDeposit } from "@/lib/types";

/** Provider-confirmed UPI top-up (SPEC §17.1): credited automatically, and only, after the payment provider confirms. */
export function OnlineDepositForm() {
  const [amount, setAmount] = useState("");
  const [key, setKey] = useState(() => crypto.randomUUID());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  return (
    <form className="flex flex-wrap items-end gap-3" onSubmit={async (e) => {
      e.preventDefault();
      setBusy(true);
      setError(undefined);
      try {
        const d = await rs<OnlineDeposit>("wallet/deposits/online", "POST", { amount: Number(amount) }, { "Idempotency-Key": key });
        setKey(crypto.randomUUID());
        if (d.payment?.redirectUrl && /^https?:\/\//.test(d.payment.redirectUrl)) window.location.assign(d.payment.redirectUrl);
        else setError("The payment could not be started. Nothing was charged; please try again.");
      } catch (err) {
        if (err instanceof ApiError) setKey(crypto.randomUUID());
        setError(err instanceof ApiError ? err.message : "Network problem — please try again.");
      } finally {
        setBusy(false);
      }
    }}>
      <Field label="Amount (₹)"><Input type="number" min={1} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required className="w-40" /></Field>
      <Button type="submit" disabled={busy || !(Number(amount) > 0)}>{busy ? "Starting…" : "Pay with UPI"}</Button>
      {error && <div className="w-full"><Alert>{error}</Alert></div>}
    </form>
  );
}

/** While an online deposit is pending, ask the backend (which asks the provider) until it settles. */
export function PendingDepositWatcher({ ids }: { ids: string[] }) {
  const router = useRouter();
  const joined = ids.join(",");
  useEffect(() => {
    if (!joined) return;
    let stop = false;
    const t = setInterval(async () => {
      const results = await Promise.all(joined.split(",").map((id) => rs<OnlineDeposit>(`wallet/deposits/online/${id}`).catch(() => null)));
      if (!stop && results.some((r) => r && r.status !== "Pending")) router.refresh();
    }, 3000);
    return () => {
      stop = true;
      clearInterval(t);
    };
  }, [joined, router]);
  return null;
}
