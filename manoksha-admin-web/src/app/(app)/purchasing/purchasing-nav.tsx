import Link from "next/link";

export function PurchasingNav({ active, showSuppliers }: { active: "orders" | "suppliers"; showSuppliers: boolean }) {
  const items = [
    { key: "orders", href: "/purchasing", label: "Purchase orders", show: true },
    { key: "suppliers", href: "/purchasing/suppliers", label: "Suppliers", show: showSuppliers },
  ] as const;
  return (
    <nav className="mb-5 flex gap-1 border-b border-slate-200">
      {items.filter((i) => i.show).map((i) => (
        <Link key={i.key} href={i.href}
          className={`-mb-px border-b-2 px-3 py-2 text-sm ${i.key === active ? "border-brand-600 font-medium text-brand-700" : "border-transparent text-slate-600 hover:text-slate-900"}`}>
          {i.label}
        </Link>
      ))}
    </nav>
  );
}

export const PO_TONE: Record<string, "slate" | "green" | "red" | "amber" | "brand"> = {
  Draft: "amber", Issued: "brand", PartiallyReceived: "amber", Received: "green", Closed: "slate", Cancelled: "red",
};
