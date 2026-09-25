"use client";

/**
 * The cart is a per-browser convenience (ADR-001 Phase 5 note): it stores only SKU ids, names and quantities.
 * Prices shown come from the backend quote; the backend re-prices and re-validates everything at checkout.
 */
export interface CartLine {
  skuId: string;
  name: string;
  quantity: number;
}

const KEY = "manoksha.reseller.cart";

export function readCart(): CartLine[] {
  try {
    return JSON.parse(localStorage.getItem(KEY) ?? "[]") as CartLine[];
  } catch {
    return [];
  }
}

export function writeCart(lines: CartLine[]) {
  try {
    localStorage.setItem(KEY, JSON.stringify(lines));
    window.dispatchEvent(new Event("manoksha-cart"));
  } catch {
    /* storage unavailable (private mode) — cart simply isn't remembered */
  }
}

export function addToCart(line: Omit<CartLine, "quantity">, quantity = 1) {
  const lines = readCart();
  const existing = lines.find((l) => l.skuId === line.skuId);
  if (existing) existing.quantity = Math.min(1000, existing.quantity + quantity);
  else lines.push({ ...line, quantity });
  writeCart(lines);
}
