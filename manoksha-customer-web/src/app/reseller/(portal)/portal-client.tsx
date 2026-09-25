"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useSyncExternalStore } from "react";
import { readCart } from "@/lib/cart";

function subscribe(onChange: () => void) {
  window.addEventListener("manoksha-cart", onChange);
  window.addEventListener("storage", onChange);
  return () => {
    window.removeEventListener("manoksha-cart", onChange);
    window.removeEventListener("storage", onChange);
  };
}

export function CartBadge() {
  const count = useSyncExternalStore(subscribe, () => readCart().reduce((n, l) => n + l.quantity, 0), () => 0);
  return <Link href="/reseller/cart" className="rounded-md bg-brand-600 px-3 py-1.5 text-sm font-medium text-white">Cart ({count})</Link>;
}

export function SignOut() {
  const router = useRouter();
  return (
    <button className="text-sm text-slate-500 hover:text-slate-800" onClick={async () => {
      await fetch("/api/reseller-auth/logout", { method: "POST" });
      router.replace("/reseller/login");
      router.refresh();
    }}>Sign out</button>
  );
}
