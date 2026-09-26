"use client";

/**
 * Carts are a per-browser convenience (ADR-001 Phase 5 note): they store only SKU ids, names and quantities. Prices shown come
 * from the backend; the backend re-prices and re-validates everything at checkout. The reseller cart and the storefront cart
 * are kept apart.
 */
export interface CartLine {
  skuId: string;
  name: string;
  quantity: number;
}

export const CART_EVENT = "manoksha-cart";

function store(key: string) {
  const read = (): CartLine[] => {
    try {
      return JSON.parse(localStorage.getItem(key) ?? "[]") as CartLine[];
    } catch {
      return [];
    }
  };
  const write = (lines: CartLine[]) => {
    try {
      localStorage.setItem(key, JSON.stringify(lines));
      window.dispatchEvent(new Event(CART_EVENT));
    } catch {
      /* storage unavailable (private mode) — cart simply isn't remembered */
    }
  };
  const add = (line: Omit<CartLine, "quantity">, quantity = 1) => {
    const lines = read();
    const existing = lines.find((l) => l.skuId === line.skuId);
    if (existing) existing.quantity = Math.min(1000, existing.quantity + quantity);
    else lines.push({ ...line, quantity });
    write(lines);
  };
  return { read, write, add };
}

const reseller = store("manoksha.reseller.cart");
export const readCart = reseller.read;
export const writeCart = reseller.write;
export const addToCart = reseller.add;

/** The public storefront's cart (customers). */
export const storeCart = store("manoksha.store.cart");
