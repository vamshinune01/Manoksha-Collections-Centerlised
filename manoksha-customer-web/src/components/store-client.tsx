"use client";

import { useEffect, useState, useSyncExternalStore } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { CART_EVENT, storeCart, type CartLine } from "@/lib/cart";
import { Button } from "./ui";

function subscribe(callback: () => void) {
  window.addEventListener(CART_EVENT, callback);
  window.addEventListener("storage", callback);
  return () => {
    window.removeEventListener(CART_EVENT, callback);
    window.removeEventListener("storage", callback);
  };
}

const count = () => storeCart.read().reduce((n, l) => n + l.quantity, 0);

let cachedJson = "";
let cachedCart: CartLine[] = [];
function snapshot() {
  const json = JSON.stringify(storeCart.read());
  if (json !== cachedJson) {
    cachedJson = json;
    cachedCart = JSON.parse(json) as CartLine[];
  }
  return cachedCart;
}
const empty: CartLine[] = [];

/** The storefront cart as React state (browser storage is the source; a stable snapshot avoids re-render loops). */
export function useStoreCart() {
  return useSyncExternalStore(subscribe, snapshot, () => empty);
}

export function CartLink() {
  const items = useSyncExternalStore(subscribe, count, () => 0);
  return (
    <Link href="/cart" className="relative rounded-md px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100">
      Cart
      {items > 0 && <span className="ml-1.5 rounded-full bg-brand-600 px-1.5 py-0.5 text-xs font-semibold text-white">{items}</span>}
    </Link>
  );
}

export function AddToCart({ skuId, name, disabled }: { skuId: string; name: string; disabled?: boolean }) {
  const [added, setAdded] = useState(false);
  useEffect(() => {
    if (!added) return;
    const t = setTimeout(() => setAdded(false), 1500);
    return () => clearTimeout(t);
  }, [added]);
  return (
    <Button
      variant={added ? "secondary" : "primary"}
      disabled={disabled}
      onClick={() => {
        storeCart.add({ skuId, name });
        setAdded(true);
      }}
      className="w-full"
    >
      {disabled ? "Out of stock" : added ? "Added ✓" : "Add to cart"}
    </Button>
  );
}

export function SignOut() {
  const router = useRouter();
  return (
    <button
      className="rounded-md px-3 py-2 text-sm text-slate-600 hover:bg-slate-100"
      onClick={async () => {
        await fetch("/api/customer-auth/logout", { method: "POST" });
        router.push("/");
        router.refresh();
      }}
    >
      Sign out
    </button>
  );
}
