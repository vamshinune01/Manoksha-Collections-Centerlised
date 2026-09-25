"use client";

import { useState } from "react";
import { Button, Input } from "@/components/ui";
import { addToCart } from "@/lib/cart";

export function AddToCart({ skuId, name }: { skuId: string; name: string }) {
  const [qty, setQty] = useState(1);
  const [added, setAdded] = useState(false);
  return (
    <div className="mt-3 flex items-center gap-2">
      <Input type="number" min={1} max={1000} value={qty} onChange={(e) => setQty(Math.max(1, Number(e.target.value)))} className="w-20" aria-label="Quantity" />
      <Button variant="secondary" onClick={() => { addToCart({ skuId, name }, qty); setAdded(true); }}>{added ? "Added ✓" : "Add to cart"}</Button>
    </div>
  );
}
